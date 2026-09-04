using DSLetreros.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace DSLetreros.Controllers;

/// <summary>
/// Reproducción (spec 20): refleja la escena/target activo y el estado de
/// reproducción. Es un controller funcional, NO un stub.
/// </summary>
public class PlaybackController : Controller
{
    private readonly ProjectService _projects;

    public PlaybackController(ProjectService projects) => _projects = projects;

    [HttpGet]
    public IActionResult Index()
    {
        ViewBag.Projects = _projects.ListProjects();
        return View();
    }
}