using DSLetreros.Domain.Deployment;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.Rendering;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Rendering;

/// <summary>
/// Golden exacto de timing aritmético y guards de capacidades (spec 20.6):
/// offset temporal con Start no-cero, signos de Slide Right/Down, wrap de Marquee,
/// reverse de Exit, y guards `> 0` de compiler con valor 0.
/// </summary>
public class TimingArithmeticGoldenTests
{
    private static Scene Scene(SceneObject o, TimeSpan? dur = null)
    {
        var s = new Scene { Name = "S", Duration = dur ?? TimeSpan.FromSeconds(10) };
        var l = new Layer { Name = "L", Order = 0 };
        l.Objects.Add(o);
        s.Layers.Add(l);
        return s;
    }

    private static readonly CanvasDefinition C = new(24, 24);

    // ---- Offset temporal con Start != 0 (mata t - timing.Start vs t + timing.Start) ----
    [Fact]
    public void Object_with_nonzero_start_uses_local_time()
    {
        var o = new TextObject
        {
            Name = "T", Text = "A", Color = RgbColor.White, Position = new PixelPoint(0, 0),
            Timing = new TimeRange(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)),
        };
        o.Animations.Add(new AnimationDefinition { Kind = AnimationKind.Pulse, SpeedPreset = AnimationSpeedPreset.Normal, Slot = AnimationSlot.Main });
        var s = Scene(o);

        // t=5s → local=0 → Pulse phase 0 → brillo 1.0 → blanco
        var t5 = SceneRenderer.Render(s, TimeSpan.FromSeconds(5), C);
        Assert.Equal(RgbColor.White, t5.GetPixel(2, 0));

        // t=5.5s → local=500ms → phase 0.5 → brillo 0 → negro
        var t5500 = SceneRenderer.Render(s, TimeSpan.FromMilliseconds(5500), C);
        Assert.Equal(RgbColor.Black, t5500.GetPixel(2, 0));

        // en t=2s (antes del start), el objeto no debe verse
        var t2 = SceneRenderer.Render(s, TimeSpan.FromSeconds(2), C);
        Assert.Equal(RgbColor.Black, t2.GetPixel(2, 0));
    }

    // ---- Slide Right / Down: signo de offset (mata -amount vs +amount) ----
    [Fact]
    public void Slide_right_has_negative_x_offset()
    {
        var o = new ShapeObject
        {
            Shape = ShapeKind.Rectangle, FillColor = RgbColor.White,
            Position = new PixelPoint(0, 0), Size = new PixelSize(4, 1),
            Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)),
        };
        o.Animations.Add(new AnimationDefinition { Kind = AnimationKind.Slide, Direction = AnimationDirection.Right, SpeedPreset = AnimationSpeedPreset.Normal, Slot = AnimationSlot.Main });
        // progress 0.5 → amount 2; Right → offset.X = -2
        var st = AnimationEvaluator.Evaluate(o, TimeSpan.FromMilliseconds(500));
        Assert.Equal(-2, st.Offset.X);
    }

    [Fact]
    public void Slide_down_has_negative_y_offset()
    {
        var o = new ShapeObject
        {
            Shape = ShapeKind.Rectangle, FillColor = RgbColor.White,
            Position = new PixelPoint(0, 0), Size = new PixelSize(4, 4),
            Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)),
        };
        o.Animations.Add(new AnimationDefinition { Kind = AnimationKind.Slide, Direction = AnimationDirection.Down, SpeedPreset = AnimationSpeedPreset.Normal, Slot = AnimationSlot.Main });
        var st = AnimationEvaluator.Evaluate(o, TimeSpan.FromMilliseconds(500));
        // Slide usa Size.Width para el monto: progress 0.5 * Width 4 = 2; Down → offset.Y = -2
        Assert.Equal(-2, st.Offset.Y);
    }

    // ---- Marquee wrap pasado el ciclo (mata local.Ticks % cycle vs *) ----
    [Fact]
    public void Marquee_wraps_after_full_cycle()
    {
        var o = new TextObject
        {
            Name = "T", Text = "A", Color = RgbColor.White, Position = new PixelPoint(0, 0), Size = new PixelSize(8, 7),
            Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)),
        };
        o.Animations.Add(new AnimationDefinition { Kind = AnimationKind.Marquee, Direction = AnimationDirection.Left, SpeedPreset = AnimationSpeedPreset.Slow, Slot = AnimationSlot.Main });

        // Slow cycle = 2000ms. t=0 → progress 0 → off 0 → offset.X 0.
        var s0 = AnimationEvaluator.Evaluate(o, TimeSpan.Zero);
        // t = 2000ms → progress 0 (wrap) → offset 0 igual que t=0
        var sWrap = AnimationEvaluator.Evaluate(o, TimeSpan.FromMilliseconds(2000));
        Assert.Equal(s0.Offset.X, sWrap.Offset.X);
    }

    // ---- Slide Exit invierte progreso (mata 1.0 - progress vs 1.0 + progress) ----
    [Fact]
    public void Slide_exit_reverses_progress()
    {
        var o = new ShapeObject
        {
            Shape = ShapeKind.Rectangle, FillColor = RgbColor.White,
            Position = new PixelPoint(0, 0), Size = new PixelSize(8, 1),
            Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)),
        };
        o.Animations.Add(new AnimationDefinition { Kind = AnimationKind.Slide, Direction = AnimationDirection.Left, SpeedPreset = AnimationSpeedPreset.Normal, Slot = AnimationSlot.Exit });
        // exit a t=250ms: progress = 1 - 0.25 = 0.75 → amount = round(0.75*8)=6
        var st = AnimationEvaluator.Evaluate(o, TimeSpan.FromMilliseconds(250));
        Assert.Equal(6, st.Offset.X);
    }

    // ---- SceneCompiler guards con valor 0 (mata > 0 vs >= 0 / < 0) ----
    [Fact]
    public void Compile_target_zero_dimensions_skip_dimension_check()
    {
        var scene = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(2) };
        var l = new Layer { Name = "L", Order = 0 };
        l.Objects.Add(new TextObject { Text = "A", Color = RgbColor.White, Position = new PixelPoint(0, 0), Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(2)) });
        scene.Layers.Add(l);

        // LogicalWidth/Height = 0 → guard `> 0` es falso → no valida dimensiones.
        var caps = new DeviceCapabilities { LogicalWidth = 0, LogicalHeight = 0, SupportedAnimations = Enum.GetValues<AnimationKind>().ToList() };
        var (pkg, err) = SceneCompiler.CompileForTarget(scene, new CanvasDefinition(16, 8), caps);
        Assert.NotNull(pkg);
        Assert.Null(err);
    }

    [Fact]
    public void Compile_target_zero_max_bytes_skips_size_check()
    {
        var scene = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(2) };
        var l = new Layer { Name = "L", Order = 0 };
        l.Objects.Add(new TextObject { Text = "A", Color = RgbColor.White, Position = new PixelPoint(0, 0), Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(2)) });
        scene.Layers.Add(l);

        var caps = new DeviceCapabilities { LogicalWidth = 32, LogicalHeight = 16, MaxSceneBytes = 0, SupportedAnimations = Enum.GetValues<AnimationKind>().ToList() };
        var (pkg, err) = SceneCompiler.CompileForTarget(scene, new CanvasDefinition(16, 8), caps);
        Assert.NotNull(pkg);
        Assert.Null(err);
    }
}