namespace DSLetreros.Domain.ValueObjects;

/// <summary>Identificador único de un tipo de entidad. Envoltorio sobre Guid.</summary>
public abstract record Id<T>(Guid Value) where T : Id<T>
{
    public static T New() => Create(Guid.NewGuid());
    public static T Create(Guid value) => (T)Activator.CreateInstance(typeof(T), value)!;

    public override string ToString() => Value.ToString("N");
}

public sealed record ProjectId : Id<ProjectId>
{
    public ProjectId(Guid value) : base(value) { }
}

public sealed record SceneId : Id<SceneId>
{
    public SceneId(Guid value) : base(value) { }
}

public sealed record ObjectId : Id<ObjectId>
{
    public ObjectId(Guid value) : base(value) { }
}

public sealed record AssetId : Id<AssetId>
{
    public AssetId(Guid value) : base(value) { }
}

public sealed record DeviceId : Id<DeviceId>
{
    public DeviceId(Guid value) : base(value) { }
}