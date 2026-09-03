using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;

namespace DSLetreros.Domain.Rendering;

/// <summary>
/// Renderer central (invariante 4): un único Render(scene, time) define editor,
/// preview, simulador y compilación. Misma entrada → mismos píxeles.
/// Aplica timing + animaciones (Slice 6) de forma determinista vía AnimationEvaluator.
/// </summary>
public static class SceneRenderer
{
    /// <summary>Renderiza una escena a un framebuffer lógico en un instante t.</summary>
    public static FrameBuffer Render(Scene scene, TimeSpan time, CanvasDefinition canvas, IReadOnlyDictionary<string, string>? embeddedAssets = null)
    {
        AnimationEvaluator.ViewportWidth = canvas.Width; // Marquee envuelve respecto del canvas

        var fb = new FrameBuffer(canvas.Width, canvas.Height);
        fb.Clear(RgbColor.Black);
        embeddedAssets ??= new Dictionary<string, string>();

        var layers = scene.Layers.OrderBy(l => l.Order);
        foreach (var layer in layers)
        {
            if (!layer.Visible) continue;
            foreach (var obj in layer.Objects)
            {
                if (!obj.Visible) continue;
                var state = AnimationEvaluator.Evaluate(obj, time);
                if (!state.Visible) continue;
                RenderObject(fb, obj, time, state.Offset, state.BrightnessFactor, state.Clip, embeddedAssets);
            }
        }
        return fb;
    }

    private static void RenderObject(FrameBuffer fb, SceneObject obj, TimeSpan time,
        PixelPoint offset, double brightness, PixelRect? clip, IReadOnlyDictionary<string, string> embeddedAssets)
    {
        switch (obj)
        {
            case TextObject t: RenderText(fb, t, offset, brightness); break;
            case DrawingObject d: RenderDrawing(fb, d, offset, brightness, clip); break;
            case ShapeObject s: RenderShape(fb, s, offset, brightness, clip); break;
            case IconObject i: RenderIcon(fb, i, offset, brightness, clip, embeddedAssets); break;
            case ImageObject im: RenderImage(fb, im, offset, brightness, clip, embeddedAssets); break;
            // ObjectGroup no se renderiza (no tiene contenido visual propio)
        }
    }

    /// <summary>Aplica factor de brillo a un color (0..1), con clamp defensivo.</summary>
    private static RgbColor Scale(RgbColor c, double factor)
    {
        // Clamp explícito: evita comparaciones de borde cuyo mutante sería equivalente.
        var f = Math.Clamp(factor, 0.0, 1.0);
        if (f == 0.0) return RgbColor.Black;
        if (f == 1.0) return c;
        return new RgbColor((byte)Math.Round(c.R * f), (byte)Math.Round(c.G * f), (byte)Math.Round(c.B * f));
    }

    /// <summary>Indica si un píxel (en coordenadas del objeto) queda fuera del clip de revelado.</summary>
    private static bool Clipped(PixelRect? clip, int x, int y)
    {
        return clip.HasValue && !clip.Value.Contains(new PixelPoint(x, y));
    }

    private static void RenderText(FrameBuffer fb, TextObject t, PixelPoint offset, double brightness)
    {
        var font = BitmapFontCatalog.Get(t.FontId);
        if (string.IsNullOrEmpty(t.Text)) return;

        int originX = t.Position.X + offset.X;
        int originY = t.Position.Y + offset.Y;
        var color = Scale(t.Color, brightness);

        int curX = originX;
        foreach (var ch in t.Text)
        {
            var glyph = font.Get(ch);
            if (glyph == null) { curX += font.MeasureGlyph(ch); continue; }
            DrawGlyph(fb, glyph, curX, originY, color, font);
            curX += font.MeasureGlyph(ch);
        }
    }

    private static void DrawGlyph(FrameBuffer fb, byte[] glyph, int x, int y, RgbColor color, BitmapFontAccessor font)
    {
        for (int row = 0; row < font.Height; row++)
        {
            byte bits = glyph[row];
            for (int col = 0; col < font.Width; col++)
            {
                if ((bits & (1 << col)) != 0)
                    fb.SetPixel(x + col, y + row, color);
            }
        }
    }

    private static void RenderDrawing(FrameBuffer fb, DrawingObject d, PixelPoint offset, double brightness, PixelRect? clip)
    {
        var color = Scale(d.Palette.Count > 0 ? d.Palette[0] : RgbColor.White, brightness);
        int w = d.Size.Width;
        int h = d.Size.Height;
        int px = d.Position.X + offset.X, py = d.Position.Y + offset.Y;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            if (Clipped(clip, x, y)) continue;
            int idx = y * w + x;
            if (idx >= d.PixelData.Length) continue;
            if (d.PixelData[idx] != 0)
                fb.SetPixel(px + x, py + y, color);
        }
    }

    private static void RenderShape(FrameBuffer fb, ShapeObject s, PixelPoint offset, double brightness, PixelRect? clip)
    {
        int x0 = s.Position.X + offset.X, y0 = s.Position.Y + offset.Y;
        int w = s.Size.Width, h = s.Size.Height;
        var stroke = Scale(s.StrokeColor, brightness);
        var fill = Scale(s.FillColor, brightness);
        switch (s.Shape)
        {
            case ShapeKind.Rectangle:
                DrawRect(fb, x0, y0, w, h, stroke, fill, s.Filled, clip);
                break;
            case ShapeKind.Ellipse:
                DrawEllipse(fb, x0, y0, w, h, stroke, fill, s.Filled, clip);
                break;
            case ShapeKind.Line:
                DrawLine(fb, x0, y0, x0 + w - 1, y0 + h - 1, stroke, clip);
                break;
        }
    }

    private static void DrawRect(FrameBuffer fb, int x, int y, int w, int h, RgbColor stroke, RgbColor fill, bool filled, PixelRect? clip)
    {
        for (int i = 0; i < w; i++)
        for (int j = 0; j < h; j++)
        {
            if (Clipped(clip, i, j)) continue;
            bool border = i == 0 || i == w - 1 || j == 0 || j == h - 1;
            if (border || filled) fb.SetPixel(x + i, y + j, border ? stroke : fill);
        }
    }

    private static void DrawEllipse(FrameBuffer fb, int x, int y, int w, int h, RgbColor stroke, RgbColor fill, bool filled, PixelRect? clip)
    {
        double cx = x + (w - 1) / 2.0, cy = y + (h - 1) / 2.0;
        double rx = (w - 1) / 2.0, ry = (h - 1) / 2.0;
        for (int i = 0; i < w; i++)
        for (int j = 0; j < h; j++)
        {
            if (Clipped(clip, i, j)) continue;
            double nx = (i - cx) / Math.Max(rx, 0.5);
            double ny = (j - cy) / Math.Max(ry, 0.5);
            double v = nx * nx + ny * ny;
            if (v <= 1.0)
            {
                bool border = v >= 0.65;
                fb.SetPixel(x + i, y + j, filled || border ? (border ? stroke : fill) : stroke);
            }
        }
    }

    private static void DrawLine(FrameBuffer fb, int x0, int y0, int x1, int y1, RgbColor color, PixelRect? clip)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        int x = x0, y = y0;
        // Bucle acotado: el trazo de Bresenham recorre exactamente |dx|+|dy| pasos
        // (más el punto inicial). El bound duro garantiza terminación (nunca cuelga).
        int maxSteps = dx + Math.Abs(dy) + 1;
        for (int step = 0; step < maxSteps; step++)
        {
            if (!Clipped(clip, x - x0, y - y0)) fb.SetPixel(x, y, color);
            if (x == x1 && y == y1) return;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
        }
    }

    private static void RenderIcon(FrameBuffer fb, IconObject icon, PixelPoint offset, double brightness, PixelRect? clip, IReadOnlyDictionary<string, string> embeddedAssets)
    {
        if (icon.AssetId == null) return;
        var asset = ResolveAsset(embeddedAssets, icon.AssetId.Value);
        if (asset == null) return;
        DrawIndexed(fb, asset, new PixelPoint(icon.Position.X + offset.X, icon.Position.Y + offset.Y), icon, brightness, clip);
    }

    private static void RenderImage(FrameBuffer fb, ImageObject image, PixelPoint offset, double brightness, PixelRect? clip, IReadOnlyDictionary<string, string> embeddedAssets)
    {
        if (image.AssetId == null) return;
        var asset = ResolveAsset(embeddedAssets, image.AssetId.Value);
        if (asset == null) return;
        DrawIndexed(fb, asset, new PixelPoint(image.Position.X + offset.X, image.Position.Y + offset.Y), image, brightness, clip);
    }

    /// <summary>Resuelve y deserializa un asset embebido (IconAsset o ImageAsset) por ID.</summary>
    private static AssetPixelData? ResolveAsset(IReadOnlyDictionary<string, string> embeddedAssets, Guid assetId)
    {
        var key = assetId.ToString("N");
        if (!embeddedAssets.TryGetValue(key, out var json)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var width = root.GetProperty("width").GetInt32();
            var height = root.GetProperty("height").GetInt32();
            var pixels = Convert.FromBase64String(root.GetProperty("pixels").GetString() ?? string.Empty);
            var palette = new List<RgbColor>();
            if (root.TryGetProperty("palette", out var palArr))
                foreach (var p in palArr.EnumerateArray())
                    palette.Add(new RgbColor(p.GetProperty("r").GetByte(), p.GetProperty("g").GetByte(), p.GetProperty("b").GetByte()));
            return new AssetPixelData(width, height, pixels, palette);
        }
        catch { return null; }
    }

    private static void DrawIndexed(FrameBuffer fb, AssetPixelData asset, PixelPoint pos, SceneObject obj, double brightness, PixelRect? clip)
    {
        var palette = asset.Palette;
        if (palette.Count == 0) palette = new List<RgbColor> { RgbColor.White };
        for (int y = 0; y < asset.Height; y++)
        for (int x = 0; x < asset.Width; x++)
        {
            if (Clipped(clip, x, y)) continue;
            int idx = y * asset.Width + x;
            if (idx >= asset.Pixels.Length) continue;
            int pi = asset.Pixels[idx];
            if (pi < 0 || pi >= palette.Count) continue;
            RgbColor color = palette[pi];
            if (obj is IconObject icon && icon.PaletteMode == IconPaletteMode.Tint)
                color = icon.Tint;
            fb.SetPixel(pos.X + x, pos.Y + y, Scale(color, brightness));
        }
    }

    private sealed class AssetPixelData
    {
        public AssetPixelData(int w, int h, byte[] pixels, List<RgbColor> palette)
        { Width = w; Height = h; Pixels = pixels; Palette = palette; }
        public int Width; public int Height; public byte[] Pixels; public List<RgbColor> Palette;
    }
}