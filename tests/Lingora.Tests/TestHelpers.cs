using System.Net;
using System.Text;
using Lingora.Core.Gemini;
using Lingora.Core.Models;

namespace Lingora.Tests;

/// <summary>Her test için silinen geçici klasör (gerçek %APPDATA% verisine dokunulmaz).</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LingoraTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public string Write(string name, string content, Encoding? encoding = null)
    {
        var path = File(name);
        System.IO.File.WriteAllText(path, content, encoding ?? new UTF8Encoding(false));
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Windows'ta dosya kilidi kalırsa geçici klasör sonra temizlenir.
        }
    }
}

/// <summary>Sabit saatli TimeProvider (İstanbul, UTC+3).</summary>
internal sealed class FixedTimeProvider : TimeProvider
{
    public static readonly TimeZoneInfo Istanbul =
        TimeZoneInfo.CreateCustomTimeZone("Test/Istanbul", TimeSpan.FromHours(3), "Istanbul", "Istanbul");

    public FixedTimeProvider(DateTimeOffset now) => Now = now;

    public DateTimeOffset Now { get; set; }

    public override TimeZoneInfo LocalTimeZone => Istanbul;

    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();

    /// <summary>İstanbul yerel saatiyle zaman oluşturur.</summary>
    public static DateTimeOffset Local(int year, int month, int day, int hour = 12, int minute = 0) =>
        new(year, month, day, hour, minute, 0, TimeSpan.FromHours(3));
}

/// <summary>İstekleri kaydeden, hazır yanıt döndüren sahte HTTP işleyici.</summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = new();

    public FakeHttpHandler Respond(HttpStatusCode status, string body)
    {
        _responses.Enqueue(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
        return this;
    }

    public FakeHttpHandler Throw(Exception exception)
    {
        _responses.Enqueue(_ => throw exception);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("Beklenmeyen HTTP isteği: " + request.RequestUri);
        }

        return _responses.Dequeue()(request);
    }
}

/// <summary>Sahte değerlendirici: Gemini çağırmadan sonuç, açıklama ve ders notu döndürür.</summary>
internal sealed class FakeEvaluator : ITranslationEvaluator, IStudyAssistant
{
    public Func<ExplainRequest, string> ExplainHandler { get; set; } =
        r => $"Ayrıntılı açıklama: {r.Item}\n• Example sentence. (Örnek cümle.)";

    public Func<DateOnly, IReadOnlyList<Lingora.Core.Journal.JournalEntry>, Lingora.Core.Journal.LessonNote> LessonHandler { get; set; } =
        (date, entries) => new Lingora.Core.Journal.LessonNote
        {
            Title = "Dolaylı anlatım ve zaman uyumu",
            Summary = "Anlamı genelde doğru aktardın; dolaylı anlatımda zamanı geri kaydırmayı çalışmalısın.",
            Topics =
            [
                new() { Title = "Reported speech", Explanation = "Dolaylı anlatımda fiil bir zaman geri gider.", Examples = ["He said he was tired. (Yorgun olduğunu söyledi.)"] },
            ],
            Mistakes = [new() { Wrong = "must", Right = "was supposed to", Why = "Geçmişte beklenen bir görev." }],
            Practice = ["Yorgun olduğunu söyledi."],
        };

    public List<ExplainRequest> ExplainCalls { get; } = new();

    public List<(DateOnly Date, int Entries)> LessonCalls { get; } = new();

    public Task<string> ExplainAsync(ExplainRequest request, string apiKey, string model, CancellationToken cancellationToken = default)
    {
        ExplainCalls.Add(request);
        return Task.FromResult(ExplainHandler(request));
    }

    public Task<Lingora.Core.Journal.LessonNote> CreateLessonAsync(DateOnly date, IReadOnlyList<Lingora.Core.Journal.JournalEntry> entries, string apiKey, string model, CancellationToken cancellationToken = default)
    {
        LessonCalls.Add((date, entries.Count));
        return Task.FromResult(LessonHandler(date, entries));
    }

    public Func<PracticeItem, string, Evaluation> Handler { get; set; } =
        (_, _) => new Evaluation { Score = 90, IsCorrect = true, CorrectedTranslation = "Hello.", NaturalTranslation = "Hello." };

    public List<(PracticeItem Item, string Text, string ApiKey, string Model)> Calls { get; } = new();

    /// <summary>Ayarlanırsa cevap bu görev tamamlanana kadar bekletilir (bekleme ekranını test etmek için).</summary>
    public Task? Gate { get; set; }

    public async Task<Evaluation> EvaluateAsync(PracticeItem item, string userTranslation, string apiKey, string model, CancellationToken cancellationToken = default)
    {
        Calls.Add((item, userTranslation, apiKey, model));
        if (Gate is not null)
        {
            await Gate;
        }

        return Handler(item, userTranslation);
    }
}

internal static class Samples
{
    /// <summary>Kullanıcının verdiği örnek satırlar (Tatoeba, CC-BY 2.0 FR).</summary>
    public const string TatoebaLines =
        "Hi.\tSelam.\tCC-BY 2.0 (France) Attribution: tatoeba.org #538123 (CM) & #483497 (duran)\n"
        + "Hi.\tMerhaba.\tCC-BY 2.0 (France) Attribution: tatoeba.org #538123 (CM) & #9019450 (soliloquist)\n"
        + "Run!\tKaç!\tCC-BY 2.0 (France) Attribution: tatoeba.org #906328 (papabear) & #2322529 (Gulo_Luscus)\n"
        + "Run!\tKoş!\tCC-BY 2.0 (France) Attribution: tatoeba.org #906328 (papabear) & #2322530 (Gulo_Luscus)\n"
        + "Run.\tKaç!\tCC-BY 2.0 (France) Attribution: tatoeba.org #4008918 (JSakuragi) & #2322529 (Gulo_Luscus)\n"
        + "Run.\tKoş!\tCC-BY 2.0 (France) Attribution: tatoeba.org #4008918 (JSakuragi) & #2322530 (Gulo_Luscus)\n"
        + "Tom said he didn't have any idea what Mary was doing.\tTom, Mary'nin ne yaptığına dair hiçbir fikri olmadığını söyledi.\tCC-BY 2.0 (France) Attribution: tatoeba.org #6353039 (CK) & #6396932 (duran)\n"
        + "Tom said he didn't know that Mary could speak French.\tTom, Mary'nin Fransızca konuşabileceğini bilmediğini söyledi.\tCC-BY 2.0 (France) Attribution: tatoeba.org #6654854 (CK) & #6943512 (duran)\n"
        + "Tom said he didn't know what Mary was supposed to do.\tTom, Mary'nin ne yapması gerektiğini bilmediğini söyledi.\tCC-BY 2.0 (France) Attribution: tatoeba.org #6526489 (CK) & #6598065 (duran)\n"
        + "Tom said he didn't know where Mary wanted to do that.\tTom, Mary'nin bunu nerede yapmak istediğini bilmediğini söyledi.\tCC-BY 2.0 (France) Attribution: tatoeba.org #6526488 (CK) & #6598066 (duran)\n"
        + "Tom said he didn't really know how to surf very well.\tTom gerçekten çok iyi sörf yapmayı bilmediğini söyledi.\tCC-BY 2.0 (France) Attribution: tatoeba.org #6526487 (CK) & #6598067 (duran)\n";

    public static string GeminiSuccess(string innerJson) =>
        "{\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":"
        + System.Text.Json.JsonSerializer.Serialize(innerJson)
        + "}]},\"finishReason\":\"STOP\"}]}";

    public const string EvaluationJson =
        "{\"score\":85,\"isCorrect\":true,\"correctedTranslation\":\"Tom said he didn't know.\",\"naturalTranslation\":\"Tom said he had no idea.\",\"mistakes\":[\"did not know → didn't know: kısaltma daha doğal\"],\"tip\":\"Dolaylı anlatımda zaman bir geri kayar.\"}";
}
