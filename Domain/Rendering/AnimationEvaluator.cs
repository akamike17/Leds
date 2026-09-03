using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;

namespace DSLetreros.Domain.Rendering;

/// <summary>
/// Resultado visual de evaluar una animación en un instante t. Aplicado de forma
/// determinista en el renderer (invariante 4: misma entrada → mismos píxeles).
/// </summary>
public readonly struct AnimationState
{
    /// <summary>El objeto no debe dibujarse en t (Blink en fase off, fuera de frames, etc.).</summary>
    public bool Visible { get; init; }

    /// <summary>Offset horizontal/vertical aplicado sobre Position (Slide/Marquee/Wipe).</summary>
    public PixelPoint Offset { get; init; }

    /// <summary>Factor de brillo global 0..1 (Pulse), 1 = sin cambio.</summary>
    public double BrightnessFactor { get; init; }

    /// <summary>Clip de revelado opcional en coordenadas del objeto (Wipe). Null = sin recorte.</summary>
    public PixelRect? Clip { get; init; }

    public static AnimationState Default => new()
    {
        Visible = true,
        Offset = new PixelPoint(0, 0),
        BrightnessFactor = 1.0,
        Clip = null,
    };
}

/// <summary>
/// Evalúa animaciones (Entrance/Main/Exit) de un objeto para un instante de tiempo.
/// Pure-function y determinista: no depende de reloj ni estado mutable.
/// </summary>
public static class AnimationEvaluator
{
    /// <summary>Velocidad por preset en milisegundos por ciclo base.</summary>
    public static readonly IReadOnlyDictionary<AnimationSpeedPreset, TimeSpan> CycleLengths =
        new Dictionary<AnimationSpeedPreset, TimeSpan>
        {
            [AnimationSpeedPreset.Slow] = TimeSpan.FromMilliseconds(2000),
            [AnimationSpeedPreset.Normal] = TimeSpan.FromMilliseconds(1000),
            [AnimationSpeedPreset.Fast] = TimeSpan.FromMilliseconds(500),
        };

    /// <summary>Evalúa el estado visual de un objeto en t. El viewport (canvas) viaja como
    /// argumento para que Render sea puro/thread-safe y dos canvases distintos no se pisen.</summary>
    public static AnimationState Evaluate(SceneObject obj, TimeSpan t, int viewportWidth = 32)
    {
        if (!obj.Timing.Contains(t))
            return new AnimationState { Visible = false, Offset = new PixelPoint(0, 0), BrightnessFactor = 1.0 };

        var local = t - obj.Timing.Start;
        var def = ResolveActive(obj.Animations, t, obj.Timing);
        if (def == null || def.Kind == AnimationKind.Fixed)
            return AnimationState.Default;

        var cycle = CycleLengths[def.SpeedPreset];
        long ticks = cycle.Ticks;

        switch (def.Kind)
        {
            case AnimationKind.Blink:
                // 50% on, 50% off dentro del ciclo (excepto Wipe/Frame que usan su propia fase)
                return new AnimationState
                {
                    Visible = (local.Ticks / (ticks / 2)) % 2 == 0,
                    Offset = new PixelPoint(0, 0),
                    BrightnessFactor = 1.0,
                };

            case AnimationKind.Pulse:
                // brillo senoidal 0..1
                var phase = (double)(local.Ticks % ticks) / ticks;
                var b = 0.5 + 0.5 * Math.Cos(2 * Math.PI * phase); // 1 → 0 → 1
                return new AnimationState
                {
                    Visible = true,
                    Offset = new PixelPoint(0, 0),
                    BrightnessFactor = b,
                };

            case AnimationKind.Slide:
                // desliza desde el borde de dirección hacia la posición (entrada) o reverso
                return SlideState(obj, local, def);

            case AnimationKind.Marquee:
                // texto/largo si no cabe: desplaza horizontalmente de forma envolvente
                return MarqueeState(obj, local, def, viewportWidth);

            case AnimationKind.Wipe:
                return WipeState(obj, local, def);

            case AnimationKind.Frame:
                // animación por frames discretos: 1 frame = una fila/columna por paso temporal
                return FrameState(obj, local, def);

            default:
                return AnimationState.Default;
        }
    }

    /// <summary>Resuelve el AnimationDefinition activo en un slot para un rango temporal dado.</summary>
    public static AnimationDefinition? ResolveActive(
        IEnumerable<AnimationDefinition> animations, TimeSpan t, TimeRange timing)
    {
        var list = animations?.Where(a => a != null).ToList() ?? new();
        if (list.Count == 0) return null;

        var local = t - timing.Start;
        var dur = timing.Duration;
        if (dur <= TimeSpan.Zero) return null;

        // Entrada (primer 20% del rango si está definida), Salida (último 20%), Main (resto).
        var entrance = list.Find(a => a.Slot == AnimationSlot.Entrance);
        var exit = list.Find(a => a.Slot == AnimationSlot.Exit);
        var main = list.Find(a => a.Slot == AnimationSlot.Main) ?? list[0];

        var entranceEnd = TimeSpan.FromTicks(dur.Ticks / 5);
        var exitStart = TimeSpan.FromTicks(dur.Ticks * 4 / 5);

        if (entrance != null && local < entranceEnd) return entrance;
        if (exit != null && local >= exitStart) return exit;
        return main;
    }

    private static AnimationState SlideState(SceneObject obj, TimeSpan local, AnimationDefinition def)
    {
        var cycle = CycleLengths[def.SpeedPreset];
        double progress = Math.Clamp((double)local.Ticks / cycle.Ticks, 0.0, 1.0);
        bool reverse = def.Slot == AnimationSlot.Exit;
        if (reverse) progress = 1.0 - progress;

        int amount = (int)Math.Round(progress * obj.Size.Width);
        var dir = def.Direction ?? AnimationDirection.Left;
        return dir switch
        {
            AnimationDirection.Left => new AnimationState { Visible = true, Offset = new PixelPoint(amount, 0), BrightnessFactor = 1.0 },
            AnimationDirection.Right => new AnimationState { Visible = true, Offset = new PixelPoint(-amount, 0), BrightnessFactor = 1.0 },
            AnimationDirection.Up => new AnimationState { Visible = true, Offset = new PixelPoint(0, amount), BrightnessFactor = 1.0 },
            AnimationDirection.Down => new AnimationState { Visible = true, Offset = new PixelPoint(0, -amount), BrightnessFactor = 1.0 },
            _ => AnimationState.Default,
        };
    }

    private static AnimationState MarqueeState(SceneObject obj, TimeSpan local, AnimationDefinition def, int viewportWidth)
    {
        var cycle = CycleLengths[def.SpeedPreset];
        double progress = (double)(local.Ticks % cycle.Ticks) / cycle.Ticks;
        var dir = def.Direction ?? AnimationDirection.Left;
        // El contenido se mueve en -x con envoltura respecto del viewport; el offset
        // replica un desplazamiento de `progress * (width+viewport)`.
        int travel = obj.Size.Width + viewportWidth;
        int off = (int)Math.Round(progress * travel);
        if (dir == AnimationDirection.Right) off = travel - off;
        return new AnimationState
        {
            Visible = true,
            Offset = new PixelPoint(-off, 0),
            BrightnessFactor = 1.0,
        };
    }

    private static AnimationState WipeState(SceneObject obj, TimeSpan local, AnimationDefinition def)
    {
        var cycle = CycleLengths[def.SpeedPreset];
        double progress = Math.Clamp((double)local.Ticks / cycle.Ticks, 0.0, 1.0);
        var dir = def.Direction ?? AnimationDirection.Left;
        // revela un rectángulo progresivo dentro del objeto
        int w = obj.Size.Width, h = obj.Size.Height;
        return dir switch
        {
            AnimationDirection.Left => new AnimationState { Visible = true, Offset = new PixelPoint(0, 0), BrightnessFactor = 1.0, Clip = new PixelRect(new PixelPoint(0, 0), new PixelSize((int)Math.Round(w * progress), h)) },
            AnimationDirection.Right => new AnimationState { Visible = true, Offset = new PixelPoint(0, 0), BrightnessFactor = 1.0, Clip = new PixelRect(new PixelPoint((int)Math.Round(w * (1 - progress)), 0), new PixelSize(w - (int)Math.Round(w * (1 - progress)), h)) },
            AnimationDirection.Up => new AnimationState { Visible = true, Offset = new PixelPoint(0, 0), BrightnessFactor = 1.0, Clip = new PixelRect(new PixelPoint(0, 0), new PixelSize(w, (int)Math.Round(h * progress))) },
            AnimationDirection.Down => new AnimationState { Visible = true, Offset = new PixelPoint(0, 0), BrightnessFactor = 1.0, Clip = new PixelRect(new PixelPoint(0, (int)Math.Round(h * (1 - progress))), new PixelSize(w, h - (int)Math.Round(h * (1 - progress)))) },
            _ => AnimationState.Default,
        };
    }

    private static AnimationState FrameState(SceneObject obj, TimeSpan local, AnimationDefinition def)
    {
        // Frames discretos derivados de la duración del ciclo: step de ~125ms.
        var cycle = CycleLengths[def.SpeedPreset];
        int frameCount = Math.Max(1, (int)(cycle.Ticks / TimeSpan.FromMilliseconds(125).Ticks));
        int frame = (int)(local.Ticks / TimeSpan.FromMilliseconds(125).Ticks) % frameCount;
        // Cada frame alterna visibilidad por franjas (mismo efecto que blink multi-paso,
        // determinista y sin estado).
        bool visible = (frame % 2) == 0;
        return new AnimationState
        {
            Visible = visible,
            Offset = new PixelPoint(0, 0),
            BrightnessFactor = 1.0,
        };
    }
}