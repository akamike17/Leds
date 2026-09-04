using DSLetreros.Domain.ValueObjects;

namespace DSLetreros.Domain.Entities;

/// <summary>Capa: contiene objetos, con orden, visibilidad y bloqueo.</summary>
public sealed class Layer
{
    public Layer()
    {
        Id = Layer.NewId();
        Name = string.Empty;
    }

    public string Id { get; set; }
    public string Name { get; set; }
    public int Order { get; set; }
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }

    /// <summary>Objetos que pertenecen a esta capa.</summary>
    public List<SceneObject> Objects { get; set; } = new();

    public static string NewId() => Guid.NewGuid().ToString("N");
}

/// <summary>Escena: una "intención" con duración, loop y capas (invariante 2).</summary>
public sealed class Scene
{
    public Scene()
    {
        Id = SceneId.New();
        Name = string.Empty;
        Duration = TimeSpan.FromSeconds(5);
        Layers = new List<Layer>();
        Groups = new List<ObjectGroup>();
    }

    public SceneId Id { get; set; }
    public string Name { get; set; }
    public TimeSpan Duration { get; set; }
    public SceneLoopMode LoopMode { get; set; } = SceneLoopMode.Loop;
    public List<Layer> Layers { get; set; }

    /// <summary>Grupos de objetos de la escena (organización sin contenido visual, invariante 7).</summary>
    public List<ObjectGroup> Groups { get; set; }

    /// <summary>Todos los objetos de la escena, en orden de capa.</summary>
    public IEnumerable<SceneObject> AllObjects =>
        Layers.OrderBy(l => l.Order).SelectMany(l => l.Objects);

    public uint ObjectCount() => (uint)Layers.Sum(l => l.Objects.Count);
}

public enum SceneLoopMode { Once, Loop, PingPong }

/// <summary>Proyecto: raíz del documento. No contiene GPIO/RGB order/buses/timings (invariante 3).</summary>
public sealed class Project
{
    public Project()
    {
        Id = ProjectId.New();
        Name = string.Empty;
        FormatVersion = 1;
        Canvas = new CanvasDefinition(32, 16);
        Scenes = new List<Scene>();
        EmbeddedAssets = new Dictionary<string, string>();
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public ProjectId Id { get; set; }
    public string Name { get; set; }
    public int FormatVersion { get; set; }
    public CanvasDefinition Canvas { get; set; }
    public List<Scene> Scenes { get; set; }

    /// <summary>Assets usados, embebidos: assetId -> contenido (invariante 8).</summary>
    public Dictionary<string, string> EmbeddedAssets { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Scene? FindScene(SceneId id) => Scenes.FirstOrDefault(s => s.Id == id);
}