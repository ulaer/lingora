using System.Net;
using System.Text.Json;
using Lingora.Core;
using Lingora.Core.Gemini;
using Lingora.Core.Journal;
using Lingora.Core.Models;
using Lingora.Core.Storage;

namespace Lingora.Tests;

public class AudioLessonSessionTests
{
    private static PracticeSession Create(TempDir dir, FakeEvaluator evaluator)
    {
        var session = new PracticeSession(
            new AppPaths(dir.Path),
            evaluator,
            new FixedTimeProvider(FixedTimeProvider.Local(2026, 11, 7, 20)),
            new ApiKeyStore(_ => null),
            new Random(1));
        if (!File.Exists(dir.File("corpus.tsv")))
        {
            session.ImportCorpus(dir.Write("tur.txt", Samples.TatoebaLines));
        }

        session.SetApiKey("AIzaTEST-audio-0000");
        return session;
    }

    private static FakeEvaluator Evaluator() => new()
    {
        Handler = (_, _) => new Evaluation
        {
            Score = 70,
            IsCorrect = false,
            CorrectedTranslation = "We have to set off early.",
            Mistakes = ["go out → set off: anlam"],
            Tip = "Yolculuk için set off.",
        },
    };

    private static async Task Translate(PracticeSession session, int count)
    {
        for (var i = 0; i < count; i++)
        {
            await session.SubmitAsync("we have to go out early");
            session.NextSentence();
        }
    }

    [Fact]
    public async Task AudioLessonIsWrittenAsTextSpokenSavedAsMp3AndThenServedFromDisk()
    {
        using var dir = new TempDir();
        var evaluator = Evaluator();
        var encoder = new FakeAudioEncoder();
        var session = Create(dir, evaluator);
        await Translate(session, 2);
        var steps = new List<string>();

        var first = await session.GetOrCreateAudioLessonAsync(session.Today, encoder, progress: new SyncProgress(steps));

        Assert.True(first.Success, first.Error);
        Assert.False(first.FromCache);
        Assert.True(File.Exists(first.AudioPath));
        Assert.EndsWith("2026-11-07.mp3", first.AudioPath);
        Assert.Contains("2 cümle", first.Lesson!.Script);
        Assert.Equal(2, first.Lesson.EntryCount);
        Assert.Equal(AppSettings.DefaultVoice, first.Lesson.Voice);
        Assert.Single(evaluator.AudioScriptCalls);
        var call = Assert.Single(evaluator.SpeechCalls);
        Assert.Equal("Sulafat", call.Voice);
        Assert.Equal(AppSettings.DefaultTtsModel, call.Model);
        Assert.Equal(["Ders anlatımı yazılıyor", "Seslendiriliyor", "MP3 kaydediliyor"], steps);

        // 0,25 sn ses + parça sonundaki 0,5 sn sessizlik.
        Assert.Equal(0.75, Assert.Single(encoder.Encoded).DurationSeconds, 3);
        Assert.Equal(0.8, first.Lesson.DurationSeconds, 1);
        Assert.Empty(Directory.GetFiles(dir.File("lessons"), "*.tmp-*"));
        Assert.True(session.Journal.Summarize(session.Today).HasAudio);

        var again = await session.GetOrCreateAudioLessonAsync(session.Today, encoder);
        Assert.True(again.FromCache);
        Assert.Single(evaluator.SpeechCalls);

        // Kapat-aç: dosyadan gelir.
        var reopenedEvaluator = new FakeEvaluator();
        var reopened = Create(dir, reopenedEvaluator);
        var loaded = await reopened.GetOrCreateAudioLessonAsync(reopened.Today, encoder);
        Assert.True(loaded.FromCache);
        Assert.Equal(first.Lesson.Script, loaded.Lesson!.Script);
        Assert.Empty(reopenedEvaluator.SpeechCalls);
    }

    [Fact]
    public async Task AudioAndLessonCanBeRegeneratedOnlyAfterNewSentences()
    {
        using var dir = new TempDir();
        var evaluator = Evaluator();
        var encoder = new FakeAudioEncoder();
        var session = Create(dir, evaluator);
        await Translate(session, 1);
        await session.GetOrCreateAudioLessonAsync(session.Today, encoder);
        await session.GetOrCreateLessonAsync(session.Today);

        // Yeni çeviri yokken yenileme istense de AI çağrılmaz.
        Assert.True((await session.GetOrCreateAudioLessonAsync(session.Today, encoder, regenerate: true)).FromCache);
        Assert.True((await session.GetOrCreateLessonAsync(session.Today, regenerate: true)).FromCache);
        Assert.Single(evaluator.AudioScriptCalls);
        Assert.Single(evaluator.LessonCalls);

        await Translate(session, 2);

        // Yenileme istenmezse eski kayıt gelir; istenirse 3 kayıtla yeniden üretilir.
        Assert.True((await session.GetOrCreateAudioLessonAsync(session.Today, encoder)).FromCache);
        var audio = await session.GetOrCreateAudioLessonAsync(session.Today, encoder, regenerate: true);
        var lesson = await session.GetOrCreateLessonAsync(session.Today, regenerate: true);

        Assert.False(audio.FromCache);
        Assert.Equal(3, audio.Lesson!.EntryCount);
        Assert.False(lesson.FromCache);
        Assert.Equal(3, lesson.Note!.EntryCount);
        Assert.Equal(2, evaluator.AudioScriptCalls.Count);
        Assert.Equal(2, evaluator.LessonCalls.Count);
        Assert.Equal(3, session.AudioLessons.Load(session.Today)!.EntryCount);
        Assert.Equal(3, session.Lessons.Load(session.Today)!.EntryCount);
    }

    [Fact]
    public async Task LongScriptIsSpokenInChunks()
    {
        using var dir = new TempDir();
        var evaluator = Evaluator();
        var paragraph = string.Join(' ', Enumerable.Repeat("Bu cümle sesli ders için yazılmış uzunca bir örnektir.", 12));
        evaluator.AudioScriptHandler = (_, _) => string.Join("\n\n", Enumerable.Repeat(paragraph, 5));
        var encoder = new FakeAudioEncoder();
        var session = Create(dir, evaluator);
        await Translate(session, 1);

        var outcome = await session.GetOrCreateAudioLessonAsync(session.Today, encoder);

        Assert.True(outcome.Success);
        Assert.True(evaluator.SpeechCalls.Count >= 3);
        Assert.All(evaluator.SpeechCalls, c => Assert.True(c.Text.Length <= SpeechChunker.MaxChunkLength));
        Assert.Equal(evaluator.SpeechCalls.Count * 0.75, encoder.Encoded[0].DurationSeconds, 3);
    }

    [Fact]
    public async Task FailedSpeechLeavesNothingBehind()
    {
        using var dir = new TempDir();
        var evaluator = Evaluator();
        evaluator.SpeechHandler = _ => throw new GeminiException("Model bulunamadı (404).");
        var session = Create(dir, evaluator);
        await Translate(session, 1);

        var outcome = await session.GetOrCreateAudioLessonAsync(session.Today, new FakeAudioEncoder());

        Assert.False(outcome.Success);
        Assert.Contains("404", outcome.Error);
        Assert.Null(session.AudioLessons.Load(session.Today));
        Assert.False(File.Exists(session.AudioLessons.AudioFile(session.Today)));
        Assert.False(File.Exists(session.AudioLessons.ScriptFile(session.Today)));
    }

    [Fact]
    public async Task EmptyDayCannotHaveAudioLesson()
    {
        using var dir = new TempDir();
        var evaluator = Evaluator();
        var session = Create(dir, evaluator);

        var outcome = await session.GetOrCreateAudioLessonAsync(session.Today, new FakeAudioEncoder());

        Assert.False(outcome.Success);
        Assert.Contains("kaydı yok", outcome.Error);
        Assert.Empty(evaluator.AudioScriptCalls);
    }

    [Fact]
    public void SpeechSettingsAreSavedAndNormalized()
    {
        using var dir = new TempDir();
        var session = Create(dir, Evaluator());
        session.UpdateSpeechSettings("Achird", "models/gemini-tts-x");
        var reopened = Create(dir, Evaluator());
        Assert.Equal("Achird", reopened.Settings.Voice);
        Assert.Equal("gemini-tts-x", reopened.Settings.TtsModel);

        reopened.UpdateSpeechSettings("  ", "");
        Assert.Equal(AppSettings.DefaultVoice, reopened.Settings.Voice);
        Assert.Equal(AppSettings.DefaultTtsModel, reopened.Settings.TtsModel);
    }

    /// <summary>Progress&lt;T&gt; geri çağrısını eşzamanlı çalıştırır (sıra kontrolü için).</summary>
    private sealed class SyncProgress(List<string> steps) : IProgress<string>
    {
        public void Report(string value) => steps.Add(value);
    }
}

public class SpeechChunkerTests
{
    [Fact]
    public void KeepsParagraphsTogetherUpToLimit()
    {
        var chunks = SpeechChunker.Split("Bir.\n\nİki.\r\n\r\nÜç.", 100);
        Assert.Equal("Bir.\n\nİki.\n\nÜç.", Assert.Single(chunks));
    }

    [Fact]
    public void SplitsLongParagraphAtSentenceEnds()
    {
        var text = string.Join(' ', Enumerable.Range(1, 30).Select(i => $"Bu {i}. cümledir."));
        var chunks = SpeechChunker.Split(text, 80);
        Assert.True(chunks.Count > 3);
        Assert.All(chunks, c => Assert.True(c.Length <= 80));
        Assert.All(chunks, c => Assert.EndsWith(".", c));
        Assert.Equal(text, string.Join(' ', chunks));
    }
}

public class SpeechGeminiTests
{
    private static (GeminiClient Client, FakeHttpHandler Handler) Create()
    {
        var handler = new FakeHttpHandler();
        return (new GeminiClient(new HttpClient(handler), "https://example.test/v1beta/"), handler);
    }

    [Fact]
    public async Task SynthesizeRequestsAudioWithVoiceAndDecodesPcm()
    {
        var (client, handler) = Create();
        var pcm = FakeEvaluator.Tone(0.1).Pcm;
        var response = "{\"candidates\":[{\"content\":{\"parts\":[{\"inlineData\":{\"mimeType\":\"audio/L16;codec=pcm;rate=24000\",\"data\":\""
                       + Convert.ToBase64String(pcm) + "\"}}]}}]}";
        handler.Respond(HttpStatusCode.OK, response);

        var audio = await client.SynthesizeAsync("Merhaba, bugün set off kalıbını öğrendik.", "Sulafat", "gemini-3.1-flash-tts-preview", "AIzaTEST-tts");

        Assert.Equal(24000, audio.SampleRate);
        Assert.Equal(pcm, audio.Pcm);
        var (request, body) = handler.Requests[0];
        Assert.Contains("/models/gemini-3.1-flash-tts-preview:generateContent", request.RequestUri!.ToString());
        Assert.DoesNotContain("AIzaTEST", request.RequestUri!.ToString());
        using var doc = JsonDocument.Parse(body!);
        var config = doc.RootElement.GetProperty("generationConfig");
        Assert.Equal("AUDIO", config.GetProperty("responseModalities")[0].GetString());
        Assert.Equal("Sulafat", config.GetProperty("speechConfig").GetProperty("voiceConfig").GetProperty("prebuiltVoiceConfig").GetProperty("voiceName").GetString());
        var text = doc.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!;
        Assert.StartsWith(StudyPrompts.SpeechStyle, text);
        Assert.EndsWith("set off kalıbını öğrendik.", text);
    }

    [Fact]
    public void SpeechWithoutAudioGivesTurkishError()
    {
        var ex = Assert.Throws<GeminiException>(() => GeminiClient.ParseSpeech(Samples.GeminiSuccess("metin")));
        Assert.Contains("ses döndürmedi", ex.Message);
    }

    [Fact]
    public async Task AudioScriptIsPlainTextWithoutMarkdown()
    {
        var (client, handler) = Create();
        handler.Respond(HttpStatusCode.OK, Samples.GeminiSuccess("## Merhaba\n\n**Bugün** iyi çalıştın.\n- \"We set off.\" yani yola çıktık."));
        var entries = new List<JournalEntry> { new() { Source = "S", UserTranslation = "U", Mistakes = ["go out → set off: anlam"] } };

        var script = await client.CreateAudioScriptAsync(new DateOnly(2026, 11, 7), entries, "AIzaTEST-script", "gemini-flash-latest");

        Assert.Equal("Merhaba\n\nBugün iyi çalıştın.\n\"We set off.\" yani yola çıktık.", script);
        using var doc = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.Equal("text/plain", doc.RootElement.GetProperty("generationConfig").GetProperty("responseMimeType").GetString());
        var system = doc.RootElement.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString()!;
        Assert.Contains("SESLİ DERS", system);
        var prompt = doc.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!;
        Assert.Contains("sesli ders anlatımını yaz", prompt);
        Assert.Contains("Hata: go out → set off", prompt);
    }
}
