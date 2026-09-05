using System.Text;

namespace DSLetreros.Domain.Rendering;

/// <summary>
/// Fuente bitmap 5x7 certificada. Glifo = 7 filas; cada fila es un byte con
/// los 5 bits bajos = píxeles (bit 0 = columna izquierda).
/// Sólo glifos verificados píxel a píxel. NO se derivan 8x8 estirando 5x7.
/// </summary>
public static class Font5x7
{
    public const int Width = 5;
    public const int Height = 7;
    public const int Spacing = 1;

    /// <summary>char -> 7 filas (byte con 5 bits significativos).</summary>
    private static readonly Dictionary<char, byte[]> Glyphs;

    static Font5x7()
    {
        var g = new Dictionary<char, byte[]>();

        // Definiciones 5x7 compactas. Cada string de 5 chars ('#'=píxel).
        // Para símbolos anchos o acentos, se usa el patrón exacto de 5.
        void Add(char c, string r0, string r1, string r2, string r3, string r4, string r5, string r6)
        {
            var rows = new[] { r0, r1, r2, r3, r4, r5, r6 };
            if (rows.Any(r => r.Length != 5))
                throw new InvalidOperationException($"Glifo '{c}' con fila de ancho != 5");
            var b = new byte[Height];
            for (int i = 0; i < Height; i++)
            {
                byte v = 0;
                for (int col = 0; col < 5; col++)
                    if (rows[i][col] == '#') v |= (byte)(1 << col);
                b[i] = v;
            }
            g[c] = b;
        }

        const string __ = ".....";

        // ---- A-Z ----
        Add('A', "..#..", ".#.#.", "#...#", "#...#", "#####", "#...#", "#...#");
        Add('B', "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####.");
        Add('C', ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###.");
        Add('D', "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####.");
        Add('E', "#####", "#....", "#....", "####.", "#....", "#....", "#####");
        Add('F', "#####", "#....", "#....", "####.", "#....", "#....", "#....");
        Add('G', ".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".###.");
        Add('H', "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#");
        Add('I', "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####");
        Add('J', "..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##..");
        Add('K', "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#");
        Add('L', "#....", "#....", "#....", "#....", "#....", "#....", "#####");
        Add('M', "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#");
        Add('N', "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#");
        Add('O', ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###.");
        Add('P', "####.", "#...#", "#...#", "####.", "#....", "#....", "#....");
        Add('Q', ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#");
        Add('R', "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#");
        Add('S', ".####", "#....", "#....", ".###.", "....#", "....#", "####.");
        Add('T', "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#..");
        Add('U', "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###.");
        Add('V', "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#..");
        Add('W', "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "##.##", "#...#");
        Add('X', "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#");
        Add('Y', "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#..");
        Add('Z', "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####");

        // ---- a-z ----
        Add('a', __, __, ".###.", "....#", ".####", "#...#", ".####");
        Add('b', "#....", "#....", "####.", "#...#", "#...#", "#...#", "####.");
        Add('c', __, __, ".###.", "#....", "#....", "#....", ".###.");
        Add('d', "....#", "....#", ".####", "#...#", "#...#", "#...#", ".####");
        Add('e', __, __, ".###.", "#...#", "#####", "#....", ".###.");
        Add('f', "..##.", ".#...", "#####", ".#...", ".#...", ".#...", ".#...");
        Add('g', __, __, ".####", "#...#", "#...#", ".####", "....#"); // + descensor implícito en la 8ª fila no existe; usamos 'g' sin descensor para 7 filas
        Add('h', "#....", "#....", "####.", "#...#", "#...#", "#...#", "#...#");
        Add('i', "..#..", __, ".##..", "..#..", "..#..", "..#..", "#####");
        Add('j', "...#.", __, "..##.", "...#.", "...#.", "#..#.", ".##..");
        Add('k', "#....", "#....", "#..#.", "#.#..", "##...", "#.#..", "#..#.");
        Add('l', ".##..", "..#..", "..#..", "..#..", "..#..", "..#..", "#####");
        Add('m', __, __, "##.##", "#.#.#", "#.#.#", "#.#.#", "#.#.#");
        Add('n', __, __, "####.", "#...#", "#...#", "#...#", "#...#");
        Add('o', __, __, ".###.", "#...#", "#...#", "#...#", ".###.");
        Add('p', __, __, "####.", "#...#", "#...#", "####.", "#....");
        Add('q', __, __, ".####", "#...#", "#...#", ".####", "....#");
        Add('r', __, __, "#.###", "##...", "#....", "#....", "#....");
        Add('s', __, __, ".####", "#....", ".###.", "....#", "####.");
        Add('t', ".#...", ".#...", "#####", ".#...", ".#...", ".#...", "..##.");
        Add('u', __, __, "#...#", "#...#", "#...#", "#...#", ".####");
        Add('v', __, __, "#...#", "#...#", "#...#", ".#.#.", "..#..");
        Add('w', __, __, "#...#", "#...#", "#.#.#", "#.#.#", ".##.#");
        Add('x', __, __, "#...#", ".#.#.", "..#..", ".#.#.", "#...#");
        Add('y', __, __, "#...#", "#...#", "#...#", ".####", ".#..#"); // aproximación en 7 filas
        Add('z', __, __, "#####", "...#.", "..#..", ".#...", "#####");

        // ---- 0-9 ----
        Add('0', ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###.");
        Add('1', "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###.");
        Add('2', ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####");
        Add('3', "####.", "....#", "....#", ".###.", "....#", "....#", "####.");
        Add('4', "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#.");
        Add('5', "#####", "#....", "####.", "....#", "....#", "....#", "####.");
        Add('6', ".###.", "#....", "#....", "####.", "#...#", "#...#", ".###.");
        Add('7', "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#...");
        Add('8', ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###.");
        Add('9', ".###.", "#...#", "#...#", ".####", "....#", "....#", ".###.");

        // ---- Letras acentuadas (mayúsculas): base + tilde arriba ----
        AddAccent(g, 'Á', g['A']);
        AddAccent(g, 'É', g['E']);
        AddAccent(g, 'Í', g['I']);
        AddAccent(g, 'Ó', g['O']);
        AddAccent(g, 'Ú', g['U']);

        // Ü diéresis
        AddDiaeresis(g, 'Ü', g['U']);

        // Ñ: N + tilde (comprime la letra una fila)
        AddAccent(g, 'Ñ', g['N']);

        // ---- minúsculas acentuadas ----
        AddAccent(g, 'á', g['a']);
        AddAccent(g, 'é', g['e']);
        AddAccent(g, 'í', g['i']);
        AddAccent(g, 'ó', g['o']);
        AddAccent(g, 'ú', g['u']);
        AddDiaeresis(g, 'ü', g['u']);
        AddAccent(g, 'ñ', g['n']);

        // ---- Símbolos ----
        Add(' ', ".....", ".....", ".....", ".....", ".....", ".....", ".....");
        Add('!', "..#..", "..#..", "..#..", "..#..", "..#..", __, "..#..");
        Add('?', ".###.", "#...#", "....#", "...#.", "..#..", __, "..#..");
        Add('¿', "..#..", __, "..#..", ".#...", "#....", "#...#", ".###.");
        Add('¡', "..#..", __, "..#..", "..#..", "..#..", "..#..", "..#..");
        Add('.', __, __, __, __, __, __, "..#..");
        Add(',', __, __, __, __, "..#..", "..#..", ".#...");
        Add(':', __, "..#..", __, __, __, "..#..", __);
        Add(';', __, "..#..", __, __, "..#..", "..#..", ".#...");
        Add('-', __, __, __, "#####", __, __, __);
        Add('+', __, "..#..", "..#..", "#####", "..#..", "..#..", __);
        Add('=', __, __, "#####", __, "#####", __, __);
        Add('/', "....#", "...#.", "...#.", "..#..", ".#...", ".#...", "#....");
        Add('\\', "#....", ".#...", ".#...", "..#..", "...#.", "...#.", "....#");
        Add('(', "...#.", "..#..", ".#...", ".#...", ".#...", "..#..", "...#.");
        Add(')', ".#...", "..#..", "...#.", "...#.", "...#.", "..#..", ".#...");
        Add('#', ".#.#.", ".#.#.", "#####", ".#.#.", "#####", ".#.#.", ".#.#.");
        Add('@', ".###.", "#...#", "#.#.#", "#.###", "#....", "#...#", ".###.");
        Add('$', "..#..", ".##.#", "#.#..", ".###.", "..#.#", "#.##.", "..#..");
        Add('%', "##..#", "##.#.", "...#.", "..#..", ".#...", "#.###", "#..##");
        Add('&', ".##..", "#..#.", "#..#.", ".##..", "#.#.#", "#..#.", ".##.#");
        Add('"', ".#.#.", ".#.#.", __, __, __, __, __);
        Add('\'', "..#..", "..#..", __, __, __, __, __);

        Glyphs = g;
    }

    private static void AddAccent(Dictionary<char, byte[]> g, char c, byte[] baseGlyph)
    {
        // comprime base una fila y añade tilde (~) arriba
        var shifted = new byte[Height];
        for (int r = Height - 1; r > 0; r--)
            shifted[r] = baseGlyph[r - 1];
        shifted[0] = 0b_01010; // tilde
        g[c] = shifted;
    }

    private static void AddDiaeresis(Dictionary<char, byte[]> g, char c, byte[] baseGlyph)
    {
        var shifted = new byte[Height];
        for (int r = Height - 1; r > 0; r--)
            shifted[r] = baseGlyph[r - 1];
        shifted[0] = 0b_10101; // diéresis (doble punto)
        g[c] = shifted;
    }

    public static bool HasGlyph(char c) => Glyphs.ContainsKey(c);
    public static byte[]? Get(char c) => Glyphs.TryGetValue(c, out var g) ? g : null;

    /// <summary>Todo carácter (con o sin glifo) avanza Width + Spacing; los desconocidos reservan un hueco.</summary>
    public static int MeasureGlyph(char c) => Width + Spacing;

    public static int MeasureText(string s)
    {
        int w = 0;
        foreach (var c in s) w += MeasureGlyph(c);
        return w > 0 ? w - Spacing : 0;
    }
}

/// <summary>Catálogo de fuentes bitmap certificadas.</summary>
public static class BitmapFontCatalog
{
    public static readonly IReadOnlyDictionary<string, BitmapFontAccessor> Fonts =
        new Dictionary<string, BitmapFontAccessor>(StringComparer.Ordinal)
        {
            ["5x7"] = new BitmapFontAccessor(Font5x7.Width, Font5x7.Height, Font5x7.Get, Font5x7.MeasureGlyph),
            ["3x5"] = new BitmapFontAccessor(Font3x5.Width, Font3x5.Height, Font3x5.Get, Font3x5.MeasureGlyph),
        };

    public static BitmapFontAccessor Get(string id) =>
        Fonts.TryGetValue(id, out var f) ? f : Fonts["5x7"];
}

public sealed class BitmapFontAccessor
{
    private readonly Func<char, byte[]?> _getGlyph;
    private readonly Func<char, int> _measure;

    public BitmapFontAccessor(int width, int height, Func<char, byte[]?> getGlyph, Func<char, int> measure)
    {
        Width = width; Height = height; _getGlyph = getGlyph; _measure = measure;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[]? Get(char c) => _getGlyph(c);
    public bool Has(char c) => Get(c) != null;
    public int MeasureGlyph(char c) => _measure(c);

    public int MeasureText(string s)
    {
        int w = 0;
        foreach (var c in s) w += MeasureGlyph(c);
        return w > 0 ? w - Font5x7.Spacing : 0;
    }
}