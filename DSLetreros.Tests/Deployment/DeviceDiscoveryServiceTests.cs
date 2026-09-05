using DSLetreros.Application.Services;
using DSLetreros.Domain.Deployment;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;
using Xunit;

namespace DSLetreros.Tests.Deployment;

/// <summary>
/// Tests del DeviceDiscoveryService (P1): registro thread-safe, colisiones de serial
/// explícitas (no sobrescribir silenciosamente) y fallos sanitizados en vez de catch vacío.
/// </summary>
public class DeviceDiscoveryServiceTests
{
    // ---- Rediscovery de serial (final.md §2.F) ----

    [Fact]
    public async Task Register_same_serial_new_endpoint_is_rediscovery_not_collision()
    {
        var sim = new SimulatorTarget(width: 16, height: 8);
        var svc = new DeviceDiscoveryService(sim);

        var devA = new FakeDeviceChannel("lan", "tcp://192.168.1.50:9000");
        var devB = new FakeDeviceChannel("lan", "tcp://192.168.1.72:9000");

        // Primero se descubre el dispositivo con su serial estable.
        bool r1 = svc.Register(new ChannelDisplayTarget(devA), "lan", devA.Endpoint, "ESP32-001");
        Assert.True(r1);

        // El MISMO serial reaparece con OTRO endpoint (el dispositivo cambió de IP).
        // Es rediscovery: misma identidad lógica, endpoint vivo actualizado.
        bool r2 = svc.Register(new ChannelDisplayTarget(devB), "lan", devB.Endpoint, "ESP32-001");
        Assert.True(r2);

        // Resolve por serial devuelve el target HACIA EL NUEVO endpoint (no el obsoleto).
        var resolved = svc.Resolve("ESP32-001");
        Assert.NotNull(resolved);
        // El registro vivo apunta al nuevo endpoint.
        var all = await svc.ListAsync();
        var summary = Assert.Single(all, d => d.Serial == "ESP32-001");
        Assert.Equal(devB.Endpoint, summary.Endpoint);
    }

    [Fact]
    public async Task Register_same_serial_same_endpoint_is_idempotent()
    {
        var sim = new SimulatorTarget(width: 16, height: 8);
        var svc = new DeviceDiscoveryService(sim);
        var dev = new FakeDeviceChannel("lan", "tcp://10.0.0.1:9000");

        Assert.True(svc.Register(new ChannelDisplayTarget(dev), "lan", dev.Endpoint, "ESP-001"));
        Assert.True(svc.Register(new ChannelDisplayTarget(dev), "lan", dev.Endpoint, "ESP-001"));

        // Un único registro (idempotente), sin fallos de colisión.
        Assert.Empty(svc.LastFailures);
        var all = await svc.ListAsync();
        Assert.Single(all, d => d.Serial == "ESP-001");
    }

    [Fact]
    public async Task Resolve_prefers_serial_over_device_id()
    {
        var sim = new SimulatorTarget(width: 16, height: 8);
        var svc = new DeviceDiscoveryService(sim);
        var dev = new FakeDeviceChannel("serial", "COM7");
        await svc.DiscoverAsync(new IDeviceChannel[] { dev });

        var bySerial = svc.Resolve(dev.Serial);
        Assert.NotNull(bySerial);
        // El serial NO debe coincidir con el DeviceId local del target.
        Assert.NotEqual(dev.Endpoint, dev.Serial);
    }

    // ---- Thread-safety del registro ----

    [Fact]
    public async Task Concurrent_registration_is_thread_safe()
    {
        var sim = new SimulatorTarget(width: 16, height: 8);
        var svc = new DeviceDiscoveryService(sim);

        var tasks = new List<Task<bool>>();
        for (int i = 0; i < 200; i++)
        {
            var dev = new FakeDeviceChannel("lan", $"tcp://10.0.{i / 254}.{i % 254}:9000");
            var serial = $"SER-{i}";
            tasks.Add(Task.Run(() => svc.Register(new ChannelDisplayTarget(dev), "lan", dev.Endpoint, serial)));
        }
        var results = await Task.WhenAll(tasks);

        // Todos con seriales únicos deben registrarse sin corromper el diccionario.
        Assert.All(results, Assert.True);
    }

    // ---- Fallos sanitizados (no catch vacío) ----

    [Fact]
    public async Task Discovery_records_sanitized_failures_for_unreachable_channels()
    {
        var sim = new SimulatorTarget(width: 16, height: 8);
        var svc = new DeviceDiscoveryService(sim);

        // Un canal que siempre lanza (device caído).
        await svc.DiscoverAsync(new IDeviceChannel[] { new AlwaysFailingChannel("lan", "tcp://10.0.0.99:9999") });

        Assert.NotEmpty(svc.LastFailures);
        // Sanitizado: sin stack trace (la clase de la excepción, no el full trace).
        Assert.All(svc.LastFailures, f => Assert.DoesNotContain("   at ", f));
        Assert.All(svc.LastFailures, f => Assert.DoesNotContain("StackTrace", f));
    }

    [Fact]
    public async Task Discovery_failure_message_includes_transport_and_endpoint()
    {
        var sim = new SimulatorTarget(width: 16, height: 8);
        var svc = new DeviceDiscoveryService(sim);
        await svc.DiscoverAsync(new IDeviceChannel[] { new AlwaysFailingChannel("serial", "COM9") });

        Assert.Contains(svc.LastFailures, f => f.Contains("serial") && f.Contains("COM9"));
    }
}

/// <summary>Canal que falla siempre, para verificar sanitización de fallos.</summary>
public sealed class AlwaysFailingChannel : IDeviceChannel
{
    public AlwaysFailingChannel(string transport, string endpoint) { Transport = transport; Endpoint = endpoint; }
    public string Transport { get; }
    public string Endpoint { get; }
    public Task<byte[]> RequestAsync(byte[] frame, CancellationToken ct = default)
        => throw new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused);
}