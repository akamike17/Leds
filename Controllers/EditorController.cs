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

        // en su forma más simple, el editor carga desde el disco; el proyecto
        // se materializa vía un endpoint JSON para evitar re-serializar aquí.
        return View(new EditorViewModel { ProjectId = id.Value });
    }

    [HttpGet]
    public async Task<IActionResult> New([FromQuery] int width = 32, [FromQuery] int height = 16, [FromQuery] string? name = null)
    {
        var project = _projects.CreateProject(name ?? "Sin título", width, height);
        // Persiste de inmediato para que el editor (que carga por Load desde disco)
        // pueda abrirlo y guardar/reabrir después.
        await _projects.SaveAsync(project);
        return RedirectToAction("Index", new { id = project.Id.Value });
    }

    [HttpGet]
    public async Task<IActionResult> Load([FromQuery] Guid id, CancellationToken ct)
    {
        var path = Path.Combine(_projects.ProjectsRoot, $"{id:N}.atlas");
        if (!Directory.Exists(path))
            return NotFound(new { success = false, message = "Proyecto no encontrado." });

        var (result, project) = await _projects.OpenAsync(path, ct);
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
        // Persiste de inmediato para que el editor pueda cargarlo desde disco.
        await _projects.SaveAsync(project);
        return RedirectToAction("Index", "Editor", new { id = project.Id.Value });
    }

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] Domain.Entities.Project project, CancellationToken ct)
    {
        if (project == null)
            return BadRequest(new { success = false, message = "Proyecto no deserializable." });

        var result = await _projects.SaveAsync(project, ct);
        return Json(new { success = result.Success, message = result.Message, path = result.Path });
    }

    [HttpPost]
    public async Task<IActionResult> Autosave([FromBody] Domain.Entities.Project project, CancellationToken ct)
    {
        if (project == null)
            return BadRequest(new { success = false, message = "Proyecto no deserializable." });

        var result = await _projects.AutosaveAsync(project, ct);
        return Json(new { success = result.Success, message = result.Message, path = result.Path });
    }

    [HttpGet]
    public async Task<IActionResult> Open([FromQuery] string path, CancellationToken ct)
    {
        var (result, project) = await _projects.OpenAsync(path, ct);
        if (!result.Success || project == null)
            return BadRequest(new { success = false, message = result.Message });

        return Json(new { success = true, project = JsonSerializer.Serialize(project, Infrastructure.Persistence.AtlasJson.Options) });
    }
}