using DSLetreros.Domain.ValueObjects;

namespace DSLetreros.Domain.Entities;

/// <summary>Objeto visible sobre el lienzo. Todo contenido visible es un objeto (invariante 5).</summary>
public abstract class SceneObject
{
    protected SceneObject()
    {
        Id = ObjectId.New();
        Name = string.Empty;
        Position = new PixelPoint(0, 0);
        Size = new PixelSize(0, 0);
        Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    public ObjectId Id { get; set; }
    public string Name { get; set; }
    public PixelPoint Position { get; set; }
    public PixelSize Size { get; set; }
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public byte Brightness { get; set; } = 255;
    public TimeRange Timing { get; set; }
    public List<AnimationDefinition> Animations { get; set; } = new();

    /// <summary>Tipo concreto para serialización polimórfica.</summary>
    public abstract string Kind { get; }
}

/// <summary>Texto LED con fuente, color y alineaciones.</summary>
public sealed class TextObject : SceneObject
{
    public override string Kind => "text";

    public string Text { get; set; } = string.Empty;
    public string FontId { get; set; } = "5x7";
    public RgbColor Color { get; set; } = RgbColor.White;
    public TextAlignment HorizontalAlignment { get; set; } = TextAlignment.Left;
    public TextAlignment VerticalAlignment { get; set; } = TextAlignment.Top;
    public TextLayoutMode LayoutMode { get; set; } = TextLayoutMode.Fit;
}

/// <summary>Icono referenciado por AssetId con tinte/paleta.</summary>
public sealed class IconObject : SceneObject
{
    public override string Kind => "icon";

    public AssetId? AssetId { get; set; }
    public IconPaletteMode PaletteMode { get; set; } = IconPaletteMode.Original;
    public RgbColor Tint { get; set; } = RgbColor.White;
}

/// <summary>Dibujo de lápiz. Una sesión continua de lápiz = un DrawingObject (invariante 6).</summary>
public sealed class DrawingObject : SceneObject
{
    public override string Kind => "drawing";

    /// <summary>Bits por píxel: 1 = monocromo indexado a Palette.</summary>
    public int BitsPerPixel { get; set; } = 1;
    public List<RgbColor> Palette { get; set; } = new() { RgbColor.White };
    public byte[] PixelData { get; set; } = Array.Empty<byte>();
    public PixelRect Bounds { get; set; }
}

/// <summary>Forma geométrica (línea/rectángulo/elipse).</summary>
public sealed class ShapeObject : SceneObject
{
    public override string Kind => "shape";

    public ShapeKind Shape { get; set; } = ShapeKind.Rectangle;
    public RgbColor StrokeColor { get; set; } = RgbColor.White;
    public RgbColor FillColor { get; set; } = RgbColor.Black;
    public int StrokeWidth { get; set; } = 1;
    public bool Filled => FillColor != RgbColor.Black;
}

/// <summary>Imagen importada (raster). Referencia asset + representación de píxeles.</summary>
public sealed class ImageObject : SceneObject
{
    public override string Kind => "image";

    public AssetId? AssetId { get; set; }
    public string ConversionMetadata { get; set; } = string.Empty;
}

/// <summary>Agrupa miembros; no tiene contenido visual ni timing propio (invariante 7).</summary>
public sealed class ObjectGroup
{
    public List<ObjectId> MemberIds { get; set; } = new();
    public string Name { get; set; } = string.Empty;
}

public enum TextAlignment { Left, Center, Right, Top, Middle, Bottom }
public enum TextLayoutMode { Fit, Multiline, Marquee }
public enum IconPaletteMode { Original, Tint, Monochrome }
public enum ShapeKind { Line, Rectangle, Ellipse }