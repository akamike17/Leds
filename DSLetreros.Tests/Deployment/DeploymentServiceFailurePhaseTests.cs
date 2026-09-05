using DSLetreros.Application.Services;
using DSLetreros.Domain.Deployment;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Deployment;

/// <summary>
/// Fallo del pipeline de despliegue (final.md §13): `DeploymentService.SendAsync` debe
/// fallar de forma limpia en CADA fase temprana (Validate / Connect / GetCapabilities /
/// Compile) sin lanzar, devolviendo DeployResult.Fail con la fase correcta. Estos son los
/// branches de fallo temprano que antes quedaban NoCoverage (sólo se probaba el camino feliz
/// y los fallos de Prepare/Upload/Verify/Activate de la state machine).
/// </summary>
public class DeploymentServiceFailurePhaseTests
{
    private static readonly CanvasDefinition Canvas = new(16, 8);

    private static Scene ValidScene()
    {
        var scene = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(2) };
        var layer = new Layer { Name = "L", Order = 0 };
        layer.Objects.Add(new TextObject
        {
            Name = "T", Text = "OK", Color = RgbColor.White, Position = new PixelPoint(0, 0),
            Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(2)),
        });
        scene.Layers.Add(layer);
        return scene;
    }

    // ---- Validate: escena sin contenido ----

    [Fact]
    public async Task Send_invalid_scene_fails_at_validate_phase()
    {
        var scene = new Scene { Name = "Vacía", Duration = TimeSpan.FromSeconds(2) };
        // Sin capas con objetos → Validate falla ("no tiene contenido visible").
        var target = new SimulatorTarget(width: 16, height: 8);
        var service = new DeploymentService();

        var r = await service.SendAsync(scene, Canvas, target);
        Assert.False(r.Success);
        Assert.Equal("Validate", r.Phase);
    }

    [Fact]
    public async Task Send_zero_duration_scene_fails_at_validate_phase()
    {
        var scene = ValidScene();
        scene.Duration = TimeSpan.Zero;
        var target = new SimulatorTarget(width: 16, height: 8);
        var service = new DeploymentService();

        var r = await service.SendAsync(scene, Canvas, target);
        Assert.False(r.Success);
        Assert.Equal("Validate", r.Phase);
    }

    // ---- Connect / GetCapabilities: target que falla al conectar o reportar capacidades ----

    private sealed class FailingTarget : IDisplayTarget
    {
        private readonly bool _failConnect;
        private readonly bool _failCapabilities;
        public FailingTarget(bool failConnect = true, bool failCapabilities = false)
        { _failConnect = failConnect; _failCapabilities = failCapabilities; }

        public DeviceId Id => DeviceId.New();
        public Task<TargetResult> ConnectAsync(CancellationToken ct = default)
            => Task.FromResult(_failConnect ? TargetResult.Fail("refused") : TargetResult.Ok());
        public Task<TargetResult<DeviceIdentity>> GetIdentityAsync(CancellationToken ct = default)
            => Task.FromResult(TargetResult<DeviceIdentity>.Ok(new DeviceIdentity { Serial = "S" }));
        public Task<TargetResult<DeviceCapabilities>> GetCapabilitiesAsync(CancellationToken ct = default)
            => Task.FromResult(_failCapabilities
                ? TargetResult<DeviceCapabilities>.Fail("no caps")
                : TargetResult<DeviceCapabilities>.Ok(new DeviceCapabilities { LogicalWidth = 64, LogicalHeight = 32 }));
        public Task<TargetResult<string>> PrepareTransferAsync(long sceneBytes, CancellationToken ct = default)
            => Task.FromResult(TargetResult<string>.Ok("ticket"));
        public Task<TargetResult> UploadAsync(string transferTicket, ScenePackage package, CancellationToken ct = default)
            => Task.FromResult(TargetResult.Ok());
        public Task<TargetResult> VerifyAsync(string transferTicket, Checksum expected, CancellationToken ct = default)
            => Task.FromResult(TargetResult.Ok());
        public Task<TargetResult> ActivateAsync(string transferTicket, CancellationToken ct = default)
            => Task.FromResult(TargetResult.Ok());
        public Task<TargetResult> StopAsync(CancellationToken ct = default)
            => Task.FromResult(TargetResult.Ok());
        public Task<TargetResult<DeviceStatus>> GetStatusAsync(CancellationToken ct = default)
            => Task.FromResult(TargetResult<DeviceStatus>.Ok(DeviceStatus.Online));
    }

    [Fact]
    public async Task Send_fails_at_connect_phase_when_target_refuses()
    {
        var target = new FailingTarget(failConnect: true);
        var service = new DeploymentService();
        var r = await service.SendAsync(ValidScene(), Canvas, target);
        Assert.False(r.Success);
        Assert.Equal("Connect", r.Phase);
    }

    [Fact]
    public async Task Send_fails_at_getcapabilities_phase_when_caps_unavailable()
    {
        var target = new FailingTarget(failConnect: false, failCapabilities: true);
        var service = new DeploymentService();
        var r = await service.SendAsync(ValidScene(), Canvas, target);
        Assert.False(r.Success);
        Assert.Equal("GetCapabilities", r.Phase);
    }
}