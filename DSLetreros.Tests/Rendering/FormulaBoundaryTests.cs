using DSLetreros.Domain.Deployment;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.Rendering;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Rendering;

/// <summary>
/// Golden de borde para las fórmulas aritméticas (spec 20.6): elipse no cuadrada,
/// rect con tamaño impar/par, y matemática de ciclo de animación. Mata mutantes
/// `*`/`/`/`+`/`-` que cambian la geometría renderizada.
/// </summary>
public class FormulaBoundaryTests
{
    private static Scene Scene(SceneObject o)
    {
        var s = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(5) };
        var l = new Layer { Name = "L", Order = 0 };
        l.Objects.Add(o);
        s.Layers.Add(l);
        o.Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        return s;
    }

    private static readonly CanvasDefinition C = new(24, 24);

    // ---- Elipse NO cuadrada: rx != ry distingue nx*nx+ny*ny de nx*nx-ny*ny ----
    [Fact]
    public void Wide_ellipse_is_wider_than_tall()
    {
        var el = new ShapeObject
        {
            Shape = ShapeKind.Ellipse, Position = new PixelPoint(0, 0), Size = new PixelSize(9, 5),
            StrokeColor = RgbColor.White, FillColor = RgbColor.Black,
        };
        var fb = SceneRenderer.Render(Scene(el), TimeSpan.Zero, C);
        // centro (4,2) dentro
        Assert.NotEqual(RgbColor.Black, fb.GetPixel(4, 2));
        // extremo horizontal lejano (8,2) fuera (rx=4, nx=(8-4)/4=1.0 → v=1.0 justo borde)
        // esquinas/en punta: (4,0) arriba-centro: cy=2, ny=(0-2)/2=-1.0 → v=1.0 borde (stroke)
        // un punto claramente fuera en vertical (4,4): ny=(4-2)/2=1.0 borde...
        // usamos (0,2) extremo izquierdo: nx=(0-4)/4=-1.0 → borde; (5,2) nx=0.25 v=0.0625 interior
        Assert.NotEqual(RgbColor.Black, fb.GetPixel(5, 2));
        // fuera en esquina arriba-izquierda (0,0): nx=-1, ny=-1, v=2.0 → negro
        Assert.Equal(RgbColor.Black, fb.GetPixel(0, 0));
    }

    // ---- Rectángulo tamaño par vs impar cambia el borde (w-1) ----
    [Fact]
    public void Rectangle_odd_width_right_edge_at_w_minus_1()
    {
        var r = new ShapeObject
        {
            Shape = ShapeKind.Rectangle, Position = new PixelPoint(0, 0), Size = new PixelSize(5, 1),
            StrokeColor = RgbColor.White, FillColor = RgbColor.Black,
        };
        var fb = SceneRenderer.Render(Scene(r), TimeSpan.Zero, C);
        // borde derecho en x = 4 (w-1), no en x=5
        Assert.Equal(RgbColor.White, fb.GetPixel(4, 0));
        Assert.Equal(RgbColor.Black, fb.GetPixel(5, 0)); // un píxel más
    }

    // ---- Matemática de ciclo: Pulse fase 0 vs 1/4 vs 1/2 ----
    [Fact]
    public void Pulse_exact_phase_values()
    {
        var o = new TextObject
        {
            Name = "T", Text = "A", Color = RgbColor.White, Position = new PixelPoint(0, 0),
            Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)),
        };
        o.Animations.Add(new AnimationDefinition { Kind = AnimationKind.Pulse, SpeedPreset = AnimationSpeedPreset.Normal, Slot = AnimationSlot.Main });
        var s = Scene(o);

        // Normal cycle = 1000ms. phase = (local % 1000)/1000.
        // t=0 → phase 0 → cos(0)=1 → b=1.0
        var t0 = SceneRenderer.Render(s, TimeSpan.Zero, new CanvasDefinition(8, 8));
        Assert.Equal(RgbColor.White, t0.GetPixel(2, 0)); // 'A' top row bit 2

        // t=250 → phase 0.25 → cos(pi/2)=0 → b=0.5 → gris 128
        var t250 = SceneRenderer.Render(s, TimeSpan.FromMilliseconds(250), new CanvasDefinition(8, 8));
        Assert.Equal(new RgbColor(128, 128, 128), t250.GetPixel(2, 0));

        // t=500 → phase 0.5 → cos(pi)=-1 → b=0 → negro
        var t500 = SceneRenderer.Render(s, TimeSpan.FromMilliseconds(500), new CanvasDefinition(8, 8));
        Assert.Equal(RgbColor.Black, t500.GetPixel(2, 0));

        // t=1000 → phase 0 (wrap) → b=1.0 → blanco (mata local.Ticks % cycle.Ticks vs *)
        var t1000 = SceneRenderer.Render(s, TimeSpan.FromMilliseconds(1000), new CanvasDefinition(8, 8));
        Assert.Equal(RgbColor.White, t1000.GetPixel(2, 0));
    }

    // ---- Wipe clip exacto en progreso fraccionario ----
    [Fact]
    public void Wipe_right_clip_mirrors_reveal()
    {
        var o = new ShapeObject
        {
            Shape = ShapeKind.Rectangle, Position = new PixelPoint(0,0), Size = new PixelSize(8, 1),
            StrokeColor = RgbColor.White, FillColor = RgbColor.White,
        };
        o.Animations.Add(new AnimationDefinition { Kind = AnimationKind.Wipe, Direction = AnimationDirection.Right, SpeedPreset = AnimationSpeedPreset.Normal, Slot = AnimationSlot.Main });
        var s = Scene(o);

        // Right a t=0: progress 0 → clip x desde w*(1-0)=8 (todo oculto) → nada
        var t0 = SceneRenderer.Render(s, TimeSpan.Zero, new CanvasDefinition(16, 4));
        Assert.Equal(RgbColor.Black, t0.GetPixel(0, 0));

        // Right a t=500: progress 0.5 → clip desde x=4 → pixels 0..3 ocultos, 4..7 revelados
        var t500 = SceneRenderer.Render(s, TimeSpan.FromMilliseconds(500), new CanvasDefinition(16, 4));
        Assert.Equal(RgbColor.Black, t500.GetPixel(0, 0));
        Assert.Equal(RgbColor.White, t500.GetPixel(7, 0));
    }
}