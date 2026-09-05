using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;
using DSLetreros.Infrastructure.Persistence;
using Xunit;

namespace DSLetreros.Tests.Infrastructure;

/// <summary>
/// RFLED §2 — Matriz de recuperación LastKnownGood completa. Verifica que
/// TryRecoverAsync consulta el nivel `.autosave.bak` (backup del autosave anterior)
/// y que toda la cadena principal → autosave → autosave.bak → main.bak se resuelve
/// en el orden correcto, sin destrucción adicional.
/// </summary>
public class AtlasStoreRecoveryMatrixTests
{
    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), "dsletras-recovery", Guid.NewGuid().ToString("N"));

    private static Project SampleProject(string name)
    {
        var p = new Project { Name = name, Canvas = new CanvasDefinition(32, 16) };
        var scene = new Scene { Name = "Escena", Duration = TimeSpan.FromSeconds(5) };
        var layer = new Layer { Name = "Capa", Order = 0 };
        layer.Objects.Add(new TextObject
        {
            Name = "T", Text = "HOLA", Color = new RgbColor(255, 0, 0),
            Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)),
        });
        scene.Layers.Add(layer);
        p.Scenes.Add(scene);
        return p;
    }

    // Corrompe el manifest.json de un directorio de proyecto (lo hace inválido).
    private static void Corrupt(string dir)
    {
        var manifest = Path.Combine(dir, AtlasProjectStore.ManifestFile);
        File.WriteAllText(manifest, "{ not-json");
    }

    private async Task<string> PrepareRecoveryFixture(AtlasProjectStore store, string target)
    {
        // Escribe un Autosave válido (fixture para el caso autosave.bak).
        var auto = await store.AutosaveAsync(SampleProject("AutoV1"), target);
        Assert.True(auto.Success, auto.Message);
        return target + AtlasProjectStore.AutosaveSuffix;
    }

    // ---- §2.1 Caso crítico: autosave corrupto + autosave.bak válido ----

    [Fact]
    public async Task Open_recovers_from_autosave_bak_when_autosave_is_corrupt()
    {
        var store = new AtlasProjectStore();
        var root = NewTempRoot();
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "p.atlas");
        try
        {
            // Guardar principal válido, luego autosave válido.
            await store.SaveAsync(SampleProject("Principal"), target);
            var autoDir = await PrepareRecoveryFixture(store, target);
            Assert.True(Directory.Exists(autoDir));

            // Simular un autosave.bak válido (backup del autosave anterior) copiando
            // el autosave válido a la ruta .autosave.bak.
            var autoBak = autoDir + AtlasProjectStore.BackupSuffix;
            Directory.CreateDirectory(Directory.GetParent(autoBak)!.FullName);
            // Copia el contenido del autosave válido a .autosave.bak
            CopyDir(autoDir, autoBak);

            // Corromper el autosave (nuevo inválido) y el principal.
            Corrupt(autoDir);
            Corrupt(target);

            var (result, project) = await store.OpenAsync(target);
            Assert.True(result.Success, result.Message);
            Assert.True(result.Recovered);
            Assert.Equal("AutoV1", project!.Name);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    // ---- §2.2 Matriz de recovery completa ----

    [Fact]
    public async Task Principal_valid_returns_principal()
    {
        var store = new AtlasProjectStore();
        var root = NewTempRoot();
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "p.atlas");
        try
        {
            await store.SaveAsync(SampleProject("Principal"), target);
            var (result, project) = await store.OpenAsync(target);
            Assert.True(result.Success);
            Assert.False(result.Recovered);
            Assert.Equal("Principal", project!.Name);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Principal_corrupt_recovers_from_autosave()
    {
        var store = new AtlasProjectStore();
        var root = NewTempRoot();
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "p.atlas");
        try
        {
            await store.SaveAsync(SampleProject("Principal"), target);
            var autoDir = await PrepareRecoveryFixture(store, target); // autosave "AutoV1"

            Corrupt(target); // principal corrupto
            var (result, project) = await store.OpenAsync(target);
            Assert.True(result.Success);
            Assert.True(result.Recovered);
            Assert.Equal("AutoV1", project!.Name);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Principal_and_autosave_corrupt_recovers_from_main_bak()
    {
        var store = new AtlasProjectStore();
        var root = NewTempRoot();
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "p.atlas");
        try
        {
            await store.SaveAsync(SampleProject("V1"), target);
            // Guardar de nuevo para generar .bak del principal (V1).
            await store.SaveAsync(SampleProject("V2"), target);
            // En un save exitoso el .bak se descarta; aquí forzamos uno manualmente.
            var bak = target + AtlasProjectStore.BackupSuffix;
            var autoDir = await PrepareRecoveryFixture(store, target);
            // Copiamos un principal válido a .bak
            await store.SaveAsync(SampleProject("BackupContent"), target);
            CopyDir(target, bak);

            Corrupt(target);
            Corrupt(autoDir);
            // sin autosave.bak (no lo creamos)

            var (result, project) = await store.OpenAsync(target);
            Assert.True(result.Success, result.Message);
            Assert.True(result.Recovered);
            Assert.Equal("BackupContent", project!.Name);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task All_corrupt_returns_clear_failure_without_destruction()
    {
        var store = new AtlasProjectStore();
        var root = NewTempRoot();
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "p.atlas");
        try
        {
            await store.SaveAsync(SampleProject("Principal"), target);
            var autoDir = await PrepareRecoveryFixture(store, target);

            Corrupt(target);
            Corrupt(autoDir);
            // no hay .autosave.bak ni .bak

            var (result, project) = await store.OpenAsync(target);
            Assert.False(result.Success);
            Assert.Null(project);
            // No se creó basura adicional
            Assert.Empty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true);
        foreach (var d in Directory.GetDirectories(src))
            CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}