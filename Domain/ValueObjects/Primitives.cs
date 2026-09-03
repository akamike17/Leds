using System;

namespace DSLetreros.Domain.ValueObjects;

/// <summary>Punto en coordenadas lógicas LED (enteros, origen arriba-izquierda).</summary>
public readonly struct PixelPoint : IEquatable<PixelPoint>
{
    public int X { get; }
    public int Y { get; }

    public PixelPoint(int x, int y) { X = x; Y = y; }

    public static PixelPoint operator +(PixelPoint a, PixelPoint b) => new(a.X + b.X, a.Y + b.Y);
    public static PixelPoint operator -(PixelPoint a, PixelPoint b) => new(a.X - b.X, a.Y - b.Y);

    public bool Equals(PixelPoint other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is PixelPoint p && Equals(p);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override string ToString() => $"({X},{Y})";
}

/// <summary>Tamaño en píxeles lógicos. Invariante: ancho y alto ≥ 0.</summary>
public readonly struct PixelSize : IEquatable<PixelSize>
{
    public int Width { get; }
    public int Height { get; }

    public PixelSize(int width, int height)
    {
        if (width < 0 || height < 0)
            throw new ArgumentOutOfRangeException(nameof(PixelSize), "Las dimensiones no pueden ser negativas.");
        Width = width;
        Height = height;
    }

    public bool Equals(PixelSize other) => Width == other.Width && Height == other.Height;
    public override bool Equals(object? obj) => obj is PixelSize s && Equals(s);
    public override int GetHashCode() => HashCode.Combine(Width, Height);
    public override string ToString() => $"{Width}x{Height}";
}

/// <summary>Rectángulo en coordenadas lógicas. Invariante: Width/Height ≥ 0.</summary>
public readonly struct PixelRect : IEquatable<PixelRect>
{
    public PixelPoint Origin { get; }
    public PixelSize Size { get; }

    public PixelRect(PixelPoint origin, PixelSize size) { Origin = origin; Size = size; }

    public int Left => Origin.X;
    public int Top => Origin.Y;
    public int Right => Origin.X + Size.Width;
    public int Bottom => Origin.Y + Size.Height;

    public bool Contains(PixelPoint p) =>
        p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;

    public static PixelRect FromLTRB(int left, int top, int right, int bottom)
    {
        int x = Math.Min(left, right);
        int y = Math.Min(top, bottom);
        int w = Math.Abs(right - left);
        int h = Math.Abs(bottom - top);
        return new PixelRect(new PixelPoint(x, y), new PixelSize(w, h));
    }

    public bool Equals(PixelRect other) => Origin.Equals(other.Origin) && Size.Equals(other.Size);
    public override bool Equals(object? obj) => obj is PixelRect r && Equals(r);
    public override int GetHashCode() => HashCode.Combine(Origin, Size);
    public override string ToString() => $"({Left},{Top},{Right},{Bottom})";
}

/// <summary>Color RGB de 24 bits (0..255 por canal).</summary>
public readonly struct RgbColor : IEquatable<RgbColor>
{
    public byte R { get; }
    public byte G { get; }
    public byte B { get; }

    public RgbColor(byte r, byte g, byte b) { R = r; G = g; B = b; }

    public static readonly RgbColor Black = new(0, 0, 0);
    public static readonly RgbColor White = new(255, 255, 255);
    public static readonly RgbColor Red = new(255, 0, 0);

    public uint ToUint24() => ((uint)R << 16) | ((uint)G << 8) | B;

    public bool Equals(RgbColor other) => R == other.R && G == other.G && B == other.B;
    public override bool Equals(object? obj) => obj is RgbColor c && Equals(c);
    public static bool operator ==(RgbColor a, RgbColor b) => a.Equals(b);
    public static bool operator !=(RgbColor a, RgbColor b) => !a.Equals(b);
    public override int GetHashCode() => HashCode.Combine(R, G, B);
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>Rango temporal no negativo. Invariante: End ≥ Start.</summary>
public readonly struct TimeRange : IEquatable<TimeRange>
{
    public TimeSpan Start { get; }
    public TimeSpan End { get; }

    public TimeRange(TimeSpan start, TimeSpan end)
    {
        if (end < start)
            throw new ArgumentOutOfRangeException(nameof(TimeRange), "End no puede ser anterior a Start.");
        Start = start;
        End = end;
    }

    public TimeSpan Duration => End - Start;
    public bool Contains(TimeSpan t) => t >= Start && t < End;

    public bool Equals(TimeRange other) => Start == other.Start && End == other.End;
    public override bool Equals(object? obj) => obj is TimeRange r && Equals(r);
    public override int GetHashCode() => HashCode.Combine(Start, End);
    public override string ToString() => $"[{Start}..{End}]";
}

/// <summary>Definición del lienzo LED. Invariante: ancho/alto > 0.</summary>
public readonly struct CanvasDefinition : IEquatable<CanvasDefinition>
{
    public int Width { get; }
    public int Height { get; }

    public CanvasDefinition(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(CanvasDefinition), "El lienzo debe tener dimensiones positivas.");
        Width = width;
        Height = height;
    }

    public PixelSize Size => new(Width, Height);

    public bool Equals(CanvasDefinition other) => Width == other.Width && Height == other.Height;
    public override bool Equals(object? obj) => obj is CanvasDefinition c && Equals(c);
    public override int GetHashCode() => HashCode.Combine(Width, Height);
    public override string ToString() => $"{Width}x{Height}";
}

/// <summary>Checksum inmutable sobre bytes.</summary>
public readonly struct Checksum : IEquatable<Checksum>
{
    public string Value { get; }

    public Checksum(string value) { Value = value ?? string.Empty; }

    public static Checksum Empty => new(string.Empty);
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    public bool Equals(Checksum other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is Checksum c && Equals(c);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => Value;
}