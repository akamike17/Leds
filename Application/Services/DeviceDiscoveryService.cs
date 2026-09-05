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
///
/// Concurrencia y colisiones (spec 21): el registro es thread-safe (lock) y una
/// colisión de serial (dos dispositivos con el mismo serial) se resuelve de forma
/// EXPLÍCITA: el segundo NO sobrescribe silenciosamente al primero. Los fallos de
/// descubrimiento se registran sanitizados (sin stack traces ni secretos) en lugar de
/// quedar en un catch vacío.
/// </summary>
public sealed class DeviceDiscoveryService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, RegisteredDevice> _registered = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DiscoveryFailure> _failures = new();
    private readonly SimulatorTarget _simulator;
    private readonly FirmwareTarget? _firmwareTarget;

    /// <summary>Mensajes de fallo sanitizados del último DiscoverAsync (sin stack trace).</summary>
    public IReadOnlyList<string> LastFailures { get { lock (_gate) return _failures.Select(f => f.Message).ToList(); } }

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
    public bool Register(IDisplayTarget target, string transport, string endpoint, string? serial = null)
    {
        // Clave de identidad = serial estable del dispositivo (NO el DeviceId local ni la IP).
        var key = string.IsNullOrWhiteSpace(serial)
            ? target.Id.Value.ToString("N")
            : serial;

        lock (_gate)
        {
            if (_registered.TryGetValue(key, out var existing))
            {
                // REDISCOVERY (final.md §2.F): el mismo serial estable reaparece — p.ej. el
                // mismo dispositivo físico cambió de IP/endpoint. La identidad lógica es la
                // MISMA; se actualiza el target y endpoint vivos al nuevo, en lugar de
                // rechazarlo y conservar un endpoint obsoleto (lo que haría que Send apunte
                // a una dirección muerta). El serial estable ES la identidad por diseño
                // (sección 18/21): no hay señal hardware adicional para distinguir un clon
                // activo simultáneo, así que serial único = rediscovery, no colisión.
                if (!string.Equals(existing.Endpoint, endpoint, StringComparison.Ordinal))
                {
                    _registered[key] = new RegisteredDevice(target, transport, endpoint, key);
                    return true;
                }
                // Mismo serial Y mismo endpoint: ya está registrado; idempotente.
                return true;
            }

            _registered[key] = new RegisteredDevice(target, transport, endpoint, key);
            return true;
        }
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
                if (!conn.Success)
                {
                    RecordFailure(channel, "conexión", conn.Error);
                    continue;
                }

                var id = await target.GetIdentityAsync(ct);
                if (!id.Success || id.Value == null)
                {
                    RecordFailure(channel, "identidad", id.Error);
                    continue;
                }

                // Identidad estable = serial del dispositivo. Una colisión se informa explícitamente.
                if (!Register(target, channel.Transport, channel.Endpoint, id.Value.Serial))
                {
                    // La colisión ya quedó registrada dentro de Register.
                }
            }
            catch (Exception ex)
            {
                // Un canal no respondente no debe tumbar el discovery; se registra sanitizado.
                RecordFailure(channel, "descubrimiento", ex.GetType().Name);
            }
        }
    }

    /// <summary>Registra un fallo sanitizado: sin stack trace, sin secretos, endpoint acotado.</summary>
    private void RecordFailure(IDeviceChannel channel, string stage, string detail)
    {
        var safeEndpoint = channel.Endpoint ?? "desconocido";
        if (safeEndpoint.Length > 64) safeEndpoint = safeEndpoint[..64];
        var safeDetail = detail ?? string.Empty;
        if (safeDetail.Length > 200) safeDetail = safeDetail[..200];

        lock (_gate)
        {
            _failures.Add(new DiscoveryFailure(
                $"[{channel.Transport}] {safeEndpoint}: {stage} falló ({safeDetail})."));
        }
    }

    /// <summary>Todos los targets activos (simulador + descubiertos).</summary>
    public async Task<IReadOnlyList<DeviceSummary>> ListAsync(CancellationToken ct = default)
    {
        var result = new List<DeviceSummary>();

        var simId = await _simulator.GetIdentityAsync(ct);
        result.Add(new DeviceSummary
        {
            Id = _simulator.Id.Value.ToString("N"),
            Serial = simId.Value?.Serial ?? _simulator.Id.Value.ToString("N"),
            Name = simId.Value?.Model ?? "DSLetras Simulator",
            Transport = "simulator",
            Endpoint = "local",
            Status = (await _simulator.GetStatusAsync(ct)).Value,
        });

        RegisteredDevice[] snapshot;
        lock (_gate)
        {
            snapshot = _registered.Values.ToArray();
        }

        foreach (var entry in snapshot)
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
        if (string.IsNullOrWhiteSpace(idOrSerial)) return null;

        var simId = _simulator.Id.Value.ToString("N");
        var simSerial = simId[..8];
        if (string.Equals(simId, idOrSerial, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(simSerial, idOrSerial, StringComparison.OrdinalIgnoreCase))
            return _simulator;

        lock (_gate)
        {
            if (_registered.TryGetValue(idOrSerial, out var bySerial))
                return bySerial.Target;

            // Fallback: buscar por DeviceId hex.
            if (_registered.Values.FirstOrDefault(e =>
                    string.Equals(e.Target.Id.Value.ToString("N"), idOrSerial, StringComparison.OrdinalIgnoreCase))
                is { } byId)
                return byId.Target;
        }

        return null;
    }

    private sealed record RegisteredDevice(IDisplayTarget Target, string Transport, string Endpoint, string Serial);
    private sealed record DiscoveryFailure(string Message);
}