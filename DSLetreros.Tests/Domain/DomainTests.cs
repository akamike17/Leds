using DSLetreros.Domain.Entities;
using DSLetreros.Domain.Validation;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Domain;

public class ProjectValidatorTests
{
    private static Project NewValidProject()
    {
        var p = new Project { Name = "Test", Canvas = new CanvasDefinition(32, 16) };
        var scene = new Scene { Name = "Escena 1", Duration = TimeSpan.FromSeconds(5) };
        var layer = new Layer { Name = "Capa 1", Order = 0 };
        layer.Objects.Add(new TextObject { Name = "Texto", Text = "HOLA" });
        scene.Layers.Add(layer);
        p.Scenes.Add(scene);
        return p;
    }

    [Fact]
    public void Valid_project_passes()
    {
        var r = ProjectValidator.Validate(NewValidProject());
        Assert.True(r.IsValid, string.Join("; ", r.Errors));
    }

    [Fact]
    public void Project_without_scenes_fails()
    {
        var p = NewValidProject();
        p.Scenes.Clear();
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    [Fact]
    public void Scene_without_layers_fails()
    {
        var p = NewValidProject();
        p.Scenes[0].Layers.Clear();
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    [Fact]
    public void Duplicate_object_id_fails()
    {
        var p = NewValidProject();
        var dup = new TextObject { Id = p.Scenes[0].Layers[0].Objects[0].Id, Text = "DUP" };
        p.Scenes[0].Layers[0].Objects.Add(dup);
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    [Fact]
    public void Scene_with_zero_duration_fails()
    {
        var p = NewValidProject();
        p.Scenes[0].Duration = TimeSpan.Zero;
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }
}

public class ValueObjectTests
{
    [Fact]
    public void PixelSize_rejects_negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelSize(-1, 2));
    }

    [Fact]
    public void CanvasDefinition_rejects_zero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CanvasDefinition(0, 16));
    }

    [Fact]
    public void TimeRange_rejects_inverted()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void PixelRect_contains_boundary()
    {
        var r = PixelRect.FromLTRB(2, 3, 6, 7);
        Assert.True(r.Contains(new PixelPoint(2, 3)));   // esquina incluida
        Assert.False(r.Contains(new PixelPoint(6, 7)));  // fuera (semiabierto)
        Assert.Equal(4, r.Size.Width);
        Assert.Equal(4, r.Size.Height);
    }

    [Fact]
    public void RgbColor_equality_works()
    {
        Assert.Equal(new RgbColor(255, 0, 0), new RgbColor(255, 0, 0));
        Assert.NotEqual(RgbColor.Black, RgbColor.White);
    }
}