using System.Net;
using System.Text.Json;
using Lingora.Core;
using Lingora.Core.Gemini;
using Lingora.Core.Models;

namespace Lingora.Tests;

public class PromptBuilderTests
{
    private static readonly PracticeItem TrEn = new(
        TranslationDirection.TurkishToEnglish, "Kaç!", ["Run!", "Run."], 2);

    [Fact]
    public void UserPromptContainsDirectionSourceReferencesAndTranslation()
    {
        var prompt = PromptBuilder.BuildUserPrompt(TrEn, "  Escape!  ");

        Assert.Contains("Çeviri yönü: Türkçe → İngilizce", prompt);
        Assert.Contains("Kaynak cümle (Türkçe): Kaç!", prompt);
        Assert.Contains("- Run!", prompt);
        Assert.Contains("- Run.", prompt);
        Assert.Contains("<<<\nEscape!\n>>>", prompt.Replace("\r\n", "\n"));
    }

    [Fact]
    public void EnglishToTurkishPromptUsesCorrectLanguages()
    {
        var item = new PracticeItem(TranslationDirection.EnglishToTurkish, "Hi.", ["Selam.", "Merhaba."], 0);

        var prompt = PromptBuilder.BuildUserPrompt(item, "Merhaba");

        Assert.Contains("İngilizce → Türkçe", prompt);
        Assert.Contains("Kaynak cümle (İngilizce): Hi.", prompt);
        Assert.Contains("Öğrencinin Türkçe çevirisi", prompt);
    }

    [Fact]
    public void RequestBodyAsksForStructuredJson()
    {
        var json = PromptBuilder.BuildRequestBody(TrEn, "Run!").ToJsonString();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Contains("çeviri öğretmeni", root.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal("user", root.GetProperty("contents")[0].GetProperty("role").GetString());
        var config = root.GetProperty("generationConfig");
        Assert.Equal("application/json", config.GetProperty("responseMimeType").GetString());
        var required = config.GetProperty("responseSchema").GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(PromptBuilder.ResponseFields, required);
    }
}

public class GeminiClientTests
{
    private const string FakeKey = "TEST-KEY-not-a-real-key";
    private static readonly PracticeItem Item = new(
        TranslationDirection.EnglishToTurkish, "Tom said he didn't know.", ["Tom bilmediğini söyledi."], 0);

    private static (GeminiClient Client, FakeHttpHandler Handler) Create()
    {
        var handler = new FakeHttpHandler();
        return (new GeminiClient(new HttpClient(handler), "https://example.test/v1beta/"), handler);
    }

    [Fact]
    public async Task SendsPostWithKeyInHeaderNotInUrl()
    {
        var (client, handler) = Create();
        handler.Respond(HttpStatusCode.OK, Samples.GeminiSuccess(Samples.EvaluationJson));

        await client.EvaluateAsync(Item, "Tom bilmediğini söyledi.", FakeKey, "models/gemini-flash-latest");

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://example.test/v1beta/models/gemini-flash-latest:generateContent", request.RequestUri!.ToString());
        Assert.Equal(FakeKey, request.Headers.GetValues("x-goog-api-key").Single());
        Assert.DoesNotContain(FakeKey, request.RequestUri!.ToString());
        Assert.Contains("Tom bilmediğini söyledi.", body);
    }

    [Fact]
    public async Task ParsesStructuredEvaluation()
    {
        var (client, handler) = Create();
        handler.Respond(HttpStatusCode.OK, Samples.GeminiSuccess(Samples.EvaluationJson));

        var evaluation = await client.EvaluateAsync(Item, "x", FakeKey, "gemini-flash-latest");

        Assert.Equal(85, evaluation.Score);
        Assert.True(evaluation.IsCorrect);
        Assert.Equal("Tom said he had no idea.", evaluation.NaturalTranslation);
        Assert.Single(evaluation.Mistakes);
        Assert.Equal("Dolaylı anlatımda zaman bir geri kayar.", evaluation.Tip);
        Assert.Null(evaluation.RawText);
    }

    [Fact]
    public void SkipsThoughtPartsAndStripsCodeFences()
    {
        var body = "{\"candidates\":[{\"content\":{\"parts\":["
                 + "{\"text\":\"düşünüyorum...\",\"thought\":true},"
                 + "{\"text\":" + JsonSerializer.Serialize("```json\n{\"score\":101,\"isCorrect\":false}\n```") + "}"
                 + "]}}]}";

        var evaluation = GeminiClient.ParseEvaluation(body);

        Assert.Equal(100, evaluation.Score);
        Assert.False(evaluation.IsCorrect);
        Assert.Empty(evaluation.Mistakes);
    }

    [Fact]
    public void NonJsonAnswerFallsBackToRawText()
    {
        var evaluation = GeminiClient.ParseEvaluation(Samples.GeminiSuccess("Çeviriniz doğru, tebrikler!"));

        Assert.Equal("Çeviriniz doğru, tebrikler!", evaluation.RawText);
        Assert.Null(evaluation.Score);
    }

    [Fact]
    public void ScoreAsStringIsAccepted()
    {
        Assert.Equal(73, GeminiClient.ParseEvaluationJson("{\"score\":\"72.6\"}").Score);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "{\"error\":{\"code\":400,\"message\":\"API key not valid. Please pass a valid API key.\",\"status\":\"INVALID_ARGUMENT\",\"details\":[{\"reason\":\"API_KEY_INVALID\"}]}}", "API anahtarı geçersiz")]
    [InlineData(HttpStatusCode.NotFound, "{\"error\":{\"code\":404,\"message\":\"models/gemini-1.5-flash is not found\",\"status\":\"NOT_FOUND\"}}", "modeli bulunamadı")]
    [InlineData(HttpStatusCode.TooManyRequests, "{\"error\":{\"code\":429,\"message\":\"Quota exceeded\",\"status\":\"RESOURCE_EXHAUSTED\"}}", "kota sınırı")]
    [InlineData(HttpStatusCode.Forbidden, "{\"error\":{\"code\":403,\"message\":\"Permission denied\",\"status\":\"PERMISSION_DENIED\"}}", "yetkisi yok")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "<html>bakımda</html>", "geçici bir sorun")]
    public async Task HttpErrorsBecomeTurkishMessages(HttpStatusCode status, string body, string expected)
    {
        var (client, handler) = Create();
        handler.Respond(status, body);

        var ex = await Assert.ThrowsAsync<GeminiException>(() => client.EvaluateAsync(Item, "x", FakeKey, "gemini-1.5-flash"));

        Assert.Contains(expected, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal((int)status, ex.StatusCode);
        Assert.DoesNotContain(FakeKey, ex.Message);
    }

    [Fact]
    public async Task BlockedPromptIsReported()
    {
        var (client, handler) = Create();
        handler.Respond(HttpStatusCode.OK, "{\"promptFeedback\":{\"blockReason\":\"SAFETY\"}}");

        var ex = await Assert.ThrowsAsync<GeminiException>(() => client.EvaluateAsync(Item, "x", FakeKey, "m"));

        Assert.Contains("SAFETY", ex.Message);
    }

    [Fact]
    public async Task NetworkFailureIsReported()
    {
        var (client, handler) = Create();
        handler.Throw(new HttpRequestException("DNS çözülemedi"));

        var ex = await Assert.ThrowsAsync<GeminiException>(() => client.EvaluateAsync(Item, "x", FakeKey, "m"));

        Assert.Contains("bağlanılamadı", ex.Message);
    }

    [Fact]
    public async Task MissingKeyFailsWithoutRequest()
    {
        var (client, handler) = Create();

        await Assert.ThrowsAsync<GeminiException>(() => client.EvaluateAsync(Item, "x", "  ", "m"));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ListModelsFiltersGenerateContentAndFollowsPages()
    {
        var (client, handler) = Create();
        handler.Respond(HttpStatusCode.OK, "{\"models\":[{\"name\":\"models/gemini-flash-latest\",\"supportedGenerationMethods\":[\"generateContent\",\"countTokens\"]},{\"name\":\"models/text-embedding-004\",\"supportedGenerationMethods\":[\"embedContent\"]}],\"nextPageToken\":\"p2\"}");
        handler.Respond(HttpStatusCode.OK, "{\"models\":[{\"name\":\"models/gemini-2.5-flash\",\"supportedGenerationMethods\":[\"generateContent\"]}]}");

        var models = await client.ListModelsAsync(FakeKey);

        Assert.Equal(new[] { "gemini-flash-latest", "gemini-2.5-flash" }, models);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("pageToken=p2", handler.Requests[1].Request.RequestUri!.Query);
        Assert.All(handler.Requests, r => Assert.Equal(FakeKey, r.Request.Headers.GetValues("x-goog-api-key").Single()));
    }
}

public class EvaluationFormatterTests
{
    private static readonly PracticeItem Item = new(TranslationDirection.TurkishToEnglish, "Kaç!", ["Run!", "Run."], 0);

    [Fact]
    public void FormatsFullEvaluation()
    {
        var text = EvaluationFormatter.Format(Item, " run ", new Evaluation
        {
            Score = 70,
            IsCorrect = false,
            CorrectedTranslation = "Run!",
            NaturalTranslation = "Run!",
            Mistakes = ["run → Run!: cümle büyük harfle başlar"],
            Tip = "Emir cümleleri fiille başlar.",
        });

        Assert.Contains("Puan: 70/100", text);
        Assert.Contains("DÜZELTİLMELİ", text);
        Assert.Contains("Senin çevirin : run", text);
        Assert.Contains("Düzeltilmiş   : Run!", text);
        Assert.Contains("Referans      : Run!  |  Run.", text);
        Assert.Contains("  - run → Run!: cümle büyük harfle başlar", text);
        Assert.Contains("İpucu: Emir cümleleri fiille başlar.", text);
    }

    [Fact]
    public void OmitsCorrectedLineWhenSameAsUserAndSaysNoMistakes()
    {
        var text = EvaluationFormatter.Format(Item, "Run!", new Evaluation { Score = 100, IsCorrect = true, CorrectedTranslation = "Run!" });

        Assert.Contains("DOĞRU", text);
        Assert.DoesNotContain("Düzeltilmiş", text);
        Assert.Contains("Hata bulunmadı.", text);
    }

    [Fact]
    public void ShowsRawTextWhenModelDidNotReturnJson()
    {
        var text = EvaluationFormatter.Format(Item, "Run", new Evaluation { RawText = "Güzel çeviri." });

        Assert.StartsWith("Gemini yanıtı:", text);
        Assert.Contains("Güzel çeviri.", text);
        Assert.Contains("Referans", text);
    }
}
