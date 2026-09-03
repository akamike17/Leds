using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;

namespace DSLetreros.Application.Services;

/// <summary>Rasterizador de imágenes: crop → nearest-neighbor scale → quantize → dither (sección 15).</summary>
public static class ImageRasterizer
{
    /// <summary>
    /// Convierte una imagen RGB (ya decodificada) en un PixelAsset para LED.
    /// Pipeline: crop opcional → escala nearest-neighbor → cuantización → dithering opcional.
    /// </summary>
    public static RasterResult Rasterize(
        byte[] rgba, int srcWidth, int srcHeight,
        int targetWidth, int targetHeight,
        bool dither = true,
        int maxColors = 16)
    {
        if (rgba == null || rgba.Length < srcWidth * srcHeight * 4)
            return RasterResult.Fail("Buffer de imagen inválido.");
        if (targetWidth <= 0 || targetHeight <= 0 || targetWidth > 512 || targetHeight > 512)
            return RasterResult.Fail("Dimensiones objetivo fuera de rango (1..512).");

        // 1) nearest-neighbor scale (crop implícito por redimensión completa)
        var scaled = new byte[targetWidth * targetHeight * 4];
        for (int y = 0; y < targetHeight; y++)
        for (int x = 0; x < targetWidth; x++)
        {
            int sx = Math.Min((int)((double)x * srcWidth / targetWidth), srcWidth - 1);
            int sy = Math.Min((int)((double)y * srcHeight / targetHeight), srcHeight - 1);
            int si = (sy * srcWidth + sx) * 4;
            int di = (y * targetWidth + x) * 4;
            scaled[di] = rgba[si];
            scaled[di + 1] = rgba[si + 1];
            scaled[di + 2] = rgba[si + 2];
            scaled[di + 3] = rgba[si + 3];
        }

        // 2) quantize a una paleta (median cut simplificado → color directo)
        var palette = BuildPalette(scaled, targetWidth * targetHeight, maxColors);

        // 3) mapear cada píxel al índice más cercano (+ dithering Floyd-Steinberg si aplica)
        int n = targetWidth * targetHeight;
        var indices = new byte[n];
        // buffers de error para dithering
        var workR = new int[n];
        var workG = new int[n];
        var workB = new int[n];
        for (int i = 0; i < n; i++)
        {
            workR[i] = scaled[i * 4];
            workG[i] = scaled[i * 4 + 1];
            workB[i] = scaled[i * 4 + 2];
        }

        for (int y = 0; y < targetHeight; y++)
        for (int x = 0; x < targetWidth; x++)
        {
            int idx = y * targetWidth + x;
            int r = Math.Clamp(workR[idx], 0, 255);
            int g = Math.Clamp(workG[idx], 0, 255);
            int b = Math.Clamp(workB[idx], 0, 255);
            int nearest = NearestPalette(palette, r, g, b);
            indices[idx] = (byte)nearest;

            if (dither)
            {
                int er = r - palette[nearest].R;
                int eg = g - palette[nearest].G;
                int eb = b - palette[nearest].B;
                Diffuse(workR, workG, workB, targetWidth, targetHeight, x, y, er, eg, eb);
            }
        }

        return RasterResult.Ok(targetWidth, targetHeight, indices, palette);
    }

    private static void Diffuse(int[] r, int[] g, int[] b, int w, int h, int x, int y, int er, int eg, int eb)
    {
        void Add(int nx, int ny, int f)
        {
            if (nx < 0 || nx >= w || ny < 0 || ny >= h) return;
            int i = ny * w + nx;
            r[i] += er * f / 16;
            g[i] += eg * f / 16;
            b[i] += eb * f / 16;
        }
        Add(x + 1, y, 7);
        Add(x - 1, y + 1, 3);
        Add(x, y + 1, 5);
        Add(x + 1, y + 1, 1);
    }

    private static int NearestPalette(List<RgbColor> palette, int r, int g, int b)
    {
        int best = 0, bestDist = int.MaxValue;
        for (int i = 0; i < palette.Count; i++)
        {
            var c = palette[i];
            int dr = r - c.R, dg = g - c.G, db = b - c.B;
            int dist = dr * dr + dg * dg + db * db;
            if (dist < bestDist) { bestDist = dist; best = i; }
        }
        return best;
    }

    /// <summary>Paleta por deduplicación de colores + reducción por distancia.</summary>
    private static List<RgbColor> BuildPalette(byte[] rgba, int pixelCount, int maxColors)
    {
        var seen = new HashSet<RgbColor>();
        var colors = new List<RgbColor>();
        for (int i = 0; i < pixelCount && colors.Count < maxColors * 4; i++)
        {
            var c = new RgbColor(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2]);
            if (seen.Add(c)) colors.Add(c);
        }
        // Si excede maxColors, redondea a 6 bits por canal y re-dedup
        if (colors.Count <= maxColors) return colors.Count > 0 ? colors : new List<RgbColor> { RgbColor.Black };

        var quantized = new Dictionary<RgbColor, RgbColor>();
        var result = new List<RgbColor>();
        foreach (var c in colors)
        {
            var q = new RgbColor((byte)(c.R & 0b11111100), (byte)(c.G & 0b11111100), (byte)(c.B & 0b11111100));
            if (quantized.TryGetValue(q, out var existing))
                continue;
            quantized[q] = q;
            result.Add(q);
            if (result.Count >= maxColors) break;
        }
        return result;
    }
}

public sealed class RasterResult
{
    public bool Success { get; }
    public string Message { get; }
    public int Width { get; }
    public int Height { get; }
    public byte[] Indices { get; }
    public List<RgbColor> Palette { get; }

    private RasterResult(bool success, string message, int w, int h, byte[] indices, List<RgbColor> palette)
    {
        Success = success; Message = message; Width = w; Height = h;
        Indices = indices; Palette = palette;
    }

    public static RasterResult Fail(string message) => new(false, message, 0, 0, Array.Empty<byte>(), new());
    public static RasterResult Ok(int w, int h, byte[] indices, List<RgbColor> palette) =>
        new(true, string.Empty, w, h, indices, palette);
}