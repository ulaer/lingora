using System.Globalization;
using System.Text;

namespace Lingora.Core.Progress;

/// <summary>İlerleme sayfasının düz metin içeriği (yalnızca yazı, görsel yok).</summary>
public static class ProgressFormatter
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string Format(ProgressSummary summary, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var builder = new StringBuilder();

        builder.AppendLine("GENEL");
        Row(builder, "Toplam çevrilen cümle", summary.TotalSentences.ToString("N0", Turkish));
        var todayStatus = summary.GoalReachedToday ? "HEDEFE ULAŞILDI" : $"hedefe {summary.RemainingToday} cümle kaldı";
        Row(builder, "Bugün", $"{summary.TodayCount} / {summary.DailyGoal}   ({todayStatus})");
        Row(builder, "Bugünkü ortalama puan", Score(summary.TodayAverageScore));
        Row(builder, "Genel ortalama puan", Score(summary.AverageScore));
        Row(builder, "Doğru kabul edilen", summary.TotalSentences == 0
            ? "-"
            : $"{summary.CorrectCount} (%{100.0 * summary.CorrectCount / summary.TotalSentences:0})");
        Row(builder, "Güncel seri", $"{summary.CurrentStreak} gün (hedefe art arda ulaşılan)");
        Row(builder, "En uzun seri", $"{summary.BestStreak} gün");
        Row(builder, "Hedefe ulaşılan gün", $"{summary.DaysGoalReached} / {summary.ActiveDays} çalışılan gün");
        Row(builder, "Mod dağılımı", $"TR→EN {summary.TurkishToEnglishCount}   ·   EN→TR {summary.EnglishToTurkishCount}");

        builder.AppendLine();
        builder.AppendLine($"SON {summary.RecentDays.Count} GÜN");
        builder.AppendLine("Tarih              Cümle   Hedef   Durum           Ort. puan");
        foreach (var day in summary.RecentDays)
        {
            var status = day.GoalReached ? "ULAŞILDI" : day.Date == today ? "devam ediyor" : "ulaşılamadı";
            var date = day.Date.ToString("dd.MM.yyyy ddd", Turkish);
            builder.AppendLine($"{date,-17}  {day.Count,5}   {day.Goal,5}   {status,-14}  {Score(day.AverageScore)}");
        }

        return builder.ToString().TrimEnd();
    }

    private static void Row(StringBuilder builder, string label, string value) =>
        builder.Append("  ").Append(label.PadRight(24)).Append(": ").AppendLine(value);

    private static string Score(double? score) => score is { } s ? s.ToString("0.0", Turkish) : "-";
}
