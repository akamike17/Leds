using DSLetreros.Domain.Deployment;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.Rendering;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Deployment;

/// <summary>
/// Correcciones auditadas P1: preflight de SceneCompiler (evitar OOM), checksum
/// completo de ScenePackage (timing/protocolo/canvas/frames), tamaño wire real y
/// límites de FrameBuffer (checked/long).
/// </summary>
public class CompilerPreflightAndPackageTests
{
    private static Scene Sample()
    {
        var s = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(5) };
        var l = new Layer { Name = "L", Order = 0 };
        l.Objects.Add(new TextObject { Name = "T", Text = "HOLA", Color = RgbColor.White, Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)) });
        s.Layers.Add(l);
        return s;
    }

    private static readonly CanvasDefinition Canvas = new(32, 16);

    // ---------- Preflight ----------

    [Fact]
    public void Compile_rejects_nonfinite_or_nonpositive_frame_interval()
    {
        Assert.Null(SceneCompiler.Compile(Sample(), Canvas, double.NaN).Package);
        Assert.Null(SceneCompiler.Compile(Sample(), Canvas, double.PositiveInfinity).Package);
        Assert.Null(SceneCompiler.Compile(Sample(), Canvas, 0).Package);
        Assert.Null(SceneCompiler.Compile(Sample(), Canvas, -5).Package);
        Assert.NotNull(SceneCompiler.Compile(Sample(), Canvas, 100).Package);
    }

    [Fact]
    public void Compile_rejects_invalid_duration()
    {
        var s = Sample();
        s.Duration = TimeSpan.Zero;
        Assert.Null(SceneCompiler.Compile(s, Canvas).Package);
    }

    [Fact]
    public void Compile_rejects_oversized_canvas()
    {
        var huge = new CanvasDefinition(100_000, 100_000);
        var (pkg, err) = SceneCompiler.Compile(Sample(), huge);
        Assert.Null(pkg);
        Assert.NotNull(err);
    }

    [Fact]
    public void Compile_rejects_too_many_frames()
    {
        // duración enorme + intervalo mínimo → demasiados frames, rechazado antes de allocar.
        var s = Sample();
        s.Duration = TimeSpan.FromHours(24);
        var (pkg, err) = SceneCompiler.Compile(s, Canvas, 1.0);
        Assert.Null(pkg);
        Assert.Contains("frames", err);
    }

    // ---------- ScenePackage checksum completo ----------

    [Fact]
    public void Package_checksum_changes_when_protocol_or_timing_changes()
    {
        var (p1, _) = SceneCompiler.Compile(Sample(), Canvas, 100);
        var (p2, _) = SceneCompiler.Compile(Sample(), Canvas, 100);
        var baseChecksum = p1!.ComputeChecksum();

        // Cambiar ProtocolVersion → checksum cambia.
        p1.ProtocolVersion = 2;
        Assert.NotEqual(baseChecksum.Value, p1.ComputeChecksum().Value);

        // Cambiar FrameInterval → checksum cambia.
        p2!.FrameIntervalMs = 50;
        Assert.NotEqual(baseChecksum.Value, p2.ComputeChecksum().Value);
    }

    [Fact]
    public void Package_checksum_changes_when_loop_mode_or_frame_pixels_change()
    {
        var (p, _) = SceneCompiler.Compile(Sample(), Canvas, 100);
        var baseChecksum = p!.ComputeChecksum();

        p.LoopMode = SceneLoopMode.PingPong;
        Assert.NotEqual(baseChecksum.Value, p.ComputeChecksum().Value);

        var (p2, _) = SceneCompiler.Compile(Sample(), Canvas, 100);
        // Corromper un píxel de un frame: checksum cambia.
        p2!.Frames[0].Pixels[0] = (byte)(p2.Frames[0].Pixels[0] ^ 0xFF);
        Assert.NotEqual(p.ComputeChecksum().Value, p2.ComputeChecksum().Value);
    }

    [Fact]
    public void Package_checksum_includes_frame_time()
    {
        var (p, _) = SceneCompiler.Compile(Sample(), Canvas, 100);
        var baseChecksum = p!.ComputeChecksum();

        // Cambiar TimeMs de un frame (timing) → checksum cambia.
        p.Frames[1].TimeMs = 1234.5;
        Assert.NotEqual(baseChecksum.Value, p.ComputeChecksum().Value);
    }

    // ---------- RealWireSize ----------

    [Fact]
    public void RealWireSize_matches_serialized_payload_length()
    {
        var (p, _) = SceneCompiler.Compile(Sample(), Canvas, 100);
        var wire = p!.RealWireSize();
        var actual = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(p, ScenePackageJson.Options).LongLength;
        Assert.Equal(actual, wire);
        // EstimatedBytes ahora == RealWireSize (no una aproximación).
        Assert.Equal(wire, p.EstimatedBytes);
    }

    // ---------- FrameBuffer límites ----------

    [Fact]
    public void FrameBuffer_rejects_overflow_dimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameBuffer(int.MaxValue, int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameBuffer(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameBuffer(10, -1));
        // Límite máximo de píxeles.
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameBuffer(10000, 10000));
    }

    [Fact]
    public void FrameBuffer_accepts_limits_and_still_ignores_out_of_range_setpixel()
    {
        // SetPixel fuera de rango se ignora silenciosamente (SEMÁNTICA PRESERVADA).
        var fb = new FrameBuffer(4, 4);
        fb.SetPixel(99, 99, RgbColor.White);   // no lanza
        Assert.Equal(RgbColor.Black, fb.GetPixel(99, 99));
        Assert.Equal(RgbColor.Black, fb.GetPixel(0, 0));
    }
}