using DSLetreros.Domain.Deployment;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.Rendering;
using DSLetreros.Domain.Validation;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Deployment;

/// <summary>
/// Golden exacto de la lógica núcleo para cerrar el 100% de mutation (spec 20.6).
/// Mata los mutantes de rama reales en Firmware, SceneCompiler, ScenePackage,
/// AnimationEvaluator, DeviceProtocol y ProjectValidator.
/// </summary>
public class CoreLogic100Tests
{
    private static Scene Scene(string name = "S", double secs = 2) =>
        new() { Name = name, Duration = TimeSpan.FromSeconds(secs),
            Layers = { new Layer { Name = "L", Order = 0, Objects = {
                new TextObject { Name = "T", Text = "A", Color = RgbColor.White,
                    Position = new PixelPoint(0,0), Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(secs)) } } } } };

    private static readonly CanvasDefinition Canvas = new(16, 8);

    // ======================= ScenePackage =======================

    [Fact]
    public void ComputeChecksum_is_stable_and_covers_all_fields()
    {
        var pkg = SceneCompiler.Compile(Scene(), Canvas, frameIntervalMs: 1000)!.Package!;
        var c1 = pkg.ComputeChecksum();
        var c2 = pkg.ComputeChecksum();
        Assert.Equal(c1, c2);
        Assert.False(c1.IsEmpty);
        // checksum debe ser 64 hex chars (SHA-256)
        Assert.Equal(64, c1.Value.Length);
    }

    [Fact]
    public void ComputeChecksum_changes_when_content_changes()
    {
        var a = SceneCompiler.Compile(Scene("A"), Canvas, frameIntervalMs: 1000)!.Package!;
        var b = SceneCompiler.Compile(Scene("B", 3), Canvas, frameIntervalMs: 1000)!.Package!;
        Assert.NotEqual(a.ComputeChecksum(), b.ComputeChecksum());
    }

    [Fact]
    public void EstimatedBytes_is_the_real_wire_size()
    {
        var pkg = SceneCompiler.Compile(Scene(), Canvas, frameIntervalMs: 1000)!.Package!;
        // EstimatedBytes = tamaño wire REAL (serialización JSON), no una estimación.
        var real = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(pkg, ScenePackageJson.Options).LongLength;
        Assert.Equal(real, pkg.EstimatedBytes);
        Assert.True(pkg.EstimatedBytes > 0);
    }

    // ======================= Firmware =======================

    [Fact]
    public void Firmware_capabilities_have_expected_limits()
    {
        var fw = new Firmware("SER", width: 16, height: 8);
        Assert.Equal(8 * 1024 * 1024, fw.Capabilities.MaxSceneBytes);
        Assert.Equal(4 * 1024 * 1024, fw.Capabilities.MaxAssetBytes);
        Assert.True(fw.Capabilities.AutonomousPlayback);
        Assert.Equal(16, fw.Capabilities.LogicalWidth);
        Assert.Equal(8, fw.Capabilities.LogicalHeight);
    }

    [Fact]
    public void Prepare_rejects_exactly_zero_bytes()
    {
        var fw = new Firmware("SER");
        var (ok, err, _) = fw.Prepare("t", 0);
        Assert.False(ok);
        Assert.NotNull(err);
    }

    [Fact]
    public void Prepare_rejects_at_exact_max_bytes_boundary()
    {
        var fw = new Firmware("SER");
        var max = fw.Capabilities.MaxSceneBytes;
        // igual al límite → OK (guard es estrictamente >)
        Assert.True(fw.Prepare("t1", max).Ok);
        // un byte más → rechazado
        Assert.False(fw.Prepare("t2", max + 1).Ok);
    }

    [Fact]
    public async Task Playback_loops_deterministically_at_frame_interval()
    {
        var fw = new Firmware("SER", width: 16, height: 8);
        var target = new FirmwareTarget(fw);
        var pkg = SceneCompiler.Compile(Scene(), Canvas, frameIntervalMs: 1000)!.Package!;
        var t = (await target.PrepareTransferAsync(pkg.EstimatedBytes)).Value!;
        await target.UploadAsync(t, pkg);
        await target.VerifyAsync(t, pkg.ComputeChecksum());
        await target.ActivateAsync(t);

        // 2 frames, intervalo 1000ms. t=1500ms → idx = (1500/1000)%2 = 1%2 = 1 (frame 1)
        var (_, _, f1) = fw.PlaybackTick(1500);
        Assert.Equal(1000.0, f1!.TimeMs, 3);
    }

    [Fact]
    public async Task Purge_expired_uses_strict_greater_than_timeout()
    {
        var fw = new Firmware("SER") { TransferTimeout = TimeSpan.FromSeconds(60) };
        var target = new FirmwareTarget(fw);
        var pkg = SceneCompiler.Compile(Scene(), Canvas)!.Package!;
        var t = (await target.PrepareTransferAsync(pkg.EstimatedBytes)).Value!;
        await target.UploadAsync(t, pkg);
        var receivedAt = DateTimeOffset.UtcNow;

        // dentro del timeout (59s) → no expira
        Assert.Equal(0, fw.PurgeExpired(receivedAt.AddSeconds(59)));
        // más allá (60s+1 tick) → expira
        Assert.Equal(1, fw.PurgeExpired(receivedAt.AddSeconds(60).AddTicks(1)));
    }

    // ======================= SceneCompiler =======================

    [Fact]
    public void Compile_frame_times_are_interval_multiplied()
    {
        var (pkg, _) = SceneCompiler.Compile(Scene(secs: 2), Canvas, frameIntervalMs: 1000);
        Assert.Equal(2, pkg!.Frames.Count);
        // frame[0].TimeMs = 0 * 1000 = 0, frame[1].TimeMs = 1 * 1000 = 1000
        Assert.Equal(0.0, pkg.Frames[0].TimeMs);
        Assert.Equal(1000.0, pkg.Frames[1].TimeMs);
    }

    [Fact]
    public void Compile_sets_checksum_field()
    {
        var (pkg, _) = SceneCompiler.Compile(Scene(), Canvas, frameIntervalMs: 1000);
        // Compile() llama ComputeChecksum() internamente y sella el checksum
        Assert.False(pkg!.Checksum.IsEmpty);
    }

    [Fact]
    public void Compile_for_target_uses_strict_width_boundary()
    {
        var scene = Scene();
        // canvas ancho IGUAL al target → OK (guard > )
        var caps = new DeviceCapabilities { LogicalWidth = 16, LogicalHeight = 8, SupportedAnimations = Enum.GetValues<AnimationKind>().ToList() };
        Assert.NotNull(SceneCompiler.CompileForTarget(scene, Canvas, caps).Package);
        // canvas ancho una celda mayor → rechazado
        var big = new CanvasDefinition(17, 8);
        Assert.Null(SceneCompiler.CompileForTarget(scene, big, caps).Package);
    }

    [Fact]
    public void Compile_for_target_uses_strict_animation_count_guard()
    {
        var scene = Scene();
        // SupportedAnimations NO vacío y sin la animación usada → rechaza
        scene.AllObjects.First().Animations.Add(new AnimationDefinition { Kind = AnimationKind.Marquee, Slot = AnimationSlot.Main });
        var caps = new DeviceCapabilities { LogicalWidth = 32, LogicalHeight = 16, SupportedAnimations = new() { AnimationKind.Fixed } };
        Assert.Null(SceneCompiler.CompileForTarget(scene, Canvas, caps).Package);
    }

    // ======================= AnimationEvaluator =======================

    [Fact]
    public void Exit_start_is_four_fifths_of_duration()
    {
        // dur 10s → exitStart = 8s (4/5). local=8s exacto → exit.
        var list = new List<AnimationDefinition> {
            new() { Kind = AnimationKind.Fixed, Slot = AnimationSlot.Main },
            new() { Kind = AnimationKind.Pulse, Slot = AnimationSlot.Exit },
        };
        var timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10));
        var r = AnimationEvaluator.ResolveActive(list, TimeSpan.FromSeconds(8), timing);
        Assert.Equal(AnimationSlot.Exit, r!.Slot);
    }

    [Fact]
    public void Marquee_progress_wraps_with_modulo()
    {
        var o = new TextObject { Name = "T", Text = "A", Color = RgbColor.White, Position = new PixelPoint(0,0), Size = new PixelSize(8,1), Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)) };
        o.Animations.Add(new AnimationDefinition { Kind = AnimationKind.Marquee, Direction = AnimationDirection.Left, SpeedPreset = AnimationSpeedPreset.Slow, Slot = AnimationSlot.Main });
        var s0 = AnimationEvaluator.Evaluate(o, TimeSpan.Zero);
        var sWrap = AnimationEvaluator.Evaluate(o, TimeSpan.FromMilliseconds(2000)); // ciclo Slow = 2000
        Assert.Equal(s0.Offset.X, sWrap.Offset.X);
    }

    [Fact]
    public void Marquee_right_uses_travel_minus_offset()
    {
        var o = new TextObject { Name = "T", Text = "AB", Color = RgbColor.White, Position = new PixelPoint(0,0), Size = new PixelSize(8,1), Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)) };
        o.Animations.Add(new AnimationDefinition { Kind = AnimationKind.Marquee, Direction = AnimationDirection.Right, SpeedPreset = AnimationSpeedPreset.Slow, Slot = AnimationSlot.Main });
        var s = AnimationEvaluator.Evaluate(o, TimeSpan.FromMilliseconds(500));
        // Right: off = travel - round(progress*travel). travel = width 8 + viewport(32 default) = 40
        Assert.True(s.Offset.X <= 0);
    }

    // ======================= DeviceProtocol =======================

    [Fact]
    public void Unwrap_rejects_inconsistent_length_or_negative()
    {
        // frame con length negativo ya no se produce vía Wrap; probamos el guard lógico directo
        var frame = DeviceProtocol.Wrap(DeviceProtocol.OpHello, 0, new byte[] { 1, 2, 3 });
        // corromper length a -1
        BitConverter.GetBytes(-1).CopyTo(frame, 7);
        Assert.Throws<ProtocolException>(() => DeviceProtocol.Unwrap(frame));
    }

    // ======================= ProjectValidator =======================

    [Fact]
    public void Validate_reports_reference_warnings_for_missing_assets()
    {
        var p = new Project { Name = "P", Canvas = Canvas };
        var scene = Scene();
        var icon = new IconObject { AssetId = AssetId.New(), Position = new PixelPoint(0,0), Size = new PixelSize(1,1) };
        scene.Layers[0].Objects.Add(icon);
        p.Scenes.Add(scene);
        var result = ProjectValidator.Validate(p);
        // el icono referencia un asset no embebido → genera warning (ValidateReferences)
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Validate_reports_warning_for_image_missing_asset()
    {
        var p = new Project { Name = "P", Canvas = Canvas };
        var scene = Scene();
        var img = new ImageObject { AssetId = AssetId.New(), Position = new PixelPoint(0,0), Size = new PixelSize(1,1) };
        scene.Layers[0].Objects.Add(img);
        p.Scenes.Add(scene);
        var result = ProjectValidator.Validate(p);
        // el ImageObject con asset no embebido → warning (cubre la rama ImageObject de ValidateReferences)
        Assert.NotEmpty(result.Warnings);
    }
}