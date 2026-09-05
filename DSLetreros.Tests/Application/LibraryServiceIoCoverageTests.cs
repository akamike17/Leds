using DSLetreros.Application.Services;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Application;

/// <summary>
/// Cobertura de las branches de I/O del LibraryService (dibujos E imágenes) que antes
/// quedaban NoCoverage: GetDrawing (existente/no-existente), DeleteDrawing (no-existente),
/// SaveCustomImage (dimensiones inválidas, null-coalescing, roundtrip), ListImages/DeleteImage
/// (filtrado de temporales, asset != null), y PalettesEqual (distinto count).
/// Son asserts de COMPORTAMIENTO real (devuelve null/false/valores), no de texto de mensaje.
/// </summary>
public class LibraryServiceIoCoverageTests : IDisposable
{
    private readonly string _root;
    private readonly LibraryService _svc;

    public LibraryServiceIoCoverageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dsletras-lib-io-" + Guid.NewGuid().ToString("N"));
        _svc = new LibraryService(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    // ----- GetDrawing: existente y no-existente -----

    [Fact]
    public void GetDrawing_returns_null_for_unknown_id()
    {
        Assert.Null(_svc.GetDrawing(new AssetId(Guid.NewGuid())));
    }

    [Fact]
    public void GetDrawing_roundtrips_saved_drawing()
    {
        var (ok, _, id) = _svc.SaveCustomDrawing("Corazón", 2, 2, new byte[] { 1, 0, 0, 1 });
        Assert.True(ok);
        var got = _svc.GetDrawing(id!);
        Assert.NotNull(got);
        Assert.Equal("Corazón", got!.Name);
    }

    // ----- DeleteDrawing: no-existente devuelve false -----

    [Fact]
    public void DeleteDrawing_returns_false_for_unknown_id()
    {
        Assert.False(_svc.DeleteDrawing(new AssetId(Guid.NewGuid())));
    }

    [Fact]
    public void DeleteDrawing_removes_existing_and_returns_true()
    {
        var (ok, _, id) = _svc.SaveCustomDrawing("X", 2, 2, new byte[] { 1, 1, 0, 0 });
        Assert.True(ok);
        Assert.True(_svc.DeleteDrawing(id!));
        Assert.Null(_svc.GetDrawing(id!));
    }

    // ----- SaveCustomImage: dimensiones inválidas + roundtrip + null-coalescing -----

    [Fact]
    public void SaveCustomImage_rejects_zero_dimensions()
    {
        var (ok, _, _) = _svc.SaveCustomImage("Img", "PNG", 0, 4, new byte[16]);
        Assert.False(ok);
    }

    [Fact]
    public void SaveCustomImage_rejects_negative_dimensions()
    {
        var (ok, _, _) = _svc.SaveCustomImage("Img", "PNG", -1, 4, new byte[16]);
        Assert.False(ok);
    }

    [Fact]
    public void SaveCustomImage_roundtrips_with_nullable_defaults()
    {
        // Ejercita los null-coalescing (palette/pixels/conversionMetadata/name a defaults).
        var (ok, msg, id) = _svc.SaveCustomImage("", "PNG", 2, 2, new byte[] { 0, 1, 1, 0 }, null, "");
        Assert.True(ok, msg);
        Assert.NotNull(id);
        var images = _svc.ListImages();
        Assert.Single(images);
        Assert.Equal("Imagen", images[0].Name); // nombre vacío → "Imagen"
        Assert.Empty(images[0].Palette);       // paleta null → vacía
    }

    // ----- ListImages / DeleteImage -----

    [Fact]
    public void ListImages_ignores_tmp_files_and_lists_saved()
    {
        // Simular un temporal residual i-*.tmp: no debe aparecer.
        File.WriteAllText(Path.Combine(_root, "i-deadbeef.json.tmp"), "...");
        var (ok, _, _) = _svc.SaveCustomImage("Foto", "JPEG", 2, 2, new byte[] { 1, 1, 1, 1 });
        Assert.True(ok);
        var images = _svc.ListImages();
        Assert.Single(images); // sólo la imagen real, no el .tmp
        Assert.Equal("Foto", images[0].Name);
    }

    [Fact]
    public void DeleteImage_returns_false_for_unknown_id_and_true_for_existing()
    {
        Assert.False(_svc.DeleteImage(new AssetId(Guid.NewGuid())));

        var (ok, _, id) = _svc.SaveCustomImage("I", "PNG", 2, 2, new byte[] { 0, 0, 1, 1 });
        Assert.True(ok);
        Assert.True(_svc.DeleteImage(id!));
        Assert.Empty(_svc.ListImages());
    }

    // ----- PalettesEqual: distinto count -----

    [Fact]
    public void Same_pixels_same_name_different_palette_length_creates_two_entries()
    {
        // Paleta de 1 color vs 2 colores (distinto Count) → NO dedup.
        byte[] pixels = { 1, 1, 1, 0 };
        var (ok1, _, id1) = _svc.SaveCustomDrawing("D", 2, 2, pixels, new() { RgbColor.Red });
        Assert.True(ok1);
        var (ok2, _, id2) = _svc.SaveCustomDrawing("D", 2, 2, pixels, new() { RgbColor.Red, RgbColor.White });
        Assert.True(ok2);
        Assert.NotEqual(id1, id2);
        Assert.Equal(2, _svc.ListDrawings().Count);
    }
}