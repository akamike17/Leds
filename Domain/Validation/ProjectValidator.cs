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

/// <summary>
/// Frontera defensiva del documento: invariantes de dominio (sección 5) más límites
/// de seguridad operativos. Todo lo que entra al editor (deserializado desde JSON,
/// importado, editado) debe pasar por aquí antes de usarse.
/// </summary>
public static class ProjectValidator
{
    // --- Límites operativos (frontera defensiva) ---
    /// <summary>Versión de formato soportada (única).</summary>
    public const int SupportedFormatVersion = 1;

    /// <summary>Dimensión máxima del lienzo (y de cualquier asset de mapa de bits).</summary>
    public const int MaxCanvasDimension = 512;

    /// <summary>Máximo número de escenas por proyecto.</summary>
    public const int MaxScenes = 4096;

    /// <summary>Máximo número de capas por escena.</summary>
    public const int MaxLayersPerScene = 1024;

    /// <summary>Máximo número de objetos por capa.</summary>
    public const int MaxObjectsPerLayer = 4096;

    /// <summary>Máximo número de objetos por escena (todos las capas).</summary>
    public const int MaxObjectsPerScene = 1000;

    /// <summary>Máximo número de assets embebidos por proyecto.</summary>
    public const int MaxEmbeddedAssets = 4096;

    /// <summary>Máxima longitud de un nombre (de cualquier entidad).</summary>
    public const int MaxNameLength = 256;

    /// <summary>Máxima longitud de un texto de objeto.</summary>
    public const int MaxTextLength = 4096;

    /// <summary>Máximo número de píxeles en un asset de mapa de bits (gráfico/ícono).</summary>
    public const int MaxBitmapPixels = 512 * 512;

    /// <summary>Máximo de píxeles por objeto de dibujo (bitmap 1bpp).</summary>
    public const int MaxDrawingPixels = 512 * 512;

    /// <summary>Máximo total de píxeles en un dibujo (ancho * alto, checked).</summary>
    public const int MaxTotalPixels = 512 * 512;

    public static ValidationResult Validate(Project project)
    {
        var result = new ValidationResult();
        if (project is null) throw new ArgumentNullException(nameof(project));

        ValidateCanvas(project, result);
        ValidateFormatVersion(project, result);
        ValidateName(project.Name, "Proyecto", result);

        if (project.Scenes.Count < 1)
            result.Error("El proyecto debe tener al menos una escena.");
        if (project.Scenes.Count > MaxScenes)
            result.Error($"El proyecto supera el máximo de {MaxScenes} escenas.");

        var seenObjectIds = new HashSet<ObjectId>();
        var seenSceneIds = new HashSet<SceneId>();
        var seenLayerIds = new HashSet<string>();

        foreach (var scene in project.Scenes)
        {
            ValidateScene(scene, result, seenSceneIds, seenLayerIds, seenObjectIds, project);
        }

        ValidateEmbeddedAssets(project, result);
        ValidateReferences(project, result);
        return result;
    }

    private static void ValidateCanvas(Project project, ValidationResult result)
    {
        var canvas = project.Canvas;
        if (canvas.Width <= 0 || canvas.Height <= 0)
            result.Error("El lienzo debe tener dimensiones positivas.");
        if (canvas.Width > MaxCanvasDimension || canvas.Height > MaxCanvasDimension)
            result.Error($"Dimensiones del lienzo fuera de límites (máx {MaxCanvasDimension}).");
        // Límite de memoria del lienzo (checked contra overflow).
        if (canvas.Width > 0 && canvas.Height > 0)
        {
            try
            {
                long pixels = checked((long)canvas.Width * canvas.Height);
                if (pixels > MaxTotalPixels)
                    result.Error($"El lienzo supera el máximo de {MaxTotalPixels} píxeles totales.");
            }
            catch (OverflowException)
            {
                result.Error("Las dimensiones del lienzo desbordan el cálculo de píxeles.");
            }
        }
    }

    private static void ValidateFormatVersion(Project project, ValidationResult result)
    {
        if (project.FormatVersion != SupportedFormatVersion)
            result.Error($"Versión de formato no soportada: {project.FormatVersion} (esperada {SupportedFormatVersion}).");
    }

    private static void ValidateScene(Scene scene, ValidationResult result,
        HashSet<SceneId> seenSceneIds, HashSet<string> seenLayerIds,
        HashSet<ObjectId> seenObjectIds, Project project)
    {
        if (!seenSceneIds.Add(scene.Id))
            result.Error($"ID de escena duplicado: {scene.Id}.");

        ValidateName(scene.Name, "Escena", result);

        if (scene.Duration <= TimeSpan.Zero || !IsFinite(scene.Duration))
            result.Error($"La escena '{OrUnnamed(scene.Name)}' debe tener duración finita y > 0.");

        if (scene.Layers.Count < 1)
            result.Error($"La escena '{OrUnnamed(scene.Name)}' debe tener al menos una capa.");
        if (scene.Layers.Count > MaxLayersPerScene)
            result.Error($"La escena '{OrUnnamed(scene.Name)}' supera el máximo de {MaxLayersPerScene} capas.");

        int sceneObjects = 0;
        foreach (var layer in scene.Layers)
        {
            if (!seenLayerIds.Add(layer.Id))
                result.Error($"ID de capa duplicado: {layer.Id}.");
            ValidateName(layer.Name, "Capa", result);

            if (layer.Objects.Count > MaxObjectsPerLayer)
                result.Error($"La capa '{OrUnnamed(layer.Name)}' supera el máximo de {MaxObjectsPerLayer} objetos.");

            sceneObjects += layer.Objects.Count;
            foreach (var obj in layer.Objects)
            {
                ValidateObject(obj, scene, project, result, seenObjectIds);
            }
        }

        if (sceneObjects > MaxObjectsPerScene)
            result.Error($"La escena '{OrUnnamed(scene.Name)}' supera el máximo de {MaxObjectsPerScene} objetos: {sceneObjects}.");
    }

    private static void ValidateObject(SceneObject obj, Scene scene, Project project,
        ValidationResult result, HashSet<ObjectId> seenObjectIds)
    {
        if (!seenObjectIds.Add(obj.Id))
            result.Error($"ID de objeto duplicado: {obj.Id}.");

        ValidateName(obj.Name, "Objeto", result);

        // Posiciones: política >= 0.
        if (obj.Position.X < 0 || obj.Position.Y < 0)
            result.Error($"Objeto '{OrUnnamed(obj.Name)}' con posición negativa: {obj.Position}.");

        // Timings: deben ser no negativos y finitos. Exceder la duración de la escena
        // es tolerable (el render recorta), por lo que se reporta como warning.
        if (obj.Timing.Start < TimeSpan.Zero)
            result.Error($"Objeto '{OrUnnamed(obj.Name)}' con inicio de timing negativo.");
        if (!IsFinite(obj.Timing.End))
            result.Error($"Objeto '{OrUnnamed(obj.Name)}' con fin de timing no finito.");
        if (obj.Timing.End > scene.Duration)
            result.Warning($"Objeto '{OrUnnamed(obj.Name)}' con timing que excede la duración de la escena.");

        // Sizes dentro de límites.
        ValidateSize(obj.Size, obj.Name, result);

        switch (obj)
        {
            case TextObject text:
                if (text.Text != null && text.Text.Length > MaxTextLength)
                    result.Error($"Texto de '{OrUnnamed(text.Name)}' supera {MaxTextLength} caracteres.");
                break;

            case DrawingObject drawing:
                ValidateDrawing(drawing, result);
                break;

            case IconObject icon:
                if (icon.AssetId is { } assetId &&
                    !project.EmbeddedAssets.ContainsKey(assetId.Value.ToString("N")))
                    result.Warning($"Icono '{OrUnnamed(icon.Name)}' referencia un asset no embebido.");
                break;

            case ImageObject image:
                if (image.AssetId is { } imgAssetId &&
                    !project.EmbeddedAssets.ContainsKey(imgAssetId.Value.ToString("N")))
                    result.Warning($"Imagen '{OrUnnamed(image.Name)}' referencia un asset no embebido.");
                break;
        }

        ValidateAnimations(obj, result);
    }

    private static void ValidateSize(PixelSize size, string objName, ValidationResult result)
    {
        // PixelSize ya garantiza >= 0; validamos límite superior y total de píxeles.
        try
        {
            long pixels = checked((long)size.Width * size.Height);
            if (pixels > MaxTotalPixels)
                result.Error($"Objeto '{OrUnnamed(objName)}' con tamaño total de píxeles excesivo: {pixels}.");
        }
        catch (OverflowException)
        {
            result.Error($"Objeto '{OrUnnamed(objName)}' con tamaño que desborda el cálculo de píxeles.");
        }
    }

    private static void ValidateDrawing(DrawingObject drawing, ValidationResult result)
    {
        // Bits por píxel soportados (por ahora solo 1 = bitmask monocromo).
        if (drawing.BitsPerPixel != 1)
            result.Error($"Dibujo '{OrUnnamed(drawing.Name)}' con bits por píxel no soportado: {drawing.BitsPerPixel}.");

        // Paleta no vacía (color "on"); el render usa Palette[0].
        if (drawing.Palette == null || drawing.Palette.Count == 0)
            result.Warning($"Dibujo '{OrUnnamed(drawing.Name)}' sin paleta definida.");

        // PixelData es una máscara de bits (0 = transparente, !=0 = dibujado):
        // la longitud debe ser exactamente w*h y dentro del máximo.
        try
        {
            long expected = checked((long)drawing.Size.Width * drawing.Size.Height);
            if (expected > MaxDrawingPixels)
                result.Error($"Dibujo '{OrUnnamed(drawing.Name)}' supera {MaxDrawingPixels} píxeles.");
            if (drawing.PixelData != null && expected != drawing.PixelData.LongLength)
                result.Error($"Dibujo '{OrUnnamed(drawing.Name)}': PixelData de {drawing.PixelData.LongLength} bytes no coincide con {expected} píxeles.");
        }
        catch (OverflowException)
        {
            result.Error($"Dibujo '{OrUnnamed(drawing.Name)}' con tamaño que desborda el cálculo de píxeles.");
        }
    }

    private static void ValidateAnimations(SceneObject obj, ValidationResult result)
    {
        var slots = new HashSet<AnimationSlot>();
        foreach (var a in obj.Animations)
        {
            if (!slots.Add(a.Slot))
                result.Error($"Objeto '{OrUnnamed(obj.Name)}' con animaciones duplicadas en slot {a.Slot}.");

            // Direction: ciertos tipos no la usan; aquí validamos únicamente slots válidos (enum).
            // Los enums ya son tipos fuertes; no hay valores inválidos posibles en C# salvo cast.
        }
    }

    private static void ValidateEmbeddedAssets(Project project, ValidationResult result)
    {
        if (project.EmbeddedAssets.Count > MaxEmbeddedAssets)
            result.Error($"El proyecto supera el máximo de {MaxEmbeddedAssets} assets embebidos.");

        var seenAssetIds = new HashSet<string>();
        foreach (var kv in project.EmbeddedAssets)
        {
            if (!seenAssetIds.Add(kv.Key))
                result.Error($"ID de asset duplicado: {kv.Key}.");

            // Contenido debe ser JSON válido.
            if (string.IsNullOrWhiteSpace(kv.Value))
            {
                result.Error($"Asset '{kv.Key}' con contenido vacío.");
                continue;
            }
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(kv.Value);
                ValidateIndexedAssetPixels(kv.Key, doc.RootElement, result);
            }
            catch (System.Text.Json.JsonException)
            {
                result.Error($"Asset '{kv.Key}' con contenido JSON inválido.");
            }
        }
    }

    /// <summary>
    /// Para assets indexados (íconos/imágenes) con `pixels` (base64) y `palette`,
    /// valida que cada índice sea &lt; palette.Count.
    /// </summary>
    private static void ValidateIndexedAssetPixels(string key, System.Text.Json.JsonElement root, ValidationResult result)
    {
        if (!root.TryGetProperty("palette", out var palArr) || palArr.ValueKind != System.Text.Json.JsonValueKind.Array)
            return;

        int paletteCount = palArr.GetArrayLength();
        if (paletteCount == 0)
        {
            result.Error($"Asset '{key}' con paleta vacía.");
            return;
        }

        if (!root.TryGetProperty("pixels", out var pixProp))
            return;

        byte[] data;
        try
        {
            data = Convert.FromBase64String(pixProp.GetString() ?? string.Empty);
        }
        catch (FormatException)
        {
            return; // no es base64; el render lo ignorará igualmente
        }

        foreach (var b in data)
        {
            if (b >= paletteCount)
            {
                result.Error($"Asset '{key}' con índice de píxel {b} fuera de paleta (tamaño {paletteCount}).");
                break;
            }
        }
    }

    /// <summary>Referencias resolubles: assets embebidos.</summary>
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
                        result.Warning($"Icono '{OrUnnamed(icon.Name)}' referencia un asset no embebido.");
                    break;
                case ImageObject image when image.AssetId != null:
                    if (!project.EmbeddedAssets.ContainsKey(image.AssetId.Value.ToString("N")))
                        result.Warning($"Imagen '{OrUnnamed(image.Name)}' referencia un asset no embebido.");
                    break;
            }
        }
    }

    private static void ValidateName(string? name, string kind, ValidationResult result)
    {
        if (name != null && name.Length > MaxNameLength)
            result.Error($"{kind} con nombre de {name.Length} caracteres (máx {MaxNameLength}).");
    }

    private static bool IsFinite(TimeSpan ts) =>
        ts != TimeSpan.MinValue && ts != TimeSpan.MaxValue &&
        !double.IsNaN(ts.TotalMilliseconds) && !double.IsInfinity(ts.TotalMilliseconds);

    private static string OrUnnamed(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "(sin nombre)" : name;
}