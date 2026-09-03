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

    /// <summary>
    /// Checksum SHA-256 del contenido significativo COMPLETO del proyecto:
    /// manifest canónico (sin checksum) + project shell + cada scene + cada asset.
    /// Cualquier modificación a una scene o asset (aunque el shell/manifest queden
    /// intactos) invalida el checksum.
    /// </summary>
    public string? Checksum { get; set; }
}

/// <summary>Resultado de una operación de persistencia.</summary>
public sealed class PersistenceResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;

    /// <summary>Indica que el proyecto se recuperó desde un backup/autosave tras corrupción.</summary>
    public bool Recovered { get; set; }

    public static PersistenceResult Ok(string path) => new() { Success = true, Path = path };
    public static PersistenceResult Fail(string message) => new() { Success = false, Message = message };
}

/// <summary>
/// Almacenamiento local .atlas autocontenido. Sin SQL (spec sección 3).
///
/// Slice 8: atomic save validado, checksum de contenido completo, recovery
/// LastKnownGood, autosave y migraciones. Blindado contra path traversal:
/// toda ruta se resuelve vía <see cref="ProjectPaths"/> (containment estricto),
/// rechazando rutas rooted, "..", separadores y nombres de archivo no simples.
/// </summary>
public sealed class AtlasProjectStore
{
    public const string ManifestFile = "manifest.json";
    public const int CurrentFormatVersion = 1;
    public const string BackupSuffix = ".bak";
    public const string AutosaveSuffix = ".autosave";

    /// <summary>
    /// Puntos de fallo para fault-injection en tests. Son hooks internos: un test
    /// puede inyectar un <see cref="Func{string,int}"/> que lance en una fase dada
    /// (ver <c>AtlasStoreFaultInjectionTests</c>). Null en producción.
    /// </summary>
    internal Func<string, bool>? FailPoint { get; set; }

    #region Save / Open / Autosave

    /// <summary>
    /// Guarda un proyecto como carpeta .atlas con reemplazo atómico validado y
    /// crash-safe (invariante 9). El backup se mantiene hasta que el NUEVO principal
    /// ha sido validado; si el segundo rename/move falla, el backup se restaura.
    /// </summary>
    public async Task<PersistenceResult> SaveAsync(Project project, string targetPath, CancellationToken ct = default)
    {
        // Defensa: la ruta de destino debe quedar dentro de su propio contexto.
        // (ProjectService ya resuelve <id>.atlas desde ProjectsRoot; aquí sólo
        //  rechazamos rutas claramente inválidas.)
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

            // 2. Validar el temp antes de reemplazar (crash-safe): vuelve a abrirlo con
            //    verificación de formato Y de checksum (sin recuperación).
            var probe = await OpenCoreAsync(tempDir, ct);
            if (!probe.Result.Success)
            {
                SafeDeleteDir(tempDir);
                return PersistenceResult.Fail("Temp no valida: " + probe.Result.Message);
            }

            // 3. Reemplazo atómico con backup recuperable.
            bool existed = Directory.Exists(dir);
            string? backupDir = null;
            if (existed)
            {
                backupDir = dir + BackupSuffix;
                SafeDeleteDir(backupDir);
                FailPoint?.Invoke("before-rename-main");
                Directory.Move(dir, backupDir);
            }

            try
            {
                FailPoint?.Invoke("before-rename-temp");
                Directory.Move(tempDir, dir);
            }
            catch
            {
                // Falló el segundo rename/move: restaurar el backup si existía.
                if (existed && backupDir != null && Directory.Exists(backupDir) && !Directory.Exists(dir))
                {
                    try { Directory.Move(backupDir, dir); }
                    catch { /* el backup queda en disco para recuperación manual */ }
                }
                SafeDeleteDir(tempDir);
                throw;
            }

            // 4. Sólo ahora, con el nuevo principal ya movido, se valida el principal
            //    y —validado— se descarta el backup. El backup se conserva si el
            //    principal no valida (defensa ante escritura parcial).
            if (existed && backupDir != null)
            {
                var finalProbe = await OpenCoreAsync(dir, ct);
                if (finalProbe.Result.Success)
                {
                    SafeDeleteDir(backupDir);
                }
                // else: se conserva el backup para recuperación manual posterior.
            }

            return PersistenceResult.Ok(dir);
        }
        catch (Exception ex)
        {
            return PersistenceResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Autosave separado: nunca toca el original, escribe un hermano `.autosave`
    /// con el MISMO mecanismo crash-safe (temp + validación + move).
    /// </summary>
    public async Task<PersistenceResult> AutosaveAsync(Project project, string targetPath, CancellationToken ct = default)
    {
        try
        {
            var validation = ProjectValidator.Validate(project);
            if (!validation.IsValid)
                return PersistenceResult.Fail("Proyecto inválido: " + string.Join("; ", validation.Errors));

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

            FailPoint?.Invoke("before-autosave-move");
            SafeDeleteDir(autoDir);
            Directory.Move(tempDir, autoDir);
            return PersistenceResult.Ok(autoDir);
        }
        catch (Exception ex)
        {
            return PersistenceResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Abre un proyecto .atlas. Detecta formato, migra, valida checksum completo,
    /// y recupera desde autosave → backup si el principal está corrupto o ausente.
    /// </summary>
    public async Task<(PersistenceResult Result, Project? Project)> OpenAsync(string path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return (PersistenceResult.Fail("Ruta vacía."), null);

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
        foreach (var sub in ProjectPaths.Subdirectories)
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

        // El checksum cubre TODO el contenido significativo: manifest canónico
        // + project shell + el archivo de CADA scene + el contenido de CADA asset.
        manifest.Checksum = ComputeContentChecksum(manifest, shell, project);

        var finalRoot = new { manifest, project = shell };
        await File.WriteAllTextAsync(Path.Combine(tempDir, ManifestFile),
            JsonSerializer.Serialize(finalRoot, AtlasJson.Options), ct);
    }

    /// <summary>
    /// Checksum determinista de contenido: manifest canónico (sin checksum ni
    /// timestamp volátil) + shell (identidad, sin timestamps) + scenes + assets.
    /// Cada scene/asset se hashea individualmente (árbol de hashes), de modo que
    /// una modificación en un único archivo detecta corrupción aunque el
    /// shell/manifest sigan intactos. Los timestamps (CreatedAt/UpdatedAt) y el
    /// checksum en sí son metadatos volátiles y NO entran en el hash, para que el
    /// checksum sea estable entre escritura y relectura.
    /// </summary>
    private static string ComputeContentChecksum(AtlasManifest manifest, ProjectShell shell, Project project)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8);

        void WriteStr(string s)
        {
            var b = Encoding.UTF8.GetBytes(s ?? string.Empty);
            w.Write(b.Length);
            w.Write(b);
        }

        // 1. manifest canónico (sin checksum, sin version/format ni timestamps:
        //    versión/formato son metadata de migración que varía legítimamente al migrar)
        WriteStr(manifest.ProjectId.ToString("N"));
        WriteStr(manifest.Name);
        w.Write(manifest.Canvas);
        foreach (var s in manifest.Scenes.OrderBy(x => x, StringComparer.Ordinal)) WriteStr(s);
        foreach (var a in manifest.Assets.OrderBy(x => x, StringComparer.Ordinal)) WriteStr(a);
        foreach (var f in manifest.Fonts.OrderBy(x => x, StringComparer.Ordinal)) WriteStr(f);

        // 2. project shell canónico (identidad, sin timestamps volátiles)
        WriteStr(shell.Id.Value.ToString("N"));
        WriteStr(shell.Name);
        w.Write(shell.FormatVersion);
        w.Write(shell.Canvas.Width);
        w.Write(shell.Canvas.Height);

        // 3. cada scene, hasheada de forma estable por id
        foreach (var scene in project.Scenes.OrderBy(s => s.Id.Value))
        {
            WriteStr(scene.Id.Value.ToString("N"));
            WriteStr(Sha256Hex(JsonSerializer.Serialize(scene, AtlasJson.Options)));
        }

        // 4. cada asset, hasheado por su nombre de archivo SANITIZADO (lo que realmente
        //    queda en disco y en manifest.Assets). El contenido es el JSON ya embebido.
        foreach (var kv in project.EmbeddedAssets.OrderBy(k => SafeName(k.Key), StringComparer.Ordinal))
        {
            WriteStr(SafeName(kv.Key));
            WriteStr(Sha256Hex(kv.Value));
        }

        return Sha256Hex(ms.ToArray());
    }

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Sha256Hex(string content) => Sha256Hex(Encoding.UTF8.GetBytes(content));

    /// <summary>Apertura base (sin recuperación): detecta formato, migra, verifica checksum y monta modelo.</summary>
    private async Task<(PersistenceResult Result, Project? Project)> OpenCoreAsync(string path, CancellationToken ct)
    {
        try
        {
            // manifest.json vive directamente en la raíz del proyecto; los nombres
            // de los archivos de contenido se validan de forma estricta más abajo.
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

            // Validar nombres de archivo simples ANTES de leerlos (path traversal).
            foreach (var sceneFile in manifest.Scenes)
            {
                if (!ProjectPaths.IsSimpleFileName(sceneFile))
                    return (PersistenceResult.Fail($"Nombre de escena no permitido: '{sceneFile}'."), null);
            }
            foreach (var assetFile in manifest.Assets)
            {
                if (!ProjectPaths.IsSimpleFileName(assetFile))
                    return (PersistenceResult.Fail($"Nombre de asset no permitido: '{assetFile}'."), null);
            }
            foreach (var fontFile in manifest.Fonts)
            {
                if (!ProjectPaths.IsSimpleFileName(fontFile))
                    return (PersistenceResult.Fail($"Nombre de fuente no permitido: '{fontFile}'."), null);
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

            // Verificar checksum COMPLETO de contenido si está presente.
            if (!string.IsNullOrWhiteSpace(manifest.Checksum))
            {
                var shell = projectEl.Deserialize<ProjectShell>(AtlasJson.Options);
                var mantifestForHash = new AtlasManifest
                {
                    Format = manifest.Format,
                    Version = manifest.Version,
                    ProjectId = manifest.ProjectId,
                    Name = manifest.Name,
                    Canvas = manifest.Canvas,
                    UpdatedAt = manifest.UpdatedAt,
                    Scenes = manifest.Scenes,
                    Assets = manifest.Assets,
                    Fonts = manifest.Fonts,
                    Checksum = null,
                };
                var actual = ComputeContentChecksum(mantifestForHash, shell!, project);
                if (!string.Equals(manifest.Checksum, actual, StringComparison.OrdinalIgnoreCase))
                    return (PersistenceResult.Fail("Checksum del contenido no coincide (archivo corrupto o modificado)."), null);
            }

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

    /// <summary>Recuperación LastKnownGood: autosave → backup. Devuelve el recuperado si alguno valida.</summary>
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

    /// <summary>Migra un proyecto de `fromVersion` a la versión actual (en memoria; nunca toca el original, spec 16).</summary>
    private static Project Migrate(Project project, int fromVersion)
    {
        if (fromVersion < 1)
            fromVersion = 1;

        while (fromVersion < CurrentFormatVersion)
        {
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

    /// <summary>
    /// Sanitiza un nombre de archivo de asset a un único segmento seguro: reemplaza
    /// TODO carácter no [0-9A-Za-z_-] (incluidos '.', '/', '\\') por '_', de modo
    /// que el resultado nunca contenga separadores de ruta ni puntos que permitan
    /// traversal. El nombre resultante pasa la validación <see cref="ProjectPaths.IsSimpleFileName"/>.
    /// </summary>
    private static string SafeName(string id)
    {
        if (string.IsNullOrEmpty(id)) return "_";
        var sb = new StringBuilder(id.Length);
        foreach (var c in id)
        {
            sb.Append(char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
        }
        var result = sb.ToString();
        return result.Length == 0 ? "_" : result;
    }

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