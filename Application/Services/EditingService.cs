using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;

namespace DSLetreros.Application.Services;

/// <summary>Casos de uso de edición (sección 8): Add/Delete/Duplicate/Move/Align/Group/Timing/Animation.</summary>
public sealed class EditingService
{
    public const int MaxObjectsPerScene = 1000;

    /// <summary>Máximo de píxeles por dibujo (ancho * alto), aplicado con checked.</summary>
    public const int MaxDrawingPixels = 512 * 512;

    public TextObject AddText(Scene scene, string text, PixelPoint position, RgbColor? color = null)
    {
        EnsureCapacity(scene, 1);
        var obj = new TextObject
        {
            Name = text.Length > 8 ? text[..8] : text,
            Text = text,
            Position = position,
            Color = color ?? RgbColor.White,
            Timing = new TimeRange(TimeSpan.Zero, scene.Duration),
        };
        var layer = EnsureLayer(scene);
        layer.Objects.Add(obj);
        return obj;
    }

    public ShapeObject AddShape(Scene scene, ShapeKind kind, PixelPoint position, PixelSize size)
    {
        EnsureCapacity(scene, 1);
        var obj = new ShapeObject
        {
            Name = kind.ToString(),
            Shape = kind,
            Position = position,
            Size = size,
            Timing = new TimeRange(TimeSpan.Zero, scene.Duration),
        };
        EnsureLayer(scene).Objects.Add(obj);
        return obj;
    }

    public DrawingObject AddDrawing(Scene scene, PixelSize size)
    {
        EnsureCapacity(scene, 1);

        // Tamaño con aritmética checked: rechaza overflow y límites excesivos.
        int pixelCount;
        try
        {
            pixelCount = checked(size.Width * size.Height);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(size),
                $"El tamaño {size.Width}x{size.Height} del dibujo desborda el cálculo de píxeles.");
        }
        if (pixelCount > MaxDrawingPixels)
            throw new ArgumentOutOfRangeException(nameof(size),
                $"El dibujo de {pixelCount} píxeles supera el máximo de {MaxDrawingPixels}.");

        var obj = new DrawingObject
        {
            Name = "Dibujo",
            Size = size,
            PixelData = new byte[pixelCount],
            Timing = new TimeRange(TimeSpan.Zero, scene.Duration),
        };
        EnsureLayer(scene).Objects.Add(obj);
        return obj;
    }

    public void DeleteObjects(Scene scene, IEnumerable<ObjectId> ids)
    {
        var set = ids.ToHashSet();
        foreach (var layer in scene.Layers)
            layer.Objects.RemoveAll(o => set.Contains(o.Id));
    }

    /// <summary>Duplica objetos generando IDs nuevos (spec: copia genera IDs nuevos).</summary>
    public List<SceneObject> DuplicateObjects(Scene scene, IEnumerable<SceneObject> objects)
    {
        var src = objects.ToList();
        if (src.Count == 0)
            return new List<SceneObject>();

        // Comprueba el total (existente + nuevos) ANTES de insertar.
        EnsureCapacity(scene, src.Count);

        var created = new List<SceneObject>(src.Count);
        var layer = EnsureLayer(scene);
        foreach (var o in src)
        {
            var copy = CloneObject(o);
            copy.Id = ObjectId.New();
            copy.Name = o.Name + " (copia)";
            copy.Position = o.Position + new PixelPoint(1, 1);
            layer.Objects.Add(copy);
            created.Add(copy);
        }
        return created;
    }

    public void MoveObjects(IEnumerable<SceneObject> objects, PixelPoint delta)
    {
        foreach (var o in objects)
            o.Position = o.Position + delta;
    }

    public void ChangePosition(SceneObject obj, PixelPoint position) => obj.Position = position;
    public void ChangeSize(SceneObject obj, PixelSize size) => obj.Size = size;
    public void ChangeTiming(SceneObject obj, TimeRange timing) => obj.Timing = timing;

    public void AssignAnimation(SceneObject obj, AnimationDefinition definition)
    {
        obj.Animations.RemoveAll(a => a.Slot == definition.Slot);
        obj.Animations.Add(definition);
    }

    /// <summary>
    /// Comprueba, antes de cualquier inserción, que el total de objetos de la escena
    /// (existentes + <paramref name="incoming"/>) no exceda <see cref="MaxObjectsPerScene"/>.
    /// </summary>
    private static void EnsureCapacity(Scene scene, int incoming)
    {
        if (scene is null) throw new ArgumentNullException(nameof(scene));
        if (incoming < 0) throw new ArgumentOutOfRangeException(nameof(incoming));

        int existing = (int)scene.ObjectCount();
        int total;
        try
        {
            total = checked(existing + incoming);
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException(
                $"El número de objetos de la escena desborda el máximo de {MaxObjectsPerScene}.");
        }

        if (total > MaxObjectsPerScene)
            throw new InvalidOperationException(
                $"Se excede el máximo de {MaxObjectsPerScene} objetos por escena " +
                $"(existentes: {existing}, intentado añadir: {incoming}).");
    }

    private static Layer EnsureLayer(Scene scene)
    {
        if (scene.Layers.Count == 0)
            scene.Layers.Add(new Layer { Name = "Capa 1", Order = 0 });
        return scene.Layers.OrderBy(l => l.Order).First();
    }

    private static SceneObject CloneObject(SceneObject src)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(src, src.GetType(), Infrastructure.Persistence.AtlasJson.Options);
        var copy = (SceneObject)System.Text.Json.JsonSerializer.Deserialize(json, src.GetType(), Infrastructure.Persistence.AtlasJson.Options)!;
        return copy;
    }
}