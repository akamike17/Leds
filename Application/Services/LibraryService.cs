using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;
using DSLetreros.Infrastructure.Persistence;

namespace DSLetreros.Application.Services;

/// <summary>Biblioteca de usuario ("Mi biblioteca"): assets personalizados reutilizables (sección 14).</summary>
public sealed class LibraryService
{
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

    /// <summary>Guarda un dibujo personalizado en Mi biblioteca.</summary>
    public (bool success, string message, AssetId? id) SaveCustomDrawing(
        string name, int width, int height, byte[] pixels, List<RgbColor>? palette = null)
    {
        if (width <= 0 || height <= 0) return (false, "Dimensiones inválidas.", null);
        if (pixels == null || pixels.Length != width * height) return (false, "Datos de píxeles inconsistentes.", null);

        var asset = new CustomDrawingAsset
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Sin título" : name.Trim(),
            Width = width,
            Height = height,
            Pixels = pixels,
            Palette = palette ?? new List<RgbColor> { RgbColor.White },
        };
        try
        {
            var path = Path.Combine(_libraryRoot, $"{asset.Id.Value:N}.json");
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(asset, AtlasJson.Options));
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
        var path = Path.Combine(_libraryRoot, $"{id.Value:N}.json");
        if (!File.Exists(path)) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<CustomDrawingAsset>(File.ReadAllText(path), AtlasJson.Options);
        }
        catch { return null; }
    }

    public bool DeleteDrawing(AssetId id)
    {
        var path = Path.Combine(_libraryRoot, $"{id.Value:N}.json");
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }
}