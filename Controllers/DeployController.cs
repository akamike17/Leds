using DSLetreros.Application.Services;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc;

namespace DSLetreros.Controllers;

public class DevicesController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();
}

/// <summary>Envío/despliegue de una escena al target seleccionado (simulador o hardware).</summary>
public class DeployController : Controller
{
    private readonly ProjectService _projects;
    private readonly DSLetreros.Domain.Deployment.SimulatorTarget _simulator;
    private readonly DeviceDiscoveryService _discovery;
    private readonly DeploymentService _deployment;

    public DeployController(
        ProjectService projects,
        DSLetreros.Domain.Deployment.SimulatorTarget simulator,
        DeviceDiscoveryService discovery,
        DeploymentService deployment)
    {
        _projects = projects;
        _simulator = simulator;
        _discovery = discovery;
        _deployment = deployment;
    }

    [HttpGet]
    public async Task<IActionResult> Discover(CancellationToken ct)
    {
        var targets = await _discovery.ListAsync(ct);
        return Json(new
        {
            targets = targets.Select(t => new
            {
                id = t.Id,
                serial = t.Serial,
                name = t.Name,
                transport = t.Transport,
                endpoint = t.Endpoint,
                online = t.Online,
            }),
        });
    }

    /// <summary>Envía una escena del proyecto al target indicado (pipeline completo).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send([FromBody] SendRequest request, CancellationToken ct)
    {
        // ProjectId llega como string (hex "N" o "D" guid del cliente JS); se valida
        // y parsea aquí de forma defensiva.
        if (request == null || string.IsNullOrWhiteSpace(request.ProjectId))
            return BadRequest(new { success = false, message = "Proyecto no especificado." });

        if (!Guid.TryParseExact(request.ProjectId, "N", out var projectId) &&
            !Guid.TryParseExact(request.ProjectId, "D", out projectId))
            return BadRequest(new { success = false, message = "Identificador de proyecto inválido." });

        if (projectId == Guid.Empty)
            return BadRequest(new { success = false, message = "Proyecto no especificado." });

        // Resolver target por serial (identidad estable) o por DeviceId hex.
        var target = !string.IsNullOrWhiteSpace(request.TargetId)
            ? _discovery.Resolve(request.TargetId)
            : _simulator;

        if (target == null)
            return BadRequest(new { success = false, message = "Target no encontrado." });

        var (open, project) = await _projects.OpenByIdAsync(projectId, ct);
        if (!open.Success || project == null || project.Scenes.Count == 0)
            return BadRequest(new { success = false, message = "Proyecto sin escenas." });

        // Escena seleccionada (la que el usuario está editando), no siempre la primera.
        var idx = Math.Clamp(request.SceneIndex, 0, project.Scenes.Count - 1);
        var scene = project.Scenes[idx];
        var result = await _deployment.SendAsync(scene, project.Canvas, target, ct);

        return Json(new
        {
            success = result.Success,
            phase = result.Phase,
            message = result.Error,
            checksum = result.Checksum?.Value,
            sceneIndex = idx,
        });
    }

    [HttpGet]
    public async Task<IActionResult> Status([FromQuery] string? target, CancellationToken ct)
    {
        var resolved = target != null ? _discovery.Resolve(target) : _simulator;
        if (resolved == null)
            return BadRequest(new { status = "NotFound" });
        var status = await resolved.GetStatusAsync(ct);
        return Json(new { status = status.Value.ToString() });
    }

    /// <summary>
    /// Devuelve el framebuffer (RGB24) del paquete ACTIVO en el simulador, en el
    /// instante `timeMs` (frame prerenderizado más cercano). Demuestra el framebuffer
    /// REAL recibido por el target (R5: editor == simulador == compilado), no un
    /// simple "Enviado".
    /// </summary>
    [HttpGet]
    public IActionResult SimulatorFrame([FromQuery] double timeMs = 0)
    {
        var active = _simulator.Active;
        if (active == null || active.Frames.Count == 0)
            return Json(new { success = false, message = "Sin escena activa en el simulador." });

        // frame prerenderizado más cercano (>= timeMs), o el último.
        var frame = active.Frames[0];
        foreach (var f in active.Frames)
        {
            if (f.TimeMs >= timeMs) { frame = f; break; }
            frame = f;
        }

        int w = active.Canvas.Width, h = active.Canvas.Height;
        var pixels = new List<object>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 3;
                if (frame.Pixels[o] > 0 || frame.Pixels[o + 1] > 0 || frame.Pixels[o + 2] > 0)
                    pixels.Add(new { x, y, r = frame.Pixels[o], g = frame.Pixels[o + 1], b = frame.Pixels[o + 2] });
            }

        return Json(new
        {
            success = true,
            width = w,
            height = h,
            timeMs = frame.TimeMs,
            checksum = active.Checksum.Value,
            lit = pixels.Count,
            pixels,
        });
    }
}

public sealed class SendRequest
{
    /// <summary>Identificador del proyecto (hex "N" o "D"; se parsea en el controlador).</summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>Serial (identidad estable) o DeviceId hex del target. Nulo = simulador.</summary>
    public string? TargetId { get; set; }

    /// <summary>Índice de la escena a enviar (la que está editando el usuario). 0 = primera.</summary>
    public int SceneIndex { get; set; }
}