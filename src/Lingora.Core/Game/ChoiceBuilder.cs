using Lingora.Core.Models;

namespace Lingora.Core.Game;

/// <summary>"Doğru cümleyi seç" sorusu: seçenekler hedef dilde, biri doğru.</summary>
public sealed record ChoiceQuestion(GameRound Round, IReadOnlyList<string> Options, int CorrectIndex, TranslationDirection Direction = TranslationDirection.TurkishToEnglish)
{
    /// <summary>A1 (scenes.json) sahnesi; A2/B1 turlarında null.</summary>
    public GameScene? Scene => Round.Legacy;

    public string Correct => Options[CorrectIndex];

    /// <summary>Resmin öbür dildeki cümlesi (günlükte kaynak olarak).</summary>
    public string SourceSentence => Round.Text(Direction == TranslationDirection.TurkishToEnglish ? TranslationDirection.EnglishToTurkish : TranslationDirection.TurkishToEnglish);
}

/// <summary>
/// Çeldiricileri yerel olarak üretir: aynı özneli ve resimde tek bir öğesi (kişi/poz, nesne, renk,
/// adet, yer) farklı sahnelerin cümleleri. Aynı resmi anlatan cümle asla çeldirici olmaz.
/// </summary>
public static class ChoiceBuilder
{
    /// <summary>Tura göre soru: A1 eski görsel kuralıyla, A2/B1 olgu (facts) tabanlı doğruluk kuralıyla.</summary>
    public static ChoiceQuestion Build(GameLibrary library, GameRound round, TranslationDirection direction, Random random, int optionCount = 4)
    {
        if (round.Legacy is { } legacy)
        {
            return Build(library, legacy, direction, random, optionCount);
        }

        var correct = round.Text(direction);
        var picture = round.Record;
        var form = round.Sentence;
        var ranked = new List<(string Text, int Distance, double Tie)>();
        foreach (var record in library.Records(picture.Level))
        {
            if (ReferenceEquals(record, picture))
            {
                continue;
            }

            var distance = FactsDistance(picture, record);
            foreach (var sentence in record.Sentences)
            {
                // Aynı biçim ve rozetler: soru biçimini rozet verir, ayırt eden yalnızca resim olmalı.
                if (sentence.Form != form.Form || !sentence.Badges.SequenceEqual(form.Badges))
                {
                    continue;
                }

                if (IsTrueFor(picture, sentence, record, library.PlaceParents))
                {
                    continue;
                }

                ranked.Add((sentence.Text(direction), distance, random.NextDouble()));
            }
        }

        var options = new List<string> { correct };
        foreach (var (text, _, _) in ranked.OrderBy(x => x.Distance).ThenBy(x => x.Tie))
        {
            if (!options.Contains(text, StringComparer.OrdinalIgnoreCase))
            {
                options.Add(text);
            }

            if (options.Count == optionCount)
            {
                break;
            }
        }

        var shuffled = options.OrderBy(_ => random.Next()).ToList();
        return new ChoiceQuestion(round, shuffled, shuffled.IndexOf(correct), direction);
    }

    /// <summary>Kaydın olguları: facts yoksa görselden türetilir (özne, fiil/poz, nesne, adet, yer, ...).</summary>
    internal static GameFacts EffectiveFacts(GameRecord r)
    {
        if (r.Facts is { } f)
        {
            return f;
        }

        var a = r.Actors.FirstOrDefault() ?? r.Panels.FirstOrDefault()?.Actors.FirstOrDefault();
        var values = new Dictionary<string, string?>
        {
            ["subject"] = r.Actors.Count > 1 ? string.Join("+", r.Actors.Select(x => x.Char)) : a?.Char,
            ["verb"] = r.Actors.Count > 1 ? string.Join("+", r.Actors.Select(x => x.Char)) : a?.Char,
            ["object"] = a?.Hold?.Obj ?? r.Prop?.Obj,
            ["variant"] = (a?.Hold?.Tint ?? string.Empty).ToUpperInvariant() + "|" + (r.Prop is null ? string.Empty : string.Join(",", r.Prop.States)),
            ["count"] = (a?.Hold?.Count ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["place"] = r.Bg,
            ["time"] = r.Time ?? r.Sky,
            ["weather"] = r.Weather,
            ["state"] = a?.Expr,
            ["person"] = null,
            ["other"] = r.Actors.Count > 1 ? r.Actors[1].Char : null,
        };
        return new GameFacts(values);
    }

    /// <summary>İki resim arasında farklı olgu sayısı (az fark = daha zor çeldirici).</summary>
    internal static int FactsDistance(GameRecord a, GameRecord b)
    {
        var fa = EffectiveFacts(a);
        var fb = EffectiveFacts(b);
        return GameFacts.Dimensions.Count(d => fa.Get(d) != fb.Get(d));
    }

    /// <summary>
    /// Doğruluk kuralı (v4 + v5): cümle, resim için doğrudur ⇔ cümlenin olumlu çekirdeğinin söylediği (mentions) her
    /// boyutta kaynak sahnesinin olgusu resmin olgusuna uyar. Olumsuzlukta ters çevirme YOK (rozet/kutupluluk yok sayılır).
    /// Özne: resmin öznesi subject_ok içinde ya da eşit. Kişi: aynı kişi. Yer: eşit ya da resmin yeri söylenen yerin
    /// çocuğu (kitchen → room). Fiili "hold" olan cümle: nesnesi resmin facts.holds listesinde (fiil eşitliği aranmaz).
    /// mentions yoksa tüm boyutlar sayılır.
    /// </summary>
    internal static bool IsTrueFor(GameRecord picture, GameSentence sentence, GameRecord source, IReadOnlyDictionary<string, string>? placeParents = null)
    {
        var pf = EffectiveFacts(picture);
        var sf = EffectiveFacts(source);
        IReadOnlyList<string> dims = sentence.Mentions.Count > 0 ? sentence.Mentions : GameFacts.Dimensions;
        var holdSentence = sf.Verb == "hold" && dims.Contains("object");
        var objectOnlyInHolds = false;
        if (holdSentence)
        {
            var wanted = sf.Object;
            var inMain = wanted is not null && pf.Object == wanted;
            objectOnlyInHolds = !inMain && wanted is not null && pf.Holds.Contains(wanted);
            if (!inMain && !objectOnlyInHolds)
            {
                return false;
            }
        }

        foreach (var d in dims)
        {
            switch (d)
            {
                case "subject" when sentence.Role == "other":
                    if (!sentence.SubjectOk.Contains(pf.Other ?? string.Empty, StringComparer.Ordinal))
                    {
                        return false;
                    }

                    break;
                case "other" when sentence.Role == "other":
                    if (pf.Subject != sf.Subject)
                    {
                        return false;
                    }

                    break;
                case "subject":
                    if (!(sentence.SubjectOk.Contains(pf.Get("subject") ?? string.Empty, StringComparer.Ordinal) || pf.Get("subject") == sf.Get("subject")))
                    {
                        return false;
                    }

                    break;
                case "person":
                    var said = sentence.Person ?? sf.Person;
                    if (pf.Person is not null && said is not null && said != pf.Person)
                    {
                        return false;
                    }

                    break;
                case "place":
                    if (!PlaceMatches(pf.Place, sf.Place, placeParents))
                    {
                        return false;
                    }

                    break;
                case "verb" when holdSentence:
                case "object" when holdSentence:
                    break;
                case "variant" or "count" when objectOnlyInHolds:
                    break;
                default:
                    if (pf.Get(d) != sf.Get(d))
                    {
                        return false;
                    }

                    break;
            }
        }

        return true;
    }

    /// <summary>Resmin yeri, cümlenin söylediği yerle aynı ya da onun çocuğu mu (kitchen → room).</summary>
    internal static bool PlaceMatches(string? picturePlace, string? saidPlace, IReadOnlyDictionary<string, string>? parents)
    {
        if (picturePlace == saidPlace)
        {
            return true;
        }

        return picturePlace is not null && parents is not null && parents.TryGetValue(picturePlace, out var parent) && parent == saidPlace;
    }

    public static ChoiceQuestion Build(GameLibrary library, GameScene scene, TranslationDirection direction, Random random, int optionCount = 4)
    {
        var correct = scene.Sentence(direction);
        var key = Visual(scene);

        var colorKeys = library.Colors.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        var ranked = library.Scenes
            .Where(s => s.Id != scene.Id)
            .Select(s => (Scene: s, Distance: Distance(key, Visual(s)), SameSubject: s.Subject == scene.Subject))
            .Where(x => x.Distance > 0 && !AlsoTrue(scene, x.Scene, colorKeys))
            .OrderBy(x => x.SameSubject ? 0 : 1)
            .ThenBy(x => x.Distance)
            .ThenBy(_ => random.Next())
            .ToList();

        var options = new List<string> { correct };
        foreach (var (candidate, _, _) in ranked)
        {
            var text = candidate.Sentence(direction);
            if (!options.Contains(text, StringComparer.OrdinalIgnoreCase))
            {
                options.Add(text);
            }

            if (options.Count == optionCount)
            {
                break;
            }
        }

        var shuffled = options.OrderBy(_ => random.Next()).ToList();
        return new ChoiceQuestion(GameRound.FromScene(scene), shuffled, shuffled.IndexOf(correct), direction);
    }

    /// <summary>Resmin görsel anahtarı: (kişiler+poz, nesne, renk/içerik, adet, yer).</summary>
    internal static string[] Visual(GameScene s) =>
    [
        s.Group is { Count: > 0 } ? string.Join("+", s.Group.Select(g => g.Character)) : s.Character,
        s.Object ?? "-",
        (s.Tint ?? "-").ToUpperInvariant(),
        s.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
        s.Background ?? "-",
    ];

    internal static int Distance(string[] a, string[] b) => a.Zip(b).Count(p => p.First != p.Second);

    /// <summary>
    /// Çeldirici cümle, farklı olduğu öğeyi hiç söylemiyorsa resim için de doğrudur; kullanılmaz.
    /// Ör. resimde sarı elma varken "holding an apple" (renk yok) ya da resimde park varken yer söylemeyen cümle.
    /// </summary>
    internal static bool AlsoTrue(GameScene picture, GameScene candidate, IReadOnlySet<string> colorKeys)
    {
        var a = Visual(picture);
        var b = Visual(candidate);
        var tintDiffers = a[2] != b[2];
        var placeDiffers = a[4] != b[4];
        var onlyTintOrPlace = a[0] == b[0] && a[1] == b[1] && a[3] == b[3];
        if (!onlyTintOrPlace)
        {
            return false;
        }

        var mentionsColor = candidate.Id.Split("__").Any(colorKeys.Contains) || candidate.Object is "glass" or "cup" or "bowl";
        var mentionsPlace = candidate.Background is not null;
        return (!tintDiffers || !mentionsColor) && (!placeDiffers || !mentionsPlace);
    }
}
