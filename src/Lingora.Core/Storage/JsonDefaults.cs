using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lingora.Core.Storage;

internal static class JsonDefaults
{
    /// <summary>Ayar dosyası için okunabilir JSON (Türkçe karakterler kaçışsız).</summary>
    public static readonly JsonSerializerOptions Indented = Create(indented: true);

    /// <summary>JSON Lines kayıtları için tek satırlık JSON.</summary>
    public static readonly JsonSerializerOptions Compact = Create(indented: false);

    private static JsonSerializerOptions Create(bool indented) => new(JsonSerializerDefaults.Web)
    {
        WriteIndented = indented,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };
}
