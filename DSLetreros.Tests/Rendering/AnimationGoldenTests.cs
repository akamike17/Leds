using DSLetreros.Domain.Entities;
using DSLetreros.Domain.Rendering;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Rendering;

/// <summary>
/// Golden temporal: matrices exactas + determinismo en instantes concretos
/// para cada AnimationKind y para timing (sección 20.3).
/// </summary>
public class AnimationGoldenTests
{
    private static Scene Scene(SceneObject obj, TimeSpan? duration = null)
    {
        var scene = new Scene { Name = "S", Duration = duration ?? TimeSpan.FromSeconds(5) };
        var layer = new Layer { Name = "L", Order = 0 };
        layer.Objects.Add(obj);
        scene.Layers.Add(layer);
        return scene;
    }

    private static TextObject Text(string text, PixelPoint? pos = null) => new()
    {
        Name = "T",
        Text = text,
        Position = pos ?? new PixelPoint(0, 0),
        Color = RgbColor.White,
        Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)),
    };

    // ---- Timing ----

    [Fact]
    public void Object_before_start_and_after_end_is_invisible()
    {
        var obj = Text("A");
        obj.Timing = new TimeRange(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3));
        var scene = Scene(obj);

        var before = SceneRenderer.Render(scene, TimeSpan.FromMilliseconds(999), new CanvasDefinition(8, 8));
        Assert.All(before.AllPixels(), p => Assert.Equal(RgbColor.Black, p));

        var after = SceneRenderer.Render(scene, TimeSpan.FromSeconds(3), new CanvasDefinition(8, 8));
        Assert.All(after.AllPixels(), p => Assert.Equal(RgbColor.Black, p));

        var during = SceneRenderer.Render(scene, TimeSpan.FromSeconds(2), new CanvasDefinition(8, 8));
        Assert.Contains(RgbColor.White, during.AllPixels());
    }

    // ---- Blink ----

    [Fact]
    public void Blink_toggles_visibility_within_cycle()
    {
        var obj = Text("A");
        obj.Animations.Add(new AnimationDefinition
        {
            Kind = AnimationKind.Blink,
            SpeedPreset = AnimationSpeedPreset.Normal, // ciclo 1000ms, on 0..500
            Slot = AnimationSlot.Main,
        });
        var scene = Scene(obj);

        var on = SceneRenderer.Render(scene, TimeSpan.FromMilliseconds(100), new CanvasDefinition(8, 8));
        Assert.Contains(RgbColor.White, on.AllPixels());

        var off = SceneRenderer.Render(scene, TimeSpan.FromMilliseconds(600), new CanvasDefinition(8, 8));
        Assert.All(off.AllPixels(), p => Assert.Equal(RgbColor.Black, p));

        var onAgain = SceneRenderer.Render(scene, TimeSpan.FromMilliseconds(1100), new CanvasDefinition(8, 8));
        Assert.Contains(RgbColor.White, onAgain.AllPixels());
    }

    // ---- Slide ----

    [Fact]
    public void Slide_shifts_object_horizontally_over_time()
    {
        var obj = Text("A");
        obj.Position = new PixelPoint(4, 0);
        obj.Animations.Add(new AnimationDefinition
        {
            Kind = AnimationKind.Slide,
            Direction = AnimationDirection.Left,
            SpeedPreset = AnimationSpeedPreset.Normal, // 1000ms para cubrir Size.Width
            Slot = AnimationSlot.Main,
        });
        var scene = Scene(obj);

        // A t=250ms, offset = round(0.25 * width=0) → el TextObject tiene Size por defecto (0,0)
        // Usamos un objeto con tamaño conocido: Shape 4 px de ancho.
        var shape = new ShapeObject
        {
            Name = "S", Shape = ShapeKind.Rectangle, FillColor = RgbColor.White,
            Position = new PixelPoint(4, 0), Size = new PixelSize(4, 1),
            Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)),
        };
        shape.Animations.Add(new AnimationDefinition
        {
            Kind = AnimationKind.Slide, Direction = AnimationDirection.Left,
            SpeedPreset = AnimationSpeedPreset.Normal, Slot = AnimationSlot.Main,
        });
        var scene2 = Scene(shape);

        var t0 = SceneRenderer.Render(scene2, TimeSpan.Zero, new CanvasDefinition(16, 4));
        Assert.Equal(RgbColor.White, t0.GetPixel(4, 0)); // sin slide aún

        var tHalf = SceneRenderer.Render(scene2, TimeSpan.FromMilliseconds(500), new CanvasDefinition(16, 4));
        // offset = round(0.5*4)=2 → x=4+2=6
        Assert.Equal(RgbColor.White, tHalf.GetPixel(6, 0));
        Assert.Equal(RgbColor.Black, tHalf.GetPixel(4, 0)); // ya se movió

        var tFull = SceneRenderer.Render(scene2, TimeSpan.FromMilliseconds(1000), new CanvasDefinition(16, 4));
        // offset = round(1.0*4)=4 → fuera de su celda original (x=8)
        Assert.Equal(RgbColor.White, tFull.GetPixel(8, 0));
    }

    // ---- Marquee ----

    [Fact]
    public void Marquee_scrolls_text_across_viewport()
    {
        var obj = Text("A", new PixelPoint(0, 0));
        obj.Size = new PixelSize(8, 1); // tamaño efectivo para envoltura
        obj.Animations.Add(new AnimationDefinition
        {
            Kind = AnimationKind.Marquee,
            Direction = AnimationDirection.Left,
            SpeedPreset = AnimationSpeedPreset.Slow, // 2000ms
            Slot = AnimationSlot.Main,
        });
        var scene = Scene(obj);

        var t0 = SceneRenderer.Render(scene, TimeSpan.Zero, new CanvasDefinition(8, 8));
        Assert.Equal(RgbColor.White, t0.GetPixel(2, 0)); // 'A' fila 0 = col 2 iluminada

        var tLate = SceneRenderer.Render(scene, TimeSpan.FromMilliseconds(1200), new CanvasDefinition(8, 8));
        // progress=0.6 → offset=round(0.6*(8+8))=10 → x=-10, 'A' fuera del viewport 8px
        Assert.Equal(RgbColor.Black, tLate.GetPixel(2, 0));
    }

    // ---- Pulse ----

    [Fact]
    public void Pulse_changes_brightness_but_keeps_object_visible()
    {
        var obj = Text("A");
        obj.Animations.Add(new AnimationDefinition
        {
            Kind = AnimationKind.Pulse,
            SpeedPreset = AnimationSpeedPreset.Normal,
            Slot = AnimationSlot.Main,
        });
        var scene = Scene(obj);

        var peak = SceneRenderer.Render(scene, TimeSpan.Zero, new CanvasDefinition(8, 8)); // cos(0)=1 → máximo
        var mid = SceneRenderer.Render(scene, TimeSpan.FromMilliseconds(500), new CanvasDefinition(8, 8)); // cos(pi)=-1 → 0
        var trough = SceneRenderer.Render(scene, TimeSpan.FromMilliseconds(250), new CanvasDefinition(8, 8)); // cos(pi/2)=0 → 0.5

        // pico = blanco puro
        Assert.Equal(RgbColor.White, peak.GetPixel(2, 0));
        // valle = apagado (factor 0 → negro)
        Assert.Equal(RgbColor.Black, mid.GetPixel(2, 0));
        // 250ms: factor 0.5 → gris ~128
        Assert.Equal(new RgbColor(128, 128, 128), trough.GetPixel(2, 0));
    }

    // ---- Wipe ----

    [Fact]
    public void Wipe_reveals_partial_object_over_time()
    {
        var shape = new ShapeObject
        {
            Shape = ShapeKind.Rectangle, FillColor = RgbColor.White,
            Position = new PixelPoint(0, 0), Size = new PixelSize(4, 1),
            Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)),
        };
        shape.Animations.Add(new AnimationDefinition
        {
            Kind = AnimationKind.Wipe, Direction = AnimationDirection.Left,
            SpeedPreset = AnimationSpeedPreset.Normal, Slot = AnimationSlot.Main,
        });
        var scene = Scene(shape);

        var t0 = SceneRenderer.Render(scene, TimeSpan.Zero, new CanvasDefinition(8, 4));
        Assert.Equal(RgbColor.Black, t0.GetPixel(0, 0)); // progress 0 → nada revelado

        var tHalf = SceneRenderer.Render(scene, TimeSpan.FromMilliseconds(500), new CanvasDefinition(8, 4));
        // clip = round(4*0.5)=2 px → cols 0,1 reveladas; 2,3 ocultas
        Assert.Equal(RgbColor.White, tHalf.GetPixel(0, 0));
        Assert.Equal(RgbColor.White, tHalf.GetPixel(1, 0));
        Assert.Equal(RgbColor.Black, tHalf.GetPixel(2, 0));

        var tFull = SceneRenderer.Render(scene, TimeSpan.FromMilliseconds(1000), new CanvasDefinition(8, 4));
        Assert.Equal(RgbColor.White, tFull.GetPixel(3, 0)); // todo revelado
    }

    // ---- Frame ----

    [Fact]
    public void Frame_animation_alternates_by_discrete_steps()
    {
        var obj = Text("A");
        obj.Animations.Add(new AnimationDefinition
        {
            Kind = AnimationKind.Frame,
            SpeedPreset = AnimationSpeedPreset.Normal,
            Slot = AnimationSlot.Main,
        });
        var scene = Scene(obj);

        var f0 = SceneRenderer.Render(scene, TimeSpan.Zero, new CanvasDefinition(8, 8));
        Assert.Contains(RgbColor.White, f0.AllPixels());

        var f1 = SceneRenderer.Render(scene, TimeSpan.FromMilliseconds(125), new CanvasDefinition(8, 8));
        Assert.All(f1.AllPixels(), p => Assert.Equal(RgbColor.Black, p));

        var f2 = SceneRenderer.Render(scene, TimeSpan.FromMilliseconds(250), new CanvasDefinition(8, 8));
        Assert.Contains(RgbColor.White, f2.AllPixels());
    }

    // ---- Determinismo: misma entrada = mismos píxeles ----

    [Theory]
    [InlineData(AnimationKind.Blink)]
    [InlineData(AnimationKind.Pulse)]
    [InlineData(AnimationKind.Wipe)]
    [InlineData(AnimationKind.Frame)]
    public void Same_time_renders_identical_pixels(AnimationKind kind)
    {
        static Scene Make(AnimationKind k) {
            var o = new TextObject
            {
                Name = "T", Text = "AB", Color = new RgbColor(0, 255, 0),
                Position = new PixelPoint(1, 1),
                Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)),
            };
            o.Animations.Add(new AnimationDefinition { Kind = k, SpeedPreset = AnimationSpeedPreset.Normal, Slot = AnimationSlot.Main });
            var s = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(5) };
            var l = new Layer { Name = "L", Order = 0 };
            l.Objects.Add(o);
            s.Layers.Add(l);
            return s;
        }

        var t = TimeSpan.FromMilliseconds(333);
        var a = SceneRenderer.Render(Make(kind), t, new CanvasDefinition(16, 8));
        var b = SceneRenderer.Render(Make(kind), t, new CanvasDefinition(16, 8));
        Assert.Equal(a.AllPixels().ToArray(), b.AllPixels().ToArray());
    }
}