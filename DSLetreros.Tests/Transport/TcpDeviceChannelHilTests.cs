using System.Net;
using System.Net.Sockets;
using DSLetreros.Domain.Deployment;
using DSLetreros.Infrastructure.Transport;
using Xunit;

namespace DSLetreros.Tests.Transport;

/// <summary>
/// Hardware-in-the-loop (HIL) del canal TCP: sockets reales en loopback, sin dispositivo
/// físico. Verifica robustez del transporte (P1): validación de header ANTES de reservar
/// payload, máximo de respuesta, reconnection y serialización de requests.
///
/// SEPARADO de los tests simulados: esta suite usa sockets reales (TcpListener).
/// </summary>
public class TcpDeviceChannelHilTests
{
    // ---- Header inválido no reserva memoria (evita OOM ante length corrupto) ----

    [Fact]
    public async Task Bad_magic_is_rejected_before_payload_allocation()
    {
        var (channel, server) = await StartServer(async stream =>
        {
            // Responde con un header con magic corrupto y length enormemente alto.
            var resp = DeviceProtocol.Ack();
            resp[0] = (byte)'X';
            await stream.WriteAsync(resp);
        });

        using (channel)
        {
            await Assert.ThrowsAsync<ProtocolException>(async () =>
                await channel.RequestAsync(DeviceProtocol.Wrap(DeviceProtocol.OpHello, 0, Array.Empty<byte>())));
        }
        await server;
    }

    [Fact]
    public async Task Oversized_length_is_rejected()
    {
        var (channel, server) = await StartServer(async stream =>
        {
            // Header válido pero length = 100 MiB (> MaxResponseBytes = 64 MiB).
            var resp = DeviceProtocol.Ack();
            BitConverter.GetBytes(100 * 1024 * 1024).CopyTo(resp, 7);
            await stream.WriteAsync(resp);
        });

        using (channel)
        {
            await Assert.ThrowsAsync<ProtocolException>(async () =>
                await channel.RequestAsync(DeviceProtocol.Wrap(DeviceProtocol.OpHello, 0, Array.Empty<byte>())));
        }
        await server;
    }

    [Fact]
    public async Task Future_version_is_rejected()
    {
        var (channel, server) = await StartServer(async stream =>
        {
            var resp = DeviceProtocol.Ack();
            resp[4] = 99; // version futura
            await stream.WriteAsync(resp);
        });

        using (channel)
        {
            await Assert.ThrowsAsync<ProtocolException>(async () =>
                await channel.RequestAsync(DeviceProtocol.Wrap(DeviceProtocol.OpHello, 0, Array.Empty<byte>())));
        }
        await server;
    }

    // ---- Serialización de requests (SemaphoreSlim) ----

    [Fact]
    public async Task Concurrent_requests_are_serialized_without_corruption()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        int served = 0;
        var serverTask = Task.Run(async () =>
        {
            // El canal almohada una conexión y responde ACK a cada frame leído secuencialmente.
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var header = new byte[11];
            while (served < 20)
            {
                var read = await ReadHeaderAsync(stream, header);
                if (read <= 0) break;
                var len = BitConverter.ToInt32(header, 7);
                if (len > 0)
                {
                    var payload = new byte[len];
                    await ReadExactAsync(stream, payload);
                }
                await stream.WriteAsync(DeviceProtocol.Ack());
                await stream.FlushAsync();
                Interlocked.Increment(ref served);
            }
        });

        using var channel = new TcpDeviceChannel("127.0.0.1", port);
        var tasks = new List<Task<byte[]>>();
        for (int i = 0; i < 20; i++)
        {
            int n = i;
            tasks.Add(Task.Run(() =>
                channel.RequestAsync(DeviceProtocol.Wrap(DeviceProtocol.OpHello, 0, System.Text.Encoding.UTF8.GetBytes($"req-{n}")))));
        }
        var results = await Task.WhenAll(tasks);

        foreach (var resp in results)
        {
            Assert.Equal(DeviceProtocol.OpAck, DeviceProtocol.Unwrap(resp).Op);
        }
        Assert.Equal(20, served);

        await serverTask.WaitAsync(TimeSpan.FromSeconds(10));
        listener.Stop();
    }

    // ---- Reconnection tras fallo de protocolo ----

    [Fact]
    public async Task Protocol_failure_triggers_reconnect_on_next_request()
    {
        // Primer servidor: responde con magic corrupto → el canal debe resetear.
        var (channel, server1) = await StartServer(async stream =>
        {
            var resp = DeviceProtocol.Ack();
            resp[0] = (byte)'X';
            await stream.WriteAsync(resp);
        });
        using var ch = channel;

        await Assert.ThrowsAsync<ProtocolException>(async () =>
            await ch.RequestAsync(DeviceProtocol.Wrap(DeviceProtocol.OpHello, 0, Array.Empty<byte>())));
        await server1;

        // Segundo servidor (nuevo puerto): debe reconectar y responder ACK OK.
        var listener2 = new TcpListener(IPAddress.Loopback, 0);
        listener2.Start();
        int port2 = ((IPEndPoint)listener2.LocalEndpoint).Port;
        var server2 = Task.Run(async () =>
        {
            using var c = await listener2.AcceptTcpClientAsync();
            var s = c.GetStream();
            var header = new byte[11];
            await ReadExactAsync(s, header);
            await s.WriteAsync(DeviceProtocol.Ack());
        });

        using var ch2 = new TcpDeviceChannel("127.0.0.1", port2);
        var ok = await ch2.RequestAsync(DeviceProtocol.Wrap(DeviceProtocol.OpHello, 0, Array.Empty<byte>()));
        Assert.Equal(DeviceProtocol.OpAck, DeviceProtocol.Unwrap(ok).Op);

        await server2.WaitAsync(TimeSpan.FromSeconds(5));
        listener2.Stop();
    }

    // ---- Helpers ----

    private static async Task<(TcpDeviceChannel channel, Task server)> StartServer(
        Func<NetworkStream, Task> responder)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();

            // Lee el header del request entrante antes de responder.
            var header = new byte[11];
            await ReadExactAsync(stream, header);
            var len = BitConverter.ToInt32(header, 7);
            if (len > 0)
            {
                var payload = new byte[len];
                await ReadExactAsync(stream, payload);
            }

            await responder(stream);
            // Cierra el stream para simular el fin de la respuesta.
            try { client.Client.Shutdown(SocketShutdown.Both); }
            catch { /* ignorar */ }
        });

        var channel = new TcpDeviceChannel("127.0.0.1", port, requestTimeout: TimeSpan.FromSeconds(5));
        return (channel, serverTask);
    }

    private static async Task<int> ReadHeaderAsync(NetworkStream stream, byte[] header)
    {
        int off = 0;
        while (off < header.Length)
        {
            int n = await stream.ReadAsync(header.AsMemory(off));
            if (n <= 0) return off;
            off += n;
        }
        return off;
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer)
    {
        int off = 0;
        while (off < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(off));
            if (n <= 0) throw new System.IO.IOException("cerrado");
            off += n;
        }
    }
}