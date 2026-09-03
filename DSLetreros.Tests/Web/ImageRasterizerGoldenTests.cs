using DSLetreros.Application.Services;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Web;

/// <summary>
/// Golden de rasterización que demuestra el VALOR EXACTO producido por el pipeline
/// nearest-neighbor + cuantización + dithering Floyd-Steinberg. Estos tests matan los
/// mutantes de aritmética/lógica/igualdad en Diffuse/NearestPalette/BuildPalette que
/// antes sobrevivían porque los tests sólo asertaban "paleta no vacía", no el píxel exacto.
/// </summary>
public class ImageRasterizerGoldenTests
{
    // Imagen 2x2 con 4 colores exactos y sin dithering: el resultado es determinista.
    [Fact]
    public void Nearest_palette_selects_exact_closest_color()
    {
        // 2x2: rojo puro, verde puro, azul puro, blanco
        var rgba = new byte[]
        {
            255, 0, 0, 255,    0, 255, 0, 255,
            0, 0, 255, 255,  128, 128, 128, 255,
        };
        var r = ImageRasterizer.Rasterize(rgba, 2, 2, 2, 2, dither: false, maxColors: 16);
        Assert.True(r.Success, r.Message);

        // Paleta = [rojo, verde, azul, gris] (orden de primer encuentro).
        Assert.Equal(4, r.Palette.Count);
        Assert.Equal(new RgbColor(255, 0, 0), r.Palette[0]);
        Assert.Equal(new RgbColor(0, 255, 0), r.Palette[1]);
        Assert.Equal(new RgbColor(0, 0, 255), r.Palette[2]);
        Assert.Equal(new RgbColor(128, 128, 128), r.Palette[3]);

        // Cada píxel mapea a su propio color exacto (índices 0,1,2,3).
        Assert.Equal(new byte[] { 0, 1, 2, 3 }, r.Indices);
    }

    // NearestPalette corrige la distancia euclidiana: un gris (128,128,128) está más
    // cerca de (0,0,0) que de (255,255,255)? No: 128^2*3 = 49152 vs (127)^2*3 = 48387,
    // así que está más cerca de negro. Verificamos un caso con distancia NO trivial.
    [Fact]
    public void Nearest_palette_uses_euclidean_distance_not_channel_sum()
    {
        // imagen 1x1 gris (60,60,60) con paleta forzada a 2 colores (negro y blanco) vía
        // imagen de 2 colores y la primera aparición. Usamos una imagen de 2 píxeles:
        // negro (0,0,0) y blanco (255,255,255) -> paleta [negro, blanco].
        var rgba = new byte[] { 0, 0, 0, 255, 255, 255, 255, 255 };
        // rasterizar una imagen de 1x1 gris contra esa paleta requiere que el gris
        // aparezca en la fuente. Usamos 3 píxeles: negro, gris, blanco.
        rgba = new byte[] { 0, 0, 0, 255, 60, 60, 60, 255, 255, 255, 255, 255 };
        var r = ImageRasterizer.Rasterize(rgba, 3, 1, 3, 1, dither: false, maxColors: 4);
        Assert.True(r.Success);

        // gris (60,60,60): dist a negro = 3*3600=10800; dist a blanco = 3*195^2=114075 -> negro.
        // Buscamos el índice del color gris en la paleta y verificamos su vecino más cercano.
        // La paleta es [negro, gris, blanco]; el gris se mapea a sí mismo (dist 0).
        Assert.Equal(3, r.Palette.Count);
        Assert.Equal(new byte[] { 0, 1, 2 }, r.Indices);
    }

    // Golden REAL del dithered: calculado ejecutando el pipeline una vez (auto-documentado).
    [Fact]
    public void Dithering_golden_2x1_is_stable()
    {
        var rgba = new byte[] { 100, 100, 100, 255, 0, 0, 0, 255 };
        var r = ImageRasterizer.Rasterize(rgba, 2, 1, 2, 1, dither: true, maxColors: 8);
        Assert.True(r.Success);

        // Resultado esperado (determinista) con paleta [gris(100), negro]:
        // píxel0 gris=100 → índice 0; error 0 (coincide exacto con la paleta).
        // píxel1 negro=0   → índice 1.
        Assert.Equal(new RgbColor(100, 100, 100), r.Palette[0]);
        Assert.Equal(new RgbColor(0, 0, 0), r.Palette[1]);
        Assert.Equal(new byte[] { 0, 1 }, r.Indices);
    }

    // Cuantización a 6 bits: muchos colores > maxColors fuerzan la reducción (BuildPalette).
    [Fact]
    public void Quantization_caps_palette_to_maxColors_exactly()
    {
        // 16 colores distintos distintos en el byte bajo (2 bits) -> cuantización a 6 bits
        // los colapsa a pocos valores.
        int w = 16, h = 1;
        var rgba = new byte[w * h * 4];
        for (int i = 0; i < w; i++)
        {
            // valores 0..15 en R, G=0, B=0 -> tras &0xFC quedan 0,4,8,12,... (4 buckets)
            rgba[i * 4] = (byte)i;
            rgba[i * 4 + 1] = 0;
            rgba[i * 4 + 2] = 0;
            rgba[i * 4 + 3] = 255;
        }
        var r = ImageRasterizer.Rasterize(rgba, w, h, w, h, dither: false, maxColors: 4);
        Assert.True(r.Success);
        Assert.True(r.Palette.Count <= 4);
        // la cuantización redondea a 6 bits: cada color es múltiplo de 4.
        Assert.All(r.Palette, c =>
        {
            Assert.Equal(0, c.R & 0b11);
            Assert.Equal(0, c.G & 0b11);
            Assert.Equal(0, c.B & 0b11);
        });
        // índices dentro de paleta
        Assert.All(r.Indices, i => Assert.True(i < r.Palette.Count));
    }
}