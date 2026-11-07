using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Lingora.Core.Models;

namespace Lingora.Core.Gemini;

/// <summary>Çeviri değerlendiren servis (testlerde sahtesi kullanılır).</summary>
public interface ITranslationEvaluator
{
    Task<Evaluation> EvaluateAsync(
        PracticeItem item,
        string userTranslation,
        string apiKey,
        string model,
        CancellationToken cancellationToken = default);
}

/// <summary>Google AI Studio (Generative Language API) istemcisi.</summary>
public sealed class GeminiClient : ITranslationEvaluator
{
    public const string DefaultBaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    private const string ApiKeyHeader = "x-goog-api-key";

    // Türkçe karakterler \u kaçışına çevrilmeden gönderilir (daha küçük ve okunur gövde).
    private static readonly JsonSerializerOptions BodyOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public GeminiClient(HttpClient http, string? baseUrl = null)
    {
        _http = http;
        _baseUrl = (baseUrl ?? DefaultBaseUrl).TrimEnd('/');
    }

    public static HttpClient CreateHttpClient() => new() { Timeout = TimeSpan.FromSeconds(90) };

    public async Task<Evaluation> EvaluateAsync(
        PracticeItem item,
        string userTranslation,
        string apiKey,
        string model,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new GeminiException("API anahtarı girilmemiş. Ayarlar > API Anahtarı bölümünden ekleyin.");
        }

        var modelName = AppSettings.NormalizeModelName(model);
        var url = $"{_baseUrl}/models/{Uri.EscapeDataString(modelName)}:generateContent";
        var body = PromptBuilder.BuildRequestBody(item, userTranslation).ToJsonString(BodyOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(ApiKeyHeader, apiKey.Trim());

        var responseBody = await SendAsync(request, modelName, cancellationToken).ConfigureAwait(false);
        return ParseEvaluation(responseBody);
    }

    /// <summary>Anahtarı doğrular ve generateContent destekleyen modelleri listeler.</summary>
    public async Task<IReadOnlyList<string>> ListModelsAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new GeminiException("API anahtarı girilmemiş.");
        }

        var models = new List<string>();
        string? pageToken = null;
        do
        {
            var url = $"{_baseUrl}/models?pageSize=1000" + (pageToken is null ? string.Empty : $"&pageToken={Uri.EscapeDataString(pageToken)}");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add(ApiKeyHeader, apiKey.Trim());
            var body = await SendAsync(request, "(model listesi)", cancellationToken).ConfigureAwait(false);
            pageToken = ParseModelPage(body, models);
        }
        while (!string.IsNullOrEmpty(pageToken));

        return models;
    }

    internal static string? ParseModelPage(string body, List<string> models)
    {
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("models", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var model in list.EnumerateArray())
            {
                if (!model.TryGetProperty("name", out var nameElement) || nameElement.GetString() is not { } name)
                {
                    continue;
                }

                var supportsGenerate = model.TryGetProperty("supportedGenerationMethods", out var methods)
                    && methods.ValueKind == JsonValueKind.Array
                    && methods.EnumerateArray().Any(m => m.GetString() == "generateContent");
                if (supportsGenerate)
                {
                    models.Add(AppSettings.NormalizeModelName(name));
                }
            }
        }

        return doc.RootElement.TryGetProperty("nextPageToken", out var token) ? token.GetString() : null;
    }

    /// <summary>generateContent yanıtından değerlendirmeyi çıkarır.</summary>
    internal static Evaluation ParseEvaluation(string responseBody)
    {
        string text;
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("promptFeedback", out var feedback)
                && feedback.TryGetProperty("blockReason", out var blockReason))
            {
                throw new GeminiException($"Gemini isteği engelledi ({blockReason.GetString()}).");
            }

            if (!root.TryGetProperty("candidates", out var candidates)
                || candidates.ValueKind != JsonValueKind.Array
                || candidates.GetArrayLength() == 0)
            {
                throw new GeminiException("Gemini boş yanıt döndürdü. Tekrar deneyin.");
            }

            var candidate = candidates[0];
            var finishReason = candidate.TryGetProperty("finishReason", out var fr) ? fr.GetString() : null;
            text = ExtractText(candidate);

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new GeminiException($"Gemini metin döndürmedi (bitiş nedeni: {finishReason ?? "bilinmiyor"}). Tekrar deneyin.");
            }
        }
        catch (JsonException ex)
        {
            throw new GeminiException("Gemini yanıtı okunamadı (geçersiz JSON).", inner: ex);
        }

        return ParseEvaluationJson(text);
    }

    /// <summary>Modelin döndürdüğü JSON metnini değerlendirmeye çevirir; olmazsa ham metni taşır.</summary>
    internal static Evaluation ParseEvaluationJson(string text)
    {
        var json = StripCodeFence(text);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new Evaluation { RawText = text.Trim() };
            }

            return new Evaluation
            {
                Score = ReadScore(root),
                IsCorrect = root.TryGetProperty("isCorrect", out var ok) && ok.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? ok.GetBoolean()
                    : null,
                CorrectedTranslation = ReadString(root, "correctedTranslation"),
                NaturalTranslation = ReadString(root, "naturalTranslation"),
                Mistakes = ReadStringArray(root, "mistakes"),
                MinorIssues = ReadStringArray(root, "minorIssues"),
                Tip = ReadString(root, "tip"),
                Vocabulary = ReadVocabulary(root),
            };
        }
        catch (JsonException)
        {
            return new Evaluation { RawText = text.Trim() };
        }
    }

    private async Task<string> SendAsync(HttpRequestMessage request, string model, CancellationToken cancellationToken)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GeminiException("Gemini zamanında yanıt vermedi. İnternet bağlantınızı kontrol edip tekrar deneyin.", inner: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new GeminiException($"Gemini'ye bağlanılamadı: {ex.Message}", inner: ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw GeminiException.FromHttpError((int)response.StatusCode, body, model);
            }

            return body;
        }
    }

    private static string ExtractText(JsonElement candidate)
    {
        if (!candidate.TryGetProperty("content", out var content)
            || !content.TryGetProperty("parts", out var parts)
            || parts.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            // Düşünme (thinking) parçaları kullanıcıya gösterilmez.
            if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            if (part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
            {
                builder.Append(t.GetString());
            }
        }

        return builder.ToString();
    }

    private static string StripCodeFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewLine = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewLine >= 0 && lastFence > firstNewLine
            ? trimmed[(firstNewLine + 1)..lastFence].Trim()
            : trimmed.Trim('`').Trim();
    }

    private static int? ReadScore(JsonElement root)
    {
        if (!root.TryGetProperty("score", out var score))
        {
            return null;
        }

        double? value = score.ValueKind switch
        {
            JsonValueKind.Number => score.GetDouble(),
            JsonValueKind.String when double.TryParse(score.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };

        return value is null ? null : (int)Math.Clamp(Math.Round(value.Value), 0, 100);
    }

    private static string ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? (value.GetString() ?? string.Empty).Trim()
            : string.Empty;

    private static IReadOnlyList<VocabularyItem> ReadVocabulary(JsonElement root)
    {
        if (!root.TryGetProperty("vocabulary", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<VocabularyItem>();
        }

        var items = new List<VocabularyItem>();
        foreach (var element in list.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var term = ReadString(element, "term");
            var meaning = ReadString(element, "meaning");
            if (term.Length == 0 || meaning.Length == 0)
            {
                continue;
            }

            var kind = ReadString(element, "type").ToLowerInvariant() switch
            {
                "kalıp" or "kalip" or "phrase" or "pattern" => "kalıp",
                "tamlama" or "collocation" => "tamlama",
                _ => "kelime",
            };
            items.Add(new VocabularyItem(term, meaning, ReadString(element, "example"), kind));
        }

        return items;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return value.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => (e.GetString() ?? string.Empty).Trim())
            .Where(s => s.Length > 0)
            .ToArray();
    }
}
