using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;
using DSLetreros.Infrastructure.Persistence;
using Xunit;

namespace DSLetreros.Tests.Infrastructure;

public class DrawingPersistenceTests
{
    [Fact]
    public async Task Drawing_object_roundtrips_with_pixels_and_palette()
    {
        var store = new AtlasProjectStore();
        var p = new Project { Name = "Dibujo", Canvas = new CanvasDefinition(16, 16) };
        var scene = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(5) };
        var layer = new Layer { Name = "L", Order = 0 };
        var drawing = new DrawingObject
        {
            Name = "Corazón",
            Size = new PixelSize(2, 2),
            BitsPerPixel = 1,
            Palette = new() { RgbColor.Red },
            PixelData = new byte[] { 0, 1, 1, 1 }, // corazón aproximado 2x2
            Bounds = PixelRect.FromLTRB(0, 0, 2, 2),
            Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)),
        };
        layer.Objects.Add(drawing);
        scene.Layers.Add(layer);
        p.Scenes.Add(scene);

        var tmp = Path.Combine(Path.GetTempPath(), "dsletras-draw-" + Guid.NewGuid().ToString("N"));
        var target = tmp + ".atlas";
        try
        {
            var save = await store.SaveAsync(p, target);
            Assert.True(save.Success, save.Message);

            var (open, loaded) = await store.OpenAsync(target);
            Assert.True(open.Success);
            var obj = Assert.IsType<DrawingObject>(loaded!.Scenes[0].Layers[0].Objects[0]);
            Assert.Equal("Corazón", obj.Name);
            Assert.Equal(new byte[] { 0, 1, 1, 1 }, obj.PixelData);
            Assert.Equal(RgbColor.Red, obj.Palette[0]);
            Assert.Equal(new PixelSize(2, 2), obj.Size);
        }
        finally
        {
            if (Directory.Exists(target)) Directory.Delete(target, true);
            if (Directory.Exists(target + ".bak")) Directory.Delete(target + ".bak", true);
        }
    }

    [Fact]
    public async Task Group_roundtrips_ids_and_members()
    {
        var store = new AtlasProjectStore();
        var p = new Project { Name = "Grupo", Canvas = new CanvasDefinition(16, 16) };
        var scene = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(5) };
        var layer = new Layer { Name = "L", Order = 0 };
        var a = new TextObject { Name = "A", Text = "A", Position = new PixelPoint(0, 0) };
        var b = new TextObject { Name = "B", Text = "B", Position = new PixelPoint(5, 0) };
        layer.Objects.Add(a);
        layer.Objects.Add(b);
        scene.Layers.Add(layer);
        scene.Groups.Add(new ObjectGroup { Name = "G1", MemberIds = new() { a.Id, b.Id } });
        p.Scenes.Add(scene);

        var tmp = Path.Combine(Path.GetTempPath(), "dsletras-group-" + Guid.NewGuid().ToString("N"));
        var target = tmp + ".atlas";
        try
        {
            var save = await store.SaveAsync(p, target);
            Assert.True(save.Success, save.Message);

            var (open, loaded) = await store.OpenAsync(target);
            Assert.True(open.Success);
            var g = Assert.Single(loaded!.Scenes[0].Groups);
            Assert.Equal("G1", g.Name);
            Assert.Equal(2, g.MemberIds.Count);
            Assert.Contains(a.Id, g.MemberIds);
            Assert.Contains(b.Id, g.MemberIds);
        }
        finally
        {
            if (Directory.Exists(target)) Directory.Delete(target, true);
            if (Directory.Exists(target + ".bak")) Directory.Delete(target + ".bak", true);
        }
    }
}