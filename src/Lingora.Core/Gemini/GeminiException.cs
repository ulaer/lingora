using System.Text.Json;

namespace Lingora.Core.Gemini;

/// <summary>Kullanıcıya gösterilebilir (Türkçe) mesaj taşıyan Gemini hatası.</summary>
public sealed class GeminiException : Exception
{
    public GeminiException(string message, int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }

    /// <summary>HTTP hata yanıtını anlaşılır bir Türkçe mesaja çevirir.</summary>
    public static GeminiException FromHttpError(int statusCode, string? body, string model)
    {
        var (apiMessage, apiStatus) = TryReadError(body);
        var detail = string.IsNullOrWhiteSpace(apiMessage) ? string.Empty : $"\nAyrıntı: {apiMessage}";

        var message = statusCode switch
        {
            400 when IsInvalidKey(apiMessage, body) =>
                "API anahtarı geçersiz. Ayarlar > API Anahtarı bölümünden anahtarı kontrol edin.",
            400 => $"İstek reddedildi (400 {apiStatus}).",
            401 or 403 =>
                $"API anahtarının bu işleme yetkisi yok ({statusCode}). Anahtarı ve Google AI Studio projesini kontrol edin.",
            404 =>
                $"'{model}' modeli bulunamadı veya kullanımdan kaldırılmış. Ayarlar > Genel Ayarlar'dan model adını değiştirin (ör. gemini-flash-latest).",
            429 => "İstek/kota sınırı aşıldı (429). Biraz bekleyip tekrar deneyin.",
            >= 500 => $"Gemini sunucusunda geçici bir sorun var ({statusCode}). Biraz sonra tekrar deneyin.",
            _ => $"Gemini hatası ({statusCode} {apiStatus}).",
        };

        return new GeminiException(message + detail, statusCode);
    }

    private static bool IsInvalidKey(string? apiMessage, string? body) =>
        (apiMessage?.Contains("API key", StringComparison.OrdinalIgnoreCase) ?? false)
        || (body?.Contains("API_KEY_INVALID", StringComparison.Ordinal) ?? false);

    private static (string? Message, string? Status) TryReadError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object)
            {
                var message = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                var status = error.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                return (message, status);
            }
        }
        catch (JsonException)
        {
            // Gövde JSON değil; ayrıntısız mesajla devam.
        }

        return (null, null);
    }
}
