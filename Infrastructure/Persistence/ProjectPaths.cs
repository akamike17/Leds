using System.Text.RegularExpressions;

namespace DSLetreros.Infrastructure.Persistence;

/// <summary>
/// Utilidad ÚNICA de canonicalización y containment para toda ruta de proyecto.
///
/// Toda ruta que apunte dentro del almacén de proyectos debe pasar por aquí:
/// la fuente de verdad es <c>ProjectsRoot</c> + un <see cref="Guid"/> de proyecto.
/// Cualquier ruta derivada (manifest, escenas, assets, fonts, previews, backups,
/// autosaves) se resuelve siempre dentro de la carpeta <c>&lt;id&gt;.atlas</c> y de
/// sus subdirectorios fijos, nunca a partir de input arbitrario.
/// </summary>
public static class ProjectPaths
{
    /// <summary>Subdirectorios fijos de un proyecto .atlas.</summary>
    public static readonly IReadOnlyList<string> Subdirectories = new[]
    {
        "scenes", "assets", "fonts", "previews",
    };

    // Nombre simple de archivo dentro de un proyecto .atlas: una secuencia de
    // caracteres seguros [0-9A-Za-z_-] terminada en `.json` (o literalmente
    // manifest.json). Rechaza de forma estructural los separadores de ruta, "..",
    // rutas rooted y cualquier carácter no alfanumérico fuera de [_-].
    private static readonly Regex SimpleName = new(
        "^[0-9A-Za-z_-]+\\.json$|^manifest\\.json$", RegexOptions.Compiled);

    /// <summary>
    /// Canonicaliza el root de proyectos (ruta física, sin symlinks) y crea el directorio.
    /// Único punto donde se define dónde viven los proyectos en disco.
    /// </summary>
    public static string CanonicalizeRoot(string projectsRoot)
    {
        if (string.IsNullOrWhiteSpace(projectsRoot))
            throw new ArgumentException("ProjectsRoot no puede ser vacío.", nameof(projectsRoot));

        var full = Path.GetFullPath(projectsRoot);
        if (!Directory.Exists(full))
            Directory.CreateDirectory(full);
        return full;
    }

    /// <summary>
    /// Resuelve la carpeta <c>&lt;root&gt;/&lt;id&gt;.atlas</c>. Rechaza Guid.Vacío.
    /// </summary>
    public static string ResolveProjectDirectory(string projectsRoot, Guid projectId)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("ProjectId no puede ser vacío.", nameof(projectId));
        return Path.Combine(CanonicalizeRoot(projectsRoot), $"{projectId:N}.atlas");
    }

    /// <summary>
    /// Verifica que <paramref name="path"/> (canonicalizado) quede ESTRICTAMENTE dentro
    /// de <paramref name="root"/>. Devuelve la ruta canonicalizada si está contenida;
    /// en caso contrario lanza <see cref="ProjectPathException"/>.
    /// </summary>
    public static string EnsureWithin(string root, string path)
    {
        var fullRoot = CanonicalizeRoot(root);
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var rootPrefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new ProjectPathException($"La ruta escapa del almacén de proyectos: '{path}'.");

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// Valida un nombre de archivo simple dentro de un proyecto .atlas
    /// (manifest.json o un nombre de 32 hex + .json). Rechaza separadores de
    /// ruta, "..", rutas rooted y extensiones inesperadas.
    /// </summary>
    public static bool IsSimpleFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        if (fileName.Contains('/') || fileName.Contains('\\')) return false;
        if (fileName.StartsWith('.') && fileName != "manifest.json") return false;
        return SimpleName.IsMatch(fileName);
    }

    /// <summary>
    /// Resuelve la ruta de un archivo de escena/asset dentro de un subdirectorio
    /// fijo del proyecto, validando nombre simple y containment.
    /// </summary>
    public static string ResolveProjectFile(string projectDir, string subdir, string fileName)
    {
        if (!Subdirectories.Contains(subdir))
            throw new ProjectPathException($"Subdirectorio no permitido: '{subdir}'.");
        if (!IsSimpleFileName(fileName))
            throw new ProjectPathException($"Nombre de archivo no permitido en proyecto: '{fileName}'.");

        var resolved = Path.GetFullPath(Path.Combine(projectDir, subdir, fileName));
        EnsureWithin(projectDir, resolved);
        return resolved;
    }
}

/// <summary>Excepción de seguridad de rutas de proyecto.</summary>
public sealed class ProjectPathException : Exception
{
    public ProjectPathException(string message) : base(message) { }
}