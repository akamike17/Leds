using System.Text;

namespace DSLetreros.Domain.Rendering;

/// <summary>
/// Fuente bitmap 3x5 certificada (compacta, para textos que no caben en 5x7).
/// Glifo = 5 filas; cada fila es un byte con los 3 bits bajos = píxeles
/// (bit 0 = columna izquierda). Sólo glifos verificados píxel a píxel.
/// Los caracteres que no caben en 3 columnas (M, W, @, %, &, #) NO tienen glifo:
/// el renderer los reserva como hueco (MeasureGlyph) sin fingir legibilidad.
/// PARIDAD C#↔JS: debe coincidir EXACTO con wwwroot/js/editor/font3x5.js.
/// </summary>
public static class Font3x5
{
    public const int Width = 3;
    public const int Height = 5;
    public const int Spacing = 1;

    private static readonly Dictionary<char, byte[]> Glyphs;

    static Font3x5()
    {
        var g = new Dictionary<char, byte[]>();

        void Add(char c, string r0, string r1, string r2, string r3, string r4)
        {
            var rows = new[] { r0, r1, r2, r3, r4 };
            if (rows.Any(r => r.Length != 3))
                throw new InvalidOperationException($"Glifo '{c}' con fila de ancho != 3");
            var b = new byte[Height];
            for (int i = 0; i < Height; i++)
            {
                byte v = 0;
                for (int col = 0; col < 3; col++)
                    if (rows[i][col] == '#') v |= (byte)(1 << col);
                b[i] = v;
            }
            g[c] = b;
        }

        const string __ = "...";

        // A-Z (3x5). Se omiten M y W (no caben en 3 columnas).
        Add('A', ".#.", "#.#", "###", "#.#", "#.#");
        Add('B', "##.", "#.#", "##.", "#.#", "##.");
        Add('C', ".##", "#..", "#..", "#..", ".##");
        Add('D', "##.", "#.#", "#.#", "#.#", "##.");
        Add('E', "###", "#..", "##.", "#..", "###");
        Add('F', "###", "#..", "##.", "#..", "#..");
        Add('G', ".##", "#..", "#.#", "#.#", ".##");
        Add('H', "#.#", "#.#", "###", "#.#", "#.#");
        Add('I', "###", ".#.", ".#.", ".#.", "###");
        Add('J', "..#", "..#", "..#", "#.#", ".#.");
        Add('K', "#.#", "##.", "#..", "##.", "#.#");
        Add('L', "#..", "#..", "#..", "#..", "###");
        Add('N', "#.#", "###", "###", "#.#", "#.#");
        Add('O', ".#.", "#.#", "#.#", "#.#", ".#.");
        Add('P', "##.", "#.#", "##.", "#..", "#..");
        Add('Q', ".#.", "#.#", "#.#", "#.#", ".#.");
        Add('R', "##.", "#.#", "##.", "#.#", "#.#");
        Add('S', ".##", "#..", ".#.", "..#", "##.");
        Add('T', "###", ".#.", ".#.", ".#.", ".#.");
        Add('U', "#.#", "#.#", "#.#", "#.#", "###");
        Add('V', "#.#", "#.#", "#.#", "#.#", ".#.");
        Add('X', "#.#", "#.#", ".#.", "#.#", "#.#");
        Add('Y', "#.#", "#.#", ".#.", ".#.", ".#.");
        Add('Z', "###", "..#", ".#.", "#..", "###");

        // 0-9
        Add('0', ".#.", "#.#", "#.#", "#.#", ".#.");
        Add('1', ".#.", "##.", ".#.", ".#.", "###");
        Add('2', "##.", "..#", ".#.", "#..", "###");
        Add('3', "###", "..#", ".#.", "..#", "###");
        Add('4', "#.#", "#.#", "###", "..#", "..#");
        Add('5', "###", "#..", "##.", "..#", "##.");
        Add('6', ".##", "#..", "##.", "#.#", ".#.");
        Add('7', "###", "..#", ".#.", ".#.", ".#.");
        Add('8', ".#.", "#.#", ".#.", "#.#", ".#.");
        Add('9', ".#.", "#.#", ".##", "..#", "##.");

        // Símbolos esenciales
        Add(' ', __, __, __, __, __);
        Add('!', ".#.", ".#.", ".#.", __, ".#.");
        Add('?', ".##", "#.#", ".#.", __, ".#.");
        Add('.', __, __, __, __, ".#.");
        Add(',', __, __, __, ".#.", "#..");
        Add(':', __, ".#.", __, ".#.", __);
        Add('-', __, __, "###", __, __);
        Add('+', __, ".#.", "###", ".#.", __);
        Add('=', __, "###", __, "###", __);
        Add('/', "..#", ".#.", ".#.", "#..", "#..");
        Add('\\', "#..", ".#.", ".#.", "..#", "..#");
        Add('(', ".#.", "#..", "#..", "#..", ".#.");
        Add(')', ".#.", "..#", "..#", "..#", ".#.");
        Add('$', ".#.", "###", "#..", "###", ".#.");
        Add('"', "#.#", "#.#", __, __, __);
        Add('\'', ".#.", ".#.", __, __, __);

        // Acentos (compresión + tilde/diéresis, mismo patrón que 5x7)
        var accMap = new (char acc, char basec)[] {
            ('Á','A'), ('É','E'), ('Í','I'), ('Ó','O'), ('Ú','U'), ('Ñ','N'),
            ('á','a'), ('é','e'), ('í','i'), ('ó','o'), ('ú','u'), ('ñ','n'),
        };
        foreach (var (acc, basec) in accMap)
            if (g.TryGetValue(basec, out var bg)) AddAccent(g, acc, bg);
        if (g.TryGetValue('U', out var ug)) AddDiaeresis(g, 'Ü', ug);

        Glyphs = g;
    }

    private static void AddAccent(Dictionary<char, byte[]> g, char c, byte[] baseGlyph)
    {
        var shifted = new byte[Height];
        for (int r = Height - 1; r > 0; r--) shifted[r] = baseGlyph[r - 1];
        shifted[0] = 0b010; // tilde (punto medio)
        g[c] = shifted;
    }

    private static void AddDiaeresis(Dictionary<char, byte[]> g, char c, byte[] baseGlyph)
    {
        var shifted = new byte[Height];
        for (int r = Height - 1; r > 0; r--) shifted[r] = baseGlyph[r - 1];
        shifted[0] = 0b101; // diéresis (dos puntos)
        g[c] = shifted;
    }

    public static bool HasGlyph(char c) => Glyphs.ContainsKey(c);
    public static byte[]? Get(char c) => Glyphs.TryGetValue(c, out var g) ? g : null;
    public static int MeasureGlyph(char c) => Width + Spacing;

    public static int MeasureText(string s)
    {
        int w = 0;
        foreach (var c in s) w += MeasureGlyph(c);
        return w > 0 ? w - Spacing : 0;
    }
}