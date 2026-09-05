using DSLetreros.Application.Services;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.Rendering;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Rendering;

/// <summary>
/// 1.md tarea A — Equivalencia semántica de los refactors de overflow (Safe Mode).
/// Estos tests capturan el comportamiento EXACTO de FrameBuffer / AddDrawing /
/// EnsureCapacity ANTES del refactor, de modo que cualquier cambio de semántica
/// (pérdida de checked, cambio de excepción, cambio de límites) falle aquí.
/// El refactor sólo debe mover el throw fuera del catch manteniendo checked y límites.
/// </summary>
public class OverflowRefactorEquivalenceTests
{
    // ---- FrameBuffer ----

    [Fact]
    public void FrameBuffer_produces_overflow_datatype_not_deadlock()
    {
        // int.MaxValue * int.MaxValue = 4.6e18 cabe en long (no overflow), así que
        // el throw proviene de MaxTotalPixels, NO del catch de OverflowException.
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new FrameBuffer(int.MaxValue, int.MaxValue));
        Assert.Contains("máximo", ex.Message); // mensaje de límite de píxeles
    }

    [Fact]
    public void FrameBuffer_accepts_exact_max_total_pixels()
    {
        // 512x512 = MaxTotalPixels exacto: es el límite VÁLIDO (no lanza).
        var ok = new FrameBuffer(512, 512);
        Assert.Equal(512, ok.Width);
        Assert.Equal(512, ok.Height);
        Assert.Equal(512 * 512, ok.AllPixels().Count());
    }

    [Fact]
    public void FrameBuffer_boundaries_preserved()
    {
        // > MaxTotalPixels -> lanza
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameBuffer(512, 513));
        // dimensiones no positivas -> lanza (mismo nombre de param)
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameBuffer(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameBuffer(10, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameBuffer(-5, 10));
    }

    // ---- EditingService.AddDrawing ----

    [Fact]
    public void AddDrawing_overflow_size_rejected_without_insert()
    {
        var svc = new EditingService();
        var scene = NewScene();
        int before = scene.Layers[0].Objects.Count;
        // int.MaxValue * 2 desborda int (checked)
        Assert.Throws<ArgumentOutOfRangeException>(
            () => svc.AddDrawing(scene, new PixelSize(int.MaxValue, 2)));
        Assert.Equal(before, scene.Layers[0].Objects.Count); // sin insertar
    }

    [Fact]
    public void AddDrawing_oversized_pixel_count_rejected_without_insert()
    {
        var svc = new EditingService();
        var scene = NewScene();
        int before = scene.Layers[0].Objects.Count;
        Assert.Throws<ArgumentOutOfRangeException>(
            () => svc.AddDrawing(scene, new PixelSize(1024, 1024)));
        Assert.Equal(before, scene.Layers[0].Objects.Count);
    }

    [Fact]
    public void AddDrawing_valid_size_creates_buffer_of_exact_pixels()
    {
        var svc = new EditingService();
        var scene = NewScene();
        var d = svc.AddDrawing(scene, new PixelSize(16, 16));
        Assert.NotNull(d);
        Assert.Equal(16 * 16, d.PixelData.Length);
        Assert.Single(scene.Layers[0].Objects);
    }

    // ---- EditingService.EnsureCapacity (via public AddText) ----

    [Fact]
    public void EnsureCapacity_exact_max_ok_then_overflow_rejected()
    {
        var svc = new EditingService();
        var scene = NewScene();
        for (int i = 0; i < EditingService.MaxObjectsPerScene; i++)
            svc.AddText(scene, $"t{i}", new PixelPoint(0, 0));

        int before = scene.Layers[0].Objects.Count;
        Assert.Equal(EditingService.MaxObjectsPerScene, before);
        // uno más -> InvalidOperationException por exceder, no inserta
        Assert.Throws<InvalidOperationException>(
            () => svc.AddText(scene, "overflow", new PixelPoint(0, 0)));
        Assert.Equal(before, scene.Layers[0].Objects.Count);
    }

    private static Scene NewScene()
    {
        var s = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(5) };
        s.Layers.Add(new Layer { Name = "L", Order = 0 });
        return s;
    }
}