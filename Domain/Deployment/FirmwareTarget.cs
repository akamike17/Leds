using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;

namespace DSLetreros.Domain.Deployment;

/// <summary>
/// Adaptador IDisplayTarget que delegas en un Firmware (lado dispositivo).
/// Permite que el firmware corra dentro del MISMO pipeline de DeploymentService y
/// los mismos contract tests que SimulatorTarget y los adapters de canal.
/// </summary>
public sealed class FirmwareTarget : IDisplayTarget
{
    private readonly Firmware _firmware;
    private DeviceStatus _status = DeviceStatus.Unknown;

    public FirmwareTarget(Firmware firmware, DeviceId? id = null)
    {
        _firmware = firmware;
        Id = id ?? DeviceId.New();
    }

    public DeviceId Id { get; }
    public Firmware Firmware => _firmware;

    public Task<TargetResult> ConnectAsync(CancellationToken ct = default)
    {
        _firmware.Hello();
        _status = _firmware.Status;
        return Task.FromResult(TargetResult.Ok());
    }

    public Task<TargetResult<DeviceIdentity>> GetIdentityAsync(CancellationToken ct = default)
        => Task.FromResult(TargetResult<DeviceIdentity>.Ok(_firmware.GetIdentity()));

    public Task<TargetResult<DeviceCapabilities>> GetCapabilitiesAsync(CancellationToken ct = default)
        => Task.FromResult(TargetResult<DeviceCapabilities>.Ok(_firmware.Capabilities));

    public Task<TargetResult<string>> PrepareTransferAsync(long sceneBytes, CancellationToken ct = default)
    {
        var ticket = Guid.NewGuid().ToString("N");
        var (ok, err, _) = _firmware.Prepare(ticket, sceneBytes);
        return ok
            ? Task.FromResult(TargetResult<string>.Ok(ticket))
            : Task.FromResult(TargetResult<string>.Fail(err!));
    }

    public Task<TargetResult> UploadAsync(string transferTicket, ScenePackage package, CancellationToken ct = default)
    {
        var (ok, err) = _firmware.Upload(transferTicket, package);
        return ok ? Task.FromResult(TargetResult.Ok()) : Task.FromResult(TargetResult.Fail(err!));
    }

    public Task<TargetResult> VerifyAsync(string transferTicket, Checksum expected, CancellationToken ct = default)
    {
        var (ok, err) = _firmware.Verify(transferTicket, expected);
        return ok ? Task.FromResult(TargetResult.Ok()) : Task.FromResult(TargetResult.Fail(err!));
    }

    public Task<TargetResult> ActivateAsync(string transferTicket, CancellationToken ct = default)
    {
        var (ok, err) = _firmware.Activate(transferTicket);
        _status = _firmware.Status;
        return ok ? Task.FromResult(TargetResult.Ok()) : Task.FromResult(TargetResult.Fail(err!));
    }

    public Task<TargetResult> StopAsync(CancellationToken ct = default)
    {
        _firmware.Stop();
        _status = _firmware.Status;
        return Task.FromResult(TargetResult.Ok());
    }

    public Task<TargetResult<DeviceStatus>> GetStatusAsync(CancellationToken ct = default)
        => Task.FromResult(TargetResult<DeviceStatus>.Ok(_firmware.Status));
}