using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;
using DSLetreros.Infrastructure.Persistence;
using Xunit;

namespace DSLetreros.Tests.Infrastructure;

public class AtlasProjectStoreTests
{
    [Fact]
    public async Task Save_and_open_roundtrip_preserves_semantics()
    {
        var store = new AtlasProjectStore();
        var p = new Project { Name = "Prueba", Canvas = new CanvasDefinition(32, 16) };
        var scene = new Scene { Name = "Escena", Duration = TimeSpan.FromSeconds(5) };
        var layer = new Layer { Name = "Capa", Order = 0 };
        layer.Objects.Add(new TextObject { Name = "T", Text = "HOLA", Color = new RgbColor(255, 0, 0) });
        scene.Layers.Add(layer);
        p.Scenes.Add(scene);

        var tmp = Path.Combine(Path.GetTempPath(), "dsletras-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(tmp)!);
        var target = tmp + ".atlas";

        try
        {
            var save = await store.SaveAsync(p, target);
            Assert.True(save.Success, save.Message);

            var (open, loaded) = await store.OpenAsync(target);
            Assert.True(open.Success, open.Message);
            Assert.NotNull(loaded);
            Assert.Equal(p.Name, loaded!.Name);
            Assert.Equal(p.Canvas.Width, loaded.Canvas.Width);
            Assert.Single(loaded.Scenes);
            Assert.Single(loaded.Scenes[0].Layers);
            var obj = Assert.IsType<TextObject>(loaded.Scenes[0].Layers[0].Objects[0]);
            Assert.Equal("HOLA", obj.Text);
            Assert.Equal(new RgbColor(255, 0, 0), obj.Color);
        }
        finally
        {
            if (Directory.Exists(target)) Directory.Delete(target, true);
            if (Directory.Exists(target + ".bak")) Directory.Delete(target + ".bak", true);
        }
    }

    [Fact]
    public async Task Open_rejects_non_atlas_directory()
    {
        var store = new AtlasProjectStore();
        var tmp = Path.Combine(Path.GetTempPath(), "dsletras-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var (result, project) = await store.OpenAsync(tmp);
            Assert.False(result.Success);
            Assert.Null(project);
        }
        finally { Directory.Delete(tmp, true); }
    }
}