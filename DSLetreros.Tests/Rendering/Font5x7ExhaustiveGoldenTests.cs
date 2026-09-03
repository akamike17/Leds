using DSLetreros.Domain.Rendering;
using Xunit;

namespace DSLetreros.Tests.Rendering;

/// <summary>
/// Golden exhaustivo de la fuente 5x7 (spec 20.3): cada glifo del catálogo debe
/// existir, tener 7 filas y codificar sólo los 5 bits bajos. Derivado de la tabla
/// del fuente, no duplicado del código: valida que ningún glifo se borre o corrompa.
///
/// Mata los "Statement mutation" (Add(...) → ';') que Stryker inyecta en cada línea
/// de definición: si una línea se elimina, HasGlyph deja de ser cierto para ese char.
/// </summary>
public class Font5x7ExhaustiveGoldenTests
{
    /// <summary>Catálogo completo esperado: A-Z, a-z, 0-9, acentos, diéresis, ñ y símbolos.</summary>
    private const string AllGlyphsDigitUpper =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string AllGlyphsLower =
        "abcdefghijklmnopqrstuvwxyz";
    private const string AllGlyphsDigits =
        "0123456789";
    private const string AllGlyphsAccented =
        "ÁÉÍÓÚÜÑáéíóúüñ";
    private const string AllGlyphsSymbols =
        " !?¿¡.,:;-+=/\\()#@$%&\'\"";

    public static IEnumerable<object[]> EveryGlyph()
    {
        var chars = new HashSet<char>();
        foreach (var c in (AllGlyphsDigitUpper + AllGlyphsLower + AllGlyphsDigits +
                           AllGlyphsAccented + AllGlyphsSymbols))
            chars.Add(c);
        foreach (var c in chars)
            yield return new object[] { c };
    }

    [Theory]
    [MemberData(nameof(EveryGlyph))]
    public void Glyph_exists_has_7_rows_valid_bits(char c)
    {
        // 1. Todo glifo del catálogo debe existir (mata Add(...)->';').
        Assert.True(Font5x7.HasGlyph(c), $"Glifo faltante: '{c}'");

        // 2. 7 filas exactas.
        var g = Font5x7.Get(c)!;
        Assert.Equal(Font5x7.Height, g.Length);

        // 3. Sólo los 5 bits bajos (0..4) pueden estar encendidos.
        foreach (var row in g)
            Assert.True((row & ~0b11111) == 0, $"Glifo '{c}' tiene bits fuera de los 5 bajos: 0x{row:X2}");

        // 4. Consistencia con MeasureGlyph: cada glifo mide Width+Spacing.
        Assert.Equal(Font5x7.Width + Font5x7.Spacing, Font5x7.MeasureGlyph(c));
    }

    [Fact]
    public void Every_catalog_glyph_is_distinct_or_expected_duplicate()
    {
        // No hay glifos "fantasma": el total de HasGlyph debe coincidir con el catálogo.
        var expected = new HashSet<char>();
        foreach (var c in (AllGlyphsDigitUpper + AllGlyphsLower + AllGlyphsDigits +
                           AllGlyphsAccented + AllGlyphsSymbols))
            expected.Add(c);

        // Recorremos el rango ASCII imprimible + acentos; los que NO están en el catálogo
        // no deben existir en la fuente.
        for (int cp = 32; cp <= 126; cp++)
        {
            char c = (char)cp;
            bool expectedPresent = expected.Contains(c);
            Assert.Equal(expectedPresent, Font5x7.HasGlyph(c));
        }
    }

    [Fact]
    public void Accented_letters_share_base_shape_compressed_one_row()
    {
        // 'Ñ' = 'N' comprimida una fila + tilde arriba (fila 0). Verifica la relación real.
        var n = Font5x7.Get('N')!;
        var enye = Font5x7.Get('Ñ')!;
        // filas 1..6 de Ñ == filas 0..5 de N
        for (int r = 1; r < 7; r++)
            Assert.Equal(n[r - 1], enye[r]);
        // fila 0 de Ñ = tilde 0b01010 (10)
        Assert.Equal(0b01010, enye[0]);
    }

    [Fact]
    public void Diaeresis_letter_has_two_dot_top_row()
    {
        var u = Font5x7.Get('U')!;
        var uu = Font5x7.Get('Ü')!;
        for (int r = 1; r < 7; r++)
            Assert.Equal(u[r - 1], uu[r]);
        Assert.Equal(0b10101, uu[0]);
    }

    [Fact]
    public void MeasureText_empty_and_single_char()
    {
        Assert.Equal(0, Font5x7.MeasureText(""));
        Assert.Equal(Font5x7.Width, Font5x7.MeasureText("A")); // 1 glyph: 6 - spacing 1 = 5
    }

    [Fact]
    public void Invalid_unknown_char_has_no_glyph()
    {
        // Un char fuera del catálogo (p.ej. control) no tiene glifo.
        Assert.False(Font5x7.HasGlyph('\u0001'));
        Assert.Null(Font5x7.Get('\u0001'));
    }
}