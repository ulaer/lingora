using System.Globalization;
using System.Text;
using System.Text.Json;
using Lingora.Core.Models;

namespace Lingora.Core.Game;

public sealed record GameContent(string Id, string Tint, string En, string Tr);

public sealed record GameAsset(
    string Id,
    string File,
    string Category,
    string? Character,
    string? Pose,
    string En,
    string Tr,
    string? EnPlural,
    bool Countable,
    IReadOnlyList<GameContent> Contents,
    string? TrLocation,
    string? EnMeasure,
    string? TrMeasure);

public sealed record GameAlias(string En, string Tr);

public sealed record GameCharacter(string Id, string En, string Tr, string Kind, IReadOnlyList<GameAlias> Aliases);

public sealed record GamePose(string Id, string VerbEn, string VerbTr);

public sealed record GameColor(string Key, string Hex, string En, string Tr);

public sealed record GameGroupMember(string Character, double X, double Scale);

public sealed record GameEmotion(string Id, string? Mouth, string? Eyes, string? Brows, string? Mark);

public sealed record GameWeather(string Id, string Kind, string Overlay);

/// <summary>manifest.json "layouts": konum (X, Y), ölçek (S) ve yığın aralığı (Dx, Dy).</summary>
public sealed record GamePlacement(double X, double Y, double S, double Dx = 0, double Dy = 0);

/// <summary>Kompozisyon yerleşimleri (manifest.json "layouts"; yoksa sözleşme varsayılanları).</summary>
public sealed record GameLayouts(
    GamePlacement Mark,
    GamePlacement Marker,
    GamePlacement Badge,
    GamePlacement SkyOverlay,
    GamePlacement Prop,
    GamePlacement Bubble,
    GamePlacement PairBubble,
    GamePlacement BubbleContent,
    GamePlacement ShowNear,
    GamePlacement ShowFar)
{
    public static GameLayouts Default { get; } = new(
        new GamePlacement(36, 6, 0.32),
        new GamePlacement(0, -34, 0.22),
        new GamePlacement(4, 4, 0.2, 0, 54),
        new GamePlacement(196, 4, 0.22),
        new GamePlacement(196, 0, 0.55),
        new GamePlacement(136, 0, 0.46),
        new GamePlacement(30, 0, 0.4),
        new GamePlacement(194, 58, 0.2),
        new GamePlacement(128, 230, 0.6),
        new GamePlacement(196, 150, 0.22));

    /// <summary>İşaret (duygu) yerleşimi: Dx/Dy = baş noktasına göre kayma.</summary>
    public static GameLayouts Parse(JsonElement root)
    {
        var d = Default;
        if (!root.TryGetProperty("layouts", out var l) || l.ValueKind != JsonValueKind.Object)
        {
            return d;
        }

        GamePlacement P(JsonElement? e, GamePlacement fallback)
        {
            if (e is not { ValueKind: JsonValueKind.Object } o)
            {
                return fallback;
            }

            double N(string n, double f) => o.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : f;
            return new GamePlacement(N("x", fallback.X), N("y", fallback.Y), N("s", fallback.S), N("dx", fallback.Dx), N("dy", fallback.Dy));
        }

        JsonElement? Get(JsonElement e, string n) => e.TryGetProperty(n, out var v) ? v : null;
        JsonElement? Sub(string a, string b) => Get(l, a) is { ValueKind: JsonValueKind.Object } x ? Get(x, b) : null;

        var mark = P(Get(l, "mark"), d.Mark);
        var marker = P(Get(l, "marker"), d.Marker);
        return new GameLayouts(
            mark with { X = Get(l, "mark") is { } m && m.TryGetProperty("dx", out var dx) ? dx.GetDouble() : d.Mark.X, Y = Get(l, "mark") is { } m2 && m2.TryGetProperty("dy", out var dy) ? dy.GetDouble() : d.Mark.Y },
            marker with { Y = Get(l, "marker") is { } k && k.TryGetProperty("dy", out var mdy) ? mdy.GetDouble() : d.Marker.Y },
            P(Get(l, "badge"), d.Badge),
            P(Get(l, "sky_overlay"), d.SkyOverlay),
            P(Sub("prop", "prop"), d.Prop),
            P(Sub("bubble", "bubble"), d.Bubble),
            P(Sub("pair_bubble", "bubble"), d.PairBubble),
            P(Sub("bubble", "content"), d.BubbleContent),
            P(Sub("show_near", "obj") ?? Get(l, "show_near"), d.ShowNear),
            P(Sub("show_far", "obj") ?? Get(l, "show_far"), d.ShowFar));
    }
}

/// <summary>scenes.json'daki bir sahne: yalnızca asset kimlikleri, dönüşüm ve kanonik EN/TR cümle.</summary>
public sealed record GameScene(
    string Id,
    string? Background,
    string Character,
    IReadOnlyList<GameGroupMember>? Group,
    string? Object,
    string? Tint,
    int Count,
    double AnchorX,
    double AnchorY,
    double Scale,
    string En,
    string Tr,
    string Subject)
{
    /// <summary>Örnekleme ağırlığı (scenes.json "weight"; yoksa 1). Kalabalık poz ailelerini (tutma) seyreltir.</summary>
    public double Weight { get; init; } = 1;

    /// <summary>Hedef dildeki kanonik cümle.</summary>
    public string Sentence(TranslationDirection direction) =>
        direction == TranslationDirection.TurkishToEnglish ? En : Tr;
}

/// <summary>game-assets klasörü: manifest.json + scenes.json. Model kullanmaz, yalnızca okur.</summary>
public sealed class GameLibrary
{
    public const string FolderName = "game-assets";

    private GameLibrary(
        string directory,
        IReadOnlyDictionary<string, GameAsset> assets,
        IReadOnlyDictionary<string, GameCharacter> characters,
        IReadOnlyDictionary<string, GamePose> poses,
        IReadOnlyList<GameColor> colors,
        IReadOnlyList<GameAlias> groupSubjects,
        IReadOnlyList<GameScene> scenes)
    {
        Directory = directory;
        Assets = assets;
        Characters = characters;
        Poses = poses;
        Colors = colors;
        GroupSubjects = groupSubjects;
        Scenes = scenes;
        foreach (var level in Enum.GetValues<GameLevel>())
        {
            var l = level;
            _records[l] = new Lazy<IReadOnlyList<GameRecord>>(() => LoadRecords(l));
        }
    }

    public string Directory { get; }

    public IReadOnlyDictionary<string, GameAsset> Assets { get; }

    public IReadOnlyDictionary<string, GameCharacter> Characters { get; }

    public IReadOnlyDictionary<string, GamePose> Poses { get; }

    public IReadOnlyList<GameColor> Colors { get; }

    /// <summary>Grup sahnelerindeki özneler ("Tom and Mary" → "Tom ve Mary").</summary>
    public IReadOnlyList<GameAlias> GroupSubjects { get; }

    public IReadOnlyList<GameScene> Scenes { get; }

    /// <summary>Kompozisyon yerleşimleri (manifest "layouts").</summary>
    public GameLayouts Layouts { get; private set; } = GameLayouts.Default;

    /// <summary>manifest "place_parents": alt yer → üst yer (kitchen → room); "at home" üst yeri söyler.</summary>
    public IReadOnlyDictionary<string, string> PlaceParents { get; private set; } = new Dictionary<string, string>();

    public IReadOnlyDictionary<string, GameEmotion> Emotions { get; private set; } = new Dictionary<string, GameEmotion>();

    public IReadOnlyDictionary<string, GameWeather> Weathers { get; private set; } = new Dictionary<string, GameWeather>();

    /// <summary>Fiil kimliği → (İngilizce -ing, Türkçe şimdiki zaman) etiketi.</summary>
    public IReadOnlyDictionary<string, (string En, string Tr)> Verbs { get; private set; } = new Dictionary<string, (string, string)>();

    private readonly Dictionary<GameLevel, Lazy<IReadOnlyList<GameRecord>>> _records = new();
    private readonly Dictionary<GameLevel, double[]> _recordCumulative = new();

    /// <summary>Tüm seviyelerin kayıtlarını okur (arka plan iş parçacığında çağrılır; UI donmaz).</summary>
    public void Preload()
    {
        foreach (var level in Enum.GetValues<GameLevel>())
        {
            _ = Records(level).Count;
        }
    }

    /// <summary>Kimliğe göre kayıt (kaçırılan resmi yeniden sormak için).</summary>
    public GameRecord? FindRecord(GameLevel level, string id) => Records(level).FirstOrDefault(r => r.Id == id);

    /// <summary>
    /// Ekran okuyucu için resmin Türkçe tarifi (olgulardan; cevabı cümle olarak vermez, wh-sorularında hiçbir şey ele vermez).
    /// </summary>
    public string AltText(GameRound round)
    {
        if (round.Sentence.Answer is not null)
        {
            return "Resim: bir sahne. Soruyu resme bakarak cevapla.";
        }

        var f = ChoiceBuilder.EffectiveFacts(round.Record);
        var parts = new List<string>();
        var who = f.Subject;
        if (who is not null && Assets.TryGetValue(who, out var wa) && wa.Character is { } ch)
        {
            who = ch;
        }

        if (who is not null)
        {
            parts.Add(Characters.TryGetValue(who, out var c) ? c.Tr : who);
        }

        var firstChar = round.Record.Actors.FirstOrDefault()?.Char;
        if (f.Verb is { } verb && Verbs.TryGetValue(verb, out var vl))
        {
            parts.Add(vl.Tr);
        }
        else if (firstChar is not null && Assets.TryGetValue(firstChar, out var pa) && pa.Pose is { } pose && Poses.TryGetValue(pose, out var pv))
        {
            parts.Add(pv.VerbTr);
        }

        if (f.Object is { } obj && Assets.TryGetValue(obj, out var oa))
        {
            parts.Add(oa.Tr);
        }

        if ((f.Place ?? round.Record.Bg) is { } place && Assets.TryGetValue(place, out var pl))
        {
            parts.Add(pl.Tr);
        }

        return "Resim: " + string.Join(", ", parts);
    }

    /// <summary>scenes/A1|A2|B1.jsonl kayıtları (ilk erişimde okunur; dosya yoksa boş).</summary>
    public IReadOnlyList<GameRecord> Records(GameLevel level) => _records[level].Value;

    /// <summary>Seviyede oynanabilir görsel var mı (A1: scenes.json ya da A1.jsonl).</summary>
    public bool HasLevel(GameLevel level) => level == GameLevel.A1 ? Scenes.Count > 0 || Records(level).Count > 0 : Records(level).Count > 0;

    private IReadOnlyList<GameRecord> LoadRecords(GameLevel level)
    {
        var path = Path.Combine(Directory, "scenes", level + ".jsonl");
        var list = new List<GameRecord>();
        if (!File.Exists(path))
        {
            return list;
        }

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(line);
            var record = GameRecord.Parse(doc.RootElement, level);
            if (record.Sentences.Count > 0)
            {
                list.Add(record);
            }
        }

        // Aynı fiil/pozlu kayıtlar çoğunluğu ele geçirmesin: ağırlık 1/√(kümedeki kayıt sayısı).
        var counts = list.GroupBy(r => r.WeightKey).ToDictionary(g => g.Key, g => g.Count());
        return list.Select(r => r with { Weight = 1 / Math.Sqrt(counts[r.WeightKey]) }).ToList();
    }

    /// <summary>
    /// Seviyeden ağırlıklı bir tur seçer. A1: scenes.json (Weight); A2/B1: jsonl kaydı (poz ağırlıklı) ve içinden
    /// rastgele bir cümle. Seviyede görsel yoksa A1'e düşer.
    /// </summary>
    public GameRound PickRound(GameLevel level, Random random)
    {
        // v5: A1 de kayıt yolundan (işaret, ifade, facts kuralı); scenes.json yalnızca A1.jsonl yoksa.
        if (level == GameLevel.A1 && Scenes.Count > 0 && Records(level).Count == 0)
        {
            return GameRound.FromScene(Pick(random));
        }

        var records = Records(level);
        if (records.Count == 0)
        {
            return level == GameLevel.A1 ? throw new InvalidOperationException("A1 sahnesi yok") : PickRound(GameLevel.A1, random);
        }

        if (!_recordCumulative.TryGetValue(level, out var sums))
        {
            sums = new double[records.Count];
            double total = 0;
            for (var i = 0; i < records.Count; i++)
            {
                total += records[i].Weight;
                sums[i] = total;
            }

            _recordCumulative[level] = sums;
        }

        var index = Array.BinarySearch(sums, random.NextDouble() * sums[^1]);
        index = index < 0 ? ~index : index + 1;
        var record = records[Math.Min(index, records.Count - 1)];
        return new GameRound(record, record.Sentences[random.Next(record.Sentences.Count)]);
    }

    public string AssetPath(string assetId) =>
        Path.Combine(Directory, Assets[assetId].File.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Uygulama klasöründeki game-assets; geliştirmede üst klasörlerde aranır.</summary>
    public static string? FindDirectory(string? start = null)
    {
        var dir = new DirectoryInfo(start ?? AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, FolderName);
            if (File.Exists(Path.Combine(candidate, "manifest.json")) && File.Exists(Path.Combine(candidate, "scenes.json")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    public static GameLibrary Load(string directory)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        var root = manifest.RootElement;

        var assets = new Dictionary<string, GameAsset>(StringComparer.Ordinal);
        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            var contents = new List<GameContent>();
            if (a.TryGetProperty("contents", out var list))
            {
                foreach (var c in list.EnumerateArray())
                {
                    contents.Add(new GameContent(Str(c, "id"), Str(c, "tint"), Str(c, "en"), Str(c, "tr")));
                }
            }

            string? enMeasure = null, trMeasure = null;
            if (a.TryGetProperty("measure", out var measure))
            {
                enMeasure = Str(measure, "en");
                trMeasure = Str(measure, "tr");
            }

            var asset = new GameAsset(
                Str(a, "id"),
                Str(a, "file"),
                Str(a, "category"),
                Opt(a, "character"),
                Opt(a, "pose"),
                Str(a, "en"),
                Str(a, "tr"),
                Opt(a, "en_plural"),
                a.TryGetProperty("countable", out var countable) && countable.ValueKind == JsonValueKind.True,
                contents,
                Opt(a, "tr_loc"),
                enMeasure,
                trMeasure);
            assets[asset.Id] = asset;
        }

        var characters = new Dictionary<string, GameCharacter>(StringComparer.Ordinal);
        foreach (var c in root.GetProperty("characters").EnumerateArray())
        {
            var aliases = c.TryGetProperty("aliases", out var al)
                ? al.EnumerateArray().Select(x => new GameAlias(Str(x, "en"), Str(x, "tr"))).ToList()
                : new List<GameAlias>();
            characters[Str(c, "id")] = new GameCharacter(Str(c, "id"), Str(c, "en"), Str(c, "tr"), Str(c, "kind"), aliases);
        }

        var poses = root.GetProperty("poses").EnumerateArray()
            .Select(p => new GamePose(Str(p, "id"), Str(p, "verb_en"), Str(p, "verb_tr")))
            .ToDictionary(p => p.Id, StringComparer.Ordinal);

        var colors = root.GetProperty("colors").EnumerateObject()
            .Select(p => new GameColor(p.Name, Str(p.Value, "hex"), Str(p.Value, "en"), Str(p.Value, "tr")))
            .ToList();

        var groupSubjects = new List<GameAlias>();
        if (root.TryGetProperty("groups", out var groups))
        {
            foreach (var g in groups.EnumerateArray())
            {
                groupSubjects.AddRange(g.GetProperty("subjects").EnumerateArray().Select(s => new GameAlias(Str(s, "en"), Str(s, "tr"))));
            }
        }

        using var scenesDoc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "scenes.json")));
        var scenes = new List<GameScene>();
        foreach (var s in scenesDoc.RootElement.GetProperty("scenes").EnumerateArray())
        {
            var t = s.GetProperty("transform");
            List<GameGroupMember>? group = null;
            if (s.TryGetProperty("group", out var g) && g.ValueKind == JsonValueKind.Array)
            {
                group = g.EnumerateArray().Select(m => new GameGroupMember(Str(m, "character"), m.GetProperty("x").GetDouble(), m.GetProperty("scale").GetDouble())).ToList();
            }

            double ax = 0, ay = 0;
            if (t.TryGetProperty("anchor", out var anchor) && anchor.ValueKind == JsonValueKind.Array)
            {
                ax = anchor[0].GetDouble();
                ay = anchor[1].GetDouble();
            }

            scenes.Add(new GameScene(
                Str(s, "id"),
                Opt(s, "background"),
                Str(s, "character"),
                group,
                Opt(s, "object"),
                Opt(t, "tint"),
                t.TryGetProperty("count", out var count) && count.ValueKind == JsonValueKind.Number ? count.GetInt32() : 0,
                ax,
                ay,
                t.TryGetProperty("scale", out var scale) && scale.ValueKind == JsonValueKind.Number ? scale.GetDouble() : 1,
                Str(s, "en"),
                Str(s, "tr"),
                Str(s, "subject"))
            {
                Weight = s.TryGetProperty("weight", out var w) && w.ValueKind == JsonValueKind.Number && w.GetDouble() > 0
                    ? w.GetDouble()
                    : 1,
            });
        }

        var library = new GameLibrary(directory, assets, characters, poses, colors, groupSubjects, scenes)
        {
            Layouts = GameLayouts.Parse(root),
        };
        if (root.TryGetProperty("emotions", out var emotions) && emotions.ValueKind == JsonValueKind.Array)
        {
            library.Emotions = emotions.EnumerateArray()
                .ToDictionary(e => Str(e, "id"), e => new GameEmotion(Str(e, "id"), Opt(e, "mouth"), Opt(e, "eyes"), Opt(e, "brows"), Opt(e, "mark")), StringComparer.Ordinal);
        }

        if (root.TryGetProperty("weather", out var weather) && weather.ValueKind == JsonValueKind.Array)
        {
            library.Weathers = weather.EnumerateArray()
                .ToDictionary(w => Str(w, "id"), w => new GameWeather(Str(w, "id"), Str(w, "kind"), Str(w, "overlay")), StringComparer.Ordinal);
        }

        if (root.TryGetProperty("place_parents", out var parents) && parents.ValueKind == JsonValueKind.Object)
        {
            library.PlaceParents = parents.EnumerateObject()
                .Where(p => p.Value.ValueKind == JsonValueKind.String)
                .ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal);
        }

        if (root.TryGetProperty("verbs", out var verbs) && verbs.ValueKind == JsonValueKind.Object)
        {
            var map = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
            foreach (var v in verbs.EnumerateObject())
            {
                var en = v.Value.TryGetProperty("en", out var e1) ? Str(e1, "ing") : string.Empty;
                var tr = v.Value.TryGetProperty("tr", out var t1) ? Str(t1, "prog") : string.Empty;
                map[v.Name] = (en, tr);
            }

            library.Verbs = map;
        }

        return library;
    }

    /// <summary>Sahne seçer; ağırlıklı (Weight). Tümü 1 ise tekdüze seçimle aynıdır.</summary>
    public GameScene Pick(Random random)
    {
        _cumulative ??= Cumulative(Scenes);
        var target = random.NextDouble() * _cumulative[^1];
        var index = Array.BinarySearch(_cumulative, target);
        index = index < 0 ? ~index : index + 1;
        return Scenes[Math.Min(index, Scenes.Count - 1)];
    }

    private double[]? _cumulative;

    private static double[] Cumulative(IReadOnlyList<GameScene> scenes)
    {
        var sums = new double[scenes.Count];
        double total = 0;
        for (var i = 0; i < scenes.Count; i++)
        {
            total += scenes[i].Weight;
            sums[i] = total;
        }

        return sums;
    }

    /// <summary>Öznenin hedef dildeki hali (kullanıcıya ipucu olarak gösterilir).</summary>
    public string SubjectIn(GameScene scene, TranslationDirection direction) => SubjectIn(scene.Subject, direction, null);

    /// <summary>Turun cümlesinin öznesi (hedef dilde).</summary>
    public string SubjectIn(GameRound round, TranslationDirection direction) =>
        SubjectIn(round.Sentence.Subject, direction, round.Sentence.Person);

    /// <summary>İngilizce özne metninin hedef dildeki karşılığı (kişi zamirleri için <paramref name="person"/> kullanılır).</summary>
    public string SubjectIn(string enSubject, TranslationDirection direction, string? person)
    {
        if (direction == TranslationDirection.TurkishToEnglish)
        {
            return enSubject;
        }

        foreach (var c in Characters.Values)
        {
            if (c.En == enSubject)
            {
                return c.Tr;
            }

            if (c.Aliases.FirstOrDefault(a => a.En == enSubject) is { } alias)
            {
                return alias.Tr;
            }
        }

        if (GroupSubjects.FirstOrDefault(g => g.En == enSubject) is { } group)
        {
            return group.Tr;
        }

        return enSubject switch
        {
            "I" => "Ben",
            "You" => person is "2p" ? "Siz" : "Sen",
            "We" => "Biz",
            "They" => "Onlar",
            _ => enSubject,
        };
    }

    /// <summary>
    /// Resimde kesin olarak görünenler (Türkçe, maddeler halinde). Gemini istemine "resmin doğru tarifi"
    /// olarak konur; değerlendirme bu olgulara göre yapılır.
    /// </summary>
    public string DescribeFacts(GameScene scene, TranslationDirection direction = TranslationDirection.TurkishToEnglish)
    {
        var b = new StringBuilder();
        var people = scene.Group is { Count: > 0 }
            ? scene.Group.Select(m => Assets[m.Character]).ToList()
            : new List<GameAsset> { Assets[scene.Character] };
        var pose = Poses.TryGetValue(people[0].Pose ?? string.Empty, out var p) ? p : null;
        var turkish = direction == TranslationDirection.EnglishToTurkish;

        var who = string.Join(" ve ", people.Select(a => Who(a.Character!)));
        b.Append("- Kim: ").Append(who).Append(". Resimde isim yazmaz; kanonik cümlede özne \"")
            .Append(SubjectIn(scene, direction)).AppendLine("\".");
        b.Append("- Kabul edilebilir özne göndermeleri: ")
            .AppendLine(turkish ? AcceptableSubjectsTr(scene, people) : AcceptableSubjects(scene, people));
        if (pose is not null)
        {
            b.Append("- Eylem: ").Append(PoseDescription(pose.Id)).Append(" (").Append(pose.VerbEn).Append(" / ").Append(pose.VerbTr).AppendLine(").");
        }

        if (scene.Object is { } objectId && Assets.TryGetValue(objectId, out var obj))
        {
            b.Append("- Nesne: ").AppendLine(ObjectDescription(scene, obj));
        }
        else
        {
            b.AppendLine("- Nesne: yok.");
        }

        if (scene.Background is { } bg && Assets.TryGetValue(bg, out var place))
        {
            b.Append("- Yer: ").Append(place.Tr).Append(" (").Append(place.En).Append(" / ").Append(place.TrLocation).AppendLine(").");
        }
        else
        {
            b.AppendLine("- Yer: arka plan yok (boş zemin); yer belirtmek gerekmez, yer uydurmak hatadır.");
        }

        return b.ToString().TrimEnd();
    }

    /// <summary>Oyundaki rozet/işaret açıklaması: (kimlik, kısa etiket, anlam).</summary>
    public static IReadOnlyList<(string Id, string Label, string Meaning)> BadgeLegend { get; } =
    [
        ("cal_yesterday", "dün", "geçmiş zaman"),
        ("cal_tomorrow", "yarın", "gelecek zaman"),
        ("badge_not", "değil", "olumsuz (çarpı: inkâr edilen şey)"),
        ("badge_question", "soru", "soru cümlesi"),
        ("cal_everyday", "her gün", "geniş zaman / alışkanlık"),
        ("badge_can", "-ebilir", "yetenek (can)"),
        ("marker_me", "ben", "konuşan kişi (ben / biz)"),
        ("marker_you", "sen", "karşıdaki kişi (sen / siz)"),
    ];

    private static readonly Dictionary<string, string> BadgeMeaning = new(StringComparer.Ordinal)
    {
        ["cal_yesterday"] = "geçmiş zaman (dün)",
        ["cal_tomorrow"] = "gelecek zaman (yarın)",
        ["cal_everyday"] = "geniş zaman / alışkanlık (her gün)",
        ["badge_not"] = "olumsuz cümle",
        ["badge_question"] = "soru cümlesi",
        ["badge_can"] = "yetenek (can / -ebilmek)",
    };

    /// <summary>Rozetin Türkçe anlamı (rozet yoksa kimliği).</summary>
    public static string BadgeText(string badge) => BadgeMeaning.TryGetValue(badge, out var t) ? t : badge;

    private string AssetLabel(string id) => Assets.TryGetValue(id, out var a) ? $"{a.Tr} ({a.En})" : id;

    /// <summary>
    /// Tur için resmin kesin olguları (Türkçe maddeler). A1'de scenes.json tarifi; A2/B1'de kaydın olguları
    /// ve sorulan cümlenin rozetleri (zaman/olumsuz/soru) Gemini istemine konur.
    /// </summary>
    public string DescribeFacts(GameRound round, TranslationDirection direction = TranslationDirection.TurkishToEnglish)
    {
        if (round.Legacy is { } legacy)
        {
            return DescribeFacts(legacy, direction);
        }

        var record = round.Record;
        var f = record.Facts;
        var b = new StringBuilder();
        var firstActor = record.Actors.FirstOrDefault() ?? record.Panels.FirstOrDefault()?.Actors.FirstOrDefault();

        var subjectId = f?.Subject ?? (firstActor is not null && Assets.TryGetValue(firstActor.Char, out var ca) ? ca.Character : null);
        var who = subjectId is null ? "belirsiz" : Characters.ContainsKey(subjectId) ? Who(subjectId) : subjectId;
        b.Append("- Kim: ").Append(who).Append(". Resimde isim yazmaz; sorulan cümlenin öznesi \"")
            .Append(SubjectIn(round, direction)).AppendLine("\".");
        var accepted = round.Sentence.SubjectOk.Select(x => SubjectIn(x, direction, null)).Append(SubjectIn(round, direction)).Distinct().ToList();
        b.Append("- Kabul edilebilir özne göndermeleri: ").Append(string.Join(", ", accepted.Select(a => $"\"{a}\"")))
            .AppendLine(" (resimde isim görünmediği için resmi doğru anlatan her özne doğrudur).");
        if (round.Sentence.Person is { } person)
        {
            b.Append("- Kişi: ").Append(person switch
            {
                "1s" => "ben (konuşan kişi işaretli)",
                "2s" => "sen (karşıdaki kişi işaretli)",
                "1p" => "biz",
                "2p" => "siz",
                "3p" => "onlar",
                _ => "o",
            }).AppendLine(".");
        }

        if (f?.Verb is { } verb)
        {
            b.Append("- Eylem: ").Append(Verbs.TryGetValue(verb, out var vl) ? $"{vl.Tr} / {vl.En}" : verb).AppendLine(".");
        }

        if (f?.Object is { } obj)
        {
            b.Append("- Nesne: ").Append(AssetLabel(obj));
            if (f.Count is { } cnt && cnt != "0")
            {
                b.Append("; adet: ").Append(cnt);
            }

            b.AppendLine(".");
        }
        else
        {
            b.AppendLine("- Nesne: yok.");
        }

        var placeId = f?.Place ?? record.Bg;
        if (placeId is not null)
        {
            b.Append("- Yer: ").Append(AssetLabel(placeId)).AppendLine(".");
        }
        else
        {
            b.AppendLine("- Yer: arka plan yok (boş zemin); yer belirtmek gerekmez, yer uydurmak hatadır.");
        }

        if ((f?.Time ?? record.Time) is { } time)
        {
            b.Append("- Günün vakti: ").Append(time).AppendLine(" (gökyüzü rengi ve güneş/ay).");
        }

        if ((f?.Weather ?? record.Weather) is { } weather)
        {
            b.Append("- Hava: ").Append(weather).AppendLine(".");
        }

        var state = f?.State ?? record.Actors.Select(a => a.Expr).FirstOrDefault(e => e is not null);
        if (state is not null)
        {
            b.Append("- Durum/duygu: ").Append(state).AppendLine(" (yüz ifadesi ve işaret).");
        }

        if (f?.Other is { } other)
        {
            b.Append("- Etkileşimdeki diğer kişi: ").Append(Characters.TryGetValue(other, out var oc) ? $"{oc.Tr} ({oc.En})" : other).AppendLine(".");
        }

        if (record.Bubble is { } bubble)
        {
            b.Append("- Balon: ").Append(bubble.Type == "thought" ? "düşünce balonu" : "konuşma balonu");
            if ((bubble.ContentObj ?? bubble.ContentChar) is { } inside)
            {
                b.Append(", içinde: ").Append(AssetLabel(inside));
            }

            b.AppendLine(".");
        }

        if (record.Prop is { } prop)
        {
            b.Append("- Nesne durumu: ").Append(AssetLabel(prop.Obj))
                .Append(" sırayla ").Append(string.Join(" → ", prop.States)).AppendLine(" durumlarına geçer.");
        }

        if (record.Panels.Count > 1)
        {
            b.Append("- Sıralı ").Append(record.Panels.Count).AppendLine(" kare: olaylar soldan sağa sırayla olur (önce, sonra).");
        }

        if (round.Sentence.Answer is { } answer)
        {
            b.Append("- Soru: ").Append(round.Text(direction)).Append(" Öğrenci bu soruyu resme bakarak KISA CEVAPLA yanıtlar; doğru cevap: ")
                .Append(answer.Text(direction)).AppendLine(" (resimdeki olgularla uyuşan her kısa cevap doğrudur).");
        }

        if (direction == TranslationDirection.EnglishToTurkish && round.Sentence.TrAlt.Count > 0)
        {
            b.Append("- Zamirsiz Türkçe biçim de doğrudur: ").Append(string.Join(" / ", round.Sentence.TrAlt)).AppendLine();
        }

        if (round.Sentence.Badges.Count > 0)
        {
            b.Append("- Cümle biçimi (köşedeki rozetler): ").Append(string.Join(" + ", round.Sentence.Badges.Select(BadgeText)))
                .AppendLine(". Öğrenci bu biçimde yazmalıdır.");
        }
        else
        {
            b.AppendLine("- Cümle biçimi: rozet yok; şimdiki zaman, olumlu, düz cümle.");
        }

        return b.ToString().TrimEnd();
    }

    private string Who(string characterId)
    {
        if (!Characters.TryGetValue(characterId, out var c))
        {
            return characterId;
        }

        var kind = (characterId, c.Kind) switch
        {
            ("tom", _) => "erkek çocuk",
            ("mary", _) => "kız çocuk",
            ("man", _) => "yetişkin bir adam",
            ("woman", _) => "yetişkin bir kadın",
            ("doctor", _) => "beyaz önlüklü bir doktor",
            ("police", _) => "üniformalı bir polis memuru",
            (_, "animal") => "hayvan",
            _ => c.Kind,
        };
        return $"{c.Tr} ({c.En}, {kind})";
    }

    private string AcceptableSubjects(GameScene scene, IReadOnlyList<GameAsset> people)
    {
        if (people.Count > 1)
        {
            return $"\"{scene.Subject}\", \"They\" ve resmi doğru anlatan çoğul özneler (ör. iki kişi/hayvan).";
        }

        var c = Characters[people[0].Character!];
        var names = new List<string> { c.En };
        names.AddRange(c.Aliases.Select(a => a.En));
        names.Add(c.Id switch
        {
            "tom" => "A boy",
            "mary" => "A girl",
            "man" => "A man",
            "woman" => "A woman",
            "doctor" => "A doctor",
            "police" => "A police officer",
            _ => "A " + c.Id,
        });
        return string.Join(", ", names.Distinct().Select(n => $"\"{n}\"")) + " (resimde isim görünmediği için bunların hepsi doğrudur).";
    }

    private string AcceptableSubjectsTr(GameScene scene, IReadOnlyList<GameAsset> people)
    {
        if (people.Count > 1)
        {
            return $"\"{SubjectIn(scene, TranslationDirection.EnglishToTurkish)}\", \"Onlar\" ve resmi doğru anlatan çoğul özneler; "
                   + "Türkçede özne düşürülebilir (ör. \"Koşuyorlar.\").";
        }

        var c = Characters[people[0].Character!];
        var names = new List<string> { c.Tr };
        names.AddRange(c.Aliases.Select(a => a.Tr));
        names.AddRange(c.Id switch
        {
            "tom" => new[] { "Bir erkek çocuk", "Oğlan" },
            "mary" => new[] { "Bir kız", "Kız çocuğu" },
            "man" => new[] { "Bir adam" },
            "woman" => new[] { "Bir kadın" },
            "doctor" => new[] { "Bir doktor" },
            "police" => new[] { "Bir polis", "Polis memuru" },
            _ => new[] { "Bir " + c.Tr.ToLower(CultureInfo.GetCultureInfo("tr-TR")) },
        });
        return string.Join(", ", names.Distinct().Select(n => $"\"{n}\""))
               + " (resimde isim görünmediği için hepsi doğrudur; Türkçede özne düşürülebilir, ör. \"Elma tutuyor.\").";
    }

    private static string PoseDescription(string pose) => pose switch
    {
        "stand" => "ayakta duruyor, nesneyi elinde tutuyor",
        "eat" => "nesneyi ağzına götürmüş, yiyor",
        "drink" => "bardağı/fincanı ağzına dayamış, içiyor",
        "read" => "nesneyi iki eliyle önünde açık tutuyor, okuyor",
        "run" => "koşuyor",
        "swim" => "suyun içinde yüzüyor",
        "sit" => "oturuyor",
        "sleep" => "yere uzanmış, uyuyor",
        "play" => "nesneyle oynuyor",
        "drink_bowl" => "kâseden içiyor",
        "fly" => "uçuyor",
        "eat_ground" => "yerdeki yiyeceği yiyor",
        "walk" => "yürüyor",
        "talk" => "eliyle işaret ederek konuşuyor",
        "wave" => "elini kaldırmış, el sallıyor",
        "call" => "telefonu kulağına tutmuş, telefonla konuşuyor",
        "think" => "eli çenesinde, düşünüyor",
        "lie" => "yatakta yatıyor, gözleri kapalı, uyuyor",
        _ => pose,
    };

    private string ObjectDescription(GameScene scene, GameAsset obj)
    {
        var count = Math.Max(1, scene.Count);
        if (obj.Contents.Count > 0)
        {
            var content = obj.Contents.FirstOrDefault(c => string.Equals(c.Tint, scene.Tint, StringComparison.OrdinalIgnoreCase));
            var what = content is null ? obj.Tr : $"{obj.TrMeasure ?? obj.Tr} {content.Tr}";
            var whatEn = content is null ? obj.En : $"{obj.EnMeasure ?? obj.En} {content.En}";
            return $"{what} ({whatEn}); içindekinin rengi içeceği gösterir.";
        }

        var color = Colors.FirstOrDefault(c => string.Equals(c.Hex, scene.Tint, StringComparison.OrdinalIgnoreCase));
        var number = count switch { 1 => "bir", 2 => "iki", 3 => "üç", _ => count.ToString(CultureInfo.InvariantCulture) };
        var numberEn = count switch { 1 => "one", 2 => "two", 3 => "three", _ => count.ToString(CultureInfo.InvariantCulture) };
        var nameEn = count > 1 ? obj.EnPlural ?? obj.En : obj.En;
        var colorText = color is null ? string.Empty : $"{color.Tr} ";
        var colorEn = color is null ? string.Empty : $"{color.En} ";
        var measure = !obj.Countable && obj.TrMeasure is not null ? $"{obj.TrMeasure} " : string.Empty;
        var measureEn = !obj.Countable && obj.EnMeasure is not null ? $"{obj.EnMeasure} " : string.Empty;
        var text = obj.Countable
            ? $"{number} {colorText}{obj.Tr} ({numberEn} {colorEn}{nameEn}); adet: {count}"
            : $"{measure}{obj.Tr} ({measureEn}{obj.En})";
        return color is null ? text + "." : text + $"; renk: {color.Tr} ({color.En}).";
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

    private static string? Opt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
