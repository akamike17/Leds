using System.Net;
using System.Net.Sockets;
using DSLetreros.Domain.Deployment;
using DSLetreros.Infrastructure.Transport;
using Xunit;

namespace DSLetreros.Tests.Transport;

/// <summary>
/// Prueba de loopback real del canal TCP (spec 18/20.9): levanta un listener local
/// que responde con el protocolo de cable, y el TcpDeviceChannel le habla de verdad
/// por socket. Valida el transporte físico sin hardware.
/// </summary>
public class TcpDeviceChannelTests
{
    [Fact]
    public async Task Tcp_channel_roundtrips_a_frame_over_real_socket()
    {
        // 1. Servidor fake en un puerto efímero.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            // Lee el header (11) + payload y responde con un ACK enmarcado.
            var header = new byte[11];
            await ReadExactAsync(stream, header);

            var op = header[5];
            if (op == DeviceProtocol.OpHello)
            {
                // Responder ACK
                var ack = DeviceProtocol.Ack();
                await stream.WriteAsync(ack);
                await stream.FlushAsync();
            }
        });

        // 2. El canal TCP real envía Hello y recibe el ACK.
        using var channel = new TcpDeviceChannel("127.0.0.1", port);
        var hello = DeviceProtocol.Hello(DSLetreros.Domain.ValueObjects.DeviceId.New());
        var resp = await channel.RequestAsync(hello);

        var (opResp, _, _) = DeviceProtocol.Unwrap(resp);
        Assert.Equal(DeviceProtocol.OpAck, opResp);

        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        listener.Stop();
    }

    [Fact]
    public void Tcp_channel_reports_lan_transport_and_endpoint()
    {
        using var channel = new TcpDeviceChannel("10.0.0.9", 9000);
        Assert.Equal("lan", channel.Transport);
        Assert.Equal("10.0.0.9:9000", channel.Endpoint);
    }

    [Fact]
    public void Serial_channel_reports_serial_transport_and_port()
    {
        using var channel = new SerialDeviceChannel("COM3", 115200);
        Assert.Equal("serial", channel.Transport);
        Assert.Equal("COM3", channel.Endpoint);
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