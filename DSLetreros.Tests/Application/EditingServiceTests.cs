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

    // ----- Group / Ungroup -----

    [Fact]
    public void GroupObjects_creates_group_with_unique_resolvable_members()
    {
        var svc = new DSLetreros.Application.Services.EditingService();
        var scene = NewScene();
        var a = svc.AddText(scene, "A", new PixelPoint(0, 0));
        var b = svc.AddText(scene, "B", new PixelPoint(1, 1));

        var group = svc.GroupObjects(scene, new[] { a, b }, "G1");

        Assert.Single(scene.Groups);
        Assert.Equal("G1", group.Name);
        Assert.Equal(2, group.MemberIds.Count);
        Assert.Contains(a.Id, group.MemberIds);
        Assert.Contains(b.Id, group.MemberIds);
        // grupo no tiene contenido visual: los objetos siguen en la capa
        Assert.Equal(2, scene.Layers[0].Objects.Count);
    }

    [Fact]
    public void GroupObjects_requires_at_least_two_members()
    {
        var svc = new DSLetreros.Application.Services.EditingService();
        var scene = NewScene();
        var a = svc.AddText(scene, "A", new PixelPoint(0, 0));
        Assert.Throws<ArgumentException>(() => svc.GroupObjects(scene, new[] { a }));
    }

    [Fact]
    public void GroupObjects_dedupes_identical_members()
    {
        var svc = new DSLetreros.Application.Services.EditingService();
        var scene = NewScene();
        var a = svc.AddText(scene, "A", new PixelPoint(0, 0));
        var b = svc.AddText(scene, "B", new PixelPoint(1, 1));
        svc.GroupObjects(scene, new[] { a, b });
        svc.GroupObjects(scene, new[] { a, b });
        Assert.Single(scene.Groups);   // mismo conjunto = no duplicar grupo
    }

    [Fact]
    public void Ungroup_conserves_objects_and_removes_group()
    {
        var svc = new DSLetreros.Application.Services.EditingService();
        var scene = NewScene();
        var a = svc.AddText(scene, "A", new PixelPoint(0, 0));
        var b = svc.AddText(scene, "B", new PixelPoint(1, 1));
        var group = svc.GroupObjects(scene, new[] { a, b });

        Assert.True(svc.Ungroup(scene, group.Id));
        Assert.Empty(scene.Groups);
        Assert.Equal(2, scene.Layers[0].Objects.Count);   // objetos conservados
    }

    [Fact]
    public void MoveGroup_moves_all_members()
    {
        var svc = new DSLetreros.Application.Services.EditingService();
        var scene = NewScene();
        var a = svc.AddText(scene, "A", new PixelPoint(2, 2));
        var b = svc.AddText(scene, "B", new PixelPoint(5, 5));
        var group = svc.GroupObjects(scene, new[] { a, b });

        svc.MoveGroup(scene, group, new PixelPoint(10, 20));

        Assert.Equal(new PixelPoint(12, 22), a.Position);
        Assert.Equal(new PixelPoint(15, 25), b.Position);
    }

    // ----- Align -----

    [Fact]
    public void Align_left_sets_common_min_x()
    {
        var svc = new DSLetreros.Application.Services.EditingService();
        var scene = NewScene();
        var a = svc.AddText(scene, "A", new PixelPoint(4, 1));
        var b = svc.AddText(scene, "B", new PixelPoint(10, 3));

        svc.AlignObjects(new[] { a, b }, Alignment.Left);

        Assert.Equal(4, a.Position.X);
        Assert.Equal(4, b.Position.X);
        Assert.Equal(1, a.Position.Y);   // Y no cambia
        Assert.Equal(3, b.Position.Y);
    }

    [Fact]
    public void Align_top_sets_common_min_y()
    {
        var svc = new DSLetreros.Application.Services.EditingService();
        var scene = NewScene();
        var a = svc.AddText(scene, "A", new PixelPoint(1, 3));
        var b = svc.AddText(scene, "B", new PixelPoint(9, 8));

        svc.AlignObjects(new[] { a, b }, Alignment.Top);

        Assert.Equal(3, a.Position.Y);
        Assert.Equal(3, b.Position.Y);
        Assert.Equal(1, a.Position.X);   // X no cambia
        Assert.Equal(9, b.Position.X);
    }

    // ----- capa destino explícita (contrato único) -----

    [Fact]
    public void AddText_respects_explicit_target_layer_when_not_first()
    {
        var svc = new DSLetreros.Application.Services.EditingService();
        var scene = NewScene();
        var layerB = new Layer { Name = "Capa B", Order = 1 };
        scene.Layers.Add(layerB);

        var t = svc.AddText(scene, "X", new PixelPoint(0, 0), layer: layerB);

        Assert.Empty(scene.Layers[0].Objects);   // capa A (orden 0) sin objetos
        Assert.Single(layerB.Objects);
        Assert.Equal("X", layerB.Objects[0] is TextObject txt ? txt.Text : "");
    }

    [Fact]
    public void Duplicate_respects_explicit_target_layer()
    {
        var svc = new DSLetreros.Application.Services.EditingService();
        var scene = NewScene();
        var layerB = new Layer { Name = "Capa B", Order = 1 };
        scene.Layers.Add(layerB);
        var orig = svc.AddText(scene, "A", new PixelPoint(0, 0));

        var copies = svc.DuplicateObjects(scene, new[] { orig }, layer: layerB);

        Assert.Single(layerB.Objects);
        Assert.Single(scene.Layers[0].Objects);   // original sigue en capa A
        Assert.Equal(copies[0].Id, layerB.Objects[0].Id);
    }
}