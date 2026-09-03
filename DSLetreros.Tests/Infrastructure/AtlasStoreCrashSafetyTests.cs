using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;
using DSLetreros.Infrastructure.Persistence;
using Xunit;

namespace DSLetreros.Tests.Infrastructure;

/// <summary>
/// Slice 8 ampliado + auditoría P0: crash-safety del SaveAsync/AutosaveAsync con
/// fault-injection ANTES/DURANTE/DESPUÉS de cada rename, y blindaje contra path
/// traversal vía ProjectPaths + AtlasProjectStore.
/// </summary>
public class AtlasStoreCrashSafetyTests
{
    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), "dsletras-crash", Guid.NewGuid().ToString("N"));

    private static Project SampleProject(string name)
    {
        var p = new Project { Name = name, Canvas = new CanvasDefinition(32, 16) };
        var scene = new Scene { Name = "Escena", Duration = TimeSpan.FromSeconds(5) };
        var layer = new Layer { Name = "Capa", Order = 0 };
        layer.Objects.Add(new TextObject { Name = "T", Text = "HOLA", Color = new RgbColor(255, 0, 0), Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)) });
        scene.Layers.Add(layer);
        p.Scenes.Add(scene);
        return p;
    }

    // ------------------------- Fault injection -------------------------

    [Fact]
    public async Task Save_failure_before_rename_main_preserves_original()
    {
        var store = new AtlasProjectStore();
        var root = NewTempRoot();
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "p.atlas");
        try
        {
            await store.SaveAsync(SampleProject("V1"), target);

            // Inyectar fallo JUSTO antes de mover el principal a .bak.
            store.FailPoint = phase => phase == "before-rename-main" ? throw new IOException("fault") : false;

            var r = await store.SaveAsync(SampleProject("V2"), target);
            Assert.False(r.Success);

            // El original sigue intacto y abreable.
            var (open, loaded) = await store.OpenAsync(target);
            Assert.True(open.Success, open.Message);
            Assert.Equal("V1", loaded!.Name);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Save_failure_before_rename_temp_restores_backup()
    {
        var store = new AtlasProjectStore();
        var root = NewTempRoot();
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "p.atlas");
        try
        {
            await store.SaveAsync(SampleProject("V1"), target);

            // Inyectar fallo justo antes de mover el temp al lugar del principal.
            store.FailPoint = phase => phase == "before-rename-temp" ? throw new IOException("fault") : false;

            var r = await store.SaveAsync(SampleProject("V2"), target);
            Assert.False(r.Success);

            // El backup debe haberse RESTAURADO: el principal vuelve a ser V1.
            var (open, loaded) = await store.OpenAsync(target);
            Assert.True(open.Success, open.Message);
            Assert.Equal("V1", loaded!.Name);
            Assert.False(Directory.Exists(target + AtlasProjectStore.BackupSuffix));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Autosave_failure_before_move_leaves_original_untouched()
    {
        var store = new AtlasProjectStore();
        var root = NewTempRoot();
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "p.atlas");
        try
        {
            await store.SaveAsync(SampleProject("Original"), target);

            store.FailPoint = phase => phase == "before-autosave-move" ? throw new IOException("fault") : false;

            var auto = await store.AutosaveAsync(SampleProject("Auto"), target);
            Assert.False(auto.Success);

            var (open, loaded) = await store.OpenAsync(target);
            Assert.True(open.Success);
            Assert.Equal("Original", loaded!.Name);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Backup_is_kept_until_new_primary_validated()
    {
        var store = new AtlasProjectStore();
        var root = NewTempRoot();
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "p.atlas");
        try
        {
            await store.SaveAsync(SampleProject("V1"), target);

            // Segundo save exitoso: el backup debe eliminarse tras validar el nuevo principal.
            var r = await store.SaveAsync(SampleProject("V2"), target);
            Assert.True(r.Success, r.Message);
            Assert.False(Directory.Exists(target + AtlasProjectStore.BackupSuffix));

            var (open, loaded) = await store.OpenAsync(target);
            Assert.Equal("V2", loaded!.Name);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    // ------------------------- Path traversal -------------------------

    [Fact]
    public async Task Open_rejects_manifest_with_traversal_scene_name()
    {
        var store = new AtlasProjectStore();
        var root = NewTempRoot();
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "p.atlas");
        try
        {
            await store.SaveAsync(SampleProject("P"), target);

            // Corromper manifest: una escena con nombre de archivo con ../ (traversal).
            var manifestPath = Path.Combine(target, "manifest.json");
            var text = await File.ReadAllTextAsync(manifestPath);
            text = text.Replace(".json", "../evil.json").Replace("\"../evil.json\"", "\"../../evil.json\"");
            await File.WriteAllTextAsync(manifestPath, text);

            var (result, _) = await store.OpenAsync(target);
            Assert.False(result.Success);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void ProjectPaths_rejects_path_escaping_root()
    {
        var root = NewTempRoot();
        Directory.CreateDirectory(root);
        try
        {
            Assert.Throws<ProjectPathException>(() =>
                ProjectPaths.EnsureWithin(root, Path.Combine(root, "..", "..", "etc", "passwd")));

            Assert.Throws<ArgumentException>(() => ProjectPaths.ResolveProjectDirectory(root, Guid.Empty));

            // Nombre simple válido vs inválido.
            Assert.True(ProjectPaths.IsSimpleFileName("manifest.json"));
            Assert.True(ProjectPaths.IsSimpleFileName("0123456789abcdef0123456789abcdef.json"));
            Assert.False(ProjectPaths.IsSimpleFileName("../../etc/passwd"));
            Assert.False(ProjectPaths.IsSimpleFileName("a/b.json"));
            Assert.False(ProjectPaths.IsSimpleFileName("evil.json/.."));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void ProjectPaths_resolves_project_file_within_dir()
    {
        var root = NewTempRoot();
        Directory.CreateDirectory(root);
        try
        {
            var proj = ProjectPaths.ResolveProjectDirectory(root, Guid.NewGuid());
            Directory.CreateDirectory(proj);
            var scene = ProjectPaths.ResolveProjectFile(proj, "scenes", "0123456789abcdef0123456789abcdef.json");
            Assert.StartsWith(Path.GetFullPath(proj), Path.GetFullPath(scene));

            // subdirectorio no permitido
            Assert.Throws<ProjectPathException>(() =>
                ProjectPaths.ResolveProjectFile(proj, "..", "manifest.json"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}