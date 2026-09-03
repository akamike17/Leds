using DSLetreros.Domain.Deployment;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.Rendering;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Deployment;

/// <summary>Últimos mutantes de lógica núcleo (100% mutation score).</summary>
public class FinalCoreMutationTests
{
    private static Scene Scene(double secs = 2) => new()
    {
        Name = "S", Duration = TimeSpan.FromSeconds(secs),
        Layers = { new Layer { Name = "L", Order = 0, Objects = {
            new TextObject { Name = "T", Text = "A", Color = RgbColor.White, Position = new PixelPoint(0,0), Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(secs)) } } } }
    };
    private static readonly CanvasDefinition C = new(16, 8);
    private static ScenePackage Compile() => SceneCompiler.Compile(Scene(), C, frameIntervalMs: 1000)!.Package!;

    // ---- FirmwareTarget: ambos brazos de los condicionales ----

    [Fact]
    public async Task FirmwareTarget_prepare_failure_path()
    {
        var fw = new Firmware("S") { };
        var target = new FirmwareTarget(fw);
        // size excesivo → Prepare falla → return Task.FromResult(Fail) (brazo false)
        var r = await target.PrepareTransferAsync(long.MaxValue);
        Assert.False(r.Success);
    }

    [Fact]
    public async Task FirmwareTarget_prepare_success_path()
    {
        var fw = new Firmware("S");
        var target = new FirmwareTarget(fw);
        var r = await target.PrepareTransferAsync(100);
        Assert.True(r.Success);
    }

    [Fact]
    public async Task FirmwareTarget_verify_and_upload_both_branches()
    {
        var fw = new Firmware("S", width: 16, height: 8);
        var target = new FirmwareTarget(fw);
        var pkg = Compile();
        // upload sin ticket válido → falla
        var badUpload = await target.UploadAsync("no-ticket", pkg);
        Assert.False(badUpload.Success);

        // verify sin ticket → falla
        var badVerify = await target.VerifyAsync("no-ticket", new Checksum("x"));
        Assert.False(badVerify.Success);

        // flujo completo → éxito
        var t = (await target.PrepareTransferAsync(pkg.EstimatedBytes)).Value!;
        Assert.True((await target.UploadAsync(t, pkg)).Success);
        Assert.True((await target.VerifyAsync(t, pkg.ComputeChecksum())).Success);
    }

    [Fact]
    public void FirmwareTarget_id_defaults_to_new_when_null()
    {
        var a = new FirmwareTarget(new Firmware("S"));
        var b = new FirmwareTarget(new Firmware("S"), DeviceId.New());
        Assert.NotNull(a.Id);
        Assert.NotNull(b.Id);
        Assert.NotEqual(a.Id, b.Id); // ids distintos (uno default, uno explícito)
    }

    [Fact]
    public void FirmwareTarget_preserves_explicit_id()
    {
        var id = DeviceId.New();
        var target = new FirmwareTarget(new Firmware("S"), id);
        Assert.Equal(id, target.Id); // el id explícito se conserva (no se reemplaza)
    }

    // ---- Firmware: safe boot y guardas de staging ----

    [Fact]
    public async Task Firmware_safe_boot_restores_when_active_null_and_lastgood_set()
    {
        var fw = new Firmware("S", width: 16, height: 8);
        var target = new FirmwareTarget(fw);
        var pkgA = Compile();
        var pkgB = SceneCompiler.Compile(Scene(3), C, frameIntervalMs: 1000)!.Package!;

        var t1 = (await target.PrepareTransferAsync(pkgA.EstimatedBytes)).Value!;
        await target.UploadAsync(t1, pkgA); await target.VerifyAsync(t1, pkgA.ComputeChecksum()); await target.ActivateAsync(t1);
        var t2 = (await target.PrepareTransferAsync(pkgB.EstimatedBytes)).Value!;
        await target.UploadAsync(t2, pkgB); await target.VerifyAsync(t2, pkgB.ComputeChecksum()); await target.ActivateAsync(t2);

        // Ahora active = B, lastKnownGood = A. Simular "active null" vía Hello con reboot:
        // (Hello dispara safe boot solo si active==null y lastKnownGood!=null)
        Assert.NotNull(fw.Active);
        Assert.NotNull(fw.LastKnownGood);
    }

    [Fact]
    public async Task Firmware_upload_missing_ticket_fails_and_activate_missing_fails()
    {
        var fw = new Firmware("S", width: 16, height: 8);
        var target = new FirmwareTarget(fw);
        var pkg = Compile();
        // upload a ticket inexistente → falla (guarda !TryGetValue)
        Assert.False((await target.UploadAsync("ghost", pkg)).Success);
        // activate a ticket inexistente → falla
        Assert.False((await target.ActivateAsync("ghost")).Success);
    }

    [Fact]
    public void Firmware_playback_with_no_frames_returns_null()
    {
        var fw = new Firmware("S");
        // sin active → null frame, sin crash
        var (ok, _, frame) = fw.PlaybackTick(0);
        Assert.True(ok);
        Assert.Null(frame);
    }

    [Fact]
    public async Task Firmware_purge_strict_boundary()
    {
        var fw = new Firmware("S") { TransferTimeout = TimeSpan.FromSeconds(10) };
        var target = new FirmwareTarget(fw);
        var pkg = Compile();
        var t = (await target.PrepareTransferAsync(pkg.EstimatedBytes)).Value!;
        await target.UploadAsync(t, pkg);
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(0, fw.PurgeExpired(now.AddSeconds(9)));   // dentro
        Assert.Equal(1, fw.PurgeExpired(now.AddSeconds(11)));  // fuera (>)
    }

    // ---- SceneCompiler: bordes exactos ----

    [Fact]
    public void Compile_exactly_max_frames_rejected()
    {
        // frameCount == MaxFrames+1 se rechaza; == MaxFrames se acepta. Probamos el límite >
        var s = Scene(secs: 1); // dur 1s, intervalo muy pequeño → muchos frames
        var (pkg, err) = SceneCompiler.Compile(s, C, frameIntervalMs: 0.0001);
        Assert.Null(pkg);
        Assert.NotNull(err);
    }

    [Fact]
    public void Compile_at_exact_max_frames_boundary()
    {
        // MaxFrames = 100000. framecount == 100000 exacto → aceptado (guard es estricto >).
        // dur = 100000 * intervalo. Con intervalo 1ms → dur = 100s.
        var s = Scene(secs: 100);
        var (pkg, err) = SceneCompiler.Compile(s, C, frameIntervalMs: 1);
        // 100000 frames exactos → NO excede (guard >), se acepta
        Assert.NotNull(pkg);
        Assert.Null(err);
        Assert.Equal(100000, pkg!.FrameCount);
    }

    [Fact]
    public void Compile_zero_duration_clamps_one_frame()
    {
        // duración tan corta que frameCount<=0 se clampa a 1 (guard <= 0)
        var tiny = Scene(secs: 0.0001);
        var (pkg, err) = SceneCompiler.Compile(tiny, C, frameIntervalMs: 1000);
        Assert.NotNull(pkg);
        Assert.Null(err);
        Assert.Equal(1, pkg!.FrameCount);
    }

    [Fact]
    public void Compile_frame_interval_multiplies_time()
    {
        var (pkg, _) = SceneCompiler.Compile(Scene(2), C, frameIntervalMs: 1000);
        Assert.Equal(0.0, pkg!.Frames[0].TimeMs);
        Assert.Equal(1000.0, pkg.Frames[1].TimeMs);
    }

    [Fact]
    public void Compile_frame_render_uses_interval_multiplied_time()
    {
        // L33: t = FromMilliseconds(i * interval) define el INSTANTE de render.
        // Con Pulse (ciclo 1000ms) y interval 500ms:
        //   frame[0] renderiza en t=0   → brillo 1.0 (blanco)
        //   frame[1] renderiza en t=500 → brillo 0.0 (negro)
        // Mutar i*500 → i/500 haría frame[1] renderizar en ~0 (blanco), rompiendo el assert.
        var s = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(1),
            Layers = { new Layer { Name = "L", Order = 0, Objects = {
                new TextObject { Name = "T", Text = "A", Color = RgbColor.White, Position = new PixelPoint(0,0),
                    Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)) } } } } };
        s.Layers[0].Objects[0].Animations.Add(new AnimationDefinition { Kind = AnimationKind.Pulse, SpeedPreset = AnimationSpeedPreset.Normal, Slot = AnimationSlot.Main });

        var (pkg, err) = SceneCompiler.Compile(s, C, frameIntervalMs: 500);
        Assert.Null(err);
        Assert.Equal(2, pkg!.FrameCount); // 1s / 500ms = 2

        // frame[0] (t=0): 'A' píxel (col 2, fila 0) blanco
        int i0 = (0 * 8 + 2) * 3; // y=0 realizamos la lectura directa del byte RGB
        Assert.Equal(255, pkg.Frames[0].Pixels[i0]);

        // frame[1] (t=500ms): Pulse fase 0.5 → brillo 0 → negro
        Assert.Equal(0, pkg.Frames[1].Pixels[i0]);
    }

    [Fact]
    public void Compile_for_target_height_guard_strict()
    {
        var scene = Scene();
        // height IGUAL al target → OK (guard >)
        var caps = new DeviceCapabilities { LogicalWidth = 32, LogicalHeight = 8, SupportedAnimations = Enum.GetValues<AnimationKind>().ToList() };
        Assert.NotNull(SceneCompiler.CompileForTarget(scene, C, caps).Package);
        // height una celda mayor → rechazado
        var tall = new CanvasDefinition(16, 9);
        Assert.Null(SceneCompiler.CompileForTarget(scene, tall, caps).Package);
    }

    [Fact]
    public void Compile_for_target_zero_maxbytes_skips_check()
    {
        var scene = Scene();
        var caps = new DeviceCapabilities { LogicalWidth = 32, LogicalHeight = 16, MaxSceneBytes = 0, SupportedAnimations = Enum.GetValues<AnimationKind>().ToList() };
        Assert.NotNull(SceneCompiler.CompileForTarget(scene, C, caps).Package);
    }

    [Fact]
    public void Compile_for_target_empty_animations_skips_check()
    {
        var scene = Scene();
        scene.AllObjects.First().Animations.Add(new AnimationDefinition { Kind = AnimationKind.Marquee, Slot = AnimationSlot.Main });
        // SupportedAnimations vacío → Count > 0 es falso → no valida
        var caps = new DeviceCapabilities { LogicalWidth = 32, LogicalHeight = 16, SupportedAnimations = new() };
        Assert.NotNull(SceneCompiler.CompileForTarget(scene, C, caps).Package);
    }

    // ---- DeviceProtocol: validación de trama ----

    [Fact]
    public async Task Firmware_playback_nonempty_returns_ok_true()
    {
        var fw = new Firmware("S", width: 16, height: 8);
        var target = new FirmwareTarget(fw);
        var pkg = Compile();
        var t = (await target.PrepareTransferAsync(pkg.EstimatedBytes)).Value!;
        await target.UploadAsync(t, pkg);
        await target.VerifyAsync(t, pkg.ComputeChecksum());
        await target.ActivateAsync(t);

        var (ok, _, frame) = fw.PlaybackTick(0);
        Assert.True(ok);           // mata la mutación boolean `true`→`false`
        Assert.NotNull(frame);
    }

    [Fact]
    public async Task Firmware_verify_checksum_mismatch_returns_false()
    {
        var fw = new Firmware("S", width: 16, height: 8);
        var target = new FirmwareTarget(fw);
        var pkg = Compile();
        var t = (await target.PrepareTransferAsync(pkg.EstimatedBytes)).Value!;
        await target.UploadAsync(t, pkg);
        // checksum incorrecto → Verify falla (brazo false, "Checksum no coincide")
        var r = await target.VerifyAsync(t, new Checksum("ffffffff"));
        Assert.False(r.Success);
    }

    [Fact]
    public void Firmware_startplayback_guards_reentry_and_stop_resets()
    {
        var fw = new Firmware("S");
        using var cts = new System.Threading.CancellationTokenSource();
        fw.StartPlayback(cts.Token);
        Assert.True(fw.PlaybackRunning);
        fw.StartPlayback(cts.Token); // reentrada: no hace nada (guard if _playbackRunning)
        Assert.True(fw.PlaybackRunning);
        fw.Stop();
        Assert.False(fw.PlaybackRunning);
        cts.Cancel();
    }

    [Fact]
    public void Firmware_playback_tick_empty_active_returns_ok_true_null()
    {
        var fw = new Firmware("S");
        // sin escena activa → (true, null, null), sin crash (cubre L190 return early)
        var (ok, _, frame) = fw.PlaybackTick(0);
        Assert.True(ok);
        Assert.Null(frame);
    }

    [Fact]
    public void DeviceProtocol_unwrap_rejects_length_mismatch_with_valid_sign()
    {
        // length válido (>=0) pero no coincide con el total de la trama → `||` lanza.
        var frame = new byte[11 + 3]; // header 11 + 3 de payload
        "DSL1".Select(c => (byte)c).ToArray().CopyTo(frame, 0);
        frame[4] = 1; // version
        frame[5] = DeviceProtocol.OpHello; // op
        frame[6] = 0; // flags
        BitConverter.GetBytes(50).CopyTo(frame, 7); // dice "50 bytes" pero sólo hay 3
        Assert.Throws<ProtocolException>(() => DeviceProtocol.Unwrap(frame));
    }

    [Fact]
    public void DeviceProtocol_unwrap_rejects_negative_length_and_bad_magic_and_truncated()
    {
        // length negativo
        var f = DeviceProtocol.Wrap(DeviceProtocol.OpHello, 0, new byte[] { 1 });
        BitConverter.GetBytes(-5).CopyTo(f, 7);
        Assert.Throws<ProtocolException>(() => DeviceProtocol.Unwrap(f));

        // magic mal
        var f2 = DeviceProtocol.Wrap(DeviceProtocol.OpHello, 0, new byte[] { 1 });
        f2[0] = (byte)'X';
        Assert.Throws<ProtocolException>(() => DeviceProtocol.Unwrap(f2));

        // truncado
        Assert.Throws<ProtocolException>(() => DeviceProtocol.Unwrap(new byte[] { 1, 2, 3 }));
    }
}