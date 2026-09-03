using System.IO.Ports;
using System.Net.Sockets;
using DSLetreros.Domain.Deployment;

namespace DSLetreros.Infrastructure.Transport;

/// <summary>
/// Canal LAN real sobre TCP (spec sección 18): Framecache binario con
/// `IDeviceChannel`. El framing se apoya en el header del `DeviceProtocol`
/// (11 bytes: magic|version|op|flags|length) para leer tramas completas del stream.
/// </summary>
public sealed class TcpDeviceChannel : IDeviceChannel, IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private TcpClient? _client;

    public TcpDeviceChannel(string host, int port)
    {
        _host = host;
        _port = port;
    }

    public string Transport => "lan";
    public string Endpoint => $"{_host}:{_port}";

    public async Task<byte[]> RequestAsync(byte[] frame, CancellationToken ct = default)
    {
        _client ??= new TcpClient();
        if (!_client.Connected)
            await _client.ConnectAsync(_host, _port, ct);

        var stream = _client.GetStream();
        await stream.WriteAsync(frame, ct);
        await stream.FlushAsync(ct);

        return await ReadFrameAsync(stream, ct);
    }

    private static async Task<byte[]> ReadFrameAsync(NetworkStream stream, CancellationToken ct)
    {
        var header = new byte[11];
        await ReadExactAsync(stream, header, ct);
        var len = BitConverter.ToInt32(header, 7);
        if (len < 0 || len > 64 * 1024 * 1024)
            throw new ProtocolException("Longitud de trama fuera de rango.");
        var payload = new byte[len];
        if (len > 0)
            await ReadExactAsync(stream, payload, ct);
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
            int n = await stream.ReadAsync(buffer.AsMemory(off), ct);
            if (n <= 0) throw new ProtocolException("Conexión cerrada a mitad de trama.");
            off += n;
        }
    }

    public void Dispose() => _client?.Dispose();
}

/// <summary>
/// Canal USB/Serial real sobre System.IO.Ports.SerialPort. Mismo framing que TCP:
/// lee 11 bytes de header y luego el payload indicado por `length`.
/// </summary>
public sealed class SerialDeviceChannel : IDeviceChannel, IDisposable
{
    private readonly string _portName;
    private readonly int _baudRate;
    private SerialPort? _port;

    public SerialDeviceChannel(string portName, int baudRate = 115200)
    {
        _portName = portName;
        _baudRate = baudRate;
    }

    public string Transport => "serial";
    public string Endpoint => _portName;

    public async Task<byte[]> RequestAsync(byte[] frame, CancellationToken ct = default)
    {
        _port ??= new SerialPort(_portName, _baudRate)
        {
            ReadTimeout = 5000,
            WriteTimeout = 5000,
        };
        if (!_port.IsOpen) _port.Open();

        await Task.Run(() => _port!.Write(frame, 0, frame.Length), ct);
        return await ReadFrameAsync(ct);
    }

    private async Task<byte[]> ReadFrameAsync(CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var header = ReadExact(_port!, 11);
            var len = BitConverter.ToInt32(header, 7);
            if (len < 0 || len > 64 * 1024 * 1024)
                throw new ProtocolException("Longitud de trama fuera de rango.");
            var payload = ReadExact(_port!, len);
            var frame = new byte[11 + len];
            header.CopyTo(frame, 0);
            payload.CopyTo(frame, 11);
            return frame;
        }, ct);
    }

    private static byte[] ReadExact(SerialPort port, int count)
    {
        var buf = new byte[count];
        int off = 0;
        while (off < count)
        {
            int n = port.Read(buf, off, count - off);
            if (n <= 0) throw new ProtocolException("Puerto serie cerrado a mitad de trama.");
            off += n;
        }
        return buf;
    }

    public void Dispose() => _port?.Dispose();
}