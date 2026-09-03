using DSLetreros.Domain.Entities;
using DSLetreros.Domain.Rendering;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Rendering;

public class Font5x7Tests
{
    [Theory]
    [InlineData('A')]
    [InlineData('Z')]
    [InlineData('a')]
    [InlineData('z')]
    [InlineData('0')]
    [InlineData('9')]
    [InlineData('Á')]
    [InlineData('É')]
    [InlineData('Í')]
    [InlineData('Ó')]
    [InlineData('Ú')]
    [InlineData('Ü')]
    [InlineData('Ñ')]
    [InlineData('á')]
    [InlineData('é')]
    [InlineData('í')]
    [InlineData('ó')]
    [InlineData('ú')]
    [InlineData('ü')]
    [InlineData('ñ')]
    [InlineData('¿')]
    [InlineData('?')]
    [InlineData('¡')]
    [InlineData('!')]
    [InlineData('$')]
    [InlineData('%')]
    [InlineData('&')]
    [InlineData('+')]
    [InlineData('-')]
    [InlineData('/')]
    [InlineData('@')]
    [InlineData('#')]
    [InlineData('(')]
    [InlineData(')')]
    public void Glyph_exists_and_has_7_rows(char c)
    {
        Assert.True(Font5x7.HasGlyph(c), $"Falta glifo: '{c}'");
        var g = Font5x7.Get(c)!;
        Assert.Equal(Font5x7.Height, g.Length);
    }

    [Fact]
    public void Glyph_A_has_expected_top_row()
    {
        // 'A': fila 0 = "..#.." → bit 2 = 0b00100 = 4
        var g = Font5x7.Get('A')!;
        Assert.Equal(0b_00100, g[0]);
    }

    [Fact]
    public void MeasureText_HOLA_width_is_23()
    {
        // cada glyph 5 + spacing 1 = 6, menos 1 spacing final → 4*6-1 = 23
        Assert.Equal(23, Font5x7.MeasureText("HOLA"));
    }

    [Fact]
    public void MeasureText_empty_is_zero()
    {
        Assert.Equal(0, Font5x7.MeasureText(""));
    }

    [Fact]
    public void Accented_letter_differs_from_base_by_tilde()
    {
        var a = Font5x7.Get('A')!;
        var aAcute = Font5x7.Get('Á')!;
        // la tilde está en fila 0 (0b01010 = 10), distinta de la A base (fila 0 = 4)
        Assert.Equal(0b_01010, aAcute[0]);
        Assert.NotEqual(a[0], aAcute[0]);
    }
}

public class SceneRendererTests
{
    private static Scene Scene(params SceneObject[] objs)
    {
        var scene = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(5) };
        var layer = new Layer { Name = "L", Order = 0 };
        layer.Objects.AddRange(objs);
        scene.Layers.Add(layer);
        return scene;
    }

    [Fact]
    public void Empty_scene_renders_all_black()
    {
        var fb = SceneRenderer.Render(Scene(), TimeSpan.Zero, new CanvasDefinition(8, 8));
        Assert.Equal(64, fb.AllPixels().Count(p => p == RgbColor.Black));
    }

    [Fact]
    public void Text_object_renders_pixels_matching_font()
    {
        var scene = Scene(new TextObject
        {
            Name = "T",
            Text = "A",
            Position = new PixelPoint(0, 0),
            Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)),
            Color = RgbColor.White,
        });
        var fb = SceneRenderer.Render(scene, TimeSpan.FromSeconds(1), new CanvasDefinition(8, 8));

        // 'A' fila 0 = "..#.." → pixel en col 2, fila 0 iluminado
        Assert.Equal(RgbColor.White, fb.GetPixel(2, 0));
        Assert.Equal(RgbColor.Black, fb.GetPixel(0, 0));
        Assert.Equal(RgbColor.Black, fb.GetPixel(4, 0));
    }

    [Fact]
    public void Object_outside_timing_is_not_rendered()
    {
        var scene = Scene(new TextObject
        {
            Name = "T",
            Text = "X",
            Position = new PixelPoint(0, 0),
            Timing = new TimeRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)),
            Color = RgbColor.White,
        });
        var before = SceneRenderer.Render(scene, TimeSpan.FromSeconds(1), new CanvasDefinition(8, 8));
        Assert.Equal(64, before.AllPixels().Count(p => p == RgbColor.Black));

        var during = SceneRenderer.Render(scene, TimeSpan.FromSeconds(3), new CanvasDefinition(8, 8));
        Assert.True(during.AllPixels().Any(p => p == RgbColor.White));
    }

    [Fact]
    public void Hidden_object_is_not_rendered()
    {
        var scene = Scene(new TextObject
        {
            Name = "T", Text = "A", Visible = false,
            Position = new PixelPoint(0, 0), Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)),
        });
        var fb = SceneRenderer.Render(scene, TimeSpan.FromSeconds(1), new CanvasDefinition(8, 8));
        Assert.Equal(64, fb.AllPixels().Count(p => p == RgbColor.Black));
    }

    [Fact]
    public void Same_input_produces_same_pixels()
    {
        static Scene Make() => Scene(new TextObject
        {
            Name = "T", Text = "AB", Color = new RgbColor(255, 0, 0),
            Position = new PixelPoint(1, 1), Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)),
        });
        var a = SceneRenderer.Render(Make(), TimeSpan.FromSeconds(1), new CanvasDefinition(16, 8));
        var b = SceneRenderer.Render(Make(), TimeSpan.FromSeconds(1), new CanvasDefinition(16, 8));
        Assert.Equal(a.AllPixels().ToArray(), b.AllPixels().ToArray());
    }
}