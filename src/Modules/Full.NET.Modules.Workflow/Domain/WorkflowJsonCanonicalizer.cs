using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

internal static class WorkflowJsonCanonicalizer
{
    public static WorkflowCompiledArtifact Compile(JsonElement value)
        => Compile(writer => WriteElement(writer, value));

    public static WorkflowCompiledArtifact Compile(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            write(writer);
        }

        var canonicalJson = Encoding.UTF8.GetString(buffer.WrittenSpan);
        var contentHash = Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant();
        return new WorkflowCompiledArtifact(canonicalJson, contentHash);
    }

    public static void WriteElement(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteElement(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                {
                    WriteElement(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
