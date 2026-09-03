using DSLetreros.Domain.Entities;
using DSLetreros.Domain.Rendering;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Rendering;

/// <summary>
/// Cobertura de ramas límite de AnimationEvaluator (spec 20.6: matar mutantes críticos).
/// Cubre ResolveActive por fases (Entrance/Exit/Main) y los bordes de dirección/duración.
/// </summary>
public class AnimationPhaseTests
{
    private static SceneObject Obj(TimeSpan start, TimeSpan dur) => new TextObject
    {
        Name = "T", Text = "A", Color = RgbColor.White,
        Position = new PixelPoint(0, 0),
        Timing = new TimeRange(start, start + dur),
    };

    // ---- ResolveActive por fases ----

    [Fact]
    public void ResolveActive_returns_entrance_in_first_20_percent()
    {
        var list = new List<AnimationDefinition>
        {
            new() { Kind = AnimationKind.Blink, Slot = AnimationSlot.Entrance },
            new() { Kind = AnimationKind.Fixed, Slot = AnimationSlot.Main },
            new() { Kind = AnimationKind.Pulse, Slot = AnimationSlot.Exit },
        };
        var timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10));

        var r = AnimationEvaluator.ResolveActive(list, TimeSpan.FromSeconds(1), timing); // 10% → entrance
        Assert.Equal(AnimationSlot.Entrance, r!.Slot);
    }

    [Fact]
    public void ResolveActive_returns_exit_in_last_20_percent()
    {
        var list = new List<AnimationDefinition>
        {
            new() { Kind = AnimationKind.Blink, Slot = AnimationSlot.Entrance },
            new() { Kind = AnimationKind.Fixed, Slot = AnimationSlot.Main },
            new() { Kind = AnimationKind.Pulse, Slot = AnimationSlot.Exit },
        };
        var timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10));

        var r = AnimationEvaluator.ResolveActive(list, TimeSpan.FromSeconds(9), timing); // 90% → exit
        Assert.Equal(AnimationSlot.Exit, r!.Slot);
    }

    [Fact]
    public void ResolveActive_returns_main_in_middle()
    {
        var list = new List<AnimationDefinition>
        {
            new() { Kind = AnimationKind.Fixed, Slot = AnimationSlot.Main },
        };
        var timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10));
        var r = AnimationEvaluator.ResolveActive(list, TimeSpan.FromSeconds(5), timing);
        Assert.Equal(AnimationSlot.Main, r!.Slot);
    }

    [Fact]
    public void ResolveActive_ignores_time_before_start()
    {
        // t < timing.Start → local negativo → cae en entrance (local < entranceEnd) o main.
        var list = new List<AnimationDefinition>
        {
            new() { Kind = AnimationKind.Fixed, Slot = AnimationSlot.Main },
        };
        var timing = new TimeRange(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
        var r = AnimationEvaluator.ResolveActive(list, TimeSpan.FromSeconds(0), timing);
        Assert.NotNull(r);
    }

    [Fact]
    public void ResolveActive_null_when_duration_not_positive()
    {
        var list = new List<AnimationDefinition> { new() { Slot = AnimationSlot.Main } };
        var timing = new TimeRange(TimeSpan.Zero, TimeSpan.Zero); // duración 0
        var r = AnimationEvaluator.ResolveActive(list, TimeSpan.Zero, timing);
        Assert.Null(r);
    }

    [Fact]
    public void ResolveActive_null_when_no_animations()
    {
        var timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        Assert.Null(AnimationEvaluator.ResolveActive(new List<AnimationDefinition>(), TimeSpan.FromSeconds(1), timing));
    }

    [Fact]
    public void ResolveActive_at_exact_entrance_end_is_main_not_entrance()
    {
        // local == entranceEnd (dur/5) → `local < entranceEnd` es falso → NO entrada → main.
        var list = new List<AnimationDefinition>
        {
            new() { Kind = AnimationKind.Blink, Slot = AnimationSlot.Entrance },
            new() { Kind = AnimationKind.Fixed, Slot = AnimationSlot.Main },
        };
        var timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10)); // entranceEnd = 2s
        var r = AnimationEvaluator.ResolveActive(list, TimeSpan.FromSeconds(2), timing);
        Assert.Equal(AnimationSlot.Main, r!.Slot);
    }

    [Fact]
    public void ResolveActive_at_exact_exit_start_is_exit()
    {
        // local == exitStart (dur*4/5) → `local >= exitStart` es verdadero → exit.
        var list = new List<AnimationDefinition>
        {
            new() { Kind = AnimationKind.Fixed, Slot = AnimationSlot.Main },
            new() { Kind = AnimationKind.Pulse, Slot = AnimationSlot.Exit },
        };
        var timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10)); // exitStart = 8s
        var r = AnimationEvaluator.ResolveActive(list, TimeSpan.FromSeconds(8), timing);
        Assert.Equal(AnimationSlot.Exit, r!.Slot);
    }

    [Fact]
    public void Slide_without_direction_defaults_left()
    {
        // def.Direction == null → `?? AnimationDirection.Left`. Slide sin Direction.
        var obj = new ShapeObject
        {
            Shape = ShapeKind.Rectangle, FillColor = RgbColor.White,
            Position = new PixelPoint(0, 0), Size = new PixelSize(4, 1),
            Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)),
        };
        obj.Animations.Add(new AnimationDefinition { Kind = AnimationKind.Slide, Direction = null, SpeedPreset = AnimationSpeedPreset.Normal, Slot = AnimationSlot.Main });
        var s = AnimationEvaluator.Evaluate(obj, TimeSpan.FromMilliseconds(500));
        Assert.True(s.Visible);
        // Left: offset +X (hacia la derecha). progress 0.5 de width 4 → offset.X = 2
        Assert.Equal(2, s.Offset.X);
    }

    [Fact]
    public void Marquee_without_direction_defaults_left()
    {
        var obj = new TextObject
        {
            Name = "T", Text = "AB", Color = RgbColor.White,
            Position = new PixelPoint(0, 0), Size = new PixelSize(8, 1),
            Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)),
        };
        obj.Animations.Add(new AnimationDefinition { Kind = AnimationKind.Marquee, Direction = null, SpeedPreset = AnimationSpeedPreset.Slow, Slot = AnimationSlot.Main });
        var s = AnimationEvaluator.Evaluate(obj, TimeSpan.Zero);
        Assert.True(s.Visible);
        // Left default: offset -off con off = 0 en t=0 → offset.X == 0
        Assert.Equal(0, s.Offset.X);
    }

    [Fact]
    public void Wipe_without_direction_defaults_left()
    {
        var obj = new ShapeObject
        {
            Shape = ShapeKind.Rectangle, FillColor = RgbColor.White,
            Position = new PixelPoint(0, 0), Size = new PixelSize(4, 2),
            Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)),
        };
        obj.Animations.Add(new AnimationDefinition { Kind = AnimationKind.Wipe, Direction = null, SpeedPreset = AnimationSpeedPreset.Normal, Slot = AnimationSlot.Main });
        var s = AnimationEvaluator.Evaluate(obj, TimeSpan.FromMilliseconds(500));
        Assert.True(s.Visible);
        Assert.NotNull(s.Clip); // Left → clip revelado
    }

    // ---- Direcciones de Slide ----

    [Theory]
    [InlineData(AnimationDirection.Left)]
    [InlineData(AnimationDirection.Right)]
    [InlineData(AnimationDirection.Up)]
    [InlineData(AnimationDirection.Down)]
    public void Slide_covers_all_directions(AnimationDirection dir)
    {
        var obj = new ShapeObject
        {
            Name = "S", Shape = ShapeKind.Rectangle, FillColor = RgbColor.White,
            Position = new PixelPoint(0, 0), Size = new PixelSize(4, 2),
            Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)),
        };
        obj.Animations.Add(new AnimationDefinition { Kind = AnimationKind.Slide, Direction = dir, SpeedPreset = AnimationSpeedPreset.Normal, Slot = AnimationSlot.Main });

        var s = AnimationEvaluator.Evaluate(obj, TimeSpan.FromMilliseconds(250));
        Assert.True(s.Visible);
    }

    [Fact]
    public void Slide_entrance_and_exit_differ_in_direction_sign()
    {
        static ShapeObject Make(AnimationSlot slot) {
            var o = new ShapeObject { Shape = ShapeKind.Rectangle, FillColor = RgbColor.White, Position = new PixelPoint(0,0), Size = new PixelSize(4,1), Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)) };
            o.Animations.Add(new AnimationDefinition { Kind = AnimationKind.Slide, Direction = AnimationDirection.Left, SpeedPreset = AnimationSpeedPreset.Normal, Slot = slot });
            return o;
        }
        var entrance = AnimationEvaluator.Evaluate(Make(AnimationSlot.Entrance), TimeSpan.FromMilliseconds(250));
        var exit = AnimationEvaluator.Evaluate(Make(AnimationSlot.Exit), TimeSpan.FromMilliseconds(250));
        // exit invierte el progreso → a t=250ms, entrance prog=0.25 (offset 1) vs exit prog=0.75 (offset 3)
        Assert.NotEqual(entrance.Offset, exit.Offset);
    }

    // ---- Direcciones de Wipe (clip revelado) ----

    [Theory]
    [InlineData(AnimationDirection.Left)]
    [InlineData(AnimationDirection.Right)]
    [InlineData(AnimationDirection.Up)]
    [InlineData(AnimationDirection.Down)]
    public void Wipe_covers_all_directions(AnimationDirection dir)
    {
        var obj = new ShapeObject { Shape = ShapeKind.Rectangle, FillColor = RgbColor.White, Position = new PixelPoint(0,0), Size = new PixelSize(4,2), Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)) };
        obj.Animations.Add(new AnimationDefinition { Kind = AnimationKind.Wipe, Direction = dir, SpeedPreset = AnimationSpeedPreset.Normal, Slot = AnimationSlot.Main });

        var s = AnimationEvaluator.Evaluate(obj, TimeSpan.FromMilliseconds(500));
        Assert.True(s.Visible);
        Assert.NotNull(s.Clip);
    }

    // ---- Marquee dirección ----

    [Fact]
    public void Marquee_right_direction_wraps_across_viewport()
    {
        var obj = new TextObject { Name = "T", Text = "AB", Color = RgbColor.White, Position = new PixelPoint(0,0), Size = new PixelSize(8,1), Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)) };
        obj.Animations.Add(new AnimationDefinition { Kind = AnimationKind.Marquee, Direction = AnimationDirection.Right, SpeedPreset = AnimationSpeedPreset.Slow, Slot = AnimationSlot.Main });

        var s = AnimationEvaluator.Evaluate(obj, TimeSpan.FromMilliseconds(0));
        Assert.True(s.Visible);
        Assert.True(s.Offset.X <= 0);
    }
}