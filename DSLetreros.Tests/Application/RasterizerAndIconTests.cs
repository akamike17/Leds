using DSLetreros.Application.Services;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.Rendering;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Application;

public class ImageRasterizerTests
{
    private static byte[] SolidRgba(int w, int h, byte r, byte g, byte b)
    {
        var buf = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            buf[i * 4] = r; buf[i * 4 + 1] = g; buf[i * 4 + 2] = b; buf[i * 4 + 3] = 255;
        }
        return buf;
    }

    [Fact]
    public void Rasterize_solid_image_single_color()
    {
        var rgba = SolidRgba(4, 4, 255, 0, 0);
        var result = ImageRasterizer.Rasterize(rgba, 4, 4, 4, 4, dither: false);
        Assert.True(result.Success, result.Message);
        Assert.Equal(4, result.Width);
        Assert.Equal(4, result.Height);
        Assert.Single(result.Palette);           // un solo color
        Assert.Equal(new RgbColor(255, 0, 0), result.Palette[0]);
        Assert.All(result.Indices, i => Assert.Equal((byte)0, i));
    }

    [Fact]
    public void Rasterize_scales_down_nearest_neighbor()
    {
        // 4x4 -> 2x2
        var rgba = SolidRgba(4, 4, 0, 255, 0);
        var result = ImageRasterizer.Rasterize(rgba, 4, 4, 2, 2, dither: false);
        Assert.True(result.Success);
        Assert.Equal(2, result.Width);
        Assert.Equal(2, result.Height);
    }

    [Fact]
    public void Rasterize_rejects_invalid_buffer()
    {
        var result = ImageRasterizer.Rasterize(new byte[] { 1, 2, 3 }, 4, 4, 4, 4);
        Assert.False(result.Success);
    }

    [Fact]
    public void BuildPalette_dedups_and_caps_colors()
    {
        // imagen con 2 colores en franjas
        int w = 8, h = 2;
        var rgba = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            bool left = (i % w) < 4;
            rgba[i * 4] = left ? (byte)255 : (byte)0;
            rgba[i * 4 + 1] = 0;
            rgba[i * 4 + 2] = left ? (byte)0 : (byte)255;
            rgba[i * 4 + 3] = 255;
        }
        var result = ImageRasterizer.Rasterize(rgba, w, h, w, h, dither: false, maxColors: 16);
        Assert.Equal(2, result.Palette.Count);
    }
}

public class IconRenderTests
{
    private static string IconJson(int w, int h, byte[] pixels, RgbColor color)
    {
        // asset monocromo: índice 0 = transparente (negro), 1 = color
        var pixelB64 = Convert.ToBase64String(pixels);
        var palette = $"[{{\"r\":0,\"g\":0,\"b\":0}},{{\"r\":{color.R},\"g\":{color.G},\"b\":{color.B}}}]";
        return $"{{\"width\":{w},\"height\":{h},\"pixels\":\"{pixelB64}\",\"palette\":{palette}}}";
    }

    [Fact]
    public void Icon_renders_from_embedded_asset()
    {
        var project = new Project { Name = "P", Canvas = new CanvasDefinition(8, 8) };
        var scene = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(5) };
        var layer = new Layer { Name = "L", Order = 0 };
        var icon = new IconObject
        {
            Name = "Corazón",
            AssetId = AssetId.New(),
            Position = new PixelPoint(0, 0),
            Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)),
        };
        layer.Objects.Add(icon);
        scene.Layers.Add(layer);
        project.Scenes.Add(scene);

        // asset 2x2 con píxeles [1,0,0,1] → diagonal
        var assetJson = IconJson(2, 2, new byte[] { 1, 0, 0, 1 }, RgbColor.White);
        project.EmbeddedAssets[icon.AssetId!.Value.ToString("N")] = assetJson;

        var fb = SceneRenderer.Render(scene, TimeSpan.FromSeconds(1), project.Canvas, project.EmbeddedAssets);
        Assert.Equal(RgbColor.White, fb.GetPixel(0, 0));
        Assert.Equal(RgbColor.White, fb.GetPixel(1, 1));
        Assert.Equal(RgbColor.Black, fb.GetPixel(0, 1));
        Assert.Equal(RgbColor.Black, fb.GetPixel(1, 0));
    }
}