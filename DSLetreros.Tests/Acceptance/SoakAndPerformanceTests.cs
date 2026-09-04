using System.Diagnostics;
using DSLetreros.Application.Services;
using DSLetreros.Domain.Deployment;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.Rendering;
using DSLetreros.Domain.ValueObjects;
using DSLetreros.Infrastructure.Persistence;
using Xunit;
using Xunit.Abstractions;

namespace DSLetreros.Tests.Acceptance;

/// <summary>
/// v2.md §9: soak automatizado (200 Save/Open, 100 Send, 100 Undo/Redo) y
/// performance medido (render/save/open/compile/send) en 16x16 / 32x16 / 64x32.
///
/// Umbrales son DEFENSIVOS y medidos, no "correctos": detectan regresiones de
/// órdenes de magnitud (fuga de memoria, crecimiento accidental de O(n^2)), no
/// micro-optimizaciones. Se reportan los números reales vía ITestOutputHelper.
/// </summary>
public class SoakAndPerformanceTests
{
    private readonly ITestOutputHelper _output;
    public SoakAndPerformanceTests(ITestOutputHelper output) => _output = output;

    private static string NewRoot() =>
        Path.Combine(Path.GetTempPath(), "dsletras-soak", Guid.NewGuid().ToString("N"));

    private static Project MakeProject(string name, int w, int h, int objectCount = 20)
    {
        var p = new Project { Name = name, Canvas = new CanvasDefinition(w, h) };
        var scene = new Scene { Name = "Escena", Duration = TimeSpan.FromSeconds(5) };
        var layer = new Layer { Name = "Capa", Order = 0 };
        for (int i = 0; i < objectCount; i++)
        {
            layer.Objects.Add(new TextObject
            {
                Name = $"T{i}", Text = "DSLETRAS",
                Color = new RgbColor((byte)(i * 7 % 255), 0, (byte)(i * 13 % 255)),
                Position = new PixelPoint(i % (w - 6), (i * 2) % (h - 7)),
                Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)),
            });
        }
        scene.Layers.Add(layer);
        p.Scenes.Add(scene);
        return p;
    }

    // ------------------------- Soak -------------------------

    [Fact]
    public async Task Soak_200_save_open_roundtrips_stable()
    {
        var store = new AtlasProjectStore();
        var root = NewRoot();
        var svc = new ProjectService(store, root);
        var project = MakeProject("SoakSaveOpen", 32, 16);

        long memBefore = GC.GetTotalMemory(forceFullCollection: true);
        var sw = Stopwatch.StartNew();
        try
        {
            for (int i = 0; i < 200; i++)
            {
                var save = await svc.SaveAsync(project);
                Assert.True(save.Success, save.Message);
                var (open, loaded) = await svc.OpenByIdAsync(project.Id.Value);
                Assert.True(open.Success, open.Message);
                Assert.Equal("SoakSaveOpen", loaded!.Name);
            }
        }
        finally
        {
            sw.Stop();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        long memAfter = GC.GetTotalMemory(forceFullCollection: true);
        _output.WriteLine($"Soak 200 Save/Open: {sw.ElapsedMilliseconds} ms; " +
            $"mem delta = {(memAfter - memBefore) / 1024.0:F1} KiB");

        // Umbral defensivo: 200 roundtrips no deben superar ~60s ni una fuga > 32 MiB.
        Assert.True(sw.ElapsedMilliseconds < 60_000, $"200 Save/Open demasiado lento: {sw.ElapsedMilliseconds}ms");
        Assert.True(memAfter - memBefore < 32 * 1024 * 1024, "posible fuga de memoria en 200 Save/Open");
    }

    [Fact]
    public async Task Soak_100_send_simulator_stable()
    {
        var target = new SimulatorTarget(width: 32, height: 16);
        var service = new DeploymentService();
        var scene = MakeProject("SoakSend", 32, 16).Scenes[0];
        var canvas = new CanvasDefinition(32, 16);

        long memBefore = GC.GetTotalMemory(forceFullCollection: true);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            var r = await service.SendAsync(scene, canvas, target);
            Assert.True(r.Success, r.Error);
        }
        sw.Stop();
        long memAfter = GC.GetTotalMemory(forceFullCollection: true);

        _output.WriteLine($"Soak 100 Send: {sw.ElapsedMilliseconds} ms; " +
            $"mem delta = {(memAfter - memBefore) / 1024.0:F1} KiB");
        Assert.Empty(target.Staging);
        Assert.True(sw.ElapsedMilliseconds < 60_000, $"100 Send demasiado lento: {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Soak_100_add_delete_edit_cycles_stable()
    {
        // Undo/Redo es cliente (history del editor); su equivalente de dominio es
        // add/delete repetido. 100 ciclos no deben crecer objetos ni dejar huérfanos.
        var editing = new EditingService();
        var scene = new Scene { Name = "SoakEdit", Duration = TimeSpan.FromSeconds(5) };
        var layer = new Layer { Name = "Capa", Order = 0 };
        scene.Layers.Add(layer);

        for (int i = 0; i < 100; i++)
        {
            var added = editing.AddText(scene, $"TXT-{i}", new PixelPoint(i % 20, i % 10), layer: layer);
            Assert.NotNull(added);
            editing.DeleteObjects(scene, new[] { added.Id });
        }

        Assert.Empty(layer.Objects);
    }

    // ------------------------- Performance -------------------------

    [Theory]
    [InlineData(16, 16)]
    [InlineData(32, 16)]
    [InlineData(64, 32)]
    public async Task Performance_render_save_open_compile_send_under_threshold(int w, int h)
    {
        var canvas = new CanvasDefinition(w, h);
        var project = MakeProject("Perf", w, h);
        var scene = project.Scenes[0];

        // render
        var sw = Stopwatch.StartNew();
        var fb = SceneRenderer.Render(scene, TimeSpan.FromSeconds(2), canvas);
        sw.Stop();
        long renderMs = sw.ElapsedMilliseconds;
        Assert.Equal(w * h, fb.Width * fb.Height);

        // compile
        sw.Restart();
        var caps = new DeviceCapabilities { LogicalWidth = w, LogicalHeight = h, SupportedAnimations = Enum.GetValues<AnimationKind>().ToList() };
        var (pkg, err) = SceneCompiler.CompileForTarget(scene, canvas, caps);
        sw.Stop();
        long compileMs = sw.ElapsedMilliseconds;
        Assert.Null(err);
        Assert.NotNull(pkg);

        // save + open
        var store = new AtlasProjectStore();
        var root = NewRoot();
        var svc = new ProjectService(store, root);
        try
        {
            sw.Restart();
            var save = await svc.SaveAsync(project);
            Assert.True(save.Success, save.Message);
            var (open, _) = await svc.OpenByIdAsync(project.Id.Value);
            Assert.True(open.Success, open.Message);
            sw.Stop();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        long saveOpenMs = sw.ElapsedMilliseconds;

        // send
        var target = new SimulatorTarget(width: w, height: h);
        var service = new DeploymentService();
        sw.Restart();
        var send = await service.SendAsync(scene, canvas, target);
        sw.Stop();
        long sendMs = sw.ElapsedMilliseconds;
        Assert.True(send.Success, send.Error);

        _output.WriteLine($"Perf {w}x{h}: render={renderMs}ms compile={compileMs}ms " +
            $"saveOpen={saveOpenMs}ms send={sendMs}ms");

        // Umbrales defensivos medidos (escala con área; 64x32 = 2048 píxeles, humano).
        // Detectan regresiones de órdenes de magnitud, no jitter de CI.
        Assert.True(renderMs < 3_000, $"render {w}x{h} demasiado lento: {renderMs}ms");
        Assert.True(compileMs < 3_000, $"compile {w}x{h} demasiado lento: {compileMs}ms");
        Assert.True(saveOpenMs < 5_000, $"save/open {w}x{h} demasiado lento: {saveOpenMs}ms");
        Assert.True(sendMs < 5_000, $"send {w}x{h} demasiado lento: {sendMs}ms");
    }
}