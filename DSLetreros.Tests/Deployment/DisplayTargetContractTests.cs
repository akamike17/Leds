using DSLetreros.Application.Services;
using DSLetreros.Domain.Deployment;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Deployment;

/// <summary>
/// Contract tests (sección 20.4): sección completa del contrato IDisplayTarget.
/// El mismo suite corre contra SimulatorTarget y (luego) adapters de hardware.
/// </summary>
public class DisplayTargetContractTests
{
    private static Scene SampleScene()
    {
        var scene = new Scene { Name = "R1", Duration = TimeSpan.FromSeconds(2) };
        var layer = new Layer { Name = "L", Order = 0 };
        layer.Objects.Add(new TextObject
        {
            Name = "T", Text = "A", Color = RgbColor.White,
            Position = new PixelPoint(0, 0),
            Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(2)),
        });
        scene.Layers.Add(layer);
        return scene;
    }

    private static readonly CanvasDefinition Canvas = new(16, 8);

    // ---- Identity / capabilities ----

    [Fact]
    public async Task Connect_then_identity_is_stable()
    {
        var target = new SimulatorTarget();
        var conn = await target.ConnectAsync();
        Assert.True(conn.Success);

        var id = await target.GetIdentityAsync();
        Assert.True(id.Success);
        Assert.False(string.IsNullOrEmpty(id.Value!.Serial));
        Assert.Equal(1, id.Value.ProtocolVersion);
    }

    [Fact]
    public async Task Capabilities_report_positive_dimensions_and_animations()
    {
        var target = new SimulatorTarget(width: 32, height: 16);
        var caps = await target.GetCapabilitiesAsync();
        Assert.True(caps.Success);
        Assert.True(caps.Value!.LogicalWidth > 0);
        Assert.True(caps.Value.LogicalHeight > 0);
        Assert.NotEmpty(caps.Value.SupportedAnimations);
    }

    // ---- Full pipeline ----

    [Fact]
    public async Task Full_pipeline_succeeds_and_activates()
    {
        var target = new SimulatorTarget(width: 32, height: 16);
        var service = new DeploymentService();
        var result = await service.SendAsync(SampleScene(), Canvas, target);

        Assert.True(result.Success, result.Error);
        Assert.Equal("Activate", result.Phase);
        Assert.NotNull(target.Active);
        Assert.NotNull(result.Checksum);
        Assert.False(result.Checksum.Value.IsEmpty);
    }

    // ---- Prepare / upload / verify ----

    [Fact]
    public async Task Prepare_rejects_oversized_scene()
    {
        var target = new SimulatorTarget(width: 32, height: 16);
        await target.ConnectAsync();
        var prep = await target.PrepareTransferAsync(long.MaxValue);
        Assert.False(prep.Success);
    }

    [Fact]
    public async Task Verify_fails_on_checksum_mismatch()
    {
        var target = new SimulatorTarget();
        var (pkg, _) = SceneCompiler.Compile(SampleScene(), Canvas)!;
        var ticket = (await target.PrepareTransferAsync(pkg.EstimatedBytes)).Value!;
        await target.UploadAsync(ticket, pkg);

        var bogus = new Checksum("deadbeef");
        var ver = await target.VerifyAsync(ticket, bogus);
        Assert.False(ver.Success);
    }

    // ---- Activate semantics ----

    [Fact]
    public async Task Activate_without_upload_fails()
    {
        var target = new SimulatorTarget();
        var ticket = (await target.PrepareTransferAsync(1024)).Value!;
        var act = await target.ActivateAsync(ticket);
        Assert.False(act.Success);
    }

    [Fact]
    public async Task Activate_preserves_last_known_good_on_new_activation()
    {
        var target = new SimulatorTarget();
        var service = new DeploymentService();

        var first = SampleScene();
        Assert.True((await service.SendAsync(first, Canvas, target)).Success);
        Assert.NotNull(target.Active);
        Assert.Null(target.LastKnownGood); // primera activación: aún no hay previo

        var second = SampleScene();
        Assert.True((await service.SendAsync(second, Canvas, target)).Success);
        Assert.NotNull(target.Active);
        Assert.NotNull(target.LastKnownGood); // la previa quedó conservada
    }

    // ---- Stop / status ----

    [Fact]
    public async Task Stop_and_status_reflect_state()
    {
        var target = new SimulatorTarget();
        Assert.Equal(DeviceStatus.Unknown, (await target.GetStatusAsync()).Value);

        await target.ConnectAsync();
        Assert.Equal(DeviceStatus.Online, (await target.GetStatusAsync()).Value);

        var stop = await target.StopAsync();
        Assert.True(stop.Success);
        Assert.Equal(DeviceStatus.Online, (await target.GetStatusAsync()).Value);
    }
}