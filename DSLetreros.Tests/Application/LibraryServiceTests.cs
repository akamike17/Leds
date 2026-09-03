using DSLetreros.Application.Services;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Application;

public class LibraryServiceTests : IDisposable
{
    private readonly string _root;
    private readonly LibraryService _svc;

    public LibraryServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dsletras-lib-" + Guid.NewGuid().ToString("N"));
        _svc = new LibraryService(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public void Save_and_list_roundtrip()
    {
        byte[] pixels = { 1, 0, 0, 1 }; // 2x2
        var (ok, msg, id) = _svc.SaveCustomDrawing("Corazón", 2, 2, pixels, new() { RgbColor.Red });
        Assert.True(ok, msg);
        Assert.NotNull(id);

        var list = _svc.ListDrawings();
        Assert.Single(list);
        Assert.Equal("Corazón", list[0].Name);
        Assert.Equal(2, list[0].Width);
        Assert.Equal(pixels, list[0].Pixels);
    }

    [Fact]
    public void Save_rejects_mismatched_pixels()
    {
        byte[] pixels = { 1, 0, 0 }; // 3 bytes, espera 4
        var (ok, _, id) = _svc.SaveCustomDrawing("Bad", 2, 2, pixels);
        Assert.False(ok);
        Assert.Null(id);
    }

    [Fact]
    public void Delete_removes_drawing()
    {
        byte[] pixels = { 1, 0, 0, 1 };
        var (ok, _, id) = _svc.SaveCustomDrawing("X", 2, 2, pixels);
        Assert.True(ok);
        Assert.True(_svc.DeleteDrawing(id!));
        Assert.Empty(_svc.ListDrawings());
    }
}