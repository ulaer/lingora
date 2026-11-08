using Lingora.Core.Models;

namespace Lingora.Core.Game;

/// <summary>"Doğru cümleyi seç" sorusu: seçenekler hedef dilde, biri doğru.</summary>
public sealed record ChoiceQuestion(GameScene Scene, IReadOnlyList<string> Options, int CorrectIndex, TranslationDirection Direction = TranslationDirection.TurkishToEnglish)
{
    public string Correct => Options[CorrectIndex];
}

/// <summary>
/// Çeldiricileri yerel olarak üretir: aynı özneli ve resimde tek bir öğesi (kişi/poz, nesne, renk,
/// adet, yer) farklı sahnelerin cümleleri. Aynı resmi anlatan cümle asla çeldirici olmaz.
/// </summary>
public static class ChoiceBuilder
{
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
        return new ChoiceQuestion(scene, shuffled, shuffled.IndexOf(correct), direction);
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
