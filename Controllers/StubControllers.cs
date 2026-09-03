using DSLetreros.Application.Services;
using DSLetreros.Domain.Deployment;
using DSLetreros.Infrastructure.Transport;
using Microsoft.AspNetCore.Mvc;

namespace DSLetreros.Controllers;

public class PlaybackController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();
}

/// <summary>
/// Ajustes del dispositivo (spec 18/21): configuración y enumeración de canales
/// LAN/Serial reales, y disparo de descubrimiento sobre <see cref="DeviceDiscoveryService"/>.
/// NO inventa hardware: el simulador local vive en memoria (para tests), y los canales
/// LAN/Serial se construyen a partir de la configuración del operador.
/// </summary>
public class SettingsController : Controller
{
    private readonly DeviceDiscoveryService _discovery;

    public SettingsController(DeviceDiscoveryService discovery)
    {
        _discovery = discovery;
    }

    [HttpGet]
    public IActionResult Index() => View();

    /// <summary>Lista los targets conocidos (simulador + descubiertos).</summary>
    [HttpGet]
    public async Task<IActionResult> Targets(CancellationToken ct)
    {
        var list = await _discovery.ListAsync(ct);
        return Json(new
        {
            targets = list.Select(t => new
            {
                id = t.Id,
                serial = t.Serial,
                name = t.Name,
                transport = t.Transport,
                endpoint = t.Endpoint,
                status = t.Status.ToString(),
                online = t.Online,
            }),
        });
    }

    /// <summary>Crea canales reales LAN/Serial a partir de la configuración y descubre.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Discover([FromBody] DiscoveryRequest request, CancellationToken ct)
    {
        if (request == null)
            return BadRequest(new { success = false, message = "Solicitud vacía." });

        var channels = new List<IDeviceChannel>();

        foreach (var lan in request.Lan ?? Array.Empty<LanEndpoint>())
        {
            if (string.IsNullOrWhiteSpace(lan.Host) || lan.Port <= 0 || lan.Port > 65535)
                continue;
            channels.Add(new TcpDeviceChannel(lan.Host.Trim(), lan.Port));
        }

        foreach (var ser in request.Serial ?? Array.Empty<SerialEndpoint>())
        {
            if (string.IsNullOrWhiteSpace(ser.PortName))
                continue;
            channels.Add(new SerialDeviceChannel(ser.PortName.Trim(), ser.BaudRate > 0 ? ser.BaudRate : 115200));
        }

        await _discovery.DiscoverAsync(channels, ct);

        var list = await _discovery.ListAsync(ct);
        return Json(new
        {
            success = true,
            failures = _discovery.LastFailures,
            targets = list.Select(t => new
            {
                id = t.Id,
                serial = t.Serial,
                name = t.Name,
                transport = t.Transport,
                endpoint = t.Endpoint,
                status = t.Status.ToString(),
                online = t.Online,
            }),
        });
    }
}

public sealed class DiscoveryRequest
{
    public LanEndpoint[]? Lan { get; set; }
    public SerialEndpoint[]? Serial { get; set; }
}

public sealed class LanEndpoint
{
    public string? Host { get; set; }
    public int Port { get; set; }
}

public sealed class SerialEndpoint
{
    public string? PortName { get; set; }
    public int BaudRate { get; set; } = 115200;
}