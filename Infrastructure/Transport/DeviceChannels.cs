using System.IO.Ports;
using System.Net.Sockets;
using DSLetreros.Domain.Deployment;

namespace DSLetreros.Infrastructure.Transport;

/// <summary>
/// Canal LAN real sobre TCP (spec sección 18): framing binario con
/// `IDeviceChannel`. El framing se apoya en el header del `DeviceProtocol`
/// (11 bytes: magic|version|op|flags|length) para leer tramas completas del stream.
///
/// Robustez (spec 18/21):
///  * Serializa RequestAsync con un SemaphoreSlim (una operación a la vez por canal).
///  * Timeout por request.
///  * Valida magic/version/length del header ANTES de reservar el payload.
///  * Máximo de respuesta específico (evita OOM ante un length corrupto).
///  * Reset/reconnect del socket tras fallo de socket o de protocolo.
///  * Cancelación robusta (ct + timeout por request).
/// </summary>
public sealed class TcpDeviceChannel : IDeviceChannel, IDisposable
{
    public const int MaxResponseBytes = 64 * 1024 * 1024; // 64 MiB tope de respuesta

    private readonly string _host;
    private readonly int _port;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _requestTimeout;

    private TcpClient? _client;

    public TcpDeviceChannel(string host, int port, TimeSpan? requestTimeout = null)
    {
        _host = host;
        _port = port;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(10);
    }

    public string Transport => "lan";
    public string Endpoint => $"{_host}:{_port}";

    public async Task<byte[]> RequestAsync(byte[] frame, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_requestTimeout);

            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    await EnsureConnectedAsync(timeoutCts.Token).ConfigureAwait(false);
                    var stream = _client!.GetStream();
                    await stream.WriteAsync(frame, timeoutCts.Token).ConfigureAwait(false);
                    await stream.FlushAsync(timeoutCts.Token).ConfigureAwait(false);
                    return await ReadFrameAsync(stream, timeoutCts.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException)
                {
                    // Un único reintento con reconexión; después se propaga. Los errores de
                    // PROTOCOLO (magic/versión/length inválidos) NO se reintentan: son
                    // violaciones permanentes, no fallos de transporte.
                    ResetConnection();
                    if (attempt >= 1)
                        throw;
                    if (timeoutCts.IsCancellationRequested)
                        throw;
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_client != null && _client.Connected)
            return;

        ResetConnection();
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(_host, _port, ct).ConfigureAwait(false);
            _client = client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private void ResetConnection()
    {
        try { _client?.Dispose(); } catch { /* ignorar */ }
        _client = null;
    }

    private static async Task<byte[]> ReadFrameAsync(NetworkStream stream, CancellationToken ct)
    {
        // 1) Leer SÓLO el header (11 bytes) y validar magic/version/length ANTES de
        //    reservar memoria para el payload (evita asignar gigabytes sin verificar).
        var header = new byte[11];
        await ReadExactAsync(stream, header, ct).ConfigureAwait(false);

        if (header.Length < 4 || !System.Text.Encoding.ASCII.GetString(header, 0, 4).Equals(DeviceProtocol.Magic, StringComparison.Ordinal))
            throw new ProtocolException("Magic inválido en trama.");
        var version = header[4];
        if (version > DeviceProtocol.CurrentProtocolVersion)
            throw new ProtocolException($"Versión de protocolo {version} no soportada.");
        var len = BitConverter.ToInt32(header, 7);
        if (len < 0 || len > MaxResponseBytes)
            throw new ProtocolException("Longitud de trama fuera de rango.");

        // 2) Ahora sí reservar el payload (acotado por MaxResponseBytes).
        var payload = new byte[len];
        if (len > 0)
            await ReadExactAsync(stream, payload, ct).ConfigureAwait(false);

        var frame = new byte[11 + len];
        header.CopyTo(frame, 0);
        payload.CopyTo(frame, 11);
        return frame;
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        int off = 0;
        while (off < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(off), ct).ConfigureAwait(false);
            if (n <= 0) throw new IOException("Conexión cerrada a mitad de trama.");
            off += n;
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
        ResetConnection();
    }
}

/// <summary>
/// Canal USB/Serial real sobre System.IO.Ports.SerialPort. Mismo framing que TCP:
/// lee 11 bytes de header, valida, y luego el payload indicado por `length`.
/// Misma garantía de robustez que el canal TCP (semáforo, timeout, validación de
/// header, máximo de respuesta, reset y cancelación).
/// </summary>
public sealed class SerialDeviceChannel : IDeviceChannel, IDisposable
{
    public const int MaxResponseBytes = 64 * 1024 * 1024;

    private readonly string _portName;
    private readonly int _baudRate;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _requestTimeout;

    private SerialPort? _port;

    public SerialDeviceChannel(string portName, int baudRate = 115200, TimeSpan? requestTimeout = null)
    {
        _portName = portName;
        _baudRate = baudRate;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(10);
    }

    public string Transport => "serial";
    public string Endpoint => _portName;

    public async Task<byte[]> RequestAsync(byte[] frame, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_requestTimeout);

            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    EnsureOpen();
                    var port = _port!;
                    await Task.Run(() => port.Write(frame, 0, frame.Length), timeoutCts.Token).ConfigureAwait(false);
                    return await ReadFrameAsync(timeoutCts.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is InvalidOperationException
                                           or TimeoutException
                                           or UnauthorizedAccessException
                                           or System.IO.IOException)
                {
                    // Reintento con reconexión ante fallo de transporte/puerto; los errores de
                    // PROTOCOLO (magic/versión/length) NO se reintentan (violación permanente).
                    ResetPort();
                    if (attempt >= 1)
                        throw;
                    if (timeoutCts.IsCancellationRequested)
                        throw;
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureOpen()
    {
        _port ??= new SerialPort(_portName, _baudRate)
        {
            ReadTimeout = (int)_requestTimeout.TotalMilliseconds,
            WriteTimeout = (int)_requestTimeout.TotalMilliseconds,
        };
        if (!_port.IsOpen)
            _port.Open();
    }

    private void ResetPort()
    {
        try { _port?.Dispose(); } catch { /* ignorar */ }
        _port = null;
    }

    private async Task<byte[]> ReadFrameAsync(CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            // 1) Header primero; validar antes de reservar el payload.
            var header = ReadExact(_port!, 11);
            if (header.Length < 4 || !System.Text.Encoding.ASCII.GetString(header, 0, 4).Equals(DeviceProtocol.Magic, StringComparison.Ordinal))
                throw new ProtocolException("Magic inválido en trama.");
            var version = header[4];
            if (version > DeviceProtocol.CurrentProtocolVersion)
                throw new ProtocolException($"Versión de protocolo {version} no soportada.");
            var len = BitConverter.ToInt32(header, 7);
            if (len < 0 || len > MaxResponseBytes)
                throw new ProtocolException("Longitud de trama fuera de rango.");

            // 2) Reservar payload acotado.
            var payload = ReadExact(_port!, len);
            var frame = new byte[11 + len];
            header.CopyTo(frame, 0);
            payload.CopyTo(frame, 11);
            return frame;
        }, ct).ConfigureAwait(false);
    }

    private static byte[] ReadExact(SerialPort port, int count)
    {
        var buf = new byte[count];
        int off = 0;
        while (off < count)
        {
            int n = port.Read(buf, off, count - off);
            if (n <= 0) throw new IOException("Puerto serie cerrado a mitad de trama.");
            off += n;
        }
        return buf;
    }

    public void Dispose()
    {
        _gate.Dispose();
        ResetPort();
    }
}