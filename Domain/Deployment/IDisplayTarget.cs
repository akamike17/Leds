using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;

namespace DSLetreros.Domain.Deployment;

/// <summary>
/// Contrato de un target de visualización (invariante 2 + sección 7):
/// Simulator y hardware implementan el MISMO contrato. Todas las fases son
/// idempotentes y devuelven una operación de sólo lectura del estado.
/// </summary>
public interface IDisplayTarget
{
    DeviceId Id { get; }

    /// <summary>Conecta (para USB/LAN) o se declara listo (simulador). Barato, idempotente.</summary>
    Task<TargetResult> ConnectAsync(CancellationToken ct = default);

    /// <summary>Identidad estable del dispositivo (no basada en IP — sección 21).</summary>
    Task<TargetResult<DeviceIdentity>> GetIdentityAsync(CancellationToken ct = default);

    /// <summary>Capacidades reales del target (límites de compilación/transferencia).</summary>
    Task<TargetResult<DeviceCapabilities>> GetCapabilitiesAsync(CancellationToken ct = default);

    /// <summary>Prepara una transferencia (staging temporal). Devuelve un ticket de transferencia.</summary>
    Task<TargetResult<string>> PrepareTransferAsync(long sceneBytes, CancellationToken ct = default);

    /// <summary>Sube un ScenePackage compilado. No activa.</summary>
    Task<TargetResult> UploadAsync(string transferTicket, ScenePackage package, CancellationToken ct = default);

    /// <summary>Verifica checksum del paquete subido.</summary>
    Task<TargetResult> VerifyAsync(string transferTicket, Checksum expected, CancellationToken ct = default);

    /// <summary>Activa el paquete (atómico; falla deja LastKnownGoodScene intacto).</summary>
    Task<TargetResult> ActivateAsync(string transferTicket, CancellationToken ct = default);

    /// <summary>Detiene la reproducción autónoma.</summary>
    Task<TargetResult> StopAsync(CancellationToken ct = default);

    /// <summary>Estado actual del target.</summary>
    Task<TargetResult<DeviceStatus>> GetStatusAsync(CancellationToken ct = default);
}

/// <summary>Identidad estable del dispositivo.</summary>
public sealed class DeviceIdentity
{
    public string Serial { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string FirmwareVersion { get; set; } = string.Empty;
    public int ProtocolVersion { get; set; } = 1;
}

/// <summary>Resultado de una operación de target.</summary>
public class TargetResult
{
    public bool Success { get; init; }
    public string Error { get; init; } = string.Empty;
    public static TargetResult Ok() => new() { Success = true };
    public static TargetResult Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>Resultado con valor de una operación de target.</summary>
public sealed class TargetResult<T> : TargetResult
{
    public T? Value { get; init; }
    public static TargetResult<T> Ok(T value) => new() { Success = true, Value = value };
    public static new TargetResult<T> Fail(string error) => new() { Success = false, Error = error };
}