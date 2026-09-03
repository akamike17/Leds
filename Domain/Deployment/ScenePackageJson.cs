using System.Text.Json;
using System.Text.Json.Serialization;
using DSLetreros.Domain.ValueObjects;

namespace DSLetreros.Domain.Deployment;

/// <summary>
/// Serialización JSON del ScenePackage para el cable (upload). Vive en Domain para no
/// crear dependencia de Infrastructure. Cubre los tipos que el paquete contiene:
/// SceneId, CanvasDefinition y TimeSpan (loop/duration ya son primitivos), y deja que
/// byte[] se serialice como base64 (comportamiento estándar de System.Text.Json).
/// </summary>
public static class ScenePackageJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new SceneIdJsonConverter(),
            new CanvasDefinitionJsonConverter(),
        }
    };
}

internal sealed class SceneIdJsonConverter : JsonConverter<SceneId>
{
    public override SceneId Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
        => SceneId.Create(Guid.Parse(r.GetString()!));
    public override void Write(Utf8JsonWriter w, SceneId v, JsonSerializerOptions o)
        => w.WriteStringValue(v.Value.ToString("N"));
}

internal sealed class CanvasDefinitionJsonConverter : JsonConverter<CanvasDefinition>
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