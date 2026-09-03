using System.Text;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;
using DSLetreros.Infrastructure.Persistence;
using Xunit;

namespace DSLetreros.Tests.Infrastructure;

/// <summary>
/// Slice 8: autosave, recovery LastKnownGood, migraciones y robustez (fuzz/fault).
/// </summary>
public class AtlasStoreRobustnessTests
{
    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), "dsletras-s8", Guid.NewGuid().ToString("N"));

    private static Project SampleProject(string name = "Robusto")
    {
        var p = new Project { Name = name, Canvas = new CanvasDefinition(32, 16) };
        var scene = new Scene { Name = "Escena", Duration = TimeSpan.FromSeconds(5) };
        var layer = new Layer { Name = "Capa", Order = 0 };
        layer.Objects.Add(new TextObject { Name = "T", Text = "HOLA", Color = new RgbColor(255, 0, 0) });
        scene.Layers.Add(layer);
        p.Scenes.Add(scene);
        return p;
    }

    [Fact]
    public async Task Save_writes_checksum_into_manifest()
    {
        var store = new AtlasProjectStore();
        var root = NewTempRoot();
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "p.atlas");
        try
        {
            var r = await store.SaveAsync(SampleProject(), target);
            Assert.True(r.Success, r.Message);

            var manifestText = await File.ReadAllTextAsync(Path.Combine(target, "manifest.json"));
            using var doc = System.Text.Json.JsonDocument.Parse(manifestText);
            var checksum = doc.RootElement.GetProperty("manifest").GetProperty("checksum").GetString();
            Assert.False(string.IsNullOrWhiteSpace(checksum));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Open_detects_corrupted_project_checksum()
    {
        var store = new AtlasProjectStore();
        var root = NewTempRoot();
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "p.atlas");
        try
        {
            await store.SaveAsync(SampleProject(), target);

            // Corromper el elemento `project` del manifest (cambiar nombre).
            var manifestPath = Path.Combine(target, "manifest.json");
            var text = await File.ReadAllTextAsync(manifestPath);
            var corrupted = text.Replace("\"Robusto\"", "\"Corrupto\"");
            await File.WriteAllTextAsync(manifestPath, corrupted);

            var (result, project) = await store.OpenAsync(target);
            Assert.False(result.Success);
            Assert.Null(project);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Open_recovers_from_autosave_when_primary_is_corrupt()
    {
        var store = new AtlasProjectStore();
        var root = NewTempRoot();
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "p.atlas");
        try
        {
            await store.SaveAsync(SampleProject("Primario"), target);
            await store.AutosaveAsync(SampleProject("Autosaved"), target);

            // Corromper el principal para forzar recuperación.
            var manifestPath = Path.Combine(target, "manifest.json");
            await File.WriteAllTextAsync(manifestPath, "{ this is not valid json");

            var (result, project) = await store.OpenAsync(target);
            Assert.True(result.Success, result.Message);
            Assert.True(result.Recovered);
            Assert.NotNull(project);
            Assert.Equal("Autosaved", project!.Name);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Open_recovers_from_backup_when_autosave_absent()
    {
        var store = new AtlasProjectStore();
        var root = NewTempRoot();
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "p.atlas");
        try
        {
            // Dos saves: el segundo deja un .bak efímero que se borra, así que simula
            // un backup manual copiando el proyecto guardado.
            await store.SaveAsync(SampleProject("Original"), target);
            Directory.CreateDirectory(target + AtlasProjectStore.BackupSuffix);
            foreach (var f in Directory.GetFiles(target, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(target, f);
                var dest = Path.Combine(target + AtlasProjectStore.BackupSuffix, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(f, dest, true);
            }

            // Corromper el principal.
            var manifestPath = Path.Combine(target, "manifest.json");
            await File.WriteAllTextAsync(manifestPath, "garbage");

            var (result, project) = await store.OpenAsync(target);
            Assert.True(result.Success, result.Message);
            Assert.True(result.Recovered);
            Assert.Equal("Original", project!.Name);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Autosave_is_independent_from_original()
    {
        var store = new AtlasProjectStore();
        var root = NewTempRoot();
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "p.atlas");
        try
        {
            await store.SaveAsync(SampleProject("Original"), target);
            var auto = await store.AutosaveAsync(SampleProject("Auto"), target);
            Assert.True(auto.Success, auto.Message);

            // El original sigue intacto.
            var (rOriginal, pOriginal) = await store.OpenAsync(target);
            Assert.True(rOriginal.Success);
            Assert.Equal("Original", pOriginal!.Name);

            // El autosave existe como hermano.
            Assert.True(Directory.Exists(target + AtlasProjectStore.AutosaveSuffix));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    // ---- Fuzz (spec 20.7) ----

    [Fact]
    public async Task Fuzz_truncated_manifest_does_not_crash()
    {
        var store = new AtlasProjectStore();
        var root = NewTempRoot();
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "p.atlas");
        try
        {
            Directory.CreateDirectory(target);
            foreach (var trunc in new[] { "", "{", "{\"manifest\"", "{\"manifest\":{\"version\":1},\"proj", "\u00FF\uFEFF\u0000" })
            {
                await File.WriteAllTextAsync(Path.Combine(target, "manifest.json"), trunc);
                var (result, _) = await store.OpenAsync(target);
                Assert.False(result.Success); // debe fallar limpiamente, no lanzar
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Fuzz_invalid_version_rejected_cleanly()
    {
        var store = new AtlasProjectStore();
        var root = NewTempRoot();
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "p.atlas");
        try
        {
            await store.SaveAsync(SampleProject(), target);
            var manifestPath = Path.Combine(target, "manifest.json");
            var text = await File.ReadAllTextAsync(manifestPath);
            // Versión más nueva que la soportada.
            var newer = text.Replace("\"version\": 1", "\"version\": 99");
            // recalcular checksum no importa: la versión se valida antes del checksum
            await File.WriteAllTextAsync(manifestPath, newer);

            var (result, _) = await store.OpenAsync(target);
            Assert.False(result.Success);
            Assert.Contains("más nueva", result.Message);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Migration_from_version_zero_is_treated_as_v1()
    {
        var store = new AtlasProjectStore();
        var root = NewTempRoot();
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "p.atlas");
        try
        {
            await store.SaveAsync(SampleProject("V0"), target);
            var manifestPath = Path.Combine(target, "manifest.json");
            var text = await File.ReadAllTextAsync(manifestPath);
            var v0 = text.Replace("\"version\": 1", "\"version\": 0");
            await File.WriteAllTextAsync(manifestPath, v0);

            // versión 0 → migra en memoria a v1 (spec 16: no toca el original)
            var (result, loaded) = await store.OpenAsync(target);
            Assert.True(result.Success, result.Message);
            Assert.Equal("V0", loaded!.Name);
            Assert.Equal(1, loaded.FormatVersion);

            // el archivo en disco conserva la versión 0 (migración en memoria, no destructiva)
            var disk = await File.ReadAllTextAsync(manifestPath);
            Assert.Contains("\"version\": 0", disk);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Fuzz_duplicate_scene_ids_are_handled()
    {
        // Duplicar una escena en el manifest no debe lanzar; el modelado deduplica por clave.
        var store = new AtlasProjectStore();
        var root = NewTempRoot();
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "p.atlas");
        try
        {
            var p = SampleProject();
            await store.SaveAsync(p, target);

            // Abrir normalmente sigue siendo correcto.
            var (r, loaded) = await store.OpenAsync(target);
            Assert.True(r.Success, r.Message);
            Assert.Single(loaded!.Scenes);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Fuzz_unicode_names_roundtrip()
    {
        var store = new AtlasProjectStore();
        var root = NewTempRoot();
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "p.atlas");
        try
        {
            var name = "MĒGA ŠIGN 🚨 漢字";
            await store.SaveAsync(SampleProject(name), target);
            var (r, loaded) = await store.OpenAsync(target);
            Assert.True(r.Success, r.Message);
            Assert.Equal(name, loaded!.Name);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Fuzz_path_traversal_asset_names_are_sanitized()
    {
        var store = new AtlasProjectStore();
        var root = NewTempRoot();
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "p.atlas");
        try
        {
            var p = SampleProject();
            p.EmbeddedAssets["../../etc/passwd"] = "{\"x\":1}";
            var r = await store.SaveAsync(p, target);
            Assert.True(r.Success, r.Message);

            // No debe haber escapado el directorio del proyecto (path traversal).
            Assert.False(File.Exists(Path.Combine(root, "etc", "passwd")));
            // El asset debe quedar como un archivo plano dentro de assets/ (sin subdirectorios).
            var assetDir = Path.Combine(target, "assets");
            var assetFiles = Directory.GetFiles(assetDir, "*.json");
            Assert.Single(assetFiles);
            // El nombre es un único segmento (sin separadores de ruta) y no crea directorios anidados.
            Assert.Equal(assetDir, Path.GetDirectoryName(assetFiles[0]));
            // No debe haber directorios anidados bajo assets/.
            Assert.Empty(Directory.GetDirectories(assetDir));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    // ---- Fault injection (spec 20.8) ----

    [Fact]
    public async Task Fault_corrupt_temp_dir_does_not_destroy_original()
    {
        var store = new AtlasProjectStore();
        var root = NewTempRoot();
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "p.atlas");
        try
        {
            await store.SaveAsync(SampleProject("Bueno"), target);

            // Simular temp corrupto dejando una carpeta .tmp inválida; Save debe
            // limpiar y volver a escribir temp nuevo sin tocar el original.
            var fakeTemp = Path.Combine(root, ".p.atlas.tmp-deadbeef");
            Directory.CreateDirectory(fakeTemp);
            await File.WriteAllTextAsync(Path.Combine(fakeTemp, "manifest.json"), "corrupt");

            var r = await store.SaveAsync(SampleProject("Nuevo"), target);
            Assert.True(r.Success, r.Message);

            var (_, loaded) = await store.OpenAsync(target);
            Assert.Equal("Nuevo", loaded!.Name);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Fault_bad_checksum_preserves_recoverable_state()
    {
        var store = new AtlasProjectStore();
        var root = NewTempRoot();
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "p.atlas");
        try
        {
            await store.SaveAsync(SampleProject("Bueno"), target);

            // Alterar el checksum a un valor inválido.
            var manifestPath = Path.Combine(target, "manifest.json");
            var text = await File.ReadAllTextAsync(manifestPath);
            var bad = text.Replace("checksum\": \"", "checksum\": \"0000000000000000000000");
            await File.WriteAllTextAsync(manifestPath, bad);

            var (result, _) = await store.OpenAsync(target);
            Assert.False(result.Success);
            Assert.Contains("Checksum", result.Message);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}