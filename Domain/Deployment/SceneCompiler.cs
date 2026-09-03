using DSLetreros.Domain.Entities;
using DSLetreros.Domain.Rendering;
using DSLetreros.Domain.ValueObjects;

namespace DSLetreros.Domain.Deployment;

/// <summary>
/// Compila una Scene a un ScenePackage determinista: prerenderiza frames golden
/// en intervalos fijos (un único Render(scene,time), invariante 4). La salida es
/// lo que Simulator y firmware reproducen; equivalencia divine (R5).
/// </summary>
public static class SceneCompiler
{
    public const double DefaultFrameIntervalMs = 100.0;
    public const int MaxFrames = 100_000;

    /// <summary>Compila una escena completa a N frames. Devuelve null con mensaje si es inválida.</summary>
    public static (ScenePackage? Package, string? Error) Compile(
        Scene scene, CanvasDefinition canvas, double frameIntervalMs = DefaultFrameIntervalMs)
    {
        if (scene == null) return (null, "Escena nula.");
        if (scene.Duration <= TimeSpan.Zero) return (null, "La escena debe tener duración > 0.");

        var durationMs = scene.Duration.TotalMilliseconds;
        int frameCount = (int)Math.Ceiling(durationMs / frameIntervalMs);
        if (frameCount <= 0) frameCount = 1;
        if (frameCount > MaxFrames)
            return (null, $"Demasiados frames ({frameCount}); reduzca la duración o aumente el intervalo.");

        var frames = new List<CompiledFrame>(frameCount);
        for (int i = 0; i < frameCount; i++)
        {
            var t = TimeSpan.FromMilliseconds(i * frameIntervalMs);
            var fb = SceneRenderer.Render(scene, t, canvas);
            frames.Add(CompiledFrame.FromFrameBuffer(fb, i * frameIntervalMs));
        }

        var pkg = new ScenePackage
        {
            SceneId = scene.Id,
            SceneName = scene.Name,
            Canvas = canvas,
            DurationMs = durationMs,
            LoopMode = scene.LoopMode,
            FrameIntervalMs = frameIntervalMs,
            Frames = frames,
        };
        pkg.ComputeChecksum();
        return (pkg, null);
    }

    /// <summary>Compila contra las capacidades del target, validando límites (TargetValidate).</summary>
    public static (ScenePackage? Package, string? Error) CompileForTarget(
        Scene scene, CanvasDefinition canvas, DeviceCapabilities caps, double frameIntervalMs = DefaultFrameIntervalMs)
    {
        var (pkg, err) = Compile(scene, canvas, frameIntervalMs);
        if (pkg == null) return (null, err);

        // validar dimensiones del target
        if (caps.LogicalWidth > 0 && canvas.Width > caps.LogicalWidth)
            return (null, $"Canvas (ancho {canvas.Width}) excede el target (ancho {caps.LogicalWidth}).");
        if (caps.LogicalHeight > 0 && canvas.Height > caps.LogicalHeight)
            return (null, $"Canvas (alto {canvas.Height}) excede el target (alto {caps.LogicalHeight}).");

        // validar animaciones soportadas
        var used = scene.AllObjects.SelectMany(o => o.Animations).Select(a => a.Kind).Distinct().ToList();
        var unsupported = used.Where(k => caps.SupportedAnimations.Count > 0 && !caps.SupportedAnimations.Contains(k)).ToList();
        if (unsupported.Count > 0)
            return (null, $"Animaciones no soportadas por el target: {string.Join(", ", unsupported)}.");

        // validar tamaño
        if (caps.MaxSceneBytes > 0 && pkg.EstimatedBytes > caps.MaxSceneBytes)
            return (null, $"Paquete ({pkg.EstimatedBytes}B) excede MaxSceneBytes ({caps.MaxSceneBytes}B).");

        return (pkg, null);
    }
}