using DSLetreros.Application.Services;
using DSLetreros.Domain.Deployment;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.Validation;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Web;

/// <summary>
/// Tests de FRONTERA EXACTA (valor inmediatamente ANTES/EN/DESPUÉS del límite) que
/// matan los mutantes de igualdad/lógica `>=`/`>`/`<=`/`<` en las validaciones de
/// límites. Documentan el behavior exacto en el borde.
/// </summary>
public class BoundaryExactTests
{
    // ============ ImageRasterizer ============

    [Fact]
    public void Rasterize_accepts_max_target_dimension_but_rejects_one_over()
    {
        var rgba = new byte[16 * 16 * 4]; // source pequeño
        // exacto en el límite: 512x512 (MaxTargetDimension = 512)
        var atLimit = ImageRasterizer.Rasterize(rgba, 4, 4, ImageRasterizer.MaxTargetDimension, ImageRasterizer.MaxTargetDimension, dither: false);
        // 512x512 = 262144 píxeles, igual a MaxTargetPixels; válido (pero requiere buffer source >= ... )
        // El buffer source es 16*16*4=1024, no suficiente para 512x512*3; pero Rasterize sólo
        // requiere source válido, no que target sea <= source. Acotamos a dimensiones manejables:
        // probamos el boundary de DIMENSION (512) con target pequeño total que no exceda pixels.
        // Nota: MaxTargetDimension=512 y MaxTargetPixels=512*512; target 512x1 es válido.
        var atDimLimit = ImageRasterizer.Rasterize(rgba, 4, 4, 512, 1, dither: false);
        Assert.True(atDimLimit.Success, atDimLimit.Message);

        // un pixel MÁS allá de la dimensión → rechazado
        var overDim = ImageRasterizer.Rasterize(rgba, 4, 4, 513, 1, dither: false);
        Assert.False(overDim.Success);
    }

    [Theory]
    [InlineData(1)]    // min válido
    [InlineData(256)]  // max válido
    public void Rasterize_accepts_maxColors_at_boundaries(int maxColors)
    {
        var rgba = new byte[4 * 4 * 4];
        var r = ImageRasterizer.Rasterize(rgba, 4, 4, 2, 2, dither: false, maxColors: maxColors);
        Assert.True(r.Success, r.Message);
    }

    [Theory]
    [InlineData(0)]    // < min → inválido
    [InlineData(257)]  // > max → inválido
    public void Rasterize_rejects_maxColors_outside_boundaries(int maxColors)
    {
        var rgba = new byte[4 * 4 * 4];
        var r = ImageRasterizer.Rasterize(rgba, 4, 4, 2, 2, dither: false, maxColors: maxColors);
        Assert.False(r.Success);
    }

    [Theory]
    [InlineData(1, 1)]                      // min positive
    [InlineData(512, 512)]                  // exact max total pixels
    public void Rasterize_accepts_target_at_pixel_boundaries(int w, int h)
    {
        // source 1x1 es suficiente (se escala por nearest neighbor).
        var rgba = new byte[] { 255, 0, 0, 255 };
        var r = ImageRasterizer.Rasterize(rgba, 1, 1, w, h, dither: false);
        // 512x512 = 262144 == MaxTargetPixels → válido; pero requiere source, que escala ok.
        Assert.True(r.Success, r.Message);
    }

    // ============ LibraryService ============

    private static string LibraryTemp() =>
        Path.Combine(Path.GetTempPath(), "dsletras-boundary-lib-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Library_accepts_dimension_at_max_but_rejects_one_over()
    {
        var root = LibraryTemp();
        var svc = new LibraryService(root);
        try
        {
            // 512 aceptado (MaxDrawingDimension)
            var atMax = svc.SaveCustomDrawing("A", 512, 1, new byte[512]);
            Assert.True(atMax.success, atMax.message);

            // 513 rechazado
            var over = svc.SaveCustomDrawing("B", 513, 1, new byte[513]);
            Assert.False(over.success);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Library_truncates_name_at_exact_max_length()
    {
        var root = LibraryTemp();
        var svc = new LibraryService(root);
        try
        {
            // nombre exactamente en el límite (256) se conserva; mayor se trunca.
            var exact = new string('x', LibraryService.MaxNameLength);
            var ok = svc.SaveCustomDrawing(exact, 2, 2, new byte[] { 0, 1, 0, 1 });
            Assert.True(ok.success, ok.message);

            var longer = new string('y', LibraryService.MaxNameLength + 10);
            var ok2 = svc.SaveCustomDrawing(longer, 2, 2, new byte[] { 0, 1, 0, 1 });
            Assert.True(ok2.success);

            var drawings = svc.ListDrawings();
            Assert.Contains(drawings, d => d.Name.Length == LibraryService.MaxNameLength);
        }
        finally { Directory.Delete(root, true); }
    }

    // ============ ProjectValidator ============

    private static Project ValidP()
    {
        var p = new Project { Name = "P", Canvas = new CanvasDefinition(8, 8), FormatVersion = 1 };
        var s = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(5) };
        var l = new Layer { Name = "L", Order = 0 };
        l.Objects.Add(new TextObject { Name = "T", Text = "X", Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)) });
        s.Layers.Add(l);
        p.Scenes.Add(s);
        return p;
    }

    [Fact]
    public void Validator_accepts_canvas_at_max_dimension_but_rejects_one_over()
    {
        var atMax = ValidP();
        atMax.Canvas = new CanvasDefinition(ProjectValidator.MaxCanvasDimension, 1);
        Assert.True(ProjectValidator.Validate(atMax).IsValid);

        var over = ValidP();
        over.Canvas = new CanvasDefinition(ProjectValidator.MaxCanvasDimension + 1, 1);
        Assert.False(ProjectValidator.Validate(over).IsValid);
    }

    [Fact]
    public void Validator_name_at_exact_max_is_ok_but_over_fails()
    {
        var ok = ValidP();
        ok.Name = new string('n', ProjectValidator.MaxNameLength);
        Assert.True(ProjectValidator.Validate(ok).IsValid);

        var tooLong = ValidP();
        tooLong.Name = new string('n', ProjectValidator.MaxNameLength + 1);
        Assert.False(ProjectValidator.Validate(tooLong).IsValid);
    }

    [Fact]
    public void Validator_text_at_exact_max_is_ok_but_over_fails()
    {
        var ok = ValidP();
        ((TextObject)ok.Scenes[0].Layers[0].Objects[0]).Text = new string('t', ProjectValidator.MaxTextLength);
        Assert.True(ProjectValidator.Validate(ok).IsValid, string.Join("; ", ProjectValidator.Validate(ok).Errors));

        var tooLong = ValidP();
        ((TextObject)tooLong.Scenes[0].Layers[0].Objects[0]).Text = new string('t', ProjectValidator.MaxTextLength + 1);
        Assert.False(ProjectValidator.Validate(tooLong).IsValid);
    }

    // ============ SimulatorTarget (MaxSceneBytes) ============

    [Fact]
    public async Task Simulator_accepts_scene_at_max_bytes_but_rejects_one_over()
    {
        var sim = new SimulatorTarget(width: 64, height: 32);
        var caps = await sim.GetCapabilitiesAsync();
        long max = caps.Value!.MaxSceneBytes;

        // exactamente en el límite → acepta (<=)
        var atMax = await sim.PrepareTransferAsync(max);
        Assert.True(atMax.Success, atMax.Error);

        // uno MÁS → rechaza
        var over = await sim.PrepareTransferAsync(max + 1);
        Assert.False(over.Success);
    }
}