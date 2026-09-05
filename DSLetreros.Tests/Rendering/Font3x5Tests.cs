using DSLetreros.Domain.Rendering;
using Xunit;

namespace DSLetreros.Tests.Rendering;

/// <summary>
/// Fuente 3x5 (RFLED/final.md §15): invariantes estructurales de la fuente compacta.
/// No hardcodea copias de píxeles: verifica que existen glifos, 5 filas, sólo 3 bits
/// bajos, y que el catálogo resuelve '3x5' (paridad con el renderer JS).
/// </summary>
public class Font3x5Tests
{
    [Fact]
    public void Catalog_resolves_3x5()
    {
        var f = BitmapFontCatalog.Get("3x5");
        Assert.Equal(3, f.Width);
        Assert.Equal(5, f.Height);
    }

    [Theory]
    [InlineData('A')][InlineData('B')][InlineData('C')][InlineData('D')][InlineData('E')]
    [InlineData('F')][InlineData('G')][InlineData('H')][InlineData('I')][InlineData('J')]
    [InlineData('K')][InlineData('L')][InlineData('N')][InlineData('O')][InlineData('P')]
    [InlineData('Q')][InlineData('R')][InlineData('S')][InlineData('T')][InlineData('U')]
    [InlineData('V')][InlineData('X')][InlineData('Y')][InlineData('Z')]
    [InlineData('0')][InlineData('1')][InlineData('2')][InlineData('3')][InlineData('4')]
    [InlineData('5')][InlineData('6')][InlineData('7')][InlineData('8')][InlineData('9')]
    public void Has_glyph_for_ascii(char c)
    {
        Assert.True(Font3x5.HasGlyph(c), $"glifo ausente para '{c}'");
    }

    [Fact]
    public void Glyphs_have_5_rows_and_only_low_3_bits_set()
    {
        foreach (var c in "ABCDEFGHIJKLNOPQRSTUVXYZ0123456789")
        {
            var g = Font3x5.Get(c);
            Assert.NotNull(g);
            Assert.Equal(5, g!.Length);
            foreach (var row in g)
                Assert.True((row & ~0b111) == 0, $"row con bit alto en '{c}': {row}");
        }
    }

    [Fact]
    public void No_glyph_for_unrepresentable_wide_chars()
    {
        // M y W no caben en 3 columnas: se reservan como hueco (get null), no se finge.
        Assert.Null(Font3x5.Get('M'));
        Assert.Null(Font3x5.Get('W'));
        Assert.Null(Font3x5.Get('@'));
    }

    [Fact]
    public void Measure_text_advances_by_width_plus_spacing()
    {
        // MeasureGlyph devuelve Width + Spacing (3 + 1 = 4), igual que 5x7 (6).
        Assert.Equal(4, Font3x5.MeasureGlyph('A'));
        // "HI" = 2 glyphs * 4 - 1 (spacing) = 7
        Assert.Equal(7, Font3x5.MeasureText("HI"));
    }
}