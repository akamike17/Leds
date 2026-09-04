using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;
using System.Text;

namespace DSLetreros.Domain.Deployment;

/// <summary>
/// Protocolo de cable compartido (spec sección 18): USB/Serial y Wi-Fi/LAN son
/// TRANSPORTS (canales de bytes) detrás del MISMO protocolo. El protocolo define
/// las operaciones de framing, identidad, transferencia transaccional (staging
/// temporal + activación atómica) y verificación por checksum.
/// </summary>
public static class DeviceProtocol
{
    public const string Magic = "DSL1";
    public const int CurrentProtocolVersion = 1;
    public const int MinProtocolVersion = 1;

    // ---- Encuadre (framing) ----
    // Cada mensaje va precedido por un header binario fijo:
    //   magic (4 bytes) | version (1) | op (1) | flags (1) | length (4, little-endian) | payload (length bytes)

    public const byte OpHello = 0x01;
    public const byte OpIdentity = 0x02;
    public const byte OpCapabilities = 0x03;
    public const byte OpPrepare = 0x04;
    public const byte OpUpload = 0x05;
    public const byte OpVerify = 0x06;
    public const byte OpActivate = 0x07;
    public const byte OpStop = 0x08;
    public const byte OpStatus = 0x09;
    public const byte OpAck = 0x7E;
    public const byte OpError = 0x7F;

    public const byte FlagFinal = 0x01;   // última parte de un payload fragmentado

    /// <summary>Enmarca un payload binario con el header del protocolo.</summary>
    public static byte[] Wrap(byte op, byte flags, ReadOnlySpan<byte> payload)
    {
        var buf = new byte[11 + payload.Length];
        Encoding.ASCII.GetBytes(Magic).CopyTo(buf, 0);
        buf[4] = CurrentProtocolVersion;
        buf[5] = op;
        buf[6] = flags;
        BitConverter.GetBytes(payload.Length).CopyTo(buf, 7);
        payload.CopyTo(buf.AsSpan(11));
        return buf;
    }

    /// <summary>Desenvuelve un mensaje; valida magic, versión y longitud.</summary>
    public static (byte Op, byte Flags, byte[] Payload) Unwrap(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 11)
            throw new ProtocolException("Trama demasiado corta.");
        var magic = Encoding.ASCII.GetString(frame[..4]);
        if (magic != Magic)
            throw new ProtocolException("Magic inválido en trama.");
        var version = frame[4];
        if (version > CurrentProtocolVersion)
            throw new ProtocolException($"Versión de protocolo {version} no soportada.");
        if (version < MinProtocolVersion)
            throw new ProtocolException($"Versión de protocolo {version} no soportada (mínima {MinProtocolVersion}).");
        var op = frame[5];
        var flags = frame[6];
        var len = BitConverter.ToInt32(frame.Slice(7, 4));
        if (len < 0 || frame.Length != 11 + len)
            throw new ProtocolException("Longitud de payload inconsistente.");
        return (op, flags, frame[11..].ToArray());
    }

    /// <summary>Serializa el protocolo de handshake HELLO (identifica al host ante el dispositivo).</summary>
    public static byte[] Hello(DeviceId hostId) =>
        Wrap(OpHello, 0, Encoding.UTF8.GetBytes(hostId.Value.ToString("N")));

    /// <summary>Serializa IDENTITY como JSON (identidad estable, no basada en IP).</summary>
    public static byte[] Identity(DeviceIdentity id) =>
        Wrap(OpIdentity, 0, Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(id)));

    /// <summary>Serializa CAPABILITIES como JSON.</summary>
    public static byte[] Capabilities(DeviceCapabilities caps) =>
        Wrap(OpCapabilities, 0, Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(caps)));

    /// <summary>Serializa PREPARE (registra un ticket de transferencia para N bytes).</summary>
    public static byte[] Prepare(string ticket, long sceneBytes) =>
        Wrap(OpPrepare, 0, Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new { ticket, sceneBytes })));

    /// <summary>Serializa UPLOAD (payload compilado ya serializado a JSON, en partes).</summary>
    public static byte[] Upload(string ticket, byte[] payload, byte flags) =>
        Wrap(OpUpload, flags, UploadBody(ticket, payload));

    private static byte[] UploadBody(string ticket, byte[] payload)
    {
        var head = Encoding.UTF8.GetBytes(ticket + "\n");
        var body = new byte[head.Length + payload.Length];
        head.CopyTo(body, 0);
        payload.CopyTo(body, head.Length);
        return body;
    }

    /// <summary>Serializa VERIFY (esperado checksum).</summary>
    public static byte[] Verify(string ticket, Checksum expected) =>
        Wrap(OpVerify, 0, Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new { ticket, checksum = expected.Value })));

    /// <summary>Serializa ACTIVATE.</summary>
    public static byte[] Activate(string ticket) =>
        Wrap(OpActivate, 0, Encoding.UTF8.GetBytes(ticket));

    /// <summary>ACK genérico (éxito).</summary>
    public static byte[] Ack() => Wrap(OpAck, 0, Array.Empty<byte>());

    /// <summary>ERROR con mensaje.</summary>
    public static byte[] Error(string message) =>
        Wrap(OpError, 0, Encoding.UTF8.GetBytes(message));
}

/// <summary>Excepción de protocolo (trama inválida, versión incompatible, etc.).</summary>
public sealed class ProtocolException : Exception
{
    public ProtocolException(string message) : base(message) { }
}

/// <summary>
/// Canal de bytes de bajo nivel: el transporte físico (USB/Serial o Wi-Fi/LAN).
/// Aislado para que los adapters compartan el mismo IDeviceProtocol y sean
/// testeables con canales en memoria.
/// </summary>
public interface IDeviceChannel
{
    /// <summary>Transporte: "usb", "serial" o "lan".</summary>
    string Transport { get; }

    /// <summary>Endpoint descriptivo (URL/puerto), sólo para diagnóstico (NO identidad).</summary>
    string Endpoint { get; }

    /// <summary>Envía una trama binaria y devuelve la respuesta enmarcada.</summary>
    Task<byte[]> RequestAsync(byte[] frame, CancellationToken ct = default);
}