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
}