using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.Validation;

namespace DSLetreros.Infrastructure.Persistence;

/// <summary>Manifest raíz del formato .atlas.</summary>
public sealed class AtlasManifest
{
    public string Format { get; set; } = "atlas-project";
    public int Version { get; set; } = 1;
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Canvas { get; set; } = "32x16";
    public DateTimeOffset UpdatedAt { get; set; }
    public List<string> Scenes { get; set; } = new();      // nombres de archivo en scenes/
    public List<string> Assets { get; set; } = new();      // nombres de archivo en assets/
    public List<string> Fonts { get; set; } = new();       // nombres de archivo en fonts/

    /// <summary>Checksum SHA-256 del contenido del proyecto (elemento `project`).</summary>
    public string? Checksum { get; set; }
}

/// <summary>Resultado de una operación de persistencia.</summary>
public sealed class PersistenceResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;

    /// <summary>Indica que el proyecto se recuperó desde un backup tras corrupción.</summary>
    public bool Recovered { get; set; }

    public static PersistenceResult Ok(string path) => new() { Success = true, Path = path };
    public static PersistenceResult Fail(string message) => new() { Success = false, Message = message };
}

/// <summary>
/// Almacenamiento local .atlas autocontenido. Sin SQL (spec sección 3).
///
/// Slice 8: atomic save validado, checksum, recovery LastKnownGood, autosave y migraciones.
/// </summary>
public sealed class AtlasProjectStore
{
    public const string ManifestFile = "manifest.json";
    public const int CurrentFormatVersion = 1;
    public const string BackupSuffix = ".bak";
    public const string AutosaveSuffix = ".autosave";

    private static readonly string[] Directories = { "scenes", "assets", "fonts", "previews" };

    #region Save / Open / Autosave

    /// <summary>Guarda un proyecto como carpeta .atlas con reemplazo atómico validado (invariante 9).</summary>
    public async Task<PersistenceResult> SaveAsync(Project project, string targetPath, CancellationToken ct = default)
    {
        try
        {
            var validation = ProjectValidator.Validate(project);
            if (!validation.IsValid)
                return PersistenceResult.Fail("Proyecto inválido: " + string.Join("; ", validation.Errors));

            var dir = Path.GetFullPath(targetPath);
            var parent = Path.GetDirectoryName(dir) ?? dir;
            Directory.CreateDirectory(parent);
            var tempDir = Path.Combine(parent, $".{Path.GetFileName(dir)}.tmp-{Guid.NewGuid():N}");

            // 1. Escribir en temp
            await WriteProjectToDirAsync(project, tempDir, ct);

            // 2. Validar el temp antes de reemplazar (crash-safe): vuelve a abrirlo con verificaci?n
            //    de formato (sin recurrir a recuperaci?n, para no falsear la validaci?n).
            var probe = await OpenCoreAsync(tempDir, ct);
            if (!probe.Result.Success)
            {
                SafeDeleteDir(tempDir);
                return PersistenceResult.Fail("Temp no valida: " + probe.Result.Message);
            }

            // 3. Reemplazo atómico con backup (recuperable ante caída durante replace).
            bool existed = Directory.Exists(dir);
            string? backupDir = null;
            if (existed)
            {
                backupDir = dir + BackupSuffix;
                SafeDeleteDir(backupDir);
                Directory.Move(dir, backupDir);
            }
            Directory.Move(tempDir, dir);

            if (existed && backupDir != null) SafeDeleteDir(backupDir);

            return PersistenceResult.Ok(dir);
        }
        catch (Exception ex)
        {
            return PersistenceResult.Fail(ex.Message);
        }
    }

    /// <summary>Autosave separado: nunca toca el original, escribe un hermano `.autosave`.</summary>
    public async Task<PersistenceResult> AutosaveAsync(Project project, string targetPath, CancellationToken ct = default)
    {
        try
        {
            var dir = Path.GetFullPath(targetPath);
            var parent = Path.GetDirectoryName(dir) ?? dir;
            Directory.CreateDirectory(parent);
            var autoDir = dir + AutosaveSuffix;
            var tempDir = Path.Combine(parent, $".{Path.GetFileName(autoDir)}.tmp-{Guid.NewGuid():N}");

            await WriteProjectToDirAsync(project, tempDir, ct);

            var probe = await OpenCoreAsync(tempDir, ct);
            if (!probe.Result.Success)
            {
                SafeDeleteDir(tempDir);
                return PersistenceResult.Fail("Autosave temp no valida: " + probe.Result.Message);
            }

            SafeDeleteDir(autoDir);
            Directory.Move(tempDir, autoDir);
            return PersistenceResult.Ok(autoDir);
        }
        catch (Exception ex)
        {
            return PersistenceResult.Fail(ex.Message);
        }
    }

    /// <summary>Abre un proyecto .atlas. Detecta formato, migra en memoria, valida, y recupera si el principal est? corrupto.</summary>
    public async Task<(PersistenceResult Result, Project? Project)> OpenAsync(string path, CancellationToken ct = default)
    {
        if (!Directory.Exists(path))
            return (PersistenceResult.Fail("El directorio no existe."), null);

        var direct = await OpenCoreAsync(path, ct);
        if (direct.Result.Success)
            return direct;

        return await TryRecoverAsync(path, direct.Result.Message, ct);
    }

    #endregion

    #region Internals

    private async Task WriteProjectToDirAsync(Project project, string tempDir, CancellationToken ct)
    {
        Directory.CreateDirectory(tempDir);
        foreach (var sub in Directories)
            Directory.CreateDirectory(Path.Combine(tempDir, sub));

        foreach (var scene in project.Scenes)
        {
            var file = Path.Combine(tempDir, "scenes", $"{scene.Id.Value:N}.json");
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(scene, AtlasJson.Options), ct);
        }

        foreach (var kv in project.EmbeddedAssets)
        {
            var file = Path.Combine(tempDir, "assets", $"{SafeName(kv.Key)}.json");
            await File.WriteAllTextAsync(file, kv.Value, ct);
        }

        var manifest = new AtlasManifest
        {
            Version = CurrentFormatVersion,
            ProjectId = project.Id.Value,
            Name = project.Name,
            Canvas = $"{project.Canvas.Width}x{project.Canvas.Height}",
            UpdatedAt = project.UpdatedAt,
            Scenes = project.Scenes.Select(s => $"{s.Id.Value:N}.json").ToList(),
            Assets = project.EmbeddedAssets.Keys.Select(SafeName).Select(n => $"{n}.json").ToList(),
        };

        var shell = new ProjectShell(project);

        // El checksum cubre el contenido del `project` shell (identidad, canvas, versiones).
        var shellJson = JsonSerializer.Serialize(shell, AtlasJson.Options);
        manifest.Checksum = ComputeChecksum(shellJson);

        var finalRoot = new { manifest, project = shell };
        await File.WriteAllTextAsync(Path.Combine(tempDir, ManifestFile),
            JsonSerializer.Serialize(finalRoot, AtlasJson.Options), ct);
    }

    /// <summary>Apertura base (sin recuperación): detecta formato, migra, verifica checksum y monta modelo.</summary>
    private async Task<(PersistenceResult Result, Project? Project)> OpenCoreAsync(string path, CancellationToken ct)
    {
        try
        {
            var manifestPath = Path.Combine(path, ManifestFile);
            if (!File.Exists(manifestPath))
                return (PersistenceResult.Fail("No es un proyecto .atlas válido (falta manifest.json)."), null);

            var rootText = await File.ReadAllTextAsync(manifestPath, ct);
            using var doc = JsonDocument.Parse(rootText);
            var root = doc.RootElement;

            if (!root.TryGetProperty("manifest", out var manifestEl))
                return (PersistenceResult.Fail("Manifest ausente en manifest.json."), null);
            if (!root.TryGetProperty("project", out var projectEl))
                return (PersistenceResult.Fail("Elemento 'project' ausente en manifest.json."), null);

            var manifest = manifestEl.Deserialize<AtlasManifest>(AtlasJson.Options)!;

            if (manifest.Format != "atlas-project")
                return (PersistenceResult.Fail($"Formato desconocido: '{manifest.Format}'."), null);

            var version = manifest.Version;
            if (version > CurrentFormatVersion)
                return (PersistenceResult.Fail($"Versión de formato {version} más nueva que la soportada ({CurrentFormatVersion})."), null);
            if (version < 0)
                return (PersistenceResult.Fail($"Versión de formato inválida: {version}."), null);

            // Verificar checksum del shell del proyecto (detecta corrupción del contenido raíz).
            if (!string.IsNullOrWhiteSpace(manifest.Checksum))
            {
                // Reeserializamos el shell de forma idéntica a como se calculó al guardar
                // (standalone, mismas opciones) para que el hash sea determinista e
                // independiente de la indentación/orden del documento anidado.
                var shell = projectEl.Deserialize<ProjectShell>(AtlasJson.Options);
                var actual = ComputeChecksum(JsonSerializer.Serialize(shell, AtlasJson.Options));
                if (!string.Equals(manifest.Checksum, actual, StringComparison.OrdinalIgnoreCase))
                    return (PersistenceResult.Fail("Checksum del proyecto no coincide (archivo corrupto)."), null);
            }

            var project = projectEl.Deserialize<Project>(AtlasJson.Options)!;
            project = Migrate(project, version);

            project.Scenes.Clear();
            foreach (var sceneFile in manifest.Scenes)
            {
                var scenePath = Path.Combine(path, "scenes", sceneFile);
                if (!File.Exists(scenePath)) continue;
                var scene = JsonSerializer.Deserialize<Scene>(await File.ReadAllTextAsync(scenePath, ct), AtlasJson.Options);
                if (scene != null) project.Scenes.Add(scene);
            }

            project.EmbeddedAssets.Clear();
            foreach (var assetFile in manifest.Assets)
            {
                var assetPath = Path.Combine(path, "assets", assetFile);
                if (!File.Exists(assetPath)) continue;
                var content = await File.ReadAllTextAsync(assetPath, ct);
                var key = Path.GetFileNameWithoutExtension(assetFile);
                project.EmbeddedAssets[key] = content;
            }

            if (project.Scenes.Count < 1)
                return (PersistenceResult.Fail("Proyecto sin escenas: archivo corrupto o vacío."), null);

            return (PersistenceResult.Ok(path), project);
        }
        catch (JsonException je)
        {
            return (PersistenceResult.Fail($"JSON inválido: {je.Message}"), null);
        }
        catch (Exception ex)
        {
            return (PersistenceResult.Fail(ex.Message), null);
        }
    }

    /// <summary>Recuperaci?n LastKnownGood: autosave → backup. Devuelve el proyecto recuperado si alguno valida.</summary>
    private async Task<(PersistenceResult Result, Project? Project)> TryRecoverAsync(string path, string originalError, CancellationToken ct)
    {
        var autoPath = path + AutosaveSuffix;
        if (Directory.Exists(autoPath))
        {
            var r = await OpenCoreAsync(autoPath, ct);
            if (r.Result.Success)
            {
                r.Result.Recovered = true;
                return r;
            }
        }

        var backupPath = path + BackupSuffix;
        if (Directory.Exists(backupPath))
        {
            var r = await OpenCoreAsync(backupPath, ct);
            if (r.Result.Success)
            {
                r.Result.Recovered = true;
                return r;
            }
        }

        return (PersistenceResult.Fail(originalError), null);
    }

    private static string ComputeChecksum(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    /// <summary>Migra un proyecto de `fromVersion` a la versión actual (en memoria; nunca toca el original, spec 16).</summary>
    private static Project Migrate(Project project, int fromVersion)
    {
        // Escal?n de migraciones acumuladas. v1 es la actual; al introducir v2 se a?ade
        // una etapa que transforma v1 -> v2 aquí, preservando el original en disco.
        if (fromVersion < 1)
            fromVersion = 1; // sin versión conocida: tratar como v1

        while (fromVersion < CurrentFormatVersion)
        {
            // Future: Migrate v1 -> v2, v2 -> v3, ...
            fromVersion++;
        }

        project.FormatVersion = CurrentFormatVersion;
        return project;
    }

    private static void SafeDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        catch { /* best-effort cleanup */ }
    }

    private static string SafeName(string id) =>
        Path.GetInvalidFileNameChars().Aggregate(id, (cur, c) => cur.Replace(c, '_'));

    #endregion
}

/// <summary>Proyección del proyecto sin escenas (se serializan aparte).</summary>
internal sealed class ProjectShell
{
    public ProjectShell()
    {
        Id = null!; Name = string.Empty; Canvas = new DSLetreros.Domain.ValueObjects.CanvasDefinition(1, 1);
    }

    public ProjectShell(Project p)
    {
        Id = p.Id; Name = p.Name; FormatVersion = p.FormatVersion;
        Canvas = p.Canvas; CreatedAt = p.CreatedAt; UpdatedAt = p.UpdatedAt;
    }
    public DSLetreros.Domain.ValueObjects.ProjectId Id { get; set; }
    public string Name { get; set; }
    public int FormatVersion { get; set; }
    public DSLetreros.Domain.ValueObjects.CanvasDefinition Canvas { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}