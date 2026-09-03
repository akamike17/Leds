using System.Text;
using DSLetreros.Domain.Deployment;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.Validation;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Deployment;

/// <summary>
/// Última ronda (ins.txt): ataca únicamente los supervivientes de LÓGICA que aún pueden
/// representar comportamiento observable real.
///
///  1. Contadores <c>Max</c> de <see cref="ProjectValidator"/> (MaxScenes=4096,
///     MaxLayersPerScene=1024, MaxEmbeddedAssets=4096): el off-by-one <c>&gt;</c>→<c>&gt;=</c>
///     es matable con frontera EXACTA (N válido, N+1 inválido) construida en un <c>for</c>
///     — no hace falta "un objeto enorme": se construyen secuencias de entidades triviales.
///  2. Guarda invariante <c>FrameIntervalMs</c> (NaN/∞/≤0) en <c>Firmware.Upload</c>
///     (ValidatePackageInvariants) y en <c>ChannelDisplayTarget.UploadAsync</c>: se demuestra
///     el rechazo exacto en el camino REAL de upload, no por reflexión.
///
/// No se añade ningún assert de string exacto ni ninguna prueba artificial de "acoplar a
/// implementación": cada assert observa <c>IsValid</c>/<c>Ok</c> y la categoría del error.
/// </summary>
public class LastRoundBoundaryTests
{
    // =====================================================================
    // 1. Contadores Max de ProjectValidator — frontera exacta N / N+1
    // =====================================================================

    private static Project NewProject() =>
        new()
        {
            Name = "P",
            FormatVersion = 1,
            Canvas = new CanvasDefinition(8, 8),
        };

    private static Scene ValidScene(string name, int layerCount, int objectsPerLayer)
    {
        var scene = new Scene { Name = name, Duration = TimeSpan.FromSeconds(5) };
        for (int i = 0; i < layerCount; i++)
        {
            var layer = new Layer { Name = $"L{i}", Order = i };
            for (int j = 0; j < objectsPerLayer; j++)
            {
                layer.Objects.Add(new TextObject
                {
                    Name = $"o{i}_{j}",
                    Text = "x",
                    Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)),
                });
            }
            scene.Layers.Add(layer);
        }
        return scene;
    }

    [Fact]
    public void Validator_accepts_exactly_MaxScenes_but_rejects_one_over()
    {
        // 4096 escenas válidas (1 capa, 1 objeto cada una) → válido.
        var atLimit = NewProject();
        for (int i = 0; i < ProjectValidator.MaxScenes; i++)
            atLimit.Scenes.Add(ValidScene($"s{i}", 1, 1));
        Assert.True(ProjectValidator.Validate(atLimit).IsValid,
            string.Join("; ", ProjectValidator.Validate(atLimit).Errors));

        // 4097 → supera el máximo de escenas.
        var over = NewProject();
        for (int i = 0; i < ProjectValidator.MaxScenes + 1; i++)
            over.Scenes.Add(ValidScene($"s{i}", 1, 1));
        var r = ProjectValidator.Validate(over);
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.Contains("máximo de " + ProjectValidator.MaxScenes + " escenas"));
    }

    [Fact]
    public void Validator_accepts_exactly_MaxLayersPerScene_but_rejects_one_over()
    {
        // 1024 capas VACÍAS (sin objetos → no dispara MaxObjectsPerScene) → válido.
        var atLimit = NewProject();
        atLimit.Scenes.Add(ValidScene("s", ProjectValidator.MaxLayersPerScene, 0));
        Assert.True(ProjectValidator.Validate(atLimit).IsValid,
            string.Join("; ", ProjectValidator.Validate(atLimit).Errors));

        // 1025 capas → supera el máximo de capas por escena.
        var over = NewProject();
        over.Scenes.Add(ValidScene("s", ProjectValidator.MaxLayersPerScene + 1, 0));
        var r = ProjectValidator.Validate(over);
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.Contains("máximo de " + ProjectValidator.MaxLayersPerScene + " capas"));
    }

    [Fact]
    public void Validator_accepts_exactly_MaxEmbeddedAssets_but_rejects_one_over()
    {
        // 4096 assets con contenido JSON válido (no indexado) → válido.
        var atLimit = NewProject();
        atLimit.Scenes.Add(ValidScene("s", 1, 1));
        for (int i = 0; i < ProjectValidator.MaxEmbeddedAssets; i++)
            atLimit.EmbeddedAssets[$"a{i}"] = "{\"width\":1,\"height\":1}";
        Assert.True(ProjectValidator.Validate(atLimit).IsValid,
            string.Join("; ", ProjectValidator.Validate(atLimit).Errors));

        // 4097 assets → supera el máximo de assets embebidos.
        var over = NewProject();
        over.Scenes.Add(ValidScene("s", 1, 1));
        for (int i = 0; i < ProjectValidator.MaxEmbeddedAssets + 1; i++)
            over.EmbeddedAssets[$"a{i}"] = "{\"width\":1,\"height\":1}";
        var r = ProjectValidator.Validate(over);
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.Contains("máximo de " + ProjectValidator.MaxEmbeddedAssets + " assets"));
    }

    // =====================================================================
    // 2. Guarda invariante FrameIntervalMs en el camino REAL de upload
    // =====================================================================

    private static Scene SampleScene() => new()
    {
        Name = "G", Duration = TimeSpan.FromSeconds(1),
        Layers = { new Layer { Name = "L", Order = 0, Objects = {
            new TextObject { Name = "T", Text = "X", Color = RgbColor.White,
                Position = new PixelPoint(0, 0),
                Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)) } } } },
    };
    private static readonly CanvasDefinition Canvas = new(16, 8);

    // ---- Firmware.Upload (ValidatePackageInvariants) ----

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void Firmware_upload_rejects_non_positive_or_non_finite_frame_interval(double interval)
    {
        var fw = new Firmware("SER-INV", width: 16, height: 8);
        var pkg = SceneCompiler.Compile(SampleScene(), Canvas)!.Package!;
        var size = pkg.EstimatedBytes;             // tamaño wire ANTES de corromper

        var (prepOk, _, ticket) = fw.Prepare("t-inv", size);
        Assert.True(prepOk);

        pkg.FrameIntervalMs = interval;            // corromper después de fijar el tamaño esperado

        // La invariante se valida ANTES de EstimatedBytes (fix): rechazo limpio, sin excepción.
        var (ok, err) = fw.Upload(ticket!, pkg);
        Assert.False(ok);
        Assert.NotNull(err);
        Assert.Contains("FrameIntervalMs", err);
    }

    [Fact]
    public void Firmware_upload_accepts_positive_finite_frame_interval()
    {
        var fw = new Firmware("SER-OK", width: 16, height: 8);
        var pkg = SceneCompiler.Compile(SampleScene(), Canvas)!.Package!;
        pkg.FrameIntervalMs = 0.1;                 // positivo y finito (> 0) → borde válido
        var size = pkg.EstimatedBytes;

        var (prepOk, _, ticket) = fw.Prepare("t-ok", size);
        Assert.True(prepOk);

        var (ok, err) = fw.Upload(ticket!, pkg);
        Assert.True(ok, err);
    }

    // ---- ChannelDisplayTarget.UploadAsync (misma guarda, L108) ----

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0.0)]
    public async Task Channel_upload_rejects_invalid_frame_interval_without_emitting_frames(double interval)
    {
        var channel = new CountingAckChannel();
        var target = new ChannelDisplayTarget(channel);

        var pkg = SceneCompiler.Compile(SampleScene(), Canvas)!.Package!;
        pkg.FrameIntervalMs = interval;

        var up = await target.UploadAsync("ticket-x", pkg);
        Assert.False(up.Success);
        Assert.Contains("FrameIntervalMs", up.Error);
        // La guarda es local y previa a la fragmentación: no debe emitir ningún frame de upload.
        Assert.Equal(0, channel.UploadFrameCount);
    }

    [Fact]
    public async Task Channel_upload_accepts_valid_frame_interval_and_fragments()
    {
        var channel = new CountingAckChannel();
        var target = new ChannelDisplayTarget(channel);

        var pkg = SceneCompiler.Compile(SampleScene(), Canvas)!.Package!;
        var up = await target.UploadAsync("ticket-ok", pkg);
        Assert.True(up.Success);
        Assert.True(channel.UploadFrameCount >= 1, "debe fragmentar/emitir al menos una parte");
    }

    /// <summary>
    /// Canal fake que responde ACK a todo y cuenta las requests de tipo Upload
    /// (para demostrar que la guarda de invariante corta ANTES de emitir bytes).
    /// </summary>
    private sealed class CountingAckChannel : IDeviceChannel
    {
        private int _uploads;
        public int UploadFrameCount => _uploads;
        public string Transport => "fake";
        public string Endpoint => "fake://counting";

        public Task<byte[]> RequestAsync(byte[] frame, CancellationToken ct = default)
        {
            var (op, _, _) = DeviceProtocol.Unwrap(frame);
            if (op == DeviceProtocol.OpUpload) Interlocked.Increment(ref _uploads);
            return Task.FromResult(DeviceProtocol.Ack());
        }
    }
}