using Lingora.Core.Models;

namespace Lingora.Core.Corpus;

/// <summary>
/// Tatoeba / manythings.org biçimindeki satırları çözümler:
/// <c>İngilizce&lt;TAB&gt;Türkçe&lt;TAB&gt;Kaynak bilgisi (isteğe bağlı)</c>.
/// </summary>
public static class CorpusParser
{
    public const string ExpectedFormat = "İngilizce<TAB>Türkçe<TAB>Kaynak (isteğe bağlı)";

    public static bool TryParseLine(string? line, out SentencePair pair)
    {
        pair = default;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var parts = line.TrimStart('﻿').Split('\t');
        if (parts.Length < 2)
        {
            return false;
        }

        var english = Clean(parts[0]);
        var turkish = Clean(parts[1]);
        if (english.Length == 0 || turkish.Length == 0)
        {
            return false;
        }

        pair = new SentencePair(english, turkish);
        return true;
    }

    private static string Clean(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
