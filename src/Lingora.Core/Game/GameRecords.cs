using System.Globalization;
using System.Text.Json;
using Lingora.Core.Models;

namespace Lingora.Core.Game;

/// <summary>Oyun seviyesi: A1 = scenes.json (tutma/eylem), A2 = zaman/olumsuz/soru/duygu/hava, B1 = etkileşim/balon/durum/panel.</summary>
public enum GameLevel
{
    A1 = 0,
    A2 = 1,
    B1 = 2,
}

public static class GameLevelExtensions
{
    public static string Label(this GameLevel level) => level.ToString();

    /// <summary>Düğmenin altındaki kısa Türkçe açıklama.</summary>
    public static string Description(this GameLevel level) => level switch
    {
        GameLevel.A1 => "Şimdiki zaman: kim ne yapıyor",
        GameLevel.A2 => "Zaman, olumsuz, soru, duygu, hava",
        _ => "Konuşma, düşünce, durum değişimi, sıralı olay",
    };
}

/// <summary>Elde tutulan nesne (aktörün içinde, anchor noktasına oturur).</summary>
public sealed record GameHold(string Obj, string? Tint, int Count, double AnchorX, double AnchorY, double Scale);

/// <summary>Sahnedeki karakter: poz, konum/ölçek, yüz ifadesi, elindeki nesne, "ben/sen" işareti.</summary>
public sealed record GameActor(string Char, double X, double Scale, string? Expr, string? Marker, GameHold? Hold);

/// <summary>Konuşma ya da düşünce balonu; içeriği bir karakter ya da nesne olabilir.</summary>
public sealed record GameBubble(string Type, string? ContentChar, string? ContentObj);

/// <summary>Durumlu sahne nesnesi (lamba, TV, kapı); States sırayla oynatılır.</summary>
public sealed record GameProp(string Obj, IReadOnlyList<string> States, double? Scale)
{
    /// <summary>v6 "show" (this/that): nesnenin rengi (data-tint="primary").</summary>
    public string? Tint { get; init; }

    /// <summary>v6 "show": adet (these/those için 2-3).</summary>
    public int Count { get; init; } = 1;
}

/// <summary>Bir görsel katmanın ortak alanları (kayıt ya da panel).</summary>
public interface IGameVisual
{
    string Layout { get; }

    string? Bg { get; }

    string? Sky { get; }

    string? SkyOverlay { get; }

    string? Weather { get; }

    IReadOnlyList<GameActor> Actors { get; }

    GameBubble? Bubble { get; }

    GameProp? Prop { get; }
}

/// <summary>"panels" yerleşimindeki tek bir kare (arka plan kaydınkini kullanır).</summary>
public sealed record GamePanel(
    string Layout,
    string? Bg,
    string? Sky,
    string? SkyOverlay,
    string? Weather,
    IReadOnlyList<GameActor> Actors,
    GameBubble? Bubble,
    GameProp? Prop) : IGameVisual;

/// <summary>Resmin olguları (v4 sözleşmesi §2).</summary>
public sealed class GameFacts
{
    public static readonly IReadOnlyList<string> Dimensions =
        ["subject", "person", "verb", "object", "variant", "count", "place", "time", "weather", "state", "other"];

    private readonly Dictionary<string, string?> _values;

    public GameFacts(IReadOnlyDictionary<string, string?> values) =>
        _values = new Dictionary<string, string?>(values, StringComparer.Ordinal);

    /// <summary>Boyutun değeri (yoksa ya da null ise null; count 0 → "0").</summary>
    public string? Get(string dimension) => _values.TryGetValue(dimension, out var v) ? v : null;

    public string? Subject => Get("subject");

    public string? Verb => Get("verb");

    public string? Object => Get("object");

    public string? Place => Get("place");

    public string? Time => Get("time");

    public string? Weather => Get("weather");

    public string? State => Get("state");

    public string? Other => Get("other");

    public string? Variant => Get("variant");

    public string? Person => Get("person");

    /// <summary>Resimde öznenin elinde görünen nesneler (facts.holds); yoksa boş.</summary>
    public IReadOnlyList<string> Holds => Get("holds") is { Length: > 0 } h ? h.Split(',') : [];

    public string? Count => Get("count");

    internal static GameFacts? Parse(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var p in e.EnumerateObject())
        {
            values[p.Name] = p.Value.ValueKind switch
            {
                JsonValueKind.String => p.Value.GetString(),
                JsonValueKind.Number => p.Value.GetRawText(),
                JsonValueKind.Array => string.Join(",", p.Value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString())),
                _ => null,
            };
        }

        return new GameFacts(values);
    }
}

/// <summary>wh-sorusunun cevabı ("An apple." / "Bir elma.").</summary>
public sealed record GameAnswer(string En, string Tr)
{
    public string Text(TranslationDirection direction) => direction == TranslationDirection.TurkishToEnglish ? En : Tr;
}

/// <summary>Bir kayıttaki cümle (kabul kümesinin üyesi).</summary>
public sealed record GameSentence(
    string Form,
    string Subject,
    string En,
    string Tr,
    IReadOnlyList<string> Badges,
    IReadOnlyList<string> Mentions,
    IReadOnlyList<string> SubjectOk,
    string? Person)
{
    /// <summary>"other": cümlenin öznesi resmin facts.other'ıdır, nesnesi facts.subject ("Mary is listening to Tom").</summary>
    public string? Role { get; init; }

    /// <summary>wh-sorusunda cevap (soru cümlenin kendisidir); diğerlerinde null.</summary>
    public GameAnswer? Answer { get; init; }

    /// <summary>Zamirsiz Türkçe kabul biçimleri ("Ben koşuyorum." → "Koşuyorum.").</summary>
    public IReadOnlyList<string> TrAlt { get; init; } = [];

    public string Text(TranslationDirection direction) =>
        direction == TranslationDirection.TurkishToEnglish ? En : Tr;
}

/// <summary>scenes/*.jsonl satırı: bir görsel + onu anlatan cümleler.</summary>
public sealed record GameRecord(
    string Id,
    GameLevel Level,
    string Layout,
    string? Bg,
    string? Sky,
    string? SkyOverlay,
    string? Weather,
    string? Time,
    IReadOnlyList<GameActor> Actors,
    GameBubble? Bubble,
    GameProp? Prop,
    IReadOnlyList<GamePanel> Panels,
    GameFacts? Facts,
    IReadOnlyList<GameSentence> Sentences) : IGameVisual
{
    /// <summary>Örnekleme ağırlığı (aynı fiil/pozlu kayıtlar seyreltilir).</summary>
    public double Weight { get; init; } = 1;

    /// <summary>Ağırlık için küme anahtarı: fiil olgusu, yoksa ilk karakter.</summary>
    internal string WeightKey =>
        Facts?.Verb ?? Actors.FirstOrDefault()?.Char ?? Panels.FirstOrDefault()?.Actors.FirstOrDefault()?.Char ?? Id;

    internal static GameRecord Parse(JsonElement e, GameLevel level)
    {
        var sentences = new List<GameSentence>();
        if (e.TryGetProperty("sentences", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in list.EnumerateArray())
            {
                sentences.Add(new GameSentence(
                    Str(s, "form") ?? string.Empty,
                    Str(s, "subject") ?? string.Empty,
                    Str(s, "en") ?? string.Empty,
                    Str(s, "tr") ?? string.Empty,
                    Strings(s, "badges"),
                    Strings(s, "mentions"),
                    Strings(s, "subject_ok"),
                    Str(s, "person"))
                {
                    Role = Str(s, "role"),
                    TrAlt = Strings(s, "tr_alt"),
                    Answer = s.TryGetProperty("answer", out var ans) && ans.ValueKind == JsonValueKind.Object
                        ? new GameAnswer(Str(ans, "en") ?? string.Empty, Str(ans, "tr") ?? string.Empty)
                        : null,
                });
            }
        }

        var panels = new List<GamePanel>();
        if (e.TryGetProperty("panels", out var panelList) && panelList.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in panelList.EnumerateArray())
            {
                panels.Add(new GamePanel(
                    Str(p, "layout") ?? "single",
                    Str(e, "bg"),
                    Str(p, "sky"),
                    Str(p, "sky_overlay"),
                    Str(p, "weather"),
                    ParseActors(p),
                    ParseBubble(p),
                    ParseProp(p)));
            }
        }

        return new GameRecord(
            Str(e, "id") ?? string.Empty,
            level,
            Str(e, "layout") ?? "single",
            Str(e, "bg"),
            Str(e, "sky"),
            Str(e, "sky_overlay"),
            Str(e, "weather"),
            Str(e, "time"),
            ParseActors(e),
            ParseBubble(e),
            ParseProp(e),
            panels,
            e.TryGetProperty("facts", out var facts) ? GameFacts.Parse(facts) : null,
            sentences);
    }

    private static List<GameActor> ParseActors(JsonElement e)
    {
        var actors = new List<GameActor>();
        if (!e.TryGetProperty("actors", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return actors;
        }

        foreach (var a in list.EnumerateArray())
        {
            GameHold? hold = null;
            if (a.TryGetProperty("hold", out var h) && h.ValueKind == JsonValueKind.Object)
            {
                double ax = 176, ay = 160;
                if (h.TryGetProperty("anchor", out var anchor) && anchor.ValueKind == JsonValueKind.Array && anchor.GetArrayLength() >= 2)
                {
                    ax = anchor[0].GetDouble();
                    ay = anchor[1].GetDouble();
                }

                hold = new GameHold(
                    Str(h, "obj") ?? string.Empty,
                    Str(h, "tint"),
                    Int(h, "n", 1),
                    ax,
                    ay,
                    Num(h, "s", 0.3));
            }

            actors.Add(new GameActor(Str(a, "char") ?? string.Empty, Num(a, "x", 128), Num(a, "s", 1), Str(a, "expr"), Str(a, "marker"), hold));
        }

        return actors;
    }

    private static GameBubble? ParseBubble(JsonElement e)
    {
        if (!e.TryGetProperty("bubble", out var b) || b.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? ch = null, obj = null;
        if (b.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.Object)
        {
            ch = Str(c, "char");
            obj = Str(c, "obj");
        }

        return new GameBubble(Str(b, "type") ?? "speech", ch, obj);
    }

    private static GameProp? ParseProp(JsonElement e)
    {
        if (!e.TryGetProperty("prop", out var p) || p.ValueKind != JsonValueKind.Object)
        {
            // v6 this/that kayıtları nesneyi "show" alanında taşır: {obj, tint, n}.
            if (e.TryGetProperty("show", out var show) && show.ValueKind == JsonValueKind.Object)
            {
                return new GameProp(Str(show, "obj") ?? string.Empty, Array.Empty<string>(), null)
                {
                    Tint = Str(show, "tint"),
                    Count = show.TryGetProperty("n", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt32() : 1,
                };
            }

            return null;
        }

        return new GameProp(
            Str(p, "obj") ?? string.Empty,
            Strings(p, "states"),
            p.TryGetProperty("s", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetDouble() : null);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double Num(JsonElement e, string name, double fallback) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;

    private static int Int(JsonElement e, string name, int fallback) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : fallback;

    private static List<string> Strings(JsonElement e, string name)
    {
        var result = new List<string>();
        if (e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in v.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s)
                {
                    result.Add(s);
                }
            }
        }

        return result;
    }
}

/// <summary>
/// Ekrandaki tur: bir görsel + sorulan cümle. A1'de görsel scenes.json'daki <see cref="GameScene"/> (Legacy),
/// A2/B1'de bir jsonl kaydıdır.
/// </summary>
public sealed record GameRound(GameRecord Record, GameSentence Sentence, GameScene? Legacy = null)
{
    public string Id => Legacy?.Id ?? Record.Id;

    public GameLevel Level => Record.Level;

    public string Text(TranslationDirection direction) => Sentence.Text(direction);

    /// <summary>scenes.json sahnesini tek cümlelik tura çevirir.</summary>
    public static GameRound FromScene(GameScene scene)
    {
        var sentence = new GameSentence("prog", scene.Subject, scene.En, scene.Tr, [], [], [], null);
        var actors = scene.Group is { Count: > 0 }
            ? scene.Group.Select(m => new GameActor(m.Character, m.X, m.Scale, null, null, null)).ToList()
            : [new GameActor(
                scene.Character,
                128,
                1,
                null,
                null,
                scene.Object is null ? null : new GameHold(scene.Object, scene.Tint, Math.Max(1, scene.Count), scene.AnchorX, scene.AnchorY, scene.Scale))];
        var record = new GameRecord(scene.Id, GameLevel.A1, scene.Group is { Count: > 0 } ? "pair" : "single", scene.Background, null, null, null, null, actors, null, null, [], null, [sentence]);
        return new GameRound(record, sentence, scene);
    }

    internal static string Culture(double d) => d.ToString(CultureInfo.InvariantCulture);
}
