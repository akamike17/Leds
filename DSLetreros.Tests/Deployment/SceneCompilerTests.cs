using DSLetreros.Domain.Deployment;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.Rendering;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Deployment;

public class SceneCompilerTests
{
    private static Scene Scene()
    {
        var s = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(2) };
        var l = new Layer { Name = "L", Order = 0 };
        l.Objects.Add(new TextObject
        {
            Text = "A", Color = RgbColor.White,
            Position = new PixelPoint(0, 0),
            Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(2)),
        });
        s.Layers.Add(l);
        return s;
    }

    private static readonly CanvasDefinition Canvas = new(16, 8);

    [Fact]
    public void Compile_produces_expected_frame_count()
    {
        var (pkg, err) = SceneCompiler.Compile(Scene(), Canvas, frameIntervalMs: 1000);
        Assert.Null(err);
        Assert.Equal(2, pkg!.FrameCount); // 2s / 1000ms = 2 frames
    }

    [Fact]
    public void Compile_rejects_invalid_scene()
    {
        var s = new Scene { Name = "X", Duration = TimeSpan.Zero };
        var (pkg, err) = SceneCompiler.Compile(s, Canvas);
        Assert.Null(pkg);
        Assert.NotNull(err);
    }

    [Fact]
    public void Compile_is_deterministic()
    {
        var (a, _) = SceneCompiler.Compile(Scene(), Canvas);
        var (b, _) = SceneCompiler.Compile(Scene(), Canvas);
        Assert.Equal(a!.Checksum, b!.Checksum);
        Assert.Equal(a.Frames[0].Pixels, b.Frames[0].Pixels);
    }

    [Fact]
    public void Compile_for_target_rejects_oversized_canvas()
    {
        var caps = new DeviceCapabilities { LogicalWidth = 4, LogicalHeight = 4, SupportedAnimations = Enum.GetValues<AnimationKind>().ToList() };
        var (pkg, err) = SceneCompiler.CompileForTarget(Scene(), Canvas, caps);
        Assert.Null(pkg);
        Assert.Contains("excede", err);
    }

    [Fact]
    public void Compile_for_target_rejects_unsupported_animation()
    {
        var s = Scene();
        var obj = s.AllObjects.First();
        obj.Animations.Add(new AnimationDefinition { Kind = AnimationKind.Marquee, Slot = AnimationSlot.Main });
        var caps = new DeviceCapabilities { LogicalWidth = 32, LogicalHeight = 16, SupportedAnimations = new() { AnimationKind.Fixed } };
        var (pkg, err) = SceneCompiler.CompileForTarget(s, Canvas, caps);
        Assert.Null(pkg);
        Assert.Contains("Marquee", err);
    }

    // ---- R5 equivalence: editor render == compiled frame semantic output ----

    [Fact]
    public void Compiled_frame_equals_editor_render_at_same_time()
    {
        var s = Scene();
        var (pkg, _) = SceneCompiler.Compile(s, Canvas, frameIntervalMs: 1000);

        var t = TimeSpan.FromMilliseconds(1000);
        var editorFb = SceneRenderer.Render(s, t, Canvas);
        var frame = pkg!.Frames[1]; // t=1000ms

        // comparar pixel a pixel (frame RGB24 aplanado vs framebuffer)
        int w = Canvas.Width, h = Canvas.Height;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = (y * w + x) * 3;
            var expected = editorFb.GetPixel(x, y);
            Assert.Equal(expected.R, frame.Pixels[i]);
            Assert.Equal(expected.G, frame.Pixels[i + 1]);
            Assert.Equal(expected.B, frame.Pixels[i + 2]);
        }
    }

    // ---- Bordes de límite (spec 20.6) ----

    [Fact]
    public void Compile_frame_count_zero_scene_clamps_to_one()
    {
        // duración muy corta con intervalo grande → frameCount ceil podría ser 0; se clampa a 1
        var s = new Scene { Name = "Tiny", Duration = TimeSpan.FromMilliseconds(10) };
        var l = new Layer { Name = "L", Order = 0 };
        l.Objects.Add(new TextObject { Text = "A", Color = RgbColor.White, Position = new PixelPoint(0,0), Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromMilliseconds(10)) });
        s.Layers.Add(l);

        var (pkg, err) = SceneCompiler.Compile(s, Canvas, frameIntervalMs: 1000_000);
        Assert.NotNull(pkg);
        Assert.Null(err);
        Assert.Equal(1, pkg!.FrameCount);
    }

    [Fact]
    public void Compile_rejects_too_many_frames()
    {
        // duración larga con frameInterval pequeño → más de MaxFrames
        var s = new Scene { Name = "Huge", Duration = TimeSpan.FromSeconds(200) };
        var l = new Layer { Name = "L", Order = 0 };
        l.Objects.Add(new TextObject { Text = "A", Color = RgbColor.White, Position = new PixelPoint(0,0), Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(200)) });
        s.Layers.Add(l);

        var (pkg, err) = SceneCompiler.Compile(s, Canvas, frameIntervalMs: 1.0);
        Assert.Null(pkg);
        Assert.Contains("Demasiados frames", err);
    }

    [Fact]
    public void Compile_for_target_rejects_oversized_package_bytes()
    {
        // Un canvas grande → EstimatedBytes supera MaxSceneBytes pequeño.
        var caps = new DeviceCapabilities
        {
            LogicalWidth = 256, LogicalHeight = 256, MaxSceneBytes = 1, // 1 byte max
            SupportedAnimations = Enum.GetValues<AnimationKind>().ToList(),
        };
        var big = new CanvasDefinition(128, 128);
        var (pkg, err) = SceneCompiler.CompileForTarget(Scene(), big, caps);
        Assert.Null(pkg);
        Assert.Contains("MaxSceneBytes", err);
    }

    [Fact]
    public void Compile_for_target_zero_annotations_skips_animation_check()
    {
        // SupportedAnimations vacío → no se valida animación (guard Count > 0).
        var caps = new DeviceCapabilities { LogicalWidth = 32, LogicalHeight = 16, SupportedAnimations = new() };
        var (pkg, err) = SceneCompiler.CompileForTarget(Scene(), Canvas, caps);
        Assert.NotNull(pkg);
        Assert.Null(err);
    }
}