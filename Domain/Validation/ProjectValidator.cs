using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;

namespace DSLetreros.Domain.Validation;

/// <summary>Resultado de validación: lista de errores (humanos) y warnings.</summary>
public sealed class ValidationResult
{
    private readonly List<string> _errors = new();
    private readonly List<string> _warnings = new();

    public bool IsValid => _errors.Count == 0;
    public IReadOnlyList<string> Errors => _errors;
    public IReadOnlyList<string> Warnings => _warnings;

    internal void Error(string message) => _errors.Add(message);
    internal void Warning(string message) => _warnings.Add(message);
}

/// <summary>Invariantes de dominio (sección 5).</summary>
public static class ProjectValidator
{
    public static ValidationResult Validate(Project project)
    {
        var result = new ValidationResult();

        if (project.Scenes.Count < 1)
            result.Error("El proyecto debe tener al menos una escena.");

        var seenObjectIds = new HashSet<ObjectId>();
        var seenSceneIds = new HashSet<SceneId>();
        var seenAssetIds = new HashSet<string>();

        foreach (var scene in project.Scenes)
        {
            if (!seenSceneIds.Add(scene.Id))
                result.Error($"ID de escena duplicado: {scene.Id}.");

            if (scene.Duration <= TimeSpan.Zero)
                result.Error($"La escena '{scene.Name}' debe tener duración > 0.");

            if (scene.Layers.Count < 1)
                result.Error($"La escena '{scene.Name}' debe tener al menos una capa.");

            foreach (var layer in scene.Layers)
            {
                foreach (var obj in layer.Objects)
                {
                    if (!seenObjectIds.Add(obj.Id))
                        result.Error($"ID de objeto duplicado: {obj.Id}.");
                }
            }
        }

        foreach (var id in project.EmbeddedAssets.Keys)
            if (!seenAssetIds.Add(id))
                result.Error($"ID de asset duplicado: {id}.");

        ValidateReferences(project, result);
        return result;
    }

    /// <summary>Referencias resolubles: assets embebidos y IDs de miembros de grupo.</summary>
    private static void ValidateReferences(Project project, ValidationResult result)
    {
        foreach (var scene in project.Scenes)
        foreach (var layer in scene.Layers)
        foreach (var obj in layer.Objects)
        {
            switch (obj)
            {
                case IconObject icon when icon.AssetId != null:
                    if (!project.EmbeddedAssets.ContainsKey(icon.AssetId.Value.ToString("N")))
                        result.Warning($"Icono '{icon.Name}' referencia un asset no embebido.");
                    break;
                case ImageObject image when image.AssetId != null:
                    if (!project.EmbeddedAssets.ContainsKey(image.AssetId.Value.ToString("N")))
                        result.Warning($"Imagen '{image.Name}' referencia un asset no embebido.");
                    break;
            }
        }
    }
}