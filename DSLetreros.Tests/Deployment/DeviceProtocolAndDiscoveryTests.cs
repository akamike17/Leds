using System.Text;
using System.Text.Json;
using DSLetreros.Application.Services;
using DSLetreros.Domain.Deployment;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Deployment;

/// <summary>
/// Slice 9: discovery, identidad estable (serial, no IP), transports LAN/USB/Serial
/// detrás del mismo protocolo, y upload transaccional.
/// </summary>
public class DeviceProtocolAndDiscoveryTests
{
    private static Scene SampleScene()
    {
        var scene = new Scene { Name = "S9", Duration = TimeSpan.FromSeconds(2) };
        var layer = new Layer { Name = "L", Order = 0 };
        layer.Objects.Add(new TextObject
        {
            Name = "T", Text = "HOLA", Color = RgbColor.White,
            Position = new PixelPoint(0, 0),
            Timing = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(2)),
        });
        scene.Layers.Add(layer);
        return scene;
    }

    private static readonly CanvasDefinition Canvas = new(16, 8);

    // ---- Framing del protocolo ----

    [Fact]
    public void Protocol_wrap_unwrap_roundtrips()
    {
        var payload = Encoding.UTF8.GetBytes("hola dispositivo");
        var frame = DeviceProtocol.Wrap(DeviceProtocol.OpHello, 0, payload);
        var (op, flags, body) = DeviceProtocol.Unwrap(frame);
        Assert.Equal(DeviceProtocol.OpHello, op);
        Assert.Equal(0, flags);
        Assert.Equal(payload, body);
    }

    [Fact]
    public void Protocol_rejects_bad_magic()
    {
        var frame = DeviceProtocol.Wrap(DeviceProtocol.OpHello, 0, new byte[] { 1 });
        frame[0] = (byte)'X';
        Assert.Throws<ProtocolException>(() => DeviceProtocol.Unwrap(frame));
    }

    [Fact]
    public void Protocol_rejects_truncated_frame()
    {
        Assert.Throws<ProtocolException>(() => DeviceProtocol.Unwrap(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void Protocol_rejects_future_version()
    {
        var frame = DeviceProtocol.Wrap(DeviceProtocol.OpHello, 0, Array.Empty<byte>());
        frame[4] = 99; // versión futura
        Assert.Throws<ProtocolException>(() => DeviceProtocol.Unwrap(frame));
    }

    // ---- Upload transaccional vía dispositivo fake (LAN y Serial, mismo contrato) ----

    public static readonly TheoryData<Func<FakeDeviceChannel, IDisplayTarget>> TargetFactories = new()
    {
        ch => new LanDisplayTarget(ch),
        ch => new SerialDisplayTarget(ch),
    };

    [Theory]
    [MemberData(nameof(TargetFactories))]
    public async Task Full_pipeline_over_channel_succeeds_and_activates(Func<FakeDeviceChannel, IDisplayTarget> make)
    {
        var channel = new FakeDeviceChannel("lan", "tcp://10.0.0.9:9000");
        var target = make(channel);

        var service = new DeploymentService();
        var result = await service.SendAsync(SampleScene(), Canvas, target);

        Assert.True(result.Success, result.Error);
        Assert.Equal("Activate", result.Phase);
        Assert.True(channel.ActivatedCount == 1, $"Se activó {channel.ActivatedCount} veces, esperado 1");

        // Identidad estable por serial: no debe depender del endpoint.
        var id = await target.GetIdentityAsync();
        Assert.True(id.Success);
        Assert.Equal(channel.Serial, id.Value!.Serial);
        Assert.NotEqual(channel.Endpoint, id.Value.Serial); // el serial NO es la IP
    }

    [Theory]
    [MemberData(nameof(TargetFactories))]
    public async Task Upload_is_transactional_failure_keeps_last_good(Func<FakeDeviceChannel, IDisplayTarget> make)
    {
        var channel = new FakeDeviceChannel("serial", "COM3");
        var target = make(channel);
        var service = new DeploymentService();

        Assert.True((await service.SendAsync(SampleScene(), Canvas, target)).Success);
        Assert.Equal(1, channel.ActivatedCount);

        // Segunda transferencia ACTIVA una nueva escena y conserva la anterior como LastKnownGood.
        Assert.True((await service.SendAsync(SampleScene(), Canvas, target)).Success);
        Assert.Equal(2, channel.ActivatedCount);
        Assert.NotNull(channel.LastActivated);
        Assert.NotNull(channel.PrevousOfLastActivate);
    }

    [Theory]
    [MemberData(nameof(TargetFactories))]
    public async Task Verify_rejects_bad_checksum(Func<FakeDeviceChannel, IDisplayTarget> make)
    {
        var channel = new FakeDeviceChannel("lan", "tcp://10.0.0.5:9000");
        var target = make(channel);
        var (pkg, _) = SceneCompiler.Compile(SampleScene(), Canvas)!;
        await target.ConnectAsync();
        var ticket = (await target.PrepareTransferAsync(pkg.EstimatedBytes)).Value!;
        await target.UploadAsync(ticket, pkg);

        var ver = await target.VerifyAsync(ticket, new Checksum("ffffffff"));
        Assert.False(ver.Success);
    }

    // ---- Discovery ----

    [Fact]
    public async Task Discovery_lists_simulator_and_discovered_devices_by_serial()
    {
        var simulator = new SimulatorTarget(width: 16, height: 8);
        var discovery = new DeviceDiscoveryService(simulator);

        // Dos dispositivos fake en el canal LAN con seriales estables.
        var devA = new FakeDeviceChannel("lan", "tcp://10.0.0.10:9000");
        var devB = new FakeDeviceChannel("lan", "tcp://10.0.0.11:9000");

        await discovery.DiscoverAsync(new IDeviceChannel[] { devA, devB });

        var list = await discovery.ListAsync();
        // simulador + 2 dispositivos
        Assert.Equal(3, list.Count);
        Assert.Contains(list, d => d.Transport == "simulator");
        Assert.Contains(list, d => d.Serial == devA.Serial);
        Assert.Contains(list, d => d.Serial == devB.Serial);
        // el serial no es el endpoint
        Assert.All(list.Where(d => d.Transport != "simulator"), d => Assert.NotEqual(d.Endpoint, d.Serial));
    }

    [Fact]
    public async Task Discovery_resolves_target_by_serial_or_deviceid()
    {
        var simulator = new SimulatorTarget(width: 16, height: 8);
        var discovery = new DeviceDiscoveryService(simulator);
        var dev = new FakeDeviceChannel("serial", "COM7");
        await discovery.DiscoverAsync(new IDeviceChannel[] { dev });

        var bySerial = discovery.Resolve(dev.Serial);
        Assert.NotNull(bySerial);

        // Por DeviceId hex del target descubierto.
        var discoveredTarget = bySerial!;
        var byId = discovery.Resolve(discoveredTarget.Id.Value.ToString("N"));
        Assert.NotNull(byId);
    }
}

/// <summary>
/// Dispositivo virtual en memoria que implementa el LADO del dispositivo del protocolo.
/// Responde a las operaciones del cable contra un IDeviceChannel, incluyendo staging
/// transaccional y activación con LastKnownGood. Sirve como fake de test Y como
/// esqueleto del firmware (slice 10).
/// </summary>
public sealed class FakeDeviceChannel : IDeviceChannel
{
    private readonly string _serial = Guid.NewGuid().ToString("N")[..12];
    private readonly DeviceCapabilities _caps = new()
    {
        LogicalWidth = 64, LogicalHeight = 32, ColorCapability = ColorCapability.Rgb24,
        MaxSceneBytes = 8 * 1024 * 1024, MaxAssetBytes = 4 * 1024 * 1024,
        SupportedAnimations = Enum.GetValues<AnimationKind>().ToList(),
        ProtocolVersion = 1, AutonomousPlayback = true,
    };

    private readonly Dictionary<string, (ScenePackage Pkg, Checksum Expected)> _staging = new();
    private ScenePackage? _active;
    private ScenePackage? _lastKnownGood;
    private int _activatedCount;

    public FakeDeviceChannel(string transport, string endpoint)
    {
        Transport = transport;
        Endpoint = endpoint;
    }

    public string Transport { get; }
    public string Endpoint { get; }
    public string Serial => _serial;
    public int ActivatedCount => _activatedCount;
    public ScenePackage? LastActivated => _active;
    public ScenePackage? PrevousOfLastActivate => _lastKnownGood;

    public Task<byte[]> RequestAsync(byte[] frame, CancellationToken ct = default)
    {
        var (op, flags, payload) = DeviceProtocol.Unwrap(frame);
        switch (op)
        {
            case DeviceProtocol.OpHello or DeviceProtocol.OpStop or DeviceProtocol.OpStatus:
                return Task.FromResult(DeviceProtocol.Ack());

            case DeviceProtocol.OpIdentity:
                var id = new DeviceIdentity
                {
                    Serial = _serial, Model = $"Fake {Transport}", FirmwareVersion = "1.0.0", ProtocolVersion = 1,
                };
                return Task.FromResult(DeviceProtocol.Identity(id));

            case DeviceProtocol.OpCapabilities:
                return Task.FromResult(DeviceProtocol.Capabilities(_caps));

            case DeviceProtocol.OpPrepare:
                var prep = JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(payload));
                var ticket = prep.GetProperty("ticket").GetString()!;
                _staging[ticket] = (null!, Checksum.Empty);
                return Task.FromResult(DeviceProtocol.Ack());

            case DeviceProtocol.OpUpload:
                var (t2, body) = SplitTicket(payload);
                var pkg = JsonSerializer.Deserialize<ScenePackage>(Encoding.UTF8.GetString(body), ScenePackageJson.Options)!;
                var existing = _staging.ContainsKey(t2) ? _staging[t2] : (null!, Checksum.Empty);
                _staging[t2] = (pkg, existing.Expected);
                return Task.FromResult(DeviceProtocol.Ack());

            case DeviceProtocol.OpVerify:
                var v = JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(payload));
                var t3 = v.GetProperty("ticket").GetString()!;
                var expected = new Checksum(v.GetProperty("checksum").GetString() ?? "");
                if (!_staging.TryGetValue(t3, out var entry) || entry.Pkg == null)
                    return Task.FromResult(DeviceProtocol.Error("Sin paquete en staging."));
                var actual = entry.Pkg.ComputeChecksum();
                if (!actual.Equals(expected))
                    return Task.FromResult(DeviceProtocol.Error("Checksum no coincide."));
                _staging[t3] = (entry.Pkg, expected);
                return Task.FromResult(DeviceProtocol.Ack());

            case DeviceProtocol.OpActivate:
                var t4 = Encoding.UTF8.GetString(payload);
                if (!_staging.TryGetValue(t4, out var e2) || e2.Pkg == null)
                    return Task.FromResult(DeviceProtocol.Error("Sin paquete verificado para activar."));
                if (_active != null) _lastKnownGood = _active;
                _active = e2.Pkg;
                _activatedCount++;
                _staging.Remove(t4);
                return Task.FromResult(DeviceProtocol.Ack());

            default:
                return Task.FromResult(DeviceProtocol.Error($"Op desconocida: {op:X2}"));
        }
    }

    private static (string Ticket, byte[] Body) SplitTicket(byte[] payload)
    {
        int nl = Array.IndexOf(payload, (byte)'\n');
        if (nl < 0) throw new ProtocolException("Upload sin separador ticket/payload.");
        var ticket = Encoding.UTF8.GetString(payload, 0, nl);
        var body = payload[(nl + 1)..];
        return (ticket, body);
    }
}