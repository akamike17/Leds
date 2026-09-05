using System.Text.Json;
using DSLetreros.Application.Services;
using DSLetreros.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DSLetreros.Controllers;

public class EditorController : Controller
{
    private readonly ProjectService _projects;

    public EditorController(ProjectService projects) => _projects = projects;

    [HttpGet]
    public IActionResult Index(Guid? id)
    {
        if (id == null || id == Guid.Empty)
            return RedirectToAction("New", "Projects");

        // el editor carga desde el disco vía un endpoint JSON (Editor/Load?id=).
        return View(new EditorViewModel { ProjectId = id.Value });
    }

    /// <summary>
    /// Crea y persiste un proyecto nuevo editando = operación mutable → POST.
    /// Requiere antiforgery (JSON también se protege vía encabezado/cookie).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> New([FromForm] int width = 32, [FromForm] int height = 16, [FromForm] string? name = null)
    {
        width = Math.Clamp(width, 1, 256);
        height = Math.Clamp(height, 1, 256);
        var project = _projects.CreateProject(name ?? "Sin título", width, height);
        var result = await _projects.SaveAsync(project);
        if (!result.Success)
            return BadRequest(new { success = false, message = "No se pudo crear el proyecto: " + result.Message });
        return RedirectToAction("Index", new { id = project.Id.Value });
    }

    [HttpGet]
    public async Task<IActionResult> Load([FromQuery] Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
            return NotFound(new { success = false, message = "Proyecto no encontrado." });

        var (result, project) = await _projects.OpenByIdAsync(id, ct);
        if (!result.Success || project == null)
            return BadRequest(new { success = false, message = result.Message });

        return Json(new
        {
            success = true,
            project = System.Text.Json.JsonSerializer.Serialize(project, Infrastructure.Persistence.AtlasJson.Options)
        });
    }
}

public class ProjectsController : Controller
{
    private readonly ProjectService _projects;

    public ProjectsController(ProjectService projects) => _projects = projects;

    [HttpGet]
    public IActionResult Index()
    {
        return View(new ProjectSummaryViewModel { Projects = _projects.ListProjects() });
    }

    [HttpGet]
    public IActionResult New()
    {
        return View(new NewProjectViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(NewProjectViewModel model)
    {
        if (!ModelState.IsValid)
            return View("New", model);
        int w = Math.Clamp(model.Width, 1, 256);
        int h = Math.Clamp(model.Height, 1, 256);
        var project = _projects.CreateProject(model.Name, w, h);
        var result = await _projects.SaveAsync(project);
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, "No se pudo crear el proyecto: " + result.Message);
            return View("New", model);
        }
        return RedirectToAction("Index", "Editor", new { id = project.Id.Value });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] Domain.Entities.Project project, CancellationToken ct)
    {
        if (project == null)
            return BadRequest(new { success = false, message = "Proyecto no deserializable." });

        var result = await _projects.SaveAsync(project, ct);
        return Json(new { success = result.Success, message = result.Message, path = result.Path });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Autosave([FromBody] Domain.Entities.Project project, CancellationToken ct)
    {
        if (project == null)
            return BadRequest(new { success = false, message = "Proyecto no deserializable." });

        var result = await _projects.AutosaveAsync(project, ct);
        return Json(new { success = result.Success, message = result.Message, path = result.Path });
    }
}