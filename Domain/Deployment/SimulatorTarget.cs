using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;

namespace DSLetreros.Domain.Deployment;

/// <summary>
/// Implementación en memoria de IDisplayTarget: cumple el MISMO contrato que el
/// hardware (usado por tests contract y por el simulador de la app). Mantiene
/// staging temporal, verificación por checksum y activación atómica con
/// LastKnownGoodScene (invariante 10).
/// </summary>
public sealed class SimulatorTarget : IDisplayTarget
{
    private readonly DeviceCapabilities _capabilities;
    private DeviceIdentity _identity;
    private DeviceStatus _status = DeviceStatus.Unknown;

    private readonly Dictionary<string, ScenePackage> _staging = new();
    private ScenePackage? _active;
    private ScenePackage? _lastKnownGood;

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
    public ScenePackage? Active => _active;
    public ScenePackage? LastKnownGood => _lastKnownGood;
    public IReadOnlyDictionary<string, ScenePackage> Staging => _staging;

    public Task<TargetResult> ConnectAsync(CancellationToken ct = default)
    {
        _status = DeviceStatus.Online;
        return Task.FromResult(TargetResult.Ok());
    }

    public Task<TargetResult<DeviceIdentity>> GetIdentityAsync(CancellationToken ct = default)
        => Task.FromResult(TargetResult<DeviceIdentity>.Ok(_identity));

    public Task<TargetResult<DeviceCapabilities>> GetCapabilitiesAsync(CancellationToken ct = default)
        => Task.FromResult(TargetResult<DeviceCapabilities>.Ok(_capabilities));

    public Task<TargetResult<string>> PrepareTransferAsync(long sceneBytes, CancellationToken ct = default)
    {
        if (sceneBytes > _capabilities.MaxSceneBytes)
            return Task.FromResult(TargetResult<string>.Fail("Escena excede MaxSceneBytes."));
        var ticket = Guid.NewGuid().ToString("N");
        _staging[ticket] = null!; // reservado hasta Upload
        return Task.FromResult(TargetResult<string>.Ok(ticket));
    }

    public Task<TargetResult> UploadAsync(string transferTicket, ScenePackage package, CancellationToken ct = default)
    {
        if (!_staging.ContainsKey(transferTicket))
            return Task.FromResult(TargetResult.Fail("Ticket de transferencia desconocido."));
        _staging[transferTicket] = package;
        return Task.FromResult(TargetResult.Ok());
    }

    public Task<TargetResult> VerifyAsync(string transferTicket, Checksum expected, CancellationToken ct = default)
    {
        if (!_staging.TryGetValue(transferTicket, out var pkg) || pkg == null)
            return Task.FromResult(TargetResult.Fail("Sin paquete en staging para verificar."));
        var actual = pkg.ComputeChecksum();
        if (!actual.Equals(expected))
            return Task.FromResult(TargetResult.Fail("Checksum no coincide."));
        return Task.FromResult(TargetResult.Ok());
    }

    public Task<TargetResult> ActivateAsync(string transferTicket, CancellationToken ct = default)
    {
        if (!_staging.TryGetValue(transferTicket, out var pkg) || pkg == null)
            return Task.FromResult(TargetResult.Fail("Sin paquete verificado para activar."));
        // Atómico: si ya había activo, se conserva como LastKnownGood.
        if (_active != null)
            _lastKnownGood = _active;
        _active = pkg;
        _status = DeviceStatus.Online;
        _staging.Remove(transferTicket);
        return Task.FromResult(TargetResult.Ok());
    }

    public Task<TargetResult> StopAsync(CancellationToken ct = default)
    {
        _status = DeviceStatus.Online;
        return Task.FromResult(TargetResult.Ok());
    }

    public Task<TargetResult<DeviceStatus>> GetStatusAsync(CancellationToken ct = default)
        => Task.FromResult(TargetResult<DeviceStatus>.Ok(_status));
}