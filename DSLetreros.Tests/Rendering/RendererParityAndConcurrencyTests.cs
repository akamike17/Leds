using DSLetreros.Domain.Entities;
using DSLetreros.Domain.Rendering;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Rendering;

/// <summary>
/// Correcciones auditadas de rendering: (a) elipse desplazada (cx/cy locales,
/// x/y sólo en SetPixel), (b) AnimationEvaluator puro/thread-safe con dos
/// canvases distintos renderizados en paralelo (marquee no pisa estado global).
/// </summary>
public class RendererParityAndConcurrencyTests
{
    private static Scene Scene(params SceneObject[] objs)
    {
        var s = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(10) };
        var l = new Layer { Name = "L", Order = 0 };
        l.Objects.AddRange(objs);
        s.Layers.Add(l);
        return s;
    }
    private static SceneObject T(SceneObject o) { o.Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10)); return o; }

    // ---- Golden: elipse centrada en posición distinta de (0,0) ----
    // Una elipse 9x9 con centro local (4,4). Al colocarla en (5,5), el centro visual
    // debe quedar en (5+4, 5+4) = (9,9), NO en (4,4) (bug del cx=x+(w-1)/2).
    [Fact]
    public void Ellipse_offset_center_is_position_plus_local_center()
    {
        var el = (ShapeObject)T(new ShapeObject
        {
            Shape = ShapeKind.Ellipse, Position = new PixelPoint(5, 5), Size = new PixelSize(9, 9),
            StrokeColor = RgbColor.White, FillColor = RgbColor.White,
        });
        var fb = SceneRenderer.Render(Scene(el), TimeSpan.Zero, new CanvasDefinition(24, 24));

        // Centro local (4,4) → centro absoluto (9,9): relleno blanca.
        Assert.Equal(RgbColor.White, fb.GetPixel(9, 9));

        // El centro de un objeto en (0,0) NO debe iluminarse (la elipse no está ahí).
        Assert.Equal(RgbColor.Black, fb.GetPixel(4, 4));

        // Esquinas de la caja del objeto (5,5)-(13,13) quedan fuera de la elipse.
        Assert.Equal(RgbColor.Black, fb.GetPixel(5, 5));
        Assert.Equal(RgbColor.Black, fb.GetPixel(13, 13));
        Assert.Equal(RgbColor.Black, fb.GetPixel(13, 5));
        Assert.Equal(RgbColor.Black, fb.GetPixel(5, 13));
    }

    // ---- Concurrencia: dos canvases distintos con marquee no se pisan ----
    // El viewport viaja como argumento: renderizar a la vez un canvas ancho y uno
    // estrecho debe producir offsets MARQUEE distintos y estables (sin estado static).
    [Fact]
    public async Task Concurrent_two_canvases_produce_independent_marquee_offsets()
    {
        TextObject MakeText(int width)
        {
            var t = (TextObject)T(new TextObject
            {
                Text = "AB", Color = RgbColor.White, Position = new PixelPoint(0, 0), Size = new PixelSize(width, 7),
            });
            t.Animations.Add(new AnimationDefinition
            {
                Kind = AnimationKind.Marquee, Direction = AnimationDirection.Left,
                SpeedPreset = AnimationSpeedPreset.Normal, Slot = AnimationSlot.Main,
            });
            return t;
        }

        // Evaluar el offset de marquee directamente para dos viewports distintos.
        var narrow = MakeText(8);   // size width 8
        var wide = MakeText(32);    // size width 32

        var tasks = new List<Task<AnimationState>>();
        for (int i = 0; i < 100; i++)
        {
            // intercala evaluaciones con viewport 8 y 32 en paralelo
            tasks.Add(Task.Run(() => AnimationEvaluator.Evaluate(narrow, TimeSpan.FromMilliseconds(500), 8)));
            tasks.Add(Task.Run(() => AnimationEvaluator.Evaluate(wide, TimeSpan.FromMilliseconds(500), 32)));
        }
        var results = await Task.WhenAll(tasks);

        // offset esperado: progress = 500/1000 = 0.5; travel = width + viewport.
        // narrow: travel = 8+8=16 → off = round(0.5*16)=8
        // wide:   travel = 32+32=64 → off = round(0.5*64)=32
        var narrowOffsets = results.Where((_, i) => i % 2 == 0).Select(r => r.Offset.X).ToArray();
        var wideOffsets = results.Where((_, i) => i % 2 == 1).Select(r => r.Offset.X).ToArray();

        Assert.All(narrowOffsets, o => Assert.Equal(-8, o));
        Assert.All(wideOffsets, o => Assert.Equal(-32, o));
    }
}