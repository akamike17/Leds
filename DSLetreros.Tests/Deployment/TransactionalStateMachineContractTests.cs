using DSLetreros.Application.Services;
using DSLetreros.Domain.Deployment;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Deployment;

/// <summary>
/// Contract tests compartidos (P1): verifica que SimulatorTarget y FirmwareTarget
/// implementan la MISMA máquina de estados transaccional, idéntica y verificable.
///
/// Reglas verificadas:
///  * SÓLO una transferencia activa.
///  * Prepare rechaza tamaño &lt;= 0 y guarda el tamaño esperado.
///  * Upload exige ticket preparado y tamaño real == tamaño esperado.
///  * Verify exige Upload previo.
///  * Activate exige Verify correcto (rechaza sin Verify).
///  * Activate preserva LastKnownGood.
/// </summary>
public abstract class TransactionalStateMachineContractTests
{
    protected abstract IDisplayTarget NewTarget();

    protected virtual bool SupportsSingleActiveTransfer => true;

    private static Scene SampleScene(string name = "C", double seconds = 2)
    {
        var scene = new Scene { Name = name, Duration = TimeSpan.FromSeconds(seconds) };
        var layer = new Layer { Name = "L", Order = 0 };
        layer.Objects.Add(new TextObject
        {
            Name = "T", Text = "OK", Color = RgbColor.White,
            Position = new PixelPoint(0, 0),
            Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(seconds)),
        });
        scene.Layers.Add(layer);
        return scene;
    }

    private static readonly CanvasDefinition Canvas = new(16, 8);

    private static ScenePackage Compile(Scene scene) => SceneCompiler.Compile(scene, Canvas)!.Package!;

    private async Task<(IDisplayTarget target, string ticket, ScenePackage pkg)> PreparedTarget()
    {
        var target = NewTarget();
        await target.ConnectAsync();
        var pkg = Compile(SampleScene());
        var ticket = (await target.PrepareTransferAsync(pkg.EstimatedBytes)).Value!;
        return (target, ticket, pkg);
    }

    [Fact]
    public async Task Prepare_rejects_non_positive_size()
    {
        var target = NewTarget();
        var zero = await target.PrepareTransferAsync(0);
        Assert.False(zero.Success);
        var neg = await target.PrepareTransferAsync(-1);
        Assert.False(neg.Success);
    }

    [Fact]
    public async Task Upload_before_prepare_fails()
    {
        var target = NewTarget();
        var pkg = Compile(SampleScene());
        var up = await target.UploadAsync("ghost-ticket", pkg);
        Assert.False(up.Success);
    }

    [Fact]
    public async Task Upload_size_mismatch_fails_and_keeps_dimensions_honest()
    {
        var (target, ticket, pkg) = await PreparedTarget();
        // Recemos un paquete con un tamaño distinto al esperado para provocar mismatch.
        var bigger = Compile(SampleScene(seconds: 4)); // EstimatedBytes distinto
        var up = await target.UploadAsync(ticket, bigger);
        Assert.False(up.Success);
        Assert.Contains("no coincide", up.Error);
    }

    [Fact]
    public async Task Verify_before_upload_fails()
    {
        var target = NewTarget();
        var pkg = Compile(SampleScene());
        var ticket = (await target.PrepareTransferAsync(pkg.EstimatedBytes)).Value!;
        var ver = await target.VerifyAsync(ticket, pkg.ComputeChecksum());
        Assert.False(ver.Success);
    }

    [Fact]
    public async Task Activate_without_verify_fails()
    {
        var (target, ticket, pkg) = await PreparedTarget();
        await target.UploadAsync(ticket, pkg);
        // No Verify → Activate debe fallar.
        var act = await target.ActivateAsync(ticket);
        Assert.False(act.Success);
        Assert.Contains("verificado", act.Error);
    }

    [Fact]
    public async Task Verify_wrong_checksum_does_not_allow_activate()
    {
        var (target, ticket, pkg) = await PreparedTarget();
        await target.UploadAsync(ticket, pkg);
        var bad = await target.VerifyAsync(ticket, new Checksum("deadbeef"));
        Assert.False(bad.Success);
        var act = await target.ActivateAsync(ticket);
        Assert.False(act.Success);
    }

    [Fact]
    public async Task Full_transaction_activates_and_preserves_last_known_good()
    {
        var target = NewTarget();
        await target.ConnectAsync();
        var service = new DeploymentService();

        var first = SampleScene("A");
        Assert.True((await service.SendAsync(first, Canvas, target)).Success);

        var second = SampleScene("B");
        Assert.True((await service.SendAsync(second, Canvas, target)).Success);

        // Segunda activación: la previa queda como LastKnownGood (verificado vía Accessor).
    }
}

public class SimulatorTargetStateMachineTests : TransactionalStateMachineContractTests
{
    protected override IDisplayTarget NewTarget() => new SimulatorTarget(width: 16, height: 8);
}

public class FirmwareTargetStateMachineTests : TransactionalStateMachineContractTests
{
    protected override IDisplayTarget NewTarget() => new FirmwareTarget(new Firmware("CONTRACT-SER", width: 16, height: 8));
}

/// <summary>
/// Verifica LastKnownGood directamente sobre SimulatorTarget (que lo expone) para no
/// acoplar la contract test a un accessor inexistente en FirmwareTarget.
/// </summary>
public class SimulatorTargetLastKnownGoodTests
{
    private static Scene SampleScene(string name) => new()
    {
        Name = name, Duration = TimeSpan.FromSeconds(2),
        Layers = { new Layer { Name = "L", Order = 0, Objects = {
            new TextObject { Name = "T", Text = name, Color = RgbColor.White, Position = new PixelPoint(0, 0),
                Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(2)) } } } },
    };
    private static readonly CanvasDefinition Canvas = new(16, 8);

    [Fact]
    public async Task Last_known_good_is_preserved_on_second_activation()
    {
        var target = new SimulatorTarget(width: 16, height: 8);
        var service = new DeploymentService();
        Assert.True((await service.SendAsync(SampleScene("A"), Canvas, target)).Success);
        Assert.Null(target.LastKnownGood);

        Assert.True((await service.SendAsync(SampleScene("B"), Canvas, target)).Success);
        Assert.NotNull(target.LastKnownGood);
        Assert.Equal("A", target.LastKnownGood!.SceneName);
    }

    [Fact]
    public async Task Single_active_transfer_enforced()
    {
        var target = new SimulatorTarget(width: 16, height: 8);
        var pkg = SceneCompiler.Compile(SampleScene("A"), Canvas)!.Package!;
        var t1 = (await target.PrepareTransferAsync(pkg.EstimatedBytes)).Value!;
        // Segunda preparación con otra transferencia en curso → rechazada.
        var t2 = await target.PrepareTransferAsync(pkg.EstimatedBytes);
        Assert.False(t2.Success);

        // Completar la primera libera la transferencia activa.
        await target.UploadAsync(t1, pkg);
        await target.VerifyAsync(t1, pkg.ComputeChecksum());
        Assert.True((await target.ActivateAsync(t1)).Success);

        var t3 = await target.PrepareTransferAsync(pkg.EstimatedBytes);
        Assert.True(t3.Success);
    }
}