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

    /// <summary>
    /// Tamaño REAL del payload a transmitir (serialización JSON del paquete con
    /// <see cref="ScenePackageJson.Options"/>), no una estimación. Se usa para el
    /// preflight Prepare y para validar contra MaxSceneBytes. No se serializa
    /// (es derivado; se excluye para evitar recursión y payload redundante).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public long EstimatedBytes => RealWireSize();

    /// <summary>Calcula el tamaño wire real serializando el paquete (JSON para el cable).</summary>
    public long RealWireSize()
    {
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(this, ScenePackageJson.Options);
        return bytes.LongLength;
    }

    /// <summary>Calcula y sella el checksum del paquete.</summary>
    public Checksum ComputeChecksum()
    {
        using var ms = new System.IO.MemoryStream();
        using var w = new System.IO.BinaryWriter(ms);
        // Cubre TODO campo que cambia la reproducción: ProtocolVersion,
        // DurationMs (representación SIN pérdida: bits double), LoopMode, Canvas,
        // FrameIntervalMs, FrameCount y, por frame, TimeMs + Pixels.
        // NO incluye SceneId (GUID aleatorio) ni SceneName (metadata) — el checksum
        // representa el CONTENIDO compilado (determinismo y equivalencia R5).
        w.Write(ProtocolVersion);
        w.Write(DurationMs);                             // double: sin pérdida de precisión
        w.Write((int)LoopMode);
        w.Write(Canvas.Width);
        w.Write(Canvas.Height);
        w.Write(FrameIntervalMs);                        // double
        w.Write(Frames.Count);
        foreach (var f in Frames)
        {
            w.Write(f.TimeMs);                           // double: timing por frame
            w.Write(f.Pixels.Length);
            w.Write(f.Pixels);
        }
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