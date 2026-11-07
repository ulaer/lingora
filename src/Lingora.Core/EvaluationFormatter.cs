using System.Text;
using Lingora.Core.Models;

namespace Lingora.Core;

/// <summary>Değerlendirmeyi ekranda gösterilecek düz metne çevirir (yalnızca yazı).</summary>
public static class EvaluationFormatter
{
    public static string Format(PracticeItem item, string userTranslation, Evaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(evaluation);
        var user = (userTranslation ?? string.Empty).Trim();
        var builder = new StringBuilder();

        if (evaluation.RawText is not null)
        {
            builder.AppendLine("Gemini yanıtı:");
            builder.AppendLine(evaluation.RawText);
            AppendReferences(builder, item);
            return builder.ToString().TrimEnd();
        }

        var score = evaluation.Score is { } s ? $"{s}/100" : "-";
        var verdict = evaluation.IsCorrect switch
        {
            true => "DOĞRU",
            false => "DÜZELTİLMELİ",
            null => "-",
        };

        builder.Append("Puan: ").Append(score).Append("    Sonuç: ").AppendLine(verdict);
        builder.AppendLine();
        builder.Append("Senin çevirin : ").AppendLine(user);

        if (evaluation.CorrectedTranslation.Length > 0
            && !string.Equals(evaluation.CorrectedTranslation, user, StringComparison.Ordinal))
        {
            builder.Append("Düzeltilmiş   : ").AppendLine(evaluation.CorrectedTranslation);
        }

        if (evaluation.NaturalTranslation.Length > 0)
        {
            builder.Append("Doğal çeviri  : ").AppendLine(evaluation.NaturalTranslation);
        }

        AppendReferences(builder, item);
        builder.AppendLine();

        if (evaluation.Mistakes.Count == 0)
        {
            builder.AppendLine("Hata bulunmadı.");
        }
        else
        {
            builder.AppendLine("Hatalar:");
            foreach (var mistake in evaluation.Mistakes)
            {
                builder.Append("  - ").AppendLine(mistake);
            }
        }

        if (evaluation.MinorIssues.Count > 0)
        {
            builder.AppendLine("Küçük hatalar (puanı etkilemez):");
            foreach (var issue in evaluation.MinorIssues)
            {
                builder.Append("  - ").AppendLine(issue);
            }
        }

        if (evaluation.Tip.Length > 0)
        {
            builder.AppendLine();
            builder.Append("İpucu: ").AppendLine(evaluation.Tip);
        }

        if (evaluation.Vocabulary.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Öğrenilecekler:");
            foreach (var word in evaluation.Vocabulary)
            {
                builder.Append("  - ").Append(word.Term).Append(" (").Append(word.Kind).Append("): ").AppendLine(word.Meaning);
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendReferences(StringBuilder builder, PracticeItem item)
    {
        if (item.References.Count > 0)
        {
            builder.Append("Referans      : ").AppendLine(string.Join("  |  ", item.References));
        }
    }
}
