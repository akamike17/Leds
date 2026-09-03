using DSLetreros.Domain.ValueObjects;

namespace DSLetreros.Domain.Entities;

/// <summary>Dispositivo de visualización (simulador o hardware; mismo contrato IDisplayTarget).</summary>
public sealed class Device
{
    public DeviceId Id { get; set; } = DeviceId.New();
    public string FriendlyName { get; set; } = string.Empty;
    public string LastKnownEndpoint { get; set; } = string.Empty;
    public DeviceStatus Status { get; set; } = DeviceStatus.Unknown;
    public DeviceCapabilities Capabilities { get; set; } = new();
}

public enum DeviceStatus { Unknown, Online, Offline, Busy, Error }

/// <summary>Capacidades del target = límites reales (invariante 2).</summary>
public sealed class DeviceCapabilities
{
    public int LogicalWidth { get; set; }
    public int LogicalHeight { get; set; }
    public ColorCapability ColorCapability { get; set; } = ColorCapability.Rgb24;
    public long MaxSceneBytes { get; set; }
    public long MaxAssetBytes { get; set; }
    public List<AnimationKind> SupportedAnimations { get; set; } = new();
    public int ProtocolVersion { get; set; } = 1;
    public bool AutonomousPlayback { get; set; }
}

public enum ColorCapability { Monochrome, Rgb24 }

// --- Configuración eléctrica (fuera de V1 usuario, sólo aquí) ---

/// <summary>Perfil eléctrico del dispositivo.</summary>
public sealed class DeviceProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public MatrixTopology Topology { get; set; } = MatrixTopology.ZigZag;
    public ControllerProfile Controller { get; set; } = new();
    public OutputDriverProfile OutputDriver { get; set; } = new();
}

public enum MatrixTopology { Progressive, ZigZag }

public sealed class ControllerProfile
{
    public string Model { get; set; } = string.Empty;
    public int MaxBrightness { get; set; } = 255;
}

public sealed class OutputDriverProfile
{
    public string Chip { get; set; } = string.Empty;
    public string RgbOrder { get; set; } = "RGB";
    public int ScanRate { get; set; }
}