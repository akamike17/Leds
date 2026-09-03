using DSLetreros.Application.Services;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.Validation;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Web;

/// <summary>
/// Tests dirigidos a los caminos de negocio internos que el análisis de cobertura
/// marcó sin cubrir: el pipeline de dithering/cuantización de ImageRasterizer y
/// caminos de error específicos del ProjectValidator.
/// </summary>
public class RasterizerPipelineAndValidationGapTests
{
    // ---------- ImageRasterizer: dithering y cuantización ----------

    private static byte[] GradientRgba(int w, int h)
    {
        // degradado de muchos colores distintos para ejercer BuildPalette + dithering.
        var buf = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = (y * w + x) * 4;
            buf[i] = (byte)(x * 255 / Math.Max(1, w - 1));
            buf[i + 1] = (byte)(y * 255 / Math.Max(1, h - 1));
            buf[i + 2] = (byte)((x + y) * 255 / Math.Max(1, (w - 1) + (h - 1)));
            buf[i + 3] = 255;
        }
        return buf;
    }

    [Fact]
    public void Rasterize_with_dithering_produces_valid_palette_and_indices()
    {
        var rgba = GradientRgba(8, 8);
        var result = ImageRasterizer.Rasterize(rgba, 8, 8, 8, 8, dither: true, maxColors: 4);
        Assert.True(result.Success, result.Message);
        Assert.True(result.Palette.Count > 0);
        Assert.True(result.Palette.Count <= 4);
        // todos los índices deben estar dentro de la paleta
        Assert.All(result.Indices, i => Assert.True(i < result.Palette.Count));
    }

    [Fact]
    public void Rasterize_quantizes_down_when_many_colors_exceed_maxColors()
    {
        // > maxColors colores distintos fuerzan la rama de cuantización de 6 bits (BuildPalette).
        int w = 16, h = 16;
        var rgba = new byte[w * h * 4];
        var rnd = new Random(42);
        for (int i = 0; i < w * h; i++)
        {
            rgba[i * 4] = (byte)rnd.Next(256);
            rgba[i * 4 + 1] = (byte)rnd.Next(256);
            rgba[i * 4 + 2] = (byte)rnd.Next(256);
            rgba[i * 4 + 3] = 255;
        }
        var result = ImageRasterizer.Rasterize(rgba, w, h, w, h, dither: false, maxColors: 4);
        Assert.True(result.Success);
        Assert.True(result.Palette.Count <= 4);
        Assert.All(result.Indices, i => Assert.True(i < result.Palette.Count));
    }

    [Fact]
    public void Rasterize_upscale_nearest_neighbor_works()
    {
        var rgba = new byte[] { 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 0, 255 };
        var result = ImageRasterizer.Rasterize(rgba, 2, 2, 4, 4, dither: false);
        Assert.True(result.Success);
        Assert.Equal(4, result.Width);
        Assert.Equal(4, result.Height);
    }

    [Fact]
    public void Rasterize_insufficient_rgba_length_fails()
    {
        // 3x3 requiere 36 bytes RGBa; pasamos sólo 8.
        var result = ImageRasterizer.Rasterize(new byte[8], 3, 3, 3, 3);
        Assert.False(result.Success);
    }

    // ---------- ProjectValidator: caminos de error residuales ----------

    private static Project ValidProject() =>
        new()
        {
            Name = "P",
            Canvas = new CanvasDefinition(8, 8),
            FormatVersion = ProjectValidator.SupportedFormatVersion,
        };

    private static void AddSceneWithText(Project p)
    {
        var scene = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(5) };
        var layer = new Layer { Name = "L", Order = 0 };
        layer.Objects.Add(new TextObject { Name = "T", Text = "HOLA", Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)) });
        scene.Layers.Add(layer);
        p.Scenes.Add(scene);
    }

    [Fact]
    public void Validate_null_project_throws()
    {
        Assert.Throws<ArgumentNullException>(() => ProjectValidator.Validate(null!));
    }

    [Fact]
    public void Overlong_project_name_fails()
    {
        var p = ValidProject();
        AddSceneWithText(p);
        p.Name = new string('n', ProjectValidator.MaxNameLength + 1);
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    [Fact]
    public void Overlong_text_content_fails()
    {
        var p = ValidProject();
        AddSceneWithText(p);
        ((TextObject)p.Scenes[0].Layers[0].Objects[0]).Text = new string('a', ProjectValidator.MaxTextLength + 1);
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    [Fact]
    public void Negative_timing_start_fails()
    {
        var p = ValidProject();
        AddSceneWithText(p);
        p.Scenes[0].Layers[0].Objects[0].Timing = new TimeRange(TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(5));
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    [Fact]
    public void Too_many_scenes_fails()
    {
        var p = ValidProject();
        AddSceneWithText(p);
        for (int i = 0; i < ProjectValidator.MaxScenes; i++)
            p.Scenes.Add(new Scene { Name = $"s{i}", Duration = TimeSpan.FromSeconds(5) });
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    [Fact]
    public void Empty_asset_content_fails()
    {
        var p = ValidProject();
        AddSceneWithText(p);
        p.EmbeddedAssets["A"] = "";
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    [Fact]
    public void Dangling_icon_reference_produces_warning_not_error()
    {
        var p = ValidProject();
        var scene = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(5) };
        var layer = new Layer { Name = "L", Order = 0 };
        layer.Objects.Add(new IconObject { Name = "I", AssetId = AssetId.New(), Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)) });
        scene.Layers.Add(layer);
        p.Scenes.Add(scene);

        var result = ProjectValidator.Validate(p);
        Assert.True(result.IsValid);      // referencia colgante = warning, no error
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Duplicate_animation_slot_fails()
    {
        var p = ValidProject();
        var scene = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(5) };
        var layer = new Layer { Name = "L", Order = 0 };
        var obj = new TextObject { Name = "T", Text = "HOLA", Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)) };
        obj.Animations.Add(new AnimationDefinition { Slot = AnimationSlot.Main, Kind = AnimationKind.Blink });
        obj.Animations.Add(new AnimationDefinition { Slot = AnimationSlot.Main, Kind = AnimationKind.Pulse });
        layer.Objects.Add(obj);
        scene.Layers.Add(layer);
        p.Scenes.Add(scene);

        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    [Fact]
    public void Unsupported_bits_per_pixel_fails()
    {
        var p = ValidProject();
        var scene = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(5) };
        var layer = new Layer { Name = "L", Order = 0 };
        layer.Objects.Add(new DrawingObject
        {
            Name = "D",
            BitsPerPixel = 4,
            Size = new PixelSize(2, 2),
            PixelData = new byte[4],
            Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)),
        });
        scene.Layers.Add(layer);
        p.Scenes.Add(scene);

        Assert.False(ProjectValidator.Validate(p).IsValid);
    }
}