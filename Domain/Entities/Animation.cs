using DSLetreros.Domain.ValueObjects;

namespace DSLetreros.Domain.Entities;

/// <summary>Definición de una animación (Entrance/Main/Exit). Valores finos = internos.</summary>
public sealed class AnimationDefinition
{
    public AnimationKind Kind { get; set; } = AnimationKind.Fixed;
    public AnimationSpeedPreset SpeedPreset { get; set; } = AnimationSpeedPreset.Normal;
    public AnimationDirection? Direction { get; set; }
    public bool Loop { get; set; }
    public AnimationSlot Slot { get; set; } = AnimationSlot.Main;
}

public enum AnimationKind { Fixed, Blink, Marquee, Slide, Pulse, Wipe, Frame }
public enum AnimationSpeedPreset { Slow, Normal, Fast }
public enum AnimationDirection { Left, Right, Up, Down }
public enum AnimationSlot { Entrance, Main, Exit }

/// <summary>Conjunto de animaciones de un objeto (Entrance?, Main?, Exit?).</summary>
public sealed class AnimationSet
{
    public AnimationDefinition? Entrance { get; set; }
    public AnimationDefinition? Main { get; set; }
    public AnimationDefinition? Exit { get; set; }
}