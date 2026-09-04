using DSLetreros.Domain.ValueObjects;

namespace DSLetreros.Domain.Entities;

/// <summary>Catálogo de assets disponibles (biblioteca).</summary>
public sealed class AssetCatalog
{
    public List<IconAsset> Icons { get; set; } = new();
    public List<CustomDrawingAsset> Drawings { get; set; } = new();
    public List<ImageAsset> Images { get; set; } = new();
}

/// <summary>Icono (pixel art) normalizado. Origen Pixelarticons FREE local.</summary>
public sealed class IconAsset
{
    public AssetId Id { get; set; } = AssetId.New();
    public string Name { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public string Category { get; set; } = string.Empty;
    public AssetLicenseInfo License { get; set; } = AssetLicenseInfo.Unset;
    public int Width { get; set; }
    public int Height { get; set; }
    public byte[] Pixels { get; set; } = Array.Empty<byte>();   // 1 byte/px indexado
    public List<RgbColor> Palette { get; set; } = new() { RgbColor.White };
    public string PreviewBase64 { get; set; } = string.Empty;

    /// <summary>
    /// Índice de la paleta que representa el fondo transparente (spec 14). Null/ausente
    /// = sin transparencia (todo índice se pinta). BuiltInIcons lo fija a 0: el fondo
    /// no borra los objetos que estén debajo.
    /// </summary>
    public int? TransparentIndex { get; set; }
}

/// <summary>Dibujo personalizado guardado en "Mi biblioteca".</summary>
public sealed class CustomDrawingAsset
{
    public AssetId Id { get; set; } = AssetId.New();
    public string Name { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public byte[] Pixels { get; set; } = Array.Empty<byte>();
    public List<RgbColor> Palette { get; set; } = new() { RgbColor.White };
}

/// <summary>Imagen rasterizada importada y cuantizada.</summary>
public sealed class ImageAsset
{
    public AssetId Id { get; set; } = AssetId.New();
    public string Name { get; set; } = string.Empty;
    public string SourceFormat { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public byte[] Pixels { get; set; } = Array.Empty<byte>();
    public List<RgbColor> Palette { get; set; } = new() { RgbColor.Black };
    public AssetLicenseInfo License { get; set; } = AssetLicenseInfo.Unset;
    public string ConversionMetadata { get; set; } = string.Empty;
}

/// <summary>Fuente bitmap certificada (conversión LED).</summary>
public sealed class BitmapFont
{
    public string Id { get; set; } = string.Empty;      // "4x6", "5x7", "8x8"
    public string Name { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Certified { get; set; }
    public AssetLicenseInfo License { get; set; } = AssetLicenseInfo.Unset;
    public Dictionary<char, string> Glyphs { get; set; } = new();   // char -> rows (bits)
}

/// <summary>Información de licencia/origen de un asset.</summary>
public sealed class AssetLicenseInfo
{
    public static readonly AssetLicenseInfo Unset = new();

    public string Origin { get; set; } = string.Empty;      // "Pixelarticons FREE", "propio", ...
    public string License { get; set; } = string.Empty;     // "MIT", "OFL", "CC0", ...
    public string SourceUrl { get; set; } = string.Empty;
    public string Attribution { get; set; } = string.Empty;
    public bool IsEmpty => string.IsNullOrEmpty(License) && string.IsNullOrEmpty(Origin);
}