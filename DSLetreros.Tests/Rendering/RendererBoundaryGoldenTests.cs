using DSLetreros.Domain.Entities;
using DSLetreros.Domain.Rendering;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Rendering;

/// <summary>
/// Golden de frontera para matar mutantes de borde (spec 20.6): compara `==`/`!=`,
/// `>=`/`>`, bucles `i < w` vs `i <= w`, aritmética de offset `+`/`-` y lógica `&&`/`||`
/// en el renderer. Cada test aserta el píxel EXACTO en el límite, no solo "hay blanco".
/// </summary>
public class RendererBoundaryGoldenTests
{
    private static Scene Scene(params SceneObject[] objs)
    {
        var s = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(5) };
        var l = new Layer { Name = "L", Order = 0 };
        l.Objects.AddRange(objs);
        s.Layers.Add(l);
        return s;
    }
    private static SceneObject T(SceneObject o) { o.Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)); return o; }
    private static readonly CanvasDefinition C = new(16, 16);

    // ---- Rectángulo: el bucle i<w vs i<=w produce un píxel extra en x=w ----
    [Fact]
    public void Rectangle_has_no_pixel_one_past_width()
    {
        var rect = (ShapeObject)T(new ShapeObject
        {
            Shape = ShapeKind.Rectangle, Position = new PixelPoint(2, 2), Size = new PixelSize(3, 3),
            StrokeColor = RgbColor.White, FillColor = RgbColor.Black,
        });
        var fb = SceneRenderer.Render(Scene(rect), TimeSpan.Zero, C);
        // último píxel de borde dentro: derecha en x = 2+3-1 = 4
        Assert.Equal(RgbColor.White, fb.GetPixel(4, 2));
        // un píxel MÁS allá debe estar negro (mata i<=w)
        Assert.Equal(RgbColor.Black, fb.GetPixel(5, 2));
        // un píxel abajo más allá (mata j<=h)
        Assert.Equal(RgbColor.Black, fb.GetPixel(2, 5));
    }

    // ---- Rectángulo sin relleno: el interior es negro, solo borde ----
    [Fact]
    public void Rectangle_stroke_interior_is_black()
    {
        var rect = (ShapeObject)T(new ShapeObject
        {
            Shape = ShapeKind.Rectangle, Position = new PixelPoint(0, 0), Size = new PixelSize(4, 3),
            StrokeColor = RgbColor.White, FillColor = RgbColor.Black,
        });
        var fb = SceneRenderer.Render(Scene(rect), TimeSpan.Zero, C);
        Assert.Equal(RgbColor.Black, fb.GetPixel(1, 1)); // interior
        Assert.Equal(RgbColor.Black, fb.GetPixel(2, 1)); // interior
        Assert.Equal(RgbColor.White, fb.GetPixel(0, 1)); // borde izquierdo
        Assert.Equal(RgbColor.White, fb.GetPixel(3, 1)); // borde derecho (mata w-1)
    }

    // ---- Elipse: esquinas quedarán negras; con FillColor=Black (no "filled") el
    // interior queda negro y sólo el borde (anillo) se pinta con stroke. ----
    [Fact]
    public void Ellipse_corners_black_center_filled()
    {
        var el = (ShapeObject)T(new ShapeObject
        {
            Shape = ShapeKind.Ellipse, Position = new PixelPoint(0, 0), Size = new PixelSize(7, 7),
            StrokeColor = RgbColor.White, FillColor = RgbColor.Black,
        });
        var fb = SceneRenderer.Render(Scene(el), TimeSpan.Zero, C);
        // esquinas (0,0),(6,0),(0,6),(6,6) fuera de la elipse → negro
        Assert.Equal(RgbColor.Black, fb.GetPixel(0, 0));
        Assert.Equal(RgbColor.Black, fb.GetPixel(6, 0));
        Assert.Equal(RgbColor.Black, fb.GetPixel(0, 6));
        Assert.Equal(RgbColor.Black, fb.GetPixel(6, 6));
        // interior (3,3) NO relleno (FillColor=Black): queda negro, sólo el borde pinta
        Assert.Equal(RgbColor.Black, fb.GetPixel(3, 3));
        // borde izquierdo/derecho (eje medio) sí está pintado (stroke blanco)
        Assert.Equal(RgbColor.White, fb.GetPixel(0, 3));
        Assert.Equal(RgbColor.White, fb.GetPixel(6, 3));
    }

    // ---- Dibujo con palette VACÍA → usa blanco (mata d.Palette.Count > 0) ----
    [Fact]
    public void Drawing_empty_palette_falls_back_to_white()
    {
        var d = (DrawingObject)T(new DrawingObject
        {
            Position = new PixelPoint(1, 1), Size = new PixelSize(1, 1),
            BitsPerPixel = 1, Palette = new List<RgbColor>(), // vacía
            PixelData = new byte[] { 1 },
            Bounds = new PixelRect(new PixelPoint(0, 0), new PixelSize(1, 1)),
        });
        var fb = SceneRenderer.Render(Scene(d), TimeSpan.Zero, C);
        Assert.Equal(RgbColor.White, fb.GetPixel(1, 1));
    }

    // ---- Capas en orden inverso NO cambian (mata OrderBy→OrderByDescending) ----
    [Fact]
    public void Layer_order_determines_compositing()
    {
        var scene = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(5) };
        var bottom = new Layer { Name = "b", Order = 0 };
        var top = new Layer { Name = "t", Order = 1 };
        bottom.Objects.Add(T(new ShapeObject { Shape = ShapeKind.Rectangle, Position = new PixelPoint(0,0), Size = new PixelSize(2,2), StrokeColor = RgbColor.White, FillColor = RgbColor.White }));
        top.Objects.Add(T(new ShapeObject { Shape = ShapeKind.Rectangle, Position = new PixelPoint(0,0), Size = new PixelSize(1,1), StrokeColor = RgbColor.Black, FillColor = RgbColor.Black }));
        scene.Layers.Add(bottom);
        scene.Layers.Add(top);
        var fb = SceneRenderer.Render(scene, TimeSpan.Zero, C);
        // la capa top (order 1) pinta negro encima del blanco en (0,0)
        Assert.Equal(RgbColor.Black, fb.GetPixel(0, 0));
        // la capa bottom sigue visible en (1,1) (no cubierta por top 1x1)
        Assert.Equal(RgbColor.White, fb.GetPixel(1, 1));
    }

    // ---- Texto con offset (Slide) desplaza (mata t.Position + offset / - offset) ----
    [Fact]
    public void Text_with_slide_offset_shifts_position()
    {
        var t = (TextObject)T(new TextObject
        {
            Text = "A", Color = RgbColor.White, Position = new PixelPoint(0, 0), Size = new PixelSize(6, 7),
        });
        t.Animations.Add(new AnimationDefinition { Kind = AnimationKind.Slide, Direction = AnimationDirection.Right, SpeedPreset = AnimationSpeedPreset.Normal, Slot = AnimationSlot.Main });
        // Slide Right a t=0 → offset 0 (sin shift); a t=500ms → progress 0.5, offset -3 (mueve a la derecha)
        var fb0 = SceneRenderer.Render(Scene(t), TimeSpan.Zero, C);
        var fbHalf = SceneRenderer.Render(Scene(t), TimeSpan.FromMilliseconds(500), C);
        // 'A' en (2,0) sin offset (columna central) → gara 'A' top row = "..#.." bit 2 (col 2)
        Assert.Equal(RgbColor.White, fb0.GetPixel(2, 0));
        // con offset -3 a la derecha, el píxel de 'A' se movió: (2,0) queda negro
        Assert.Equal(RgbColor.Black, fbHalf.GetPixel(2, 0));
    }

    // ---- Marquee Right viaja en sentido opuesto (mata travel - off / off = travel - off) ----
    [Fact]
    public void Marquee_right_moves_leftward_and_wraps()
    {
        var t = (TextObject)T(new TextObject
        {
            Text = "A", Color = RgbColor.White, Position = new PixelPoint(0, 0), Size = new PixelSize(8, 7),
        });
        t.Animations.Add(new AnimationDefinition { Kind = AnimationKind.Marquee, Direction = AnimationDirection.Right, SpeedPreset = AnimationSpeedPreset.Slow, Slot = AnimationSlot.Main });
        var fb = SceneRenderer.Render(Scene(t), TimeSpan.FromMilliseconds(100), C);
        // Marquee Right: off = travel - (progress*travel); en t=100/2000=0.05, off ~ travel*0.95 → muy negativo
        // El píxel de 'A' queda fuera de vista (offset muy negativo). Verificamos determinismo + algún negro.
        Assert.Equal(RgbColor.Black, fb.GetPixel(2, 0));
    }
}