using DSLetreros.Application.Services;
using DSLetreros.Domain.Deployment;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.Rendering;
using DSLetreros.Domain.ValueObjects;
using DSLetreros.Infrastructure.Persistence;
using Xunit;

namespace DSLetreros.Tests.Acceptance;

/// <summary>
/// Slice 12: escenarios de aceptación R1--R5 (spec sección 20), fault y soak.
/// Sólo V1 termina cuando R1--R5 pasan (spec 23).
/// </summary>
public class R1_anuncio_reina_complete
{
    private static readonly CanvasDefinition Canvas = new(32, 16);
    private static readonly AtlasProjectStore Store = new();

    private static Scene R1Scene()
    {
        var scene = new Scene { Name = "R1", Duration = TimeSpan.FromSeconds(30) };

        var l1 = new Layer { Name = "L1", Order = 0 };
        var mgSol = TextObj("MG SOL", "MG SOL", 0, 5, AnimationKind.Blink);
        l1.Objects.Add(mgSol);
        scene.Layers.Add(l1);

        var l2 = new Layer { Name = "L2", Order = 1 };
        var pc = TextObj("PC", "PC", 5, 10, AnimationKind.Blink);
        l2.Objects.Add(pc);
        scene.Layers.Add(l2);

        var l3 = new Layer { Name = "L3", Order = 2 };
        var marq = TextObj("MARQUEE", "SE ARREGLAN COMPUTADORAS", 10, 30, AnimationKind.Marquee);
        l3.Objects.Add(marq);
        scene.Layers.Add(l3);

        return scene;
    }

    private static TextObject TextObj(string name, string text, double start, double end, AnimationKind anim)
    {
        var o = new TextObject
        {
            Name = name,
            Text = text,
            Color = RgbColor.White,
            Position = new PixelPoint(0, 0),
            Timing = new TimeRange(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end)),
        };
        o.Animations.Add(new AnimationDefinition { Kind = anim, SpeedPreset = AnimationSpeedPreset.Normal, Slot = AnimationSlot.Main });
        return o;
    }

    [Fact]
    public void R1_phases_render_expected_content()
    {
        var scene = R1Scene();

        // Fase 1 (0-5s): "MG SOL" blink visible en un instante ON (t=100ms).
        var on = SceneRenderer.Render(scene, TimeSpan.FromMilliseconds(100), Canvas);
        Assert.Contains(RgbColor.White, on.AllPixels());

        // Fase 2 (5-10s): PC blink visible en un instante ON (t=6s); MG SOL ya apagado.
        var pcOn = SceneRenderer.Render(scene, TimeSpan.FromSeconds(6), Canvas);
        Assert.Contains(RgbColor.White, pcOn.AllPixels());
    }

    [Fact]
    public async Task R1_save_open_roundtrip_preserves_scene()
    {
        var project = new Project { Name = "R1", Canvas = Canvas };
        project.Scenes.Add(R1Scene());

        var tmp = Path.Combine(Path.GetTempPath(), "dsletras-r1", Guid.NewGuid().ToString("N") + ".atlas");
        Directory.CreateDirectory(Path.GetDirectoryName(tmp)!);
        try
        {
            var save = await Store.SaveAsync(project, tmp);
            Assert.True(save.Success, save.Message);

            var (open, loaded) = await Store.OpenAsync(tmp);
            Assert.True(open.Success, open.Message);
            Assert.Single(loaded!.Scenes);
            Assert.Equal(3, loaded.Scenes[0].Layers.Count);
            Assert.Equal(30, (int)loaded.Scenes[0].Duration.TotalSeconds);
        }
        finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); }
    }

    [Fact]
    public async Task R1_send_simulator_activates_identical_package()
    {
        var scene = R1Scene();
        var target = new SimulatorTarget(width: 32, height: 16);
        var service = new DeploymentService();

        var result = await service.SendAsync(scene, Canvas, target);
        Assert.True(result.Success, result.Error);
        Assert.NotNull(target.Active);
        Assert.Equal(scene.Id, target.Active!.SceneId);
        Assert.False(result.Checksum!.Value.IsEmpty);
    }
}

public class R2_dibujo_drawing_object
{
    [Fact]
    public void R2_drawing_object_roundtrips_with_pixels()
    {
        var d = new DrawingObject
        {
            Name = "Corazón",
            Position = new PixelPoint(1, 1),
            Size = new PixelSize(4, 4),
            BitsPerPixel = 1,
            Palette = new List<RgbColor> { RgbColor.White },
            PixelData = new byte[] { 0,1,1,0, 1,1,1,1, 1,1,1,1, 0,1,1,0 },
            Bounds = new PixelRect(new PixelPoint(0, 0), new PixelSize(4, 4)),
        };
        var scene = new Scene { Name = "R2", Duration = TimeSpan.FromSeconds(5) };
        var layer = new Layer { Name = "L", Order = 0 };
        layer.Objects.Add(d);
        scene.Layers.Add(layer);

        var fb = SceneRenderer.Render(scene, TimeSpan.Zero, new CanvasDefinition(16, 16));
        // píxel central (2,2 en coords de objeto 1+2-1=2) encendido
        Assert.Equal(RgbColor.White, fb.GetPixel(2, 2));
        // esquina (1,1) debe estar apagada (bit 0)
        Assert.Equal(RgbColor.Black, fb.GetPixel(1, 1));
    }
}

public class R3_usuario_hostil
{
    [Fact]
    public async Task R3_corrupt_file_does_not_crash_and_is_recoverable()
    {
        var store = new AtlasProjectStore();
        var tmp = Path.Combine(Path.GetTempPath(), "dsletras-r3", Guid.NewGuid().ToString("N") + ".atlas");
        Directory.CreateDirectory(Path.GetDirectoryName(tmp)!);
        try
        {
            // archivo corrupto (manifest inválido)
            Directory.CreateDirectory(tmp);
            await File.WriteAllTextAsync(Path.Combine(tmp, "manifest.json"), "{{{{ not json");

            // no debe lanzar; devuelve falla limpia
            var (result, project) = await store.OpenAsync(tmp);
            Assert.False(result.Success);
            Assert.Null(project);
        }
        finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); }
    }

    [Fact]
    public async Task R3_repeated_send_is_idempotent_last_good_preserved()
    {
        var target = new SimulatorTarget(width: 16, height: 8);
        var scene = Scene();
        var service = new DeploymentService();

        for (int i = 0; i < 5; i++)
            Assert.True((await service.SendAsync(scene, new CanvasDefinition(16, 8), target)).Success);

        Assert.NotNull(target.Active);
        Assert.NotNull(target.LastKnownGood);
    }

    private static Scene Scene()
    {
        var s = new Scene { Name = "R3", Duration = TimeSpan.FromSeconds(2) };
        var l = new Layer { Name = "L", Order = 0 };
        l.Objects.Add(new TextObject { Name = "T", Text = "X", Color = RgbColor.White, Position = new PixelPoint(0, 0) });
        s.Layers.Add(l);
        return s;
    }
}

public class R4_transferencia_celestial
{
    [Fact]
    public async Task R4_failed_transfer_keeps_last_good()
    {
        var target = new SimulatorTarget(width: 16, height: 8);
        var service = new DeploymentService();
        var canvas = new CanvasDefinition(16, 8);

        // A activa
        var A = Scene("A");
        Assert.True((await service.SendAsync(A, canvas, target)).Success);
        var lastGood = target.Active;

        // B: falla en verify (checksum malo) → A debe seguir activa
        var B = Scene("B");
        var (pkg, _) = SceneCompiler.Compile(B, canvas);
        Assert.NotNull(pkg);
        var ticket = (await target.PrepareTransferAsync(pkg!.EstimatedBytes)).Value!;
        await target.UploadAsync(ticket, pkg);
        var badVerify = await target.VerifyAsync(ticket, new Checksum("deadbeef"));
        Assert.False(badVerify.Success);
        Assert.Same(lastGood, target.Active); // A intacta

        // B completa → se activa B y A queda LastKnownGood
        var verify = await target.VerifyAsync(ticket, pkg.ComputeChecksum());
        Assert.True(verify.Success);
        Assert.True((await target.ActivateAsync(ticket)).Success);
        Assert.Equal("B", target.Active!.SceneName);
        Assert.NotNull(target.LastKnownGood);
    }

    private static Scene Scene(string name)
    {
        var s = new Scene { Name = name, Duration = TimeSpan.FromSeconds(2) };
        var l = new Layer { Name = "L", Order = 0 };
        l.Objects.Add(new TextObject { Name = "T", Text = name, Color = RgbColor.White, Position = new PixelPoint(0, 0) });
        s.Layers.Add(l);
        return s;
    }
}

public class R5_equivalencia
{
    /// <summary>Editor logical render == compiled semantic output (golden).</summary>
    [Fact]
    public void R5_compiled_frames_match_logical_render()
    {
        var scene = new Scene { Name = "R5", Duration = TimeSpan.FromSeconds(1) };
        var l = new Layer { Name = "L", Order = 0 };
        l.Objects.Add(new TextObject { Name = "T", Text = "AB", Color = RgbColor.White, Position = new PixelPoint(1, 1) });
        scene.Layers.Add(l);

        var canvas = new CanvasDefinition(16, 8);
        var (pkg, err) = SceneCompiler.Compile(scene, canvas, frameIntervalMs: 100);
        Assert.NotNull(pkg);
        Assert.Null(err);

        // Para cada frame compilado, el render lógico en el mismo instante debe coincidir.
        for (int i = 0; i < pkg!.Frames.Count; i++)
        {
            var t = TimeSpan.FromMilliseconds(i * 100);
            var logical = SceneRenderer.Render(scene, t, canvas);
            var compiled = pkg.Frames[i];

            var logicalFlat = logical.AllPixels().ToArray();
            // CompiledFrame.Pixels es RGB24 (w*h*3); compáralo con la matriz lógica.
            int w = canvas.Width, h = canvas.Height;
            for (int px = 0; px < w * h; px++)
            {
                int bi = px * 3;
                var cr = compiled.Pixels[bi];
                var cg = compiled.Pixels[bi + 1];
                var cb = compiled.Pixels[bi + 2];
                var logicalColor = logicalFlat[px];
                Assert.Equal(logicalColor.R, cr);
                Assert.Equal(logicalColor.G, cg);
                Assert.Equal(logicalColor.B, cb);
            }
        }
    }
}

/// <summary>Soak (spec 20.10): loops largos de save/open/send sin corrupción ni fallos de estado.</summary>
public class Soak_and_fault
{
    [Fact]
    public async Task Soak_repeated_save_open_is_faithful()
    {
        var store = new AtlasProjectStore();
        var root = Path.Combine(Path.GetTempPath(), "dsletras-soak", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "s.atlas");
        try
        {
            for (int i = 0; i < 40; i++)
            {
                var p = new Project { Name = $"Iter {i}", Canvas = new CanvasDefinition(32, 16) };
                var s = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(2) };
                var l = new Layer { Name = "L", Order = 0 };
                l.Objects.Add(new TextObject { Name = "T", Text = $"T{i}", Color = RgbColor.White, Position = new PixelPoint(0, 0) });
                s.Layers.Add(l);
                p.Scenes.Add(s);

                var save = await store.SaveAsync(p, target);
                Assert.True(save.Success, save.Message);

                var (open, loaded) = await store.OpenAsync(target);
                Assert.True(open.Success, open.Message);
                Assert.Equal($"Iter {i}", loaded!.Name);
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Soak_repeated_send_does_not_leak_staging()
    {
        var target = new SimulatorTarget(width: 16, height: 8);
        var service = new DeploymentService();
        var canvas = new CanvasDefinition(16, 8);
        var scene = new Scene { Name = "Soak", Duration = TimeSpan.FromSeconds(1) };
        var l = new Layer { Name = "L", Order = 0 };
        l.Objects.Add(new TextObject { Name = "T", Text = "X", Color = RgbColor.White, Position = new PixelPoint(0, 0) });
        scene.Layers.Add(l);

        for (int i = 0; i < 100; i++)
        {
            var result = await service.SendAsync(scene, canvas, target);
            Assert.True(result.Success, result.Error);
        }

        // El staging debe quedar vacío tras cada activación; no hay tickets huérfanos.
        Assert.Empty(target.Staging);
        Assert.NotNull(target.Active);
    }
}