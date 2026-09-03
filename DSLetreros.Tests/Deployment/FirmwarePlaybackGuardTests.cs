using DSLetreros.Domain.Deployment;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Deployment;

/// <summary>
/// Última ronda: demuestra el comportamiento OBSERVABLE de las guardas de
/// Firmware.PlaybackTick ante tiempo/intervalo no finito (NaN/Infinity) y negativo.
/// Estos valores nunca se probaban, por eso los mutantes de igualdad/lógica de esas
/// guardas sobrevivían. Los tests asertan el resultado exacto (Ok + Frame).
/// </summary>
public class FirmwarePlaybackGuardTests
{
    private const string Serial = "SER-GUARD";

    private static Scene SampleScene(double seconds = 1)
    {
        var scene = new Scene { Name = "G", Duration = TimeSpan.FromSeconds(seconds) };
        var layer = new Layer { Name = "L", Order = 0 };
        layer.Objects.Add(new TextObject
        {
            Name = "T", Text = "X", Color = RgbColor.White,
            Position = new PixelPoint(0, 0),
            Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(seconds)),
        });
        scene.Layers.Add(layer);
        return scene;
    }
    private static readonly CanvasDefinition Canvas = new(16, 8);

    // Construye un Firmware con una escena activa (vía FirmwareTarget + pipeline).
    private static Firmware NewActiveFirmware()
    {
        var fw = new Firmware(Serial, width: 16, height: 8);
        var pkg = SceneCompiler.Compile(SampleScene(1), Canvas)!.Package!;
        var target = new FirmwareTarget(fw);
        var t = target.PrepareTransferAsync(pkg.EstimatedBytes).GetAwaiter().GetResult().Value!;
        target.UploadAsync(t, pkg).GetAwaiter().GetResult();
        target.VerifyAsync(t, pkg.ComputeChecksum()).GetAwaiter().GetResult();
        target.ActivateAsync(t).GetAwaiter().GetResult();
        return fw;
    }

    [Fact]
    public void PlaybackTick_nan_time_is_rejected()
    {
        var fw = NewActiveFirmware();
        var (ok, err, frame) = fw.PlaybackTick(double.NaN);
        Assert.False(ok);
        Assert.Null(frame);
        Assert.NotNull(err);
    }

    [Fact]
    public void PlaybackTick_positive_infinity_time_is_rejected()
    {
        var fw = NewActiveFirmware();
        var (ok, _, frame) = fw.PlaybackTick(double.PositiveInfinity);
        Assert.False(ok);
        Assert.Null(frame);
    }

    [Fact]
    public void PlaybackTick_negative_infinity_time_is_rejected()
    {
        var fw = NewActiveFirmware();
        var (ok, _, frame) = fw.PlaybackTick(double.NegativeInfinity);
        Assert.False(ok);
        Assert.Null(frame);
    }

    [Fact]
    public void PlaybackTick_negative_time_clamps_to_first_frame()
    {
        var fw = NewActiveFirmware();
        var (ok, _, frame) = fw.PlaybackTick(-1000);
        Assert.True(ok);
        Assert.NotNull(frame);
        Assert.Equal(0.0, frame!.TimeMs, 3); // clampa a 0 → frame 0
    }

    [Fact]
    public void PlaybackTick_zero_time_returns_first_frame_at_zero()
    {
        var fw = NewActiveFirmware();
        var (ok, err, frame) = fw.PlaybackTick(0);
        Assert.True(ok, err);
        Assert.NotNull(frame);
    }

    // FrameIntervalMs inválido (0/NaN/∞) se rechaza con Ok=false.
    [Fact]
    public void PlaybackTick_requires_valid_finite_frame_interval()
    {
        var fw = NewActiveFirmware();
        // Corrompemos el intervalo de la escena activa a 0 para forzar la guarda <= 0.
        var intervalProp = typeof(ScenePackage).GetProperty(nameof(ScenePackage.FrameIntervalMs))!;
        intervalProp.SetValue(fw.Active!, 0.0);

        var (ok, err, frame) = fw.PlaybackTick(100);
        Assert.False(ok, "intervalo 0 debe rechazarse");
        Assert.Null(frame);
        Assert.NotNull(err);
    }
}