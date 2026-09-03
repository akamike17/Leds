using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Application;

public class EditingServiceTests
{
    private static Scene NewScene()
    {
        var s = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(5) };
        s.Layers.Add(new Layer { Name = "L", Order = 0 });
        return s;
    }

    [Fact]
    public void AddText_creates_object_in_layer()
    {
        var svc = new DSLetreros.Application.Services.EditingService();
        var scene = NewScene();
        var t = svc.AddText(scene, "HOLA", new PixelPoint(1, 1), RgbColor.Red);
        Assert.Equal("HOLA", t.Text);
        Assert.Single(scene.Layers[0].Objects);
        Assert.Equal(RgbColor.Red, t.Color);
    }

    [Fact]
    public void Duplicate_generates_new_ids()
    {
        var svc = new DSLetreros.Application.Services.EditingService();
        var scene = NewScene();
        var orig = svc.AddText(scene, "A", new PixelPoint(0, 0));
        var copies = svc.DuplicateObjects(scene, new[] { orig });
        Assert.Single(copies);
        Assert.NotEqual(orig.Id, copies[0].Id);
        Assert.Equal(2, scene.Layers[0].Objects.Count);
        // posición desplazada
        Assert.Equal(new PixelPoint(1, 1), copies[0].Position);
    }

    [Fact]
    public void Delete_removes_only_target()
    {
        var svc = new DSLetreros.Application.Services.EditingService();
        var scene = NewScene();
        var a = svc.AddText(scene, "A", new PixelPoint(0, 0));
        var b = svc.AddText(scene, "B", new PixelPoint(0, 0));
        svc.DeleteObjects(scene, new[] { a.Id });
        Assert.Single(scene.Layers[0].Objects);
        Assert.Equal(b.Id, scene.Layers[0].Objects[0].Id);
    }

    [Fact]
    public void Move_applies_delta()
    {
        var svc = new DSLetreros.Application.Services.EditingService();
        var scene = NewScene();
        var a = svc.AddText(scene, "A", new PixelPoint(2, 3));
        svc.MoveObjects(new[] { a }, new PixelPoint(5, -1));
        Assert.Equal(new PixelPoint(7, 2), a.Position);
    }

    [Fact]
    public void AssignAnimation_replaces_same_slot()
    {
        var svc = new DSLetreros.Application.Services.EditingService();
        var obj = new TextObject();
        svc.AssignAnimation(obj, new AnimationDefinition { Slot = AnimationSlot.Main, Kind = AnimationKind.Blink });
        svc.AssignAnimation(obj, new AnimationDefinition { Slot = AnimationSlot.Main, Kind = AnimationKind.Marquee });
        Assert.Single(obj.Animations);
        Assert.Equal(AnimationKind.Marquee, obj.Animations[0].Kind);
    }
}