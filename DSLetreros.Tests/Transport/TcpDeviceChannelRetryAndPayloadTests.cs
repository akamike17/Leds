using System.Net;
using System.Net.Sockets;
using DSLetreros.Domain.Deployment;
using DSLetreros.Infrastructure.Transport;
using Xunit;

namespace DSLetreros.Tests.Transport;

/// <summary>
/// HIL adicional del canal TCP (final.md §14): cubre los branches de retry/conexión/
/// payload que antes quedaban NoCoverage en `TcpDeviceChannel`.
///   - Payload no vacío (len > 0) en la respuesta.
///   - Conexión rechazada (ConnectAsync falla → propagación tras el retry).
///   - Cierre a mitad de payload (n <= 0 → IOException).
/// NO toca el SerialDeviceChannel (requiere puerto COM físico, no simulable).
/// </summary>
public class TcpDeviceChannelRetryAndPayloadTests
{
    // ---- Payload no vacío: ejercita `if (len > 0) await ReadExactAsync(...)` ----

    [Fact]
    public async Task Response_with_nonempty_payload_is_read_fully()
    {
        // El servidor responde un ACK con un payload de 4 bytes no vacío.
        var (channel, server) = await StartServer(async stream =>
        {
            byte[] payload = { 0xDE, 0xAD, 0xBE, 0xEF };
            var frame = DeviceProtocol.Wrap(DeviceProtocol.OpAck, 0, payload);
            await stream.WriteAsync(frame);
        });

        using (channel)
        {
            var resp = await channel.RequestAsync(
                DeviceProtocol.Wrap(DeviceProtocol.OpHello, 0, Array.Empty<byte>()));
            var (op, _, payload) = DeviceProtocol.Unwrap(resp);
            Assert.Equal(DeviceProtocol.OpAck, op);
            Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, payload);
        }
        await server;
    }

    // ---- Conexión rechazada: ConnectAsync falla y el retry propaga ----

    [Fact]
    public async Task Connection_refused_propagates_after_single_retry()
    {
        // Puerto sin listener → ConnectAsync lanza SocketException; el canal reintenta
        // una vez y luego propaga (cubre los `throw` del retry, L70/L72 y L97).
        using var channel = new TcpDeviceChannel("127.0.0.1", PortWithNoListener(),
            requestTimeout: TimeSpan.FromSeconds(3));

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await channel.RequestAsync(DeviceProtocol.Wrap(DeviceProtocol.OpHello, 0, Array.Empty<byte>())));
    }

    // ---- Cierre a mitad de payload: n <= 0 → IOException ----

    [Fact]
    public async Task Connection_closed_mid_payload_throws()
    {
        // El servidor anuncia un payload de 8 bytes pero sólo envía 3 y cierra.
        var (channel, server) = await StartServer(async stream =>
        {
            // Construye un header válido que declara len=8 pero envía poco payload.
            var header = DeviceProtocol.Ack();
            BitConverter.GetBytes(8).CopyTo(header, 7);
            await stream.WriteAsync(header);
            await stream.WriteAsync(new byte[] { 1, 2, 3 });
            await stream.FlushAsync();
            // cierre abrupto sin el resto del payload
        });

        using (channel)
        {
            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await channel.RequestAsync(DeviceProtocol.Wrap(DeviceProtocol.OpHello, 0, Array.Empty<byte>())));
        }
        await server;
    }

    // ---- Helpers ----

    private static int PortWithNoListener()
    {
        // Reserva un puerto libre y lo libera: conexión a él será rechazada.
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

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

            var header = new byte[11];
            await ReadExactAsync(stream, header);
            var len = BitConverter.ToInt32(header, 7);
            if (len > 0)
            {
                var payload = new byte[len];
                await ReadExactAsync(stream, payload);
            }

            await responder(stream);
            try { client.Client.Shutdown(SocketShutdown.Both); } catch { /* ignorar */ }
        });

        var channel = new TcpDeviceChannel("127.0.0.1", port, requestTimeout: TimeSpan.FromSeconds(5));
        return (channel, serverTask);
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