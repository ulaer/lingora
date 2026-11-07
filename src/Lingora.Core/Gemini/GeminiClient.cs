using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Lingora.Core.Journal;
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
public sealed class GeminiClient : ITranslationEvaluator, IStudyAssistant
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

        var responseBody = await GenerateAsync(PromptBuilder.BuildRequestBody(item, userTranslation), apiKey, model, cancellationToken)
            .ConfigureAwait(false);
        return ParseEvaluation(responseBody);
    }

    /// <summary>Hata veya ipucu satırının ayrıntılı açıklaması (düz metin).</summary>
    public async Task<string> ExplainAsync(ExplainRequest request, string apiKey, string model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var responseBody = await GenerateAsync(StudyPrompts.BuildExplainBody(request), apiKey, model, cancellationToken)
            .ConfigureAwait(false);
        return ParseExplanation(responseBody);
    }

    /// <summary>Bir günün kayıtlarından ders notu.</summary>
    public async Task<LessonNote> CreateLessonAsync(
        DateOnly date,
        IReadOnlyList<JournalEntry> entries,
        string apiKey,
        string model,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var responseBody = await GenerateAsync(StudyPrompts.BuildLessonBody(date, entries), apiKey, model, cancellationToken)
            .ConfigureAwait(false);
        var note = ParseLesson(responseBody);
        note.Date = date;
        note.EntryCount = entries.Count;
        note.Model = AppSettings.NormalizeModelName(model);
        return note;
    }

    private async Task<string> GenerateAsync(System.Text.Json.Nodes.JsonObject requestBody, string apiKey, string model, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new GeminiException("API anahtarı girilmemiş. Ayarlar > API Anahtarı bölümünden ekleyin.");
        }

        var modelName = AppSettings.NormalizeModelName(model);
        var url = $"{_baseUrl}/models/{Uri.EscapeDataString(modelName)}:generateContent";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(requestBody.ToJsonString(BodyOptions), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(ApiKeyHeader, apiKey.Trim());
        return await SendAsync(request, modelName, cancellationToken).ConfigureAwait(false);
    }

    internal static string ParseExplanation(string responseBody) => StripCodeFence(ExtractResponseText(responseBody));

    internal static LessonNote ParseLesson(string responseBody)
    {
        var json = StripCodeFence(ExtractResponseText(responseBody));
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new GeminiException("Gemini ders notunu beklenen biçimde döndürmedi. Tekrar deneyin.");
            }

            var note = new LessonNote
            {
                Title = ReadString(root, "title"),
                Summary = ReadString(root, "summary"),
                Practice = ReadStringArray(root, "practice").ToList(),
            };

            foreach (var topic in ReadObjects(root, "topics"))
            {
                var title = ReadString(topic, "title");
                var explanation = ReadString(topic, "explanation");
                if (title.Length > 0 || explanation.Length > 0)
                {
                    note.Topics.Add(new LessonTopic { Title = title, Explanation = explanation, Examples = ReadStringArray(topic, "examples").ToList() });
                }
            }

            foreach (var mistake in ReadObjects(root, "mistakes"))
            {
                var wrong = ReadString(mistake, "wrong");
                var right = ReadString(mistake, "right");
                if (wrong.Length > 0 || right.Length > 0)
                {
                    note.Mistakes.Add(new LessonMistake { Wrong = wrong, Right = right, Why = ReadString(mistake, "why") });
                }
            }

            if (note.Title.Length == 0 && note.Topics.Count == 0)
            {
                throw new GeminiException("Gemini boş bir ders notu döndürdü. Tekrar deneyin.");
            }

            return note;
        }
        catch (JsonException ex)
        {
            throw new GeminiException("Gemini ders notu okunamadı (geçersiz JSON). Tekrar deneyin.", inner: ex);
        }
    }

    private static IEnumerable<JsonElement> ReadObjects(JsonElement root, string name) =>
        root.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToArray()
            : Array.Empty<JsonElement>();

    /// <summary>generateContent yanıtından modelin metnini çıkarır; boş/engelli yanıtta anlaşılır hata verir.</summary>
    internal static string ExtractResponseText(string responseBody)
    {
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
            var text = ExtractText(candidate);
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new GeminiException($"Gemini metin döndürmedi (bitiş nedeni: {finishReason ?? "bilinmiyor"}). Tekrar deneyin.");
            }

            return text;
        }
        catch (JsonException ex)
        {
            throw new GeminiException("Gemini yanıtı okunamadı (geçersiz JSON).", inner: ex);
        }
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
    internal static Evaluation ParseEvaluation(string responseBody) => ParseEvaluationJson(ExtractResponseText(responseBody));

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
