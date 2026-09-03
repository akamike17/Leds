using System.Text;
using DSLetreros.Domain.Deployment;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Deployment;

/// <summary>
/// Tests unitarios del ChannelDisplayTarget (P1): validación centralizada de respuestas
/// (ExpectAck/ExpectOpcode). Verifica que un opcode distinto de Error/Ack NUNCA se trata
/// como éxito, que un payload truncado falla y que una versión incompatible falla.
///
/// Usa un fake de canal programable que devuelve la respuesta que queramos, sin
/// hardware real (simulado). El hardware-in-the-loop vive en su propia suite.
/// </summary>
public class ChannelDisplayTargetProtocolTests
{
    private static Scene SampleScene() => new()
    {
        Name = "S", Duration = TimeSpan.FromSeconds(2),
        Layers = { new Layer { Name = "L", Order = 0, Objects = {
            new TextObject { Name = "T", Text = "OK", Color = RgbColor.White, Position = new PixelPoint(0, 0),
                Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(2)) } } } },
    };
    private static readonly CanvasDefinition Canvas = new(16, 8);

    // ---- Opcode fuera de orden ----

    [Fact]
    public async Task Connect_treats_unexpected_opcode_as_failure()
    {
        // El dispositivo responde con un opcode que NO es Ack ni Error (p.ej. OpIdentity).
        var ch = new ProgrammableChannel(DeviceProtocol.Identity(new DeviceIdentity
        {
            Serial = "X", Model = "M", FirmwareVersion = "1", ProtocolVersion = 1,
        }));
        var target = new ChannelDisplayTarget(ch);
        var conn = await target.ConnectAsync();
        Assert.False(conn.Success);
        Assert.Contains("opcode", conn.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetIdentity_rejects_ack_instead_of_identity()
    {
        // Se pidió Identity; el dispositivo devuelve un ACK vacío (payload truncado de facto).
        var ch = new ProgrammableChannel(DeviceProtocol.Ack());
        var target = new ChannelDisplayTarget(ch);
        var id = await target.GetIdentityAsync();
        Assert.False(id.Success);
    }

    [Fact]
    public async Task GetStatus_rejects_ack_instead_of_status()
    {
        var ch = new ProgrammableChannel(DeviceProtocol.Ack());
        var target = new ChannelDisplayTarget(ch);
        var st = await target.GetStatusAsync();
        Assert.False(st.Success);
    }

    // ---- Payload truncado ----

    [Fact]
    public async Task Identity_with_empty_payload_fails()
    {
        // Opcode correcto (Identity) pero payload vacío → truncado.
        var ch = new ProgrammableChannel(DeviceProtocol.Wrap(DeviceProtocol.OpIdentity, 0, Array.Empty<byte>()));
        var target = new ChannelDisplayTarget(ch);
        var id = await target.GetIdentityAsync();
        Assert.False(id.Success);
        Assert.Contains("truncado", id.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Capabilities_with_empty_payload_fails()
    {
        var ch = new ProgrammableChannel(DeviceProtocol.Wrap(DeviceProtocol.OpCapabilities, 0, Array.Empty<byte>()));
        var target = new ChannelDisplayTarget(ch);
        var caps = await target.GetCapabilitiesAsync();
        Assert.False(caps.Success);
    }

    // ---- Versión incompatible ----

    [Fact]
    public async Task Future_protocol_version_is_rejected()
    {
        // Construye una respuesta con versión futura (99) y magic válido.
        var frame = DeviceProtocol.Ack();
        frame[4] = 99; // version futura
        var ch = new ProgrammableChannel(frame);
        var target = new ChannelDisplayTarget(ch);
        var conn = await target.ConnectAsync();
        Assert.False(conn.Success);
    }

    [Fact]
    public async Task Bad_magic_is_rejected()
    {
        var frame = DeviceProtocol.Ack();
        frame[0] = (byte)'X';
        var ch = new ProgrammableChannel(frame);
        var target = new ChannelDisplayTarget(ch);
        var conn = await target.ConnectAsync();
        Assert.False(conn.Success);
    }

    // ---- Error del dispositivo se propaga como fallo (no como excepción) ----

    [Fact]
    public async Task Device_error_opcode_returns_failure_with_message()
    {
        var ch = new ProgrammableChannel(DeviceProtocol.Error("Dispositivo ocupado."));
        var target = new ChannelDisplayTarget(ch);
        var conn = await target.ConnectAsync();
        Assert.False(conn.Success);
        Assert.Contains("Dispositivo ocupado", conn.Error);
    }

    // ---- Upload fragmentado recibe ACK por cada parte ----

    [Fact]
    public async Task Upload_fragmentation_expects_ack_per_chunk()
    {
        // Fake que responde ACK a todas las requests y cuenta cuántas recibe.
        var ackAll = new AckAllChannel();
        var target = new ChannelDisplayTarget(ackAll);
        await target.ConnectAsync();

        var pkg = SceneCompiler.Compile(SampleScene(), Canvas)!.Package!;
        var ticket = (await target.PrepareTransferAsync(pkg.EstimatedBytes)).Value!;
        var up = await target.UploadAsync(ticket, pkg);
        Assert.True(up.Success);
        Assert.True(ackAll.RequestCount >= 2, $"Se esperaban varias partes, hubo {ackAll.RequestCount}");
    }
}

/// <summary>Canal fake que responde SIEMPRE con la misma trama (para una sola request).</summary>
public sealed class ProgrammableChannel : IDeviceChannel
{
    private readonly byte[] _response;
    public ProgrammableChannel(byte[] response) { _response = response; }
    public string Transport => "fake";
    public string Endpoint => "fake://programmable";
    public Task<byte[]> RequestAsync(byte[] frame, CancellationToken ct = default)
        => Task.FromResult(_response);
}

/// <summary>Canal fake que responde ACK a todas las requests y cuenta cuántas recibe.</summary>
public sealed class AckAllChannel : IDeviceChannel
{
    private int _count;
    public int RequestCount => _count;
    public string Transport => "fake";
    public string Endpoint => "fake://ack-all";

    public Task<byte[]> RequestAsync(byte[] frame, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _count);
        var (op, _, payload) = DeviceProtocol.Unwrap(frame);
        switch (op)
        {
            case DeviceProtocol.OpStatus:
                return Task.FromResult(DeviceProtocol.Wrap(
                    DeviceProtocol.OpStatus, 0,
                    Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(DeviceStatus.Online))));
            case DeviceProtocol.OpIdentity:
                return Task.FromResult(DeviceProtocol.Identity(new DeviceIdentity
                {
                    Serial = "ACK-SER", Model = "M", FirmwareVersion = "1", ProtocolVersion = 1,
                }));
            case DeviceProtocol.OpCapabilities:
                return Task.FromResult(DeviceProtocol.Capabilities(new DeviceCapabilities
                {
                    LogicalWidth = 64, LogicalHeight = 32, ColorCapability = ColorCapability.Rgb24,
                    MaxSceneBytes = 8 * 1024 * 1024, MaxAssetBytes = 4 * 1024 * 1024,
                    SupportedAnimations = Enum.GetValues<AnimationKind>().ToList(),
                    ProtocolVersion = 1, AutonomousPlayback = true,
                }));
            default:
                return Task.FromResult(DeviceProtocol.Ack());
        }
    }
}