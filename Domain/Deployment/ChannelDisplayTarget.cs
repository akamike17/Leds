using System.Text;
using System.Text.Json;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;

namespace DSLetreros.Domain.Deployment;

/// <summary>
/// Adapter IDisplayTarget que habla con un dispositivo real a través de un
/// IDeviceChannel (USB/Serial o Wi-Fi/LAN). El MISMO contrato y protocolo que
/// SimulatorTarget (invariante 2 + sección 18): la única diferencia es el canal.
///
/// Upload transaccional (spec 18): los bytes se envían a un staging temporal en el
/// dispositivo; la activación es atómica y cualquier fallo conserva la escena activa
/// anterior (LastKnownGood). El checksum verifica la integridad antes de activar.
/// </summary>
public class ChannelDisplayTarget : IDisplayTarget
{
    private readonly IDeviceChannel _channel;
    private DeviceIdentity? _identity;
    private DeviceCapabilities? _capabilities;
    private DeviceStatus _status = DeviceStatus.Unknown;

    public ChannelDisplayTarget(IDeviceChannel channel, DeviceId? id = null)
    {
        _channel = channel;
        Id = id ?? DeviceId.New();
    }

    public DeviceId Id { get; }

    /// <summary>Transporte del canal (usb/serial/lan), para diagnóstico y claves de identidad.</summary>
    public string Transport => _channel.Transport;
    public string Endpoint => _channel.Endpoint;

    public async Task<TargetResult> ConnectAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _channel.RequestAsync(DeviceProtocol.Hello(Id), ct);
            var (op, _, payload) = DeviceProtocol.Unwrap(resp);
            if (op == DeviceProtocol.OpError)
                return TargetResult.Fail(Encoding.UTF8.GetString(payload));
            _status = DeviceStatus.Online;
            return TargetResult.Ok();
        }
        catch (Exception ex)
        {
            _status = DeviceStatus.Error;
            return TargetResult.Fail("Connect falló: " + ex.Message);
        }
    }

    public async Task<TargetResult<DeviceIdentity>> GetIdentityAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _channel.RequestAsync(DeviceProtocol.Wrap(DeviceProtocol.OpIdentity, 0, Array.Empty<byte>()), ct);
            var (op, _, payload) = DeviceProtocol.Unwrap(resp);
            if (op == DeviceProtocol.OpError)
                return TargetResult<DeviceIdentity>.Fail(Encoding.UTF8.GetString(payload));
            var id = JsonSerializer.Deserialize<DeviceIdentity>(Encoding.UTF8.GetString(payload));
            _identity = id ?? throw new ProtocolException("Identidad vacía.");
            return TargetResult<DeviceIdentity>.Ok(_identity!);
        }
        catch (Exception ex)
        {
            return TargetResult<DeviceIdentity>.Fail("Identity falló: " + ex.Message);
        }
    }

    public async Task<TargetResult<DeviceCapabilities>> GetCapabilitiesAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _channel.RequestAsync(DeviceProtocol.Wrap(DeviceProtocol.OpCapabilities, 0, Array.Empty<byte>()), ct);
            var (op, _, payload) = DeviceProtocol.Unwrap(resp);
            if (op == DeviceProtocol.OpError)
                return TargetResult<DeviceCapabilities>.Fail(Encoding.UTF8.GetString(payload));
            var caps = JsonSerializer.Deserialize<DeviceCapabilities>(Encoding.UTF8.GetString(payload));
            _capabilities = caps ?? throw new ProtocolException("Capacidades vacías.");
            return TargetResult<DeviceCapabilities>.Ok(_capabilities!);
        }
        catch (Exception ex)
        {
            return TargetResult<DeviceCapabilities>.Fail("Capabilities falló: " + ex.Message);
        }
    }

    public async Task<TargetResult<string>> PrepareTransferAsync(long sceneBytes, CancellationToken ct = default)
    {
        try
        {
            var ticket = Guid.NewGuid().ToString("N");
            var resp = await _channel.RequestAsync(DeviceProtocol.Prepare(ticket, sceneBytes), ct);
            var (op, _, payload) = DeviceProtocol.Unwrap(resp);
            if (op == DeviceProtocol.OpError)
                return TargetResult<string>.Fail(Encoding.UTF8.GetString(payload));
            return TargetResult<string>.Ok(ticket);
        }
        catch (Exception ex)
        {
            return TargetResult<string>.Fail("Prepare falló: " + ex.Message);
        }
    }

    public async Task<TargetResult> UploadAsync(string transferTicket, ScenePackage package, CancellationToken ct = default)
    {
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(package, ScenePackageJson.Options);
            // Fragmentación defensiva en bloques de 64 KB (FlagFinal en la última parte).
            const int chunk = 64 * 1024;
            for (int off = 0; off < payload.Length; off += chunk)
            {
                var count = Math.Min(chunk, payload.Length - off);
                var last = off + chunk >= payload.Length;
                var flags = last ? DeviceProtocol.FlagFinal : (byte)0;
                var frame = DeviceProtocol.Upload(transferTicket, payload.AsSpan(off, count).ToArray(), flags);
                var resp = await _channel.RequestAsync(frame, ct);
                var (op, _, body) = DeviceProtocol.Unwrap(resp);
                if (op == DeviceProtocol.OpError)
                    return TargetResult.Fail(Encoding.UTF8.GetString(body));
            }
            return TargetResult.Ok();
        }
        catch (Exception ex)
        {
            return TargetResult.Fail("Upload falló: " + ex.Message);
        }
    }

    public async Task<TargetResult> VerifyAsync(string transferTicket, Checksum expected, CancellationToken ct = default)
    {
        try
        {
            var resp = await _channel.RequestAsync(DeviceProtocol.Verify(transferTicket, expected), ct);
            var (op, _, payload) = DeviceProtocol.Unwrap(resp);
            if (op == DeviceProtocol.OpError)
                return TargetResult.Fail(Encoding.UTF8.GetString(payload));
            return TargetResult.Ok();
        }
        catch (Exception ex)
        {
            return TargetResult.Fail("Verify falló: " + ex.Message);
        }
    }

    public async Task<TargetResult> ActivateAsync(string transferTicket, CancellationToken ct = default)
    {
        try
        {
            var resp = await _channel.RequestAsync(DeviceProtocol.Activate(transferTicket), ct);
            var (op, _, payload) = DeviceProtocol.Unwrap(resp);
            if (op == DeviceProtocol.OpError)
                return TargetResult.Fail(Encoding.UTF8.GetString(payload));
            _status = DeviceStatus.Online;
            return TargetResult.Ok();
        }
        catch (Exception ex)
        {
            _status = DeviceStatus.Error;
            return TargetResult.Fail("Activate falló: " + ex.Message);
        }
    }

    public async Task<TargetResult> StopAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _channel.RequestAsync(DeviceProtocol.Wrap(DeviceProtocol.OpStop, 0, Array.Empty<byte>()), ct);
            var (op, _, payload) = DeviceProtocol.Unwrap(resp);
            if (op == DeviceProtocol.OpError)
                return TargetResult.Fail(Encoding.UTF8.GetString(payload));
            _status = DeviceStatus.Online;
            return TargetResult.Ok();
        }
        catch (Exception ex)
        {
            return TargetResult.Fail("Stop falló: " + ex.Message);
        }
    }

    public async Task<TargetResult<DeviceStatus>> GetStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _channel.RequestAsync(DeviceProtocol.Wrap(DeviceProtocol.OpStatus, 0, Array.Empty<byte>()), ct);
            var (op, _, payload) = DeviceProtocol.Unwrap(resp);
            if (op == DeviceProtocol.OpError)
                return TargetResult<DeviceStatus>.Fail(Encoding.UTF8.GetString(payload));
            var status = JsonSerializer.Deserialize<DeviceStatus>(Encoding.UTF8.GetString(payload));
            _status = status;
            return TargetResult<DeviceStatus>.Ok(_status);
        }
        catch (Exception ex)
        {
            return TargetResult<DeviceStatus>.Fail("Status falló: " + ex.Message);
        }
    }
}

/// <summary>Dispositivo vía Wi-Fi/LAN (mismo protocolo, canal Ethernet).</summary>
public sealed class LanDisplayTarget : ChannelDisplayTarget
{
    public LanDisplayTarget(IDeviceChannel channel, DeviceId? id = null) : base(channel, id) { }
}

/// <summary>Dispositivo vía USB/Serial (mismo protocolo, canal serie).</summary>
public sealed class SerialDisplayTarget : ChannelDisplayTarget
{
    public SerialDisplayTarget(IDeviceChannel channel, DeviceId? id = null) : base(channel, id) { }
}