using Lingora.Core.Models;

namespace Lingora.Core.Progress;

/// <summary>Bir günün özeti.</summary>
public sealed record DayStat(DateOnly Date, int Count, int Goal, double? AverageScore)
{
    public bool GoalReached => Count >= Goal;
}

/// <summary>İlerleme sayfasında gösterilen özet.</summary>
public sealed record ProgressSummary(
    int TotalSentences,
    int TodayCount,
    int DailyGoal,
    int CurrentStreak,
    int BestStreak,
    int ActiveDays,
    int DaysGoalReached,
    int CorrectCount,
    double? AverageScore,
    double? TodayAverageScore,
    int TurkishToEnglishCount,
    int EnglishToTurkishCount,
    IReadOnlyList<DayStat> RecentDays)
{
    public bool GoalReachedToday => TodayCount >= DailyGoal;

    public int RemainingToday => Math.Max(0, DailyGoal - TodayCount);
}

public static class ProgressCalculator
{
    /// <param name="attempts">Tüm kayıtlı denemeler.</param>
    /// <param name="currentGoal">Bugün için geçerli günlük hedef.</param>
    /// <param name="today">Yerel saate göre bugün.</param>
    /// <param name="timeZone">Kayıt zamanlarını yerel güne çevirmek için saat dilimi.</param>
    /// <param name="recentDayCount">Geçmiş tablosunda gösterilecek gün sayısı (bugün dahil).</param>
    public static ProgressSummary Calculate(
        IEnumerable<AttemptRecord> attempts,
        int currentGoal,
        DateOnly today,
        TimeZoneInfo timeZone,
        int recentDayCount = 14)
    {
        ArgumentNullException.ThrowIfNull(attempts);
        ArgumentNullException.ThrowIfNull(timeZone);
        currentGoal = Math.Max(1, currentGoal);

        var list = attempts.ToList();
        var byDay = list
            .GroupBy(a => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(a.Timestamp, timeZone).DateTime))
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.Timestamp).ToList());

        DayStat StatFor(DateOnly date)
        {
            if (!byDay.TryGetValue(date, out var dayAttempts))
            {
                return new DayStat(date, 0, currentGoal, null);
            }

            // Bugün güncel hedefle, geçmiş günler o gün kaydedilen hedefle değerlendirilir.
            var recordedGoal = dayAttempts[^1].DailyGoal;
            var goal = date == today || recordedGoal <= 0 ? currentGoal : recordedGoal;
            return new DayStat(date, dayAttempts.Count, goal, Average(dayAttempts));
        }

        var reachedDays = byDay.Keys.Where(d => StatFor(d).GoalReached).ToHashSet();

        var todayStat = StatFor(today);
        var streakStart = todayStat.GoalReached ? today : today.AddDays(-1);
        var currentStreak = 0;
        for (var day = streakStart; reachedDays.Contains(day); day = day.AddDays(-1))
        {
            currentStreak++;
        }

        var bestStreak = 0;
        var run = 0;
        DateOnly? previous = null;
        foreach (var day in reachedDays.OrderBy(d => d))
        {
            run = previous is { } p && p.AddDays(1) == day ? run + 1 : 1;
            bestStreak = Math.Max(bestStreak, run);
            previous = day;
        }

        var recent = Enumerable.Range(0, Math.Max(1, recentDayCount))
            .Select(offset => StatFor(today.AddDays(-offset)))
            .ToList();

        return new ProgressSummary(
            TotalSentences: list.Count,
            TodayCount: todayStat.Count,
            DailyGoal: currentGoal,
            CurrentStreak: currentStreak,
            BestStreak: bestStreak,
            ActiveDays: byDay.Count,
            DaysGoalReached: reachedDays.Count,
            CorrectCount: list.Count(a => a.IsCorrect == true),
            AverageScore: Average(list),
            TodayAverageScore: todayStat.AverageScore,
            TurkishToEnglishCount: list.Count(a => a.Direction == TranslationDirection.TurkishToEnglish),
            EnglishToTurkishCount: list.Count(a => a.Direction == TranslationDirection.EnglishToTurkish),
            RecentDays: recent);
    }

    private static double? Average(IEnumerable<AttemptRecord> attempts)
    {
        var scores = attempts.Where(a => a.Score.HasValue).Select(a => (double)a.Score!.Value).ToList();
        return scores.Count == 0 ? null : Math.Round(scores.Average(), 1);
    }
}
