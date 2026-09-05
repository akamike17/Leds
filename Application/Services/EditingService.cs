using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;

namespace DSLetreros.Application.Services;

/// <summary>Casos de uso de edición (sección 8): Add/Delete/Duplicate/Move/Align/Group/Timing/Animation.</summary>
public sealed class EditingService
{
    public const int MaxObjectsPerScene = 1000;

    /// <summary>Máximo de píxeles por dibujo (ancho * alto), aplicado con checked.</summary>
    public const int MaxDrawingPixels = 512 * 512;

    public TextObject AddText(Scene scene, string text, PixelPoint position, RgbColor? color = null, Layer? layer = null)
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
        EnsureLayer(scene, layer).Objects.Add(obj);
        return obj;
    }

    public ShapeObject AddShape(Scene scene, ShapeKind kind, PixelPoint position, PixelSize size, Layer? layer = null)
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
        EnsureLayer(scene, layer).Objects.Add(obj);
        return obj;
    }

    public DrawingObject AddDrawing(Scene scene, PixelSize size, Layer? layer = null)
    {
        EnsureCapacity(scene, 1);

        // Tamaño con aritmética checked: rechaza overflow y límites excesivos.
        // (1.md §A) `pixelCount`/`overflowed` definitivamente asignados (evita CS0165
        // de Stryker → Safe Mode). `checked` y throw por overflow se conservan.
        int pixelCount;
        bool overflowed = false;
        try
        {
            pixelCount = checked(size.Width * size.Height);
        }
        catch (OverflowException)
        {
            pixelCount = 0;
            overflowed = true;
        }

        if (overflowed)
            throw new ArgumentOutOfRangeException(nameof(size),
                $"El tamaño {size.Width}x{size.Height} del dibujo desborda el cálculo de píxeles.");

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
        EnsureLayer(scene, layer).Objects.Add(obj);
        return obj;
    }

    public void DeleteObjects(Scene scene, IEnumerable<ObjectId> ids)
    {
        var set = ids.ToHashSet();
        foreach (var layer in scene.Layers)
            layer.Objects.RemoveAll(o => set.Contains(o.Id));
        // limpia referencias de grupos a miembros borrados
        foreach (var g in scene.Groups)
            g.MemberIds.RemoveAll(set.Contains);
    }

    /// <summary>Duplica objetos generando IDs nuevos (spec: copia genera IDs nuevos).</summary>
    public List<SceneObject> DuplicateObjects(Scene scene, IEnumerable<SceneObject> objects, Layer? layer = null)
    {
        var src = objects.ToList();
        if (src.Count == 0)
            return new List<SceneObject>();

        // Comprueba el total (existente + nuevos) ANTES de insertar.
        EnsureCapacity(scene, src.Count);

        var created = new List<SceneObject>(src.Count);
        var target = EnsureLayer(scene, layer);
        foreach (var o in src)
        {
            var copy = CloneObject(o);
            copy.Id = ObjectId.New();
            copy.Name = o.Name + " (copia)";
            copy.Position = o.Position + new PixelPoint(1, 1);
            target.Objects.Add(copy);
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

    // ----- Group / Ungroup (spec 5 + 8) -----

    /// <summary>Crea un grupo con los objetos dados (≥2, IDs únicos y resolubles).</summary>
    public ObjectGroup GroupObjects(Scene scene, IEnumerable<SceneObject> objects, string? name = null)
    {
        var members = objects.ToList();
        if (members.Count < 2)
            throw new ArgumentException("Se requieren al menos 2 objetos para agrupar.", nameof(objects));

        var ids = members.Select(o => o.Id).ToList();
        if (ids.Distinct().Count() != ids.Count)
            throw new ArgumentException("El grupo contiene IDs duplicados.", nameof(objects));

        var resolvable = scene.AllObjects.Select(o => o.Id).ToHashSet();
        if (ids.Any(id => !resolvable.Contains(id)))
            throw new ArgumentException("El grupo referencia objetos que no existen en la escena.");

        // evita duplicados: un grupo con exactamente los mismos miembros ya existe
        var existing = scene.Groups.FirstOrDefault(g => g.MemberIds.OrderBy(x => x.Value).SequenceEqual(ids.OrderBy(x => x.Value)));
        if (existing != null) return existing;

        var group = new ObjectGroup { MemberIds = ids, Name = name ?? $"Grupo {scene.Groups.Count + 1}" };
        scene.Groups.Add(group);
        return group;
    }

    /// <summary>Elimina un grupo conservando sus objetos (framebuffer idéntico).</summary>
    public bool Ungroup(Scene scene, GroupId groupId)
        => scene.Groups.RemoveAll(g => g.Id == groupId) > 0;

    /// <summary>Mueve todos los miembros resolubles de un grupo como operación única.</summary>
    public void MoveGroup(Scene scene, ObjectGroup group, PixelPoint delta)
    {
        var byId = scene.AllObjects.ToDictionary(o => o.Id);
        foreach (var id in group.MemberIds)
            if (byId.TryGetValue(id, out var obj))
                obj.Position = obj.Position + delta;
    }

    // ----- Align (spec 8) -----

    /// <summary>
    /// Alinea la selección (≥1 objeto) en coordenadas LED según el bounding box común.
    /// No modifica tamaño, timing, animación ni capa.
    /// </summary>
    public void AlignObjects(IEnumerable<SceneObject> objects, Alignment alignment)
    {
        var list = objects.ToList();
        if (list.Count == 0) return;

        int left = list.Min(o => o.Position.X);
        int right = list.Max(o => o.Position.X + o.Size.Width);
        int top = list.Min(o => o.Position.Y);
        int bottom = list.Max(o => o.Position.Y + o.Size.Height);

        foreach (var o in list)
        {
            switch (alignment)
            {
                case Alignment.Left:
                    o.Position = new PixelPoint(left, o.Position.Y);
                    break;
                case Alignment.Right:
                    o.Position = new PixelPoint(right - o.Size.Width, o.Position.Y);
                    break;
                case Alignment.HorizontalCenter:
                    o.Position = new PixelPoint(left + (right - left - o.Size.Width) / 2, o.Position.Y);
                    break;
                case Alignment.Top:
                    o.Position = new PixelPoint(o.Position.X, top);
                    break;
                case Alignment.Bottom:
                    o.Position = new PixelPoint(o.Position.X, bottom - o.Size.Height);
                    break;
                case Alignment.VerticalMiddle:
                    o.Position = new PixelPoint(o.Position.X, top + (bottom - top - o.Size.Height) / 2);
                    break;
            }
        }
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
        bool overflowed = false;
        try
        {
            total = checked(existing + incoming);
        }
        catch (OverflowException)
        {
            total = 0;
            overflowed = true;
        }

        if (overflowed)
            throw new InvalidOperationException(
                $"El número de objetos de la escena desborda el máximo de {MaxObjectsPerScene}.");

        if (total > MaxObjectsPerScene)
            throw new InvalidOperationException(
                $"Se excede el máximo de {MaxObjectsPerScene} objetos por escena " +
                $"(existentes: {existing}, intentado añadir: {incoming}).");
    }

    /// <summary>
    /// Contrato de capa destino: si se indica <paramref name="layer"/> explícito se usa
    /// (si pertenece a la escena); si no, se usa la capa por defecto (primera ordenada).
    /// Evita la ambigüedad de "primera capa" cuando la UI tiene una capa activa distinta.
    /// </summary>
    private static Layer EnsureLayer(Scene scene, Layer? layer = null)
    {
        if (scene.Layers.Count == 0)
            scene.Layers.Add(new Layer { Name = "Capa 1", Order = 0 });

        if (layer != null)
        {
            var resolved = scene.Layers.FirstOrDefault(l => ReferenceEquals(l, layer) || l.Id == layer.Id);
            if (resolved != null) return resolved;
        }
        return scene.Layers.OrderBy(l => l.Order).First();
    }

    private static SceneObject CloneObject(SceneObject src)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(src, src.GetType(), Infrastructure.Persistence.AtlasJson.Options);
        var copy = (SceneObject)System.Text.Json.JsonSerializer.Deserialize(json, src.GetType(), Infrastructure.Persistence.AtlasJson.Options)!;
        return copy;
    }
}