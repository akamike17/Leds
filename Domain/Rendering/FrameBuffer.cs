using DSLetreros.Domain.ValueObjects;

namespace DSLetreros.Domain.Rendering;

/// <summary>Contexto de render: canvas de destino y tiempo.</summary>
public sealed class RenderContext
{
    public CanvasDefinition Canvas { get; set; }
    public TimeSpan Time { get; set; }
}

/// <summary>Verdad visual: buffer de píxeles lógicos. Misma entrada = mismos píxeles (invariante 4).</summary>
public sealed class FrameBuffer
{
    private readonly RgbColor[] _pixels;

    public int Width { get; }
    public int Height { get; }

    public FrameBuffer(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Buffer con dimensiones positivas.");
        Width = width;
        Height = height;
        _pixels = new RgbColor[width * height];
    }

    public RgbColor GetPixel(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return RgbColor.Black;
        return _pixels[y * Width + x];
    }

    public void SetPixel(int x, int y, RgbColor color)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return;
        _pixels[y * Width + x] = color;
    }

    public void Clear(RgbColor color = default) => Array.Fill(_pixels, color);

    public IEnumerable<RgbColor> AllPixels() => _pixels;
}