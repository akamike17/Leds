using DSLetreros.Domain.Entities;
using DSLetreros.Domain.Validation;
using DSLetreros.Infrastructure.Persistence;

namespace DSLetreros.Application.Services;

/// <summary>Casos de uso de proyectos (sección 8): Create/Open/Save/Validate/Recover.</summary>
public sealed class ProjectService
{
    private readonly AtlasProjectStore _store;
    private readonly string _projectsRoot;

    public ProjectService(AtlasProjectStore store, IWebHostEnvironment env)
    {
        _store = store;
        // biblioteca local fuera de wwwroot; p.ej. %temp%/dsletras o App_Data
        _projectsRoot = Path.Combine(env.ContentRootPath, "App_Data", "projects");
        Directory.CreateDirectory(_projectsRoot);
    }

    public string ProjectsRoot => _projectsRoot;

    public Project CreateProject(string name, int width, int height)
    {
        var project = new Project
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Sin título" : name.Trim(),
            Canvas = new Domain.ValueObjects.CanvasDefinition(width, height),
        };
        var scene = new Scene { Name = "Escena 1", Duration = TimeSpan.FromSeconds(5) };
        scene.Layers.Add(new Layer { Name = "Capa 1", Order = 0 });
        project.Scenes.Add(scene);
        return project;
    }

    public ValidationResult Validate(Project project) => ProjectValidator.Validate(project);

    public async Task<PersistenceResult> SaveAsync(Project project, CancellationToken ct = default)
    {
        project.UpdatedAt = DateTimeOffset.UtcNow;
        var validation = ProjectValidator.Validate(project);
        if (!validation.IsValid)
            return PersistenceResult.Fail("Proyecto inválido: " + string.Join("; ", validation.Errors));

        var path = Path.Combine(_projectsRoot, $"{project.Id.Value:N}.atlas");
        return await _store.SaveAsync(project, path, ct);
    }

    /// <summary>Autosave separado (spec sección 16): escribe `&lt;id&gt;.atlas.autosave` sin tocar el original.</summary>
    public async Task<PersistenceResult> AutosaveAsync(Project project, CancellationToken ct = default)
    {
        var path = Path.Combine(_projectsRoot, $"{project.Id.Value:N}.atlas");
        return await _store.AutosaveAsync(project, path, ct);
    }

    public async Task<(PersistenceResult, Project?)> OpenAsync(string path, CancellationToken ct = default)
        => await _store.OpenAsync(path, ct);

    /// <summary>Lista proyectos guardados (resumen).</summary>
    public IReadOnlyList<ProjectSummary> ListProjects()
    {
        if (!Directory.Exists(_projectsRoot)) return Array.Empty<ProjectSummary>();
        var result = new List<ProjectSummary>();
        foreach (var dir in Directory.EnumerateDirectories(_projectsRoot, "*.atlas", SearchOption.TopDirectoryOnly))
        {
            var manifestPath = Path.Combine(dir, AtlasProjectStore.ManifestFile);
            if (!File.Exists(manifestPath)) continue;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
                var m = doc.RootElement.GetProperty("manifest");
                result.Add(new ProjectSummary
                {
                    Id = m.GetProperty("projectId").GetGuid(),
                    Name = m.GetProperty("name").GetString() ?? Path.GetFileName(dir),
                    Canvas = m.GetProperty("canvas").GetString() ?? "?",
                    UpdatedAt = m.GetProperty("updatedAt").GetDateTimeOffset(),
                    Path = dir,
                });
            }
            catch { /* skip corrupt */ }
        }
        return result.OrderByDescending(p => p.UpdatedAt).ToList();
    }
}

public sealed class ProjectSummary
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Canvas { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
    public string Path { get; set; } = string.Empty;
}