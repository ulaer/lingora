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
    }

    public string Directory { get; }

    public IReadOnlyDictionary<string, GameAsset> Assets { get; }

    public IReadOnlyDictionary<string, GameCharacter> Characters { get; }

    public IReadOnlyDictionary<string, GamePose> Poses { get; }

    public IReadOnlyList<GameColor> Colors { get; }

    /// <summary>Grup sahnelerindeki özneler ("Tom and Mary" → "Tom ve Mary").</summary>
    public IReadOnlyList<GameAlias> GroupSubjects { get; }

    public IReadOnlyList<GameScene> Scenes { get; }

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
                Str(s, "subject")));
        }

        return new GameLibrary(directory, assets, characters, poses, colors, groupSubjects, scenes);
    }

    public GameScene Pick(Random random) => Scenes[random.Next(Scenes.Count)];

    /// <summary>Öznenin hedef dildeki hali (kullanıcıya ipucu olarak gösterilir).</summary>
    public string SubjectIn(GameScene scene, TranslationDirection direction)
    {
        if (direction == TranslationDirection.TurkishToEnglish)
        {
            return scene.Subject;
        }

        foreach (var c in Characters.Values)
        {
            if (c.En == scene.Subject)
            {
                return c.Tr;
            }

            if (c.Aliases.FirstOrDefault(a => a.En == scene.Subject) is { } alias)
            {
                return alias.Tr;
            }
        }

        return GroupSubjects.FirstOrDefault(g => g.En == scene.Subject)?.Tr ?? scene.Subject;
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
