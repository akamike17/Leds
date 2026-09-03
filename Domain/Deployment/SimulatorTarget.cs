using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;

namespace DSLetreros.Domain.Deployment;

/// <summary>
/// Implementación en memoria de IDisplayTarget: cumple el MISMO contrato (y la MISMA
/// máquina de estados transaccional) que el hardware. Comparte la semántica exacta del
/// <see cref="Firmware"/> (lado dispositivo) para que SimulatorTarget y
/// Firmware/FirmwareTarget sean verificables con los mismos contract tests.
///
/// Reglas de la máquina de estados (spec 18/21):
///  * SÓLO una transferencia activa a la vez.
///  * Prepare rechaza tamaño &lt;= 0 y guarda el tamaño esperado en el staging.
///  * Upload exige un ticket preparado previamente; el tamaño real del paquete debe
///    coincidir con el esperado.
///  * Verify exige que Upload ya se haya completado en el mismo ticket.
///  * Activate exige que Verify haya tenido éxito en el mismo ticket (rechaza sin Verify).
///  * Activar atómico: la escena previa se conserva como LastKnownGood (invariante 10).
/// </summary>
public sealed class SimulatorTarget : IDisplayTarget
{
    private readonly DeviceCapabilities _capabilities;
    private readonly DeviceIdentity _identity;
    private readonly object _gate = new();

    private readonly Dictionary<string, StagedScene> _staging = new();
    private ScenePackage? _active;
    private ScenePackage? _lastKnownGood;
    private DeviceStatus _status = DeviceStatus.Unknown;

    /// <summary>Una transferencia activa a la vez (spec 21). Ticket actual, o null.</summary>
    private string? _activeTransferTicket;

    public SimulatorTarget(
        DeviceId? id = null,
        int width = 64, int height = 32,
        IReadOnlyList<AnimationKind>? supportedAnimations = null)
    {
        Id = id ?? DeviceId.New();
        _identity = new DeviceIdentity
        {
            Serial = Id.Value.ToString("N")[..8],
            Model = "DSLetras Simulator",
            FirmwareVersion = "1.0.0",
            ProtocolVersion = 1,
        };
        _capabilities = new DeviceCapabilities
        {
            LogicalWidth = width,
            LogicalHeight = height,
            ColorCapability = ColorCapability.Rgb24,
            MaxSceneBytes = 8 * 1024 * 1024,
            MaxAssetBytes = 4 * 1024 * 1024,
            SupportedAnimations = supportedAnimations?.ToList() ?? Enum.GetValues<AnimationKind>().ToList(),
            ProtocolVersion = 1,
            AutonomousPlayback = true,
        };
    }

    public DeviceId Id { get; }

    /// <summary>Activa en el simulador; usado por tests para leer la escena reproducida.</summary>
    public ScenePackage? Active { get { lock (_gate) return _active; } }
    public ScenePackage? LastKnownGood { get { lock (_gate) return _lastKnownGood; } }

    /// <summary>Snapshot de staging (ticket → si está verificado). Sólo los paquetes subidos.</summary>
    public IReadOnlyDictionary<string, ScenePackage> Staging
    {
        get
        {
            lock (_gate)
            {
                return _staging
                    .Where(kv => kv.Value.Package != null)
                    .ToDictionary(kv => kv.Key, kv => kv.Value.Package!);
            }
        }
    }

    public Task<TargetResult> ConnectAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_active == null && _lastKnownGood != null)
            {
                // Safe boot: restaura la última buena si no hay escena activa.
                _active = _lastKnownGood;
                _lastKnownGood = null;
            }
            _status = DeviceStatus.Online;
        }
        return Task.FromResult(TargetResult.Ok());
    }

    public Task<TargetResult<DeviceIdentity>> GetIdentityAsync(CancellationToken ct = default)
        => Task.FromResult(TargetResult<DeviceIdentity>.Ok(_identity));

    public Task<TargetResult<DeviceCapabilities>> GetCapabilitiesAsync(CancellationToken ct = default)
        => Task.FromResult(TargetResult<DeviceCapabilities>.Ok(_capabilities));

    public Task<TargetResult<string>> PrepareTransferAsync(long sceneBytes, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_activeTransferTicket != null)
                return Task.FromResult(TargetResult<string>.Fail("Ya hay una transferencia en curso."));

            if (sceneBytes <= 0)
                return Task.FromResult(TargetResult<string>.Fail("Tamaño de escena inválido."));

            if (sceneBytes > _capabilities.MaxSceneBytes)
                return Task.FromResult(TargetResult<string>.Fail("Escena excede MaxSceneBytes."));

            var ticket = Guid.NewGuid().ToString("N");
            _staging[ticket] = new StagedScene
            {
                ExpectedBytes = sceneBytes,
                ReceivedAt = DateTimeOffset.UtcNow,
            };
            _activeTransferTicket = ticket;
            return Task.FromResult(TargetResult<string>.Ok(ticket));
        }
    }

    public Task<TargetResult> UploadAsync(string transferTicket, ScenePackage package, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_staging.TryGetValue(transferTicket, out var staged))
                return Task.FromResult(TargetResult.Fail("Ticket de transferencia desconocido."));

            // El tamaño REAL del paquete debe coincidir con el esperado guardado en Prepare.
            if (package.EstimatedBytes != staged.ExpectedBytes)
                return Task.FromResult(TargetResult.Fail(
                    $"Tamaño del paquete ({package.EstimatedBytes}B) no coincide con el esperado ({staged.ExpectedBytes}B)."));

            staged.Package = package;
            staged.ReceivedAt = DateTimeOffset.UtcNow;
            _staging[transferTicket] = staged;
            return Task.FromResult(TargetResult.Ok());
        }
    }

    public Task<TargetResult> VerifyAsync(string transferTicket, Checksum expected, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_staging.TryGetValue(transferTicket, out var staged) || staged.Package == null)
                return Task.FromResult(TargetResult.Fail("Sin paquete en staging para verificar."));

            var actual = staged.Package.ComputeChecksum();
            if (!actual.Equals(expected))
                return Task.FromResult(TargetResult.Fail("Checksum no coincide."));

            staged.Verified = true;
            staged.ExpectedChecksum = expected;
            _staging[transferTicket] = staged;
            return Task.FromResult(TargetResult.Ok());
        }
    }

    public Task<TargetResult> ActivateAsync(string transferTicket, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_staging.TryGetValue(transferTicket, out var staged) || staged.Package == null)
                return Task.FromResult(TargetResult.Fail("Sin paquete verificado para activar."));

            // Rechaza Activate sin Verify previo correcto.
            if (!staged.Verified)
                return Task.FromResult(TargetResult.Fail("El paquete no fue verificado (checksum)."));

            // Atómico: si ya había activo, se conserva como LastKnownGood.
            if (_active != null)
                _lastKnownGood = _active;
            _active = staged.Package;
            _status = DeviceStatus.Online;
            _staging.Remove(transferTicket);
            _activeTransferTicket = null;
            return Task.FromResult(TargetResult.Ok());
        }
    }

    public Task<TargetResult> StopAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            _status = DeviceStatus.Online;
        }
        return Task.FromResult(TargetResult.Ok());
    }

    public Task<TargetResult<DeviceStatus>> GetStatusAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(TargetResult<DeviceStatus>.Ok(_status));
        }
    }

    private sealed class StagedScene
    {
        public long ExpectedBytes { get; set; }
        public ScenePackage? Package { get; set; }
        public Checksum ExpectedChecksum { get; set; }
        public bool Verified { get; set; }
        public DateTimeOffset ReceivedAt { get; set; }
    }
}