namespace DSLetreros.Web.ViewModels;

/// <summary>Vista del editor.</summary>
public sealed class EditorViewModel
{
    public Guid ProjectId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public int CanvasWidth { get; set; }
    public int CanvasHeight { get; set; }
    public string ProjectJson { get; set; } = "{}";
}

public sealed class NewProjectViewModel
{
    public string Name { get; set; } = "Sin título";
    public int Width { get; set; } = 32;
    public int Height { get; set; } = 16;
}

public sealed class ProjectSummaryViewModel
{
    public IReadOnlyList<Application.Services.ProjectSummary> Projects { get; set; } =
        Array.Empty<Application.Services.ProjectSummary>();
}

public sealed class SendResultViewModel
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}