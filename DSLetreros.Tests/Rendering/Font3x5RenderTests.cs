using DSLetreros.Domain.Entities;
using DSLetreros.Domain.Rendering;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Rendering;

/// <summary>
/// Paridad de fuente 3x5 (RFLED/final.md §15/§35): el renderer C# debe producir
/// un framebuffer distinto (más compacto) para FontId="3x5" que para "5x7", y el
/// resultado debe ser determinista (misma entrada -> mismos píxeles, invariante 4).
/// </summary>
public class Font3x5RenderTests
{
    private static TextObject Text(string fontId) => new()
    {
        Name = "T", Text = "MG",
        FontId = fontId, Color = RgbColor.White,
        Position = new PixelPoint(0, 0),
        Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)),
    };

    [Fact]
    public void Render_3x5_is_more_compact_than_5x7()
    {
        var scene5 = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(5) };
        scene5.Layers.Add(new Layer { Name = "L", Order = 0 });
        scene5.Layers[0].Objects.Add(Text("5x7"));
        var fb5 = SceneRenderer.Render(scene5, TimeSpan.Zero, new CanvasDefinition(32, 16));

        var scene3 = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(5) };
        scene3.Layers.Add(new Layer { Name = "L", Order = 0 });
        scene3.Layers[0].Objects.Add(Text("3x5"));
        var fb3 = SceneRenderer.Render(scene3, TimeSpan.Zero, new CanvasDefinition(32, 16));

        int lit5 = fb5.AllPixels().Count(c => c != RgbColor.Black);
        int lit3 = fb3.AllPixels().Count(c => c != RgbColor.Black);
        Assert.True(lit5 > 0);
        Assert.True(lit3 > 0);
        Assert.True(lit3 < lit5, $"3x5 ({lit3}) debería encender menos píxeles que 5x7 ({lit5})");
    }

    [Fact]
    public void Render_3x5_is_deterministic()
    {
        var scene = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(5) };
        scene.Layers.Add(new Layer { Name = "L", Order = 0 });
        scene.Layers[0].Objects.Add(Text("3x5"));
        var a = SceneRenderer.Render(scene, TimeSpan.Zero, new CanvasDefinition(32, 16));
        var b = SceneRenderer.Render(scene, TimeSpan.Zero, new CanvasDefinition(32, 16));
        Assert.Equal(a.AllPixels(), b.AllPixels());
    }

    // ----- final.md §2.A/B: ningún carácter soportado desaparece en silencio -----

    [Theory]
    [InlineData("MIGUEL")]
    [InlineData("WWW")]
    [InlineData("MW")]
    [InlineData("mañana")]
    [InlineData("áéíóúñ")]
    [InlineData("ÁÉÍÓÚÑ")]
    [InlineData("$100")]
    [InlineData("HOLA")]
    public void Render_3x5_never_loses_characters_silently(string text)
    {
        // Cada carácter del texto debe producir al menos un píxel encendido en su
        // columna de avance (fallback 5x7 para M/W/minúsculas/acentos), NO un hueco.
        var scene = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(5) };
        scene.Layers.Add(new Layer { Name = "L", Order = 0 });
        var obj = Text("3x5");
        obj.Text = text;
        scene.Layers[0].Objects.Add(obj);

        var fb = SceneRenderer.Render(scene, TimeSpan.Zero, new CanvasDefinition(128, 32));

        // Cada carácter distinto de espacio debe encender píxeles en su propia franja.
        int curX = 0;
        foreach (var ch in text)
        {
            bool hasGlyph = Font3x5.HasGlyph(ch);
            int glyphWidth = hasGlyph ? Font3x5.Width : Font5x7.Width;
            if (ch != ' ')
            {
                bool anyLit = false;
                for (int col = curX; col < curX + glyphWidth && col < 128; col++)
                    for (int row = 0; row < 32; row++)
                        if (fb.GetPixel(col, row) != RgbColor.Black) { anyLit = true; break; }
                Assert.True(anyLit, $"El carácter '{ch}' de \"{text}\" desapareció en silencio (sin píxeles en [{curX},{curX + glyphWidth})).");
            }
            curX += hasGlyph ? (Font3x5.Width + Font3x5.Spacing) : (Font5x7.Width + Font5x7.Spacing);
        }
    }

    [Fact]
    public void Render_3x5_fallback_glyph_matches_5x7_glyph_exactly()
    {
        // Paridad: el glifo fallback de un carácter sin glifo 3x5 (p.ej. 'M') debe ser
        // píxel-idéntico al glifo 5x7 nativo del mismo carácter en la misma posición.
        var scene5 = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(5) };
        scene5.Layers.Add(new Layer { Name = "L", Order = 0 });
        var t5 = Text("5x7");
        t5.Text = "M";
        scene5.Layers[0].Objects.Add(t5);
        var fb5 = SceneRenderer.Render(scene5, TimeSpan.Zero, new CanvasDefinition(8, 8));

        var scene3m = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(5) };
        scene3m.Layers.Add(new Layer { Name = "L", Order = 0 });
        var t3m = Text("3x5");
        t3m.Text = "M";
        scene3m.Layers[0].Objects.Add(t3m);
        var fb3m = SceneRenderer.Render(scene3m, TimeSpan.Zero, new CanvasDefinition(8, 8));

        Assert.Equal(fb5.AllPixels(), fb3m.AllPixels());
    }
}