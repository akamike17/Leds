using DSLetreros.Domain.Deployment;
using DSLetreros.Domain.Entities;

namespace DSLetreros.Application.Services;

/// <summary>Resumen de un target descubierto (para UI de selección).</summary>
public sealed class DeviceSummary
{
    public string Id { get; set; } = string.Empty;          // DeviceId "N"
    public string Serial { get; set; } = string.Empty;      // identidad estable (no IP)
    public string Name { get; set; } = string.Empty;        // modelo/amigable
    public string Transport { get; set; } = string.Empty;   // "simulator", "lan", "serial"
    public string Endpoint { get; set; } = string.Empty;    // diagnóstico (NO identidad)
    public DeviceStatus Status { get; set; } = DeviceStatus.Unknown;
    public bool Online => Status == DeviceStatus.Online;
}

/// <summary>
/// Discovery + registro de targets (spec sección 18, slice 9).
///
/// Claves de identidad por SERIAL estable (no por IP ni endpoint): el mismo
/// dispositivo físico reaparece con la misma identidad aunque cambie de IP/puerto.
/// El simulador siempre está registrado; los transports LAN/USB/Serial se descubren
/// y registran bajo el MISMO contrato IDisplayTarget.
/// </summary>
public sealed class DeviceDiscoveryService
{
    private readonly Dictionary<string, RegisteredDevice> _registered = new(StringComparer.OrdinalIgnoreCase);
    private readonly SimulatorTarget _simulator;
    private readonly FirmwareTarget? _firmwareTarget;

    public DeviceDiscoveryService(SimulatorTarget simulator, Firmware? firmware = null)
    {
        _simulator = simulator;
        if (firmware != null)
        {
            _firmwareTarget = new FirmwareTarget(firmware);
            // El firmware se registra por su serial estable.
            Register(_firmwareTarget, "firmware", "embedded", firmware.Identity.Serial);
        }
    }

    /// <summary>Registra el simulador local y un target descubierto por canal.</summary>
    public void Register(IDisplayTarget target, string transport, string endpoint, string? serial = null)
    {
        // Clave de identidad = serial estable del dispositivo (NO el DeviceId local ni la IP).
        // Si no se provee, se deriva del DeviceId como respaldo.
        var key = string.IsNullOrWhiteSpace(serial)
            ? target.Id.Value.ToString("N")
            : serial;
        var entry = new RegisteredDevice(target, transport, endpoint, key);
        _registered[key] = entry;
    }

    /// <summary>Descubre targets vía los transports provistos (LAN y Serial).</summary>
    public async Task DiscoverAsync(IEnumerable<IDeviceChannel> channels, CancellationToken ct = default)
    {
        foreach (var channel in channels)
        {
            try
            {
                var target = new ChannelDisplayTarget(channel);
                var conn = await target.ConnectAsync(ct);
                if (!conn.Success) continue;

                var id = await target.GetIdentityAsync(ct);
                if (id.Success && id.Value != null)
                {
                    // Identidad estable = serial del dispositivo (no el endpoint ni el DeviceId local).
                    Register(target, channel.Transport, channel.Endpoint, id.Value.Serial);
                }
            }
            catch
            {
                // Un canal no respondente no debe tumbar el discovery.
            }
        }
    }

    /// <summary>Todos los targets activos (simulador + descubiertos).</summary>
    public async Task<IReadOnlyList<DeviceSummary>> ListAsync(CancellationToken ct = default)
    {
        var result = new List<DeviceSummary>();

        var simId = await _simulator.GetIdentityAsync(ct);
        var simCaps = await _simulator.GetCapabilitiesAsync(ct);
        result.Add(new DeviceSummary
        {
            Id = _simulator.Id.Value.ToString("N"),
            Serial = simId.Value?.Serial ?? _simulator.Id.Value.ToString("N"),
            Name = simId.Value?.Model ?? "DSLetras Simulator",
            Transport = "simulator",
            Endpoint = "local",
            Status = (await _simulator.GetStatusAsync(ct)).Value,
        });

        foreach (var entry in _registered.Values)
        {
            var status = (await entry.Target.GetStatusAsync(ct)).Value;
            var identity = await entry.Target.GetIdentityAsync(ct);
            result.Add(new DeviceSummary
            {
                Id = entry.Target.Id.Value.ToString("N"),
                Serial = entry.Serial,
                Name = identity.Value?.Model ?? "Dispositivo",
                Transport = entry.Transport,
                Endpoint = entry.Endpoint,
                Status = status,
            });
        }

        return result;
    }

    /// <summary>Resuelve un target por serial (identidad estable) o por DeviceId hex.</summary>
    public IDisplayTarget? Resolve(string idOrSerial)
    {
        var simId = _simulator.Id.Value.ToString("N");
        if (string.Equals(simId, idOrSerial, StringComparison.OrdinalIgnoreCase))
            return _simulator;

        if (_registered.TryGetValue(idOrSerial, out var bySerial))
            return bySerial.Target;

        // Fallback: buscar por DeviceId hex.
        if (_registered.Values.FirstOrDefault(e =>
                string.Equals(e.Target.Id.Value.ToString("N"), idOrSerial, StringComparison.OrdinalIgnoreCase))
            is { } byId)
            return byId.Target;

        return null;
    }

    private sealed record RegisteredDevice(IDisplayTarget Target, string Transport, string Endpoint, string Serial);
}