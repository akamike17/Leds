using DSLetreros.Domain.Entities;
using DSLetreros.Domain.Validation;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Validation;

/// <summary>
/// Frontera EXACTA del ProjectValidator: ejercita el valor límite preciso de cada
/// tope (no valores muy por encima/debajo, sino `== Max` y `== Max+1`), para matar
/// los mutantes Equality/Logical (`>` vs `>=`, `<=` vs `<`, `||` vs `&&`) que un test
/// de "600 > 512" deja sobrevivir porque no distingue el borde.
/// final.md §16 + §20: la frontera defensiva debe probarse en el punto exacto que define
/// el requisito de aceptación/rechazo.
/// </summary>
public class ValidatorExactBoundaryTests
{
    private static Project NewValidProject()
    {
        var p = new Project { Name = "Test", Canvas = new CanvasDefinition(32, 16), FormatVersion = 1 };
        var scene = new Scene { Name = "Escena 1", Duration = TimeSpan.FromSeconds(5) };
        var layer = new Layer { Name = "Capa 1", Order = 0 };
        layer.Objects.Add(new TextObject { Name = "Texto", Text = "HOLA" });
        scene.Layers.Add(layer);
        p.Scenes.Add(scene);
        return p;
    }

    // ----- Canvas: dimensiones exactas (mata `> Max`, `>= Max`, total exacto) -----
    // NOTA: `CanvasDefinition` rechaza width/height <= 0 en su constructor, así que el
    // guard `canvas.Width <= 0` del validador es defensivo (inalcanzable vía constructor)
    // y su mutante `< 0` es EQUIVALENTE — no se fuerza cobertura falsa sobre él.

    [Fact]
    public void Canvas_at_exactly_max_dimension_is_valid()
    {
        var p = NewValidProject();
        p.Canvas = new CanvasDefinition(ProjectValidator.MaxCanvasDimension, ProjectValidator.MaxCanvasDimension); // 512x512
        // 512x512 == MaxTotalPixels → NO supera el máximo (borde exacto del `>` de total).
        // Pero: 512x512 == 262144 píxeles == MaxTotalPixels → `pixels > MaxTotalPixels` es false.
        var r = ProjectValidator.Validate(p);
        // El lienzo 512x512 es válido (dimensión <= Max, total == Max → no excede).
        Assert.DoesNotContain(r.Errors, e => e.Contains("fuera de límites"));
        Assert.DoesNotContain(r.Errors, e => e.Contains("supera el máximo de"));
    }

    [Fact]
    public void Canvas_one_over_max_dimension_fails()
    {
        var p = NewValidProject();
        p.Canvas = new CanvasDefinition(ProjectValidator.MaxCanvasDimension + 1, 16); // 513
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    [Fact]
    public void Canvas_total_pixels_one_over_max_fails()
    {
        var p = NewValidProject();
        // 512 x 513 = 262656 > 262144 (MaxTotalPixels) → `pixels > MaxTotalPixels` exacto.
        p.Canvas = new CanvasDefinition(ProjectValidator.MaxCanvasDimension, ProjectValidator.MaxCanvasDimension + 1);
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    // ----- FormatVersion (mata `!=` de L119) -----

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Format_version_not_exactly_supported_fails(int version)
    {
        var p = NewValidProject();
        p.FormatVersion = version;
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    [Fact]
    public void Format_version_exactly_supported_is_valid()
    {
        var p = NewValidProject();
        p.FormatVersion = ProjectValidator.SupportedFormatVersion; // 1
        Assert.True(ProjectValidator.Validate(p).IsValid);
    }

    // ----- Nombre: longitud exacta (mata `> MaxNameLength` de ValidateName) -----

    [Fact]
    public void Name_at_exactly_max_length_is_valid()
    {
        var p = NewValidProject();
        p.Name = new string('a', ProjectValidator.MaxNameLength); // 256
        var r = ProjectValidator.Validate(p);
        Assert.DoesNotContain(r.Errors, e => e.Contains("nombre"));
    }

    [Fact]
    public void Name_one_over_max_length_fails()
    {
        var p = NewValidProject();
        p.Name = new string('a', ProjectValidator.MaxNameLength + 1); // 257
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    // ----- Objetos por capa/escena (mata `> MaxObjectsPerLayer` y `> MaxObjectsPerScene`) -----

    [Fact]
    public void Scene_at_exactly_max_objects_is_valid()
    {
        var p = NewValidProject();
        var layer = p.Scenes[0].Layers[0];
        // Ya hay 1 objeto; añadir hasta MaxObjectsPerScene exacto.
        for (int i = layer.Objects.Count; i < ProjectValidator.MaxObjectsPerScene; i++)
            layer.Objects.Add(new TextObject { Name = $"t{i}", Text = "x" });
        Assert.Equal(ProjectValidator.MaxObjectsPerScene, p.Scenes[0].Layers[0].Objects.Count);
        var r = ProjectValidator.Validate(p);
        Assert.DoesNotContain(r.Errors, e => e.Contains("objetos"));
    }

    [Fact]
    public void Scene_one_over_max_objects_fails()
    {
        var p = NewValidProject();
        var layer = p.Scenes[0].Layers[0];
        for (int i = layer.Objects.Count; i <= ProjectValidator.MaxObjectsPerScene; i++)
            layer.Objects.Add(new TextObject { Name = $"t{i}", Text = "x" });
        // 1001 > 1000
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    // ----- Timing.End vs Duration (mata `> scene.Duration` → warning, y `>=`/`<`) -----

    [Fact]
    public void Timing_exactly_equal_to_duration_is_no_warning()
    {
        var p = NewValidProject();
        var scene = p.Scenes[0];
        var obj = scene.Layers[0].Objects[0];
        obj.Timing = new TimeRange(TimeSpan.Zero, scene.Duration); // End == Duration exacto
        var r = ProjectValidator.Validate(p);
        Assert.DoesNotContain(r.Warnings, w => w.Contains("excede la duración"));
    }

    [Fact]
    public void Timing_one_ms_over_duration_warns()
    {
        var p = NewValidProject();
        var scene = p.Scenes[0];
        var obj = scene.Layers[0].Objects[0];
        obj.Timing = new TimeRange(TimeSpan.Zero, scene.Duration + TimeSpan.FromMilliseconds(1)); // End > Duration
        var r = ProjectValidator.Validate(p);
        Assert.Contains(r.Warnings, w => w.Contains("excede la duración"));
    }

    // ----- Paleta de asset indexado (mata `paletteCount == 0` y `b >= paletteCount`) -----

    [Fact]
    public void Embedded_asset_empty_palette_fails()
    {
        var p = NewValidProject();
        p.EmbeddedAssets["A"] = "{\"width\":1,\"height\":1,\"pixels\":\"" +
            Convert.ToBase64String(new byte[] { 0 }) + "\",\"palette\":[]}";
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    [Fact]
    public void Embedded_asset_index_at_palette_boundary_is_valid()
    {
        // índice == paletteCount-1 (último válido) no debe fallar.
        var p = NewValidProject();
        // paleta de 2 colores, índice 1 (el último válido).
        p.EmbeddedAssets["A"] = "{\"width\":1,\"height\":1,\"pixels\":\"" +
            Convert.ToBase64String(new byte[] { 1 }) + "\",\"palette\":[{\"r\":0,\"g\":0,\"b\":0},{\"r\":255,\"g\":255,\"b\":255}]}";
        var r = ProjectValidator.Validate(p);
        Assert.DoesNotContain(r.Errors, e => e.Contains("fuera de paleta"));
    }

    [Fact]
    public void Embedded_asset_index_equals_palette_count_fails()
    {
        // índice == paletteCount (2) → fuera de paleta (b >= paletteCount exacto).
        var p = NewValidProject();
        p.EmbeddedAssets["A"] = "{\"width\":1,\"height\":2,\"pixels\":\"" +
            Convert.ToBase64String(new byte[] { 0, 2 }) + "\",\"palette\":[{\"r\":0,\"g\":0,\"b\":0},{\"r\":255,\"g\":255,\"b\":255}]}";
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    // ----- Branches de error hoy NoCoverage (IDs duplicados, grupos inválidos) -----

    [Fact]
    public void Duplicate_scene_id_fails()
    {
        var p = NewValidProject();
        var scene = p.Scenes[0];
        var dup = new Scene { Name = "Escena 2", Id = scene.Id, Duration = TimeSpan.FromSeconds(5) };
        dup.Layers.Add(new Layer { Name = "L", Order = 0 });
        p.Scenes.Add(dup);
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    [Fact]
    public void Duplicate_object_id_fails()
    {
        var p = NewValidProject();
        var layer = p.Scenes[0].Layers[0];
        var obj = layer.Objects[0];
        var dup = new TextObject { Name = "T2", Id = obj.Id, Text = "X" };
        layer.Objects.Add(dup);
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    [Fact]
    public void Group_with_less_than_two_members_fails()
    {
        var p = NewValidProject();
        var scene = p.Scenes[0];
        var obj = scene.Layers[0].Objects[0];
        scene.Groups.Add(new ObjectGroup { Name = "G", MemberIds = new() { obj.Id } });
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    [Fact]
    public void Group_with_duplicate_members_fails()
    {
        var p = NewValidProject();
        var scene = p.Scenes[0];
        var layer = scene.Layers[0];
        var o1 = layer.Objects[0];
        var o2 = new TextObject { Name = "T2", Text = "Y" };
        layer.Objects.Add(o2);
        scene.Groups.Add(new ObjectGroup { Name = "G", MemberIds = new() { o1.Id, o1.Id } });
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    [Fact]
    public void Group_referencing_nonexistent_object_fails()
    {
        var p = NewValidProject();
        var scene = p.Scenes[0];
        var layer = scene.Layers[0];
        var o1 = layer.Objects[0];
        scene.Groups.Add(new ObjectGroup { Name = "G", MemberIds = new() { o1.Id, ObjectId.New() } });
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }
}