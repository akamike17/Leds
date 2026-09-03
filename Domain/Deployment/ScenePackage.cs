using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;

namespace DSLetreros.Domain.Deployment;

/// <summary>Paquete compilado de una escena: representación ejecutable determinista (invariante 2).</summary>
public sealed class ScenePackage
{
    public SceneId SceneId { get; set; } = SceneId.New();
    public string SceneName { get; set; } = string.Empty;
    public CanvasDefinition Canvas { get; set; } = new(32, 16);

    /// <summary>Duración total del timeline (ms) y loop mode.</summary>
    public double DurationMs { get; set; }
    public SceneLoopMode LoopMode { get; set; } = SceneLoopMode.Loop;
    public int ProtocolVersion { get; set; } = 1;

    /// <summary>
    /// Frames prerenderizados (golden): frame[i] es el framebuffer lógico en el
    /// instante t = i * FrameInterval. Determinista: misma entrada → mismos frames.
    /// </summary>
    public List<CompiledFrame> Frames { get; set; } = new();

    public double FrameIntervalMs { get; set; } = 100.0;

    /// <summary>Checksum del contenido compilado (excluye metadata mutable).</summary>
    public Checksum Checksum { get; set; } = Checksum.Empty;

    public int FrameCount => Frames.Count;

    /// <summary>Bytes serializados aproximados (para MaxSceneBytes).</summary>
    public long EstimatedBytes =>
        Frames.Sum(f => (long)f.Pixels.Length) + Frames.Count * 16L + 256L;

    /// <summary>Calcula y sella el checksum del paquete.</summary>
    public Checksum ComputeChecksum()
    {
        using var ms = new System.IO.MemoryStream();
        using var w = new System.IO.BinaryWriter(ms);
        // NOTA: no incluimos SceneId (GUID aleatorio) — el checksum representa el
        // CONTENIDO compilado, para determinismo y equivalencia R5.
        w.Write((int)DurationMs);
        w.Write((int)LoopMode);
        w.Write(Canvas.Width);
        w.Write(Canvas.Height);
        foreach (var f in Frames)
            w.Write(f.Pixels);
        unchecked
        {
            var bytes = ms.ToArray();
            var hash = System.Security.Cryptography.SHA256.HashData(bytes);
            Checksum = new Checksum(Convert.ToHexString(hash));
        }
        return Checksum;
    }
}

/// <summary>Frame compilado: framebuffer lógico aplanado (RGB24 por píxel, row-major).</summary>
public sealed class CompiledFrame
{
    public double TimeMs { get; set; }
    public byte[] Pixels { get; set; } = Array.Empty<byte>();

    public static CompiledFrame FromFrameBuffer(Rendering.FrameBuffer fb, double timeMs)
    {
        int w = fb.Width, h = fb.Height;
        var bytes = new byte[w * h * 3];
        int i = 0;
        foreach (var c in fb.AllPixels())
        {
            bytes[i++] = c.R;
            bytes[i++] = c.G;
            bytes[i++] = c.B;
        }
        return new CompiledFrame { TimeMs = timeMs, Pixels = bytes };
    }
}