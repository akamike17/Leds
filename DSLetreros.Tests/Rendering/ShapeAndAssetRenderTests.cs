using DSLetreros.Domain.Entities;
using DSLetreros.Domain.Rendering;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Rendering;

/// <summary>
/// Golden de render para formas (rect/ellipse/line relleno+borde), DrawingObject y
/// assets embebidos (icono/imagen). Cierra los huecos de cobertura de SceneRenderer
/// (DrawRect/DrawEllipse/DrawLine/DrawIndexed/RenderImage/RenderIcon) → mata mutantes
/// de branch (spec 20.6).
/// </summary>
public class ShapeAndAssetRenderTests
{
    private static Scene Scene(params SceneObject[] objs)
    {
        var s = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(5) };
        var l = new Layer { Name = "L", Order = 0 };
        l.Objects.AddRange(objs);
        s.Layers.Add(l);
        return s;
    }

    private static SceneObject WithTiming(SceneObject o) { o.Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)); return o; }

    // ---- Rectángulo ----

    [Fact]
    public void Rectangle_stroke_draws_border_only()
    {
        var rect = (ShapeObject)WithTiming(new ShapeObject
        {
            Shape = ShapeKind.Rectangle, Position = new PixelPoint(0, 0), Size = new PixelSize(4, 3),
            StrokeColor = RgbColor.White, FillColor = RgbColor.Black,
        });
        var fb = SceneRenderer.Render(Scene(rect), TimeSpan.Zero, new CanvasDefinition(8, 8));

        // bordes blancos
        Assert.Equal(RgbColor.White, fb.GetPixel(0, 0));
        Assert.Equal(RgbColor.White, fb.GetPixel(3, 0));
        Assert.Equal(RgbColor.White, fb.GetPixel(0, 2));
        // interior negro (no relleno)
        Assert.Equal(RgbColor.Black, fb.GetPixel(1, 1));
    }

    [Fact]
    public void Rectangle_filled_covers_interior()
    {
        var rect = (ShapeObject)WithTiming(new ShapeObject
        {
            Shape = ShapeKind.Rectangle, Position = new PixelPoint(0, 0), Size = new PixelSize(3, 3),
            StrokeColor = RgbColor.White, FillColor = new RgbColor(0, 0, 255),
        });
        var fb = SceneRenderer.Render(Scene(rect), TimeSpan.Zero, new CanvasDefinition(8, 8));
        // interior (1,1) azul; borde (0,0) blanco
        Assert.Equal(new RgbColor(0, 0, 255), fb.GetPixel(1, 1));
        Assert.Equal(RgbColor.White, fb.GetPixel(0, 0));
    }

    // ---- Elipse ----

    [Fact]
    public void Ellipse_filled_draws_center_and_no_corners()
    {
        var el = (ShapeObject)WithTiming(new ShapeObject
        {
            Shape = ShapeKind.Ellipse, Position = new PixelPoint(0, 0), Size = new PixelSize(5, 5),
            StrokeColor = RgbColor.White, FillColor = new RgbColor(0, 255, 0),
        });
        var fb = SceneRenderer.Render(Scene(el), TimeSpan.Zero, new CanvasDefinition(8, 8));
        // centro (2,2) dentro de la elipse
        Assert.Equal(new RgbColor(0, 255, 0), fb.GetPixel(2, 2));
        // esquina (0,0) fuera
        Assert.Equal(RgbColor.Black, fb.GetPixel(0, 0));
    }

    // ---- Línea (Bresenham) ----

    [Fact]
    public void Line_draws_diagonal()
    {
        var line = (ShapeObject)WithTiming(new ShapeObject
        {
            Shape = ShapeKind.Line, Position = new PixelPoint(0, 0), Size = new PixelSize(4, 4),
            StrokeColor = RgbColor.White,
        });
        var fb = SceneRenderer.Render(Scene(line), TimeSpan.Zero, new CanvasDefinition(8, 8));
        Assert.Equal(RgbColor.White, fb.GetPixel(0, 0));
        Assert.Equal(RgbColor.White, fb.GetPixel(3, 3));
    }

    // ---- DrawingObject (píxeles) ----

    [Fact]
    public void Drawing_object_renders_set_bits()
    {
        var d = (DrawingObject)WithTiming(new DrawingObject
        {
            Position = new PixelPoint(0, 0), Size = new PixelSize(2, 2),
            BitsPerPixel = 1, Palette = new() { RgbColor.White },
            PixelData = new byte[] { 1, 0, 0, 1 }, // diagonal
            Bounds = new PixelRect(new PixelPoint(0, 0), new PixelSize(2, 2)),
        });
        var fb = SceneRenderer.Render(Scene(d), TimeSpan.Zero, new CanvasDefinition(8, 8));
        Assert.Equal(RgbColor.White, fb.GetPixel(0, 0));
        Assert.Equal(RgbColor.Black, fb.GetPixel(1, 0));
        Assert.Equal(RgbColor.White, fb.GetPixel(1, 1));
    }

    // ---- Icono (asset embebido, tinte) ----

    private static (IconObject Icon, Dictionary<string, string> Assets) IconCase(bool tint)
    {
        // asset 2x2 indexado: índices 0 y 1
        var pixels = System.Convert.ToBase64String(new byte[] { 0, 1, 1, 0 });
        var json = $"{{\"width\":2,\"height\":2,\"pixels\":\"{pixels}\",\"palette\":[{{\"r\":255,\"g\":0,\"b\":0}},{{\"r\":0,\"g\":255,\"b\":0}}]}}";
        var assetId = AssetId.New();
        var icon = new IconObject
        {
            AssetId = assetId,
            PaletteMode = tint ? IconPaletteMode.Tint : IconPaletteMode.Original,
            Tint = new RgbColor(0, 0, 255),
            Position = new PixelPoint(0, 0), Size = new PixelSize(2, 2),
        };
        icon.Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        var assets = new Dictionary<string, string> { [assetId.Value.ToString("N")] = json };
        return (icon, assets);
    }

    [Fact]
    public void Icon_original_palette_uses_asset_colors()
    {
        var (icon, assets) = IconCase(tint: false);
        var fb = SceneRenderer.Render(Scene(icon), TimeSpan.Zero, new CanvasDefinition(8, 8), assets);
        Assert.Equal(new RgbColor(0, 255, 0), fb.GetPixel(1, 0)); // índice 1
        Assert.Equal(new RgbColor(255, 0, 0), fb.GetPixel(0, 0)); // índice 0
    }

    [Fact]
    public void Icon_tint_overrides_palette()
    {
        var (icon, assets) = IconCase(tint: true);
        var fb = SceneRenderer.Render(Scene(icon), TimeSpan.Zero, new CanvasDefinition(8, 8), assets);
        Assert.Equal(new RgbColor(0, 0, 255), fb.GetPixel(1, 0)); // tint blue para todo
    }

    [Fact]
    public void Icon_with_missing_asset_renders_nothing()
    {
        var icon = new IconObject { AssetId = AssetId.New(), Position = new PixelPoint(0, 0), Size = new PixelSize(2, 2) };
        icon.Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        var fb = SceneRenderer.Render(Scene(icon), TimeSpan.Zero, new CanvasDefinition(8, 8), new Dictionary<string, string>());
        Assert.Equal(64, fb.AllPixels().Count(p => p == RgbColor.Black));
    }
}