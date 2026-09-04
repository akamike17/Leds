using DSLetreros.Domain.Entities;
using DSLetreros.Domain.Rendering;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Rendering;

/// <summary>
/// R1 "anuncio reina" del MASTER SPEC (sección 20): 32x16, MG SOL blink 0-5s,
/// PC blink 5-10s, SE ARREGLAN COMPUTADORAS marquee 10-30s. Verifica el framebuffer
/// (presencia/ausencia de cada texto) en los timestamps frontera exactos.
/// </summary>
public class R1ExactTests
{
    private static Scene BuildR1()
    {
        var scene = new Scene { Name = "R1", Duration = TimeSpan.FromSeconds(30) };
        var layer = new Layer { Name = "L", Order = 0 };

        var mgSol = new TextObject
        {
            Text = "MG SOL", Position = new PixelPoint(0, 4), Color = RgbColor.White,
            Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)),
        };
        mgSol.Animations.Add(new AnimationDefinition { Kind = AnimationKind.Blink, SpeedPreset = AnimationSpeedPreset.Normal, Slot = AnimationSlot.Main });

        var pc = new TextObject
        {
            Text = "PC", Position = new PixelPoint(0, 4), Color = RgbColor.White,
            Timing = new TimeRange(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)),
        };
        pc.Animations.Add(new AnimationDefinition { Kind = AnimationKind.Blink, SpeedPreset = AnimationSpeedPreset.Normal, Slot = AnimationSlot.Main });

        var seArreglan = new TextObject
        {
            Text = "SE ARREGLAN COMPUTADORAS", Position = new PixelPoint(0, 4), Color = RgbColor.White,
            Timing = new TimeRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)),
        };
        seArreglan.Animations.Add(new AnimationDefinition { Kind = AnimationKind.Marquee, SpeedPreset = AnimationSpeedPreset.Normal, Direction = AnimationDirection.Left, Slot = AnimationSlot.Main, Loop = true });

        layer.Objects.Add(mgSol);
        layer.Objects.Add(pc);
        layer.Objects.Add(seArreglan);
        scene.Layers.Add(layer);
        return scene;
    }

    private static bool AnyWhite(FrameBuffer fb) => fb.AllPixels().Any(c => c.R > 250 && c.G > 250 && c.B > 250);

    [Fact]
    public void R1_phases_by_timestamp()
    {
        var scene = BuildR1();
        var canvas = new CanvasDefinition(32, 16);

        // 0-5s: MG SOL (blink). En t=1s (fase on) debe haber contenido blanco.
        Assert.True(AnyWhite(SceneRenderer.Render(scene, TimeSpan.FromSeconds(1), canvas)), "MG SOL debe verse en fase on.");
        // En t=1.5s (blink off) no debe verse.
        Assert.False(AnyWhite(SceneRenderer.Render(scene, TimeSpan.FromMilliseconds(1500), canvas)), "MG SOL blink off.");

        // 5-10s: PC.
        Assert.True(AnyWhite(SceneRenderer.Render(scene, TimeSpan.FromMilliseconds(5100), canvas)), "PC debe verse en fase on.");
        Assert.False(AnyWhite(SceneRenderer.Render(scene, TimeSpan.FromMilliseconds(6500), canvas)), "PC blink off.");

        // 10-30s: SE ARREGLAN COMPUTADORAS (marquee).
        Assert.True(AnyWhite(SceneRenderer.Render(scene, TimeSpan.FromSeconds(15), canvas)), "Marquee debe verse.");
        Assert.True(AnyWhite(SceneRenderer.Render(scene, TimeSpan.FromMilliseconds(29999), canvas)), "Marquee sigue antes del final.");
        Assert.False(AnyWhite(SceneRenderer.Render(scene, TimeSpan.FromMilliseconds(30000), canvas)), "Fuera de rango tras 30s.");
    }

    [Fact]
    public void R1_disjoint_timings_no_overlap()
    {
        var scene = BuildR1();
        var canvas = new CanvasDefinition(32, 16);

        // En los bordes exactos: antes de 5s no hay PC; antes de 10s no hay marquee.
        // blink on en local % 1000 < 500. En t=4400ms: local=4400, floor(4400/500)=8 → on.
        var at4s4 = SceneRenderer.Render(scene, TimeSpan.FromMilliseconds(4400), canvas);
        Assert.True(AnyWhite(at4s4), "MG SOL en 4.400s (blink on).");

        // en t=4999ms el blink de MG SOL está en fase off (floor(4999/500)=9 → off).
        var at4s999 = SceneRenderer.Render(scene, TimeSpan.FromMilliseconds(4999), canvas);
        Assert.False(AnyWhite(at4s999), "MG SOL blink off en 4.999s.");

        // t=5000 pertenece a PC (blink on en 0..500 del ciclo, local=0 → on)
        var at5s = SceneRenderer.Render(scene, TimeSpan.FromMilliseconds(5000), canvas);
        Assert.True(AnyWhite(at5s), "PC en 5.000s (on).");

        // t=10000 pertenece a marquee
        var at10s = SceneRenderer.Render(scene, TimeSpan.FromMilliseconds(10000), canvas);
        Assert.True(AnyWhite(at10s), "Marquee en 10.000s.");
    }

    [Fact]
    public void R1_compiles_to_discrete_frames()
    {
        var scene = BuildR1();
        var (package, error) = DSLetreros.Domain.Deployment.SceneCompiler.CompileForTarget(
            scene, new CanvasDefinition(32, 16),
            new DeviceCapabilities { LogicalWidth = 32, LogicalHeight = 16, ProtocolVersion = 1, MaxSceneBytes = 8 * 1024 * 1024, AutonomousPlayback = true });

        Assert.Null(error);
        Assert.NotNull(package);
        Assert.True(package.Frames.Count > 0);
        // los frames cubren hasta ~30s
        Assert.True(package.Frames[^1].TimeMs >= 29000, $"Último frame a {package.Frames[^1].TimeMs}ms debe acercarse a 30s.");
    }
}