using System.Text.Json;
using System.Text.Json.Serialization;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;

namespace DSLetreros.Infrastructure.Persistence;

/// <summary>Converters JSON para value objects y entidades con propiedades tipadas.</summary>
public static class AtlasJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new TimeSpanConverter(),
            new PixelPointConverter(),
            new PixelSizeConverter(),
            new PixelRectConverter(),
            new RgbColorConverter(),
            new ProjectIdConverter(),
            new SceneIdConverter(),
            new ObjectIdConverter(),
            new AssetIdConverter(),
            new DeviceIdConverter(),
            new CanvasDefinitionConverter(),
            new SceneObjectPolymorphicConverter(),
        }
    };
}

internal sealed class TimeSpanConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) =>
        TimeSpan.FromMilliseconds(r.GetDouble());
    public override void Write(Utf8JsonWriter w, TimeSpan v, JsonSerializerOptions o) =>
        w.WriteNumberValue(v.TotalMilliseconds);
}

internal sealed class PixelPointConverter : JsonConverter<PixelPoint>
{
    public override PixelPoint Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
    {
        using var doc = JsonDocument.ParseValue(ref r);
        var x = doc.RootElement.GetProperty("x").GetInt32();
        var y = doc.RootElement.GetProperty("y").GetInt32();
        return new PixelPoint(x, y);
    }
    public override void Write(Utf8JsonWriter w, PixelPoint v, JsonSerializerOptions o)
    {
        w.WriteStartObject(); w.WriteNumber("x", v.X); w.WriteNumber("y", v.Y); w.WriteEndObject();
    }
}

internal sealed class PixelSizeConverter : JsonConverter<PixelSize>
{
    public override PixelSize Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
    {
        using var doc = JsonDocument.ParseValue(ref r);
        return new PixelSize(doc.RootElement.GetProperty("width").GetInt32(),
                             doc.RootElement.GetProperty("height").GetInt32());
    }
    public override void Write(Utf8JsonWriter w, PixelSize v, JsonSerializerOptions o)
    {
        w.WriteStartObject(); w.WriteNumber("width", v.Width); w.WriteNumber("height", v.Height); w.WriteEndObject();
    }
}

internal sealed class PixelRectConverter : JsonConverter<PixelRect>
{
    public override PixelRect Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
    {
        using var doc = JsonDocument.ParseValue(ref r);
        var o1 = doc.RootElement.GetProperty("origin");
        var s = doc.RootElement.GetProperty("size");
        return new PixelRect(
            new PixelPoint(o1.GetProperty("x").GetInt32(), o1.GetProperty("y").GetInt32()),
            new PixelSize(s.GetProperty("width").GetInt32(), s.GetProperty("height").GetInt32()));
    }
    public override void Write(Utf8JsonWriter w, PixelRect v, JsonSerializerOptions o)
    {
        w.WriteStartObject();
        w.WritePropertyName("origin"); w.WriteStartObject(); w.WriteNumber("x", v.Origin.X); w.WriteNumber("y", v.Origin.Y); w.WriteEndObject();
        w.WritePropertyName("size"); w.WriteStartObject(); w.WriteNumber("width", v.Size.Width); w.WriteNumber("height", v.Size.Height); w.WriteEndObject();
        w.WriteEndObject();
    }
}

internal sealed class RgbColorConverter : JsonConverter<RgbColor>
{
    public override RgbColor Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
    {
        using var doc = JsonDocument.ParseValue(ref r);
        return new RgbColor(doc.RootElement.GetProperty("r").GetByte(),
                            doc.RootElement.GetProperty("g").GetByte(),
                            doc.RootElement.GetProperty("b").GetByte());
    }
    public override void Write(Utf8JsonWriter w, RgbColor v, JsonSerializerOptions o)
    {
        w.WriteStartObject(); w.WriteNumber("r", v.R); w.WriteNumber("g", v.G); w.WriteNumber("b", v.B); w.WriteEndObject();
    }
}

internal sealed class CanvasDefinitionConverter : JsonConverter<CanvasDefinition>
{
    public override CanvasDefinition Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
    {
        using var doc = JsonDocument.ParseValue(ref r);
        return new CanvasDefinition(doc.RootElement.GetProperty("width").GetInt32(),
                                    doc.RootElement.GetProperty("height").GetInt32());
    }
    public override void Write(Utf8JsonWriter w, CanvasDefinition v, JsonSerializerOptions o)
    {
        w.WriteStartObject(); w.WriteNumber("width", v.Width); w.WriteNumber("height", v.Height); w.WriteEndObject();
    }
}

internal abstract class IdConverterBase<T, TId> : JsonConverter<T>
    where T : Id<TId>
    where TId : Id<TId>
{
    private static T FromGuid(Guid g) => (T)Activator.CreateInstance(typeof(T), g)!;
    public override T Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
    {
        var s = r.GetString() ?? throw new JsonException("ID nulo");
        return FromGuid(Guid.Parse(s));
    }
    public override void Write(Utf8JsonWriter w, T v, JsonSerializerOptions o) =>
        w.WriteStringValue(v.Value.ToString("N"));
}

internal sealed class ProjectIdConverter : IdConverterBase<ProjectId, ProjectId> { }
internal sealed class SceneIdConverter : IdConverterBase<SceneId, SceneId> { }
internal sealed class ObjectIdConverter : IdConverterBase<ObjectId, ObjectId> { }
internal sealed class AssetIdConverter : IdConverterBase<AssetId, AssetId> { }
internal sealed class DeviceIdConverter : IdConverterBase<DeviceId, DeviceId> { }

/// <summary>Serializa SceneObject como polimórfico vía discriminador "kind".</summary>
internal sealed class SceneObjectPolymorphicConverter : JsonConverter<SceneObject>
{
    public override SceneObject? Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
    {
        using var doc = JsonDocument.ParseValue(ref r);
        var root = doc.RootElement;
        var kind = root.GetProperty("kind").GetString();
        var type = kind switch
        {
            "text" => typeof(TextObject),
            "icon" => typeof(IconObject),
            "drawing" => typeof(DrawingObject),
            "shape" => typeof(ShapeObject),
            "image" => typeof(ImageObject),
            _ => throw new JsonException($"Tipo de objeto desconocido: {kind}")
        };
        var raw = root.GetRawText();
        return (SceneObject?)JsonSerializer.Deserialize(raw, type, o);
    }

    public override void Write(Utf8JsonWriter w, SceneObject v, JsonSerializerOptions o)
    {
        var raw = JsonSerializer.Serialize(v, v.GetType(), o);
        using var doc = JsonDocument.Parse(raw);
        w.WriteStartObject();
        foreach (var prop in doc.RootElement.EnumerateObject())
            prop.WriteTo(w);
        w.WriteEndObject();
    }
}