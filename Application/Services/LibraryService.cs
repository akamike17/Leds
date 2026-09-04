using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;
using DSLetreros.Infrastructure.Persistence;

namespace DSLetreros.Application.Services;

/// <summary>Biblioteca de usuario ("Mi biblioteca"): assets personalizados reutilizables (sección 14).</summary>
public sealed class LibraryService
{
    /// <summary>Máximo de píxeles por dibujo (ancho * alto), aplicado con checked.</summary>
    public const int MaxDrawingPixels = 512 * 512;

    /// <summary>Máximo de dimensiones individuales de un dibujo.</summary>
    public const int MaxDrawingDimension = 512;

    /// <summary>Máximo de entradas de paleta por dibujo.</summary>
    public const int MaxPaletteEntries = 256;

    /// <summary>Máximo de caracteres en el nombre de un dibujo.</summary>
    public const int MaxNameLength = 256;

    private readonly string _libraryRoot;
    private readonly object _lock = new();

    public LibraryService(IWebHostEnvironment env)
        : this(Path.Combine(env.ContentRootPath, "App_Data", "library"))
    {
    }

    /// <summary>Constructor para tests: root explícito.</summary>
    public LibraryService(string libraryRoot)
    {
        _libraryRoot = libraryRoot;
        Directory.CreateDirectory(_libraryRoot);
    }

    public string LibraryRoot => _libraryRoot;

    /// <summary>Guarda un dibujo personalizado en Mi biblioteca (escritura atómica).</summary>
    public (bool success, string message, AssetId? id) SaveCustomDrawing(
        string name, int width, int height, byte[] pixels, List<RgbColor>? palette = null)
    {
        if (width <= 0 || height <= 0)
            return (false, "Dimensiones inválidas.", null);
        if (width > MaxDrawingDimension || height > MaxDrawingDimension)
            return (false, $"Dimensiones fuera de límites (máx {MaxDrawingDimension}).", null);

        // checked width*height: rechaza overflow y excesos de píxeles.
        int pixelCount;
        try
        {
            pixelCount = checked(width * height);
        }
        catch (OverflowException)
        {
            return (false, "El tamaño del dibujo desborda el cálculo de píxeles.", null);
        }
        if (pixelCount > MaxDrawingPixels)
            return (false, $"El dibujo de {pixelCount} píxeles supera el máximo de {MaxDrawingPixels}.", null);

        if (pixels == null || pixels.LongLength != pixelCount)
            return (false, "Datos de píxeles inconsistentes.", null);

        var effectivePalette = palette ?? new List<RgbColor> { RgbColor.White };
        if (effectivePalette.Count == 0)
            return (false, "La paleta no puede estar vacía.", null);
        if (effectivePalette.Count > MaxPaletteEntries)
            return (false, $"La paleta supera el máximo de {MaxPaletteEntries} colores.", null);

        var trimmedName = string.IsNullOrWhiteSpace(name) ? "Sin título" : name.Trim();
        if (trimmedName.Length > MaxNameLength)
            trimmedName = trimmedName[..MaxNameLength];

        var asset = new CustomDrawingAsset
        {
            Name = trimmedName,
            Width = width,
            Height = height,
            Pixels = pixels,
            Palette = effectivePalette,
        };

        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(asset, AtlasJson.Options);
            WriteAtomic(Path.Combine(_libraryRoot, $"{asset.Id.Value:N}.json"), json);
            return (true, "Guardado en Mi biblioteca.", asset.Id);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }

    public IReadOnlyList<CustomDrawingAsset> ListDrawings()
    {
        lock (_lock)
        {
            var result = new List<CustomDrawingAsset>();
            foreach (var f in Directory.EnumerateFiles(_libraryRoot, "*.json"))
            {
                // Ignora archivos temporales de escritura atómica e imágenes (i-*.json).
                if (f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                if (Path.GetFileName(f).StartsWith("i-", StringComparison.Ordinal)) continue;
                try
                {
                    var asset = System.Text.Json.JsonSerializer.Deserialize<CustomDrawingAsset>(
                        File.ReadAllText(f), AtlasJson.Options);
                    if (asset != null) result.Add(asset);
                }
                catch { /* skip corrupt */ }
            }
            return result.OrderBy(a => a.Name, StringComparer.Ordinal).ToList();
        }
    }

    public CustomDrawingAsset? GetDrawing(AssetId id)
    {
        lock (_lock)
        {
            var path = Path.Combine(_libraryRoot, $"{id.Value:N}.json");
            if (!File.Exists(path)) return null;
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<CustomDrawingAsset>(File.ReadAllText(path), AtlasJson.Options);
            }
            catch { return null; }
        }
    }

    public bool DeleteDrawing(AssetId id)
    {
        lock (_lock)
        {
            var path = Path.Combine(_libraryRoot, $"{id.Value:N}.json");
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
    }

    /// <summary>Guarda una imagen importada (rasterizada) en la biblioteca (escritura atómica).</summary>
    public (bool success, string message, AssetId? id) SaveCustomImage(
        string name, string sourceFormat, int width, int height, byte[] pixels,
        List<RgbColor>? palette = null, string conversionMetadata = "")
    {
        if (width <= 0 || height <= 0) return (false, "Dimensiones inválidas.", null);

        int pixelCount;
        try { pixelCount = checked(width * height); }
        catch (OverflowException) { return (false, "Desborde de píxeles.", null); }
        if (pixelCount > MaxDrawingPixels) return (false, $"Imagen supera el máximo de {MaxDrawingPixels} píxeles.", null);

        try
        {
            var asset = new ImageAsset
            {
                Name = string.IsNullOrWhiteSpace(name) ? "Imagen" : name.Trim(),
                SourceFormat = sourceFormat ?? "",
                Width = width,
                Height = height,
                Pixels = pixels ?? Array.Empty<byte>(),
                Palette = palette ?? new List<RgbColor>(),
                ConversionMetadata = conversionMetadata ?? "",
                License = new AssetLicenseInfo { Origin = "importado por usuario", License = "desconocida" },
            };
            var json = System.Text.Json.JsonSerializer.Serialize(asset, AtlasJson.Options);
            WriteAtomic(Path.Combine(_libraryRoot, $"i-{asset.Id.Value:N}.json"), json);
            return (true, "Imagen guardada.", asset.Id);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }

    public IReadOnlyList<ImageAsset> ListImages()
    {
        lock (_lock)
        {
            var result = new List<ImageAsset>();
            foreach (var f in Directory.EnumerateFiles(_libraryRoot, "i-*.json"))
            {
                if (f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var asset = System.Text.Json.JsonSerializer.Deserialize<ImageAsset>(File.ReadAllText(f), AtlasJson.Options);
                    if (asset != null) result.Add(asset);
                }
                catch { /* skip corrupt */ }
            }
            return result.OrderBy(a => a.Name, StringComparer.Ordinal).ToList();
        }
    }

    public bool DeleteImage(AssetId id)
    {
        lock (_lock)
        {
            var path = Path.Combine(_libraryRoot, $"i-{id.Value:N}.json");
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
    }

    /// <summary>
    /// Escritura atómica: escribe a un temporal en el mismo directorio y luego
    /// lo renombra al destino, garantizando que los lectores nunca vean un archivo
    /// a medio escribir.
    /// </summary>
    private void WriteAtomic(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        try
        {
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { /* best-effort */ } }
            throw;
        }
    }
}