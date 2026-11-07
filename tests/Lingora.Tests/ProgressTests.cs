using Lingora.Core.Models;
using Lingora.Core.Progress;

namespace Lingora.Tests;

public class ProgressStoreTests
{
    [Fact]
    public void AppendsAndLoadsRecords()
    {
        using var dir = new TempDir();
        var store = new ProgressStore(dir.File("data/attempts.jsonl"));
        var when = FixedTimeProvider.Local(2026, 11, 6, 9, 30);

        store.Append(new AttemptRecord { Timestamp = when, Direction = TranslationDirection.EnglishToTurkish, Source = "Hi.", UserTranslation = "Merhaba, nasılsın?", Score = 80, IsCorrect = true, DailyGoal = 10 });
        store.Append(new AttemptRecord { Timestamp = when.AddMinutes(1), Source = "Kaç!", UserTranslation = "Run!" });

        var records = store.LoadAll();

        Assert.Equal(2, records.Count);
        Assert.Equal(when, records[0].Timestamp);
        Assert.Equal(TranslationDirection.EnglishToTurkish, records[0].Direction);
        Assert.Equal("Merhaba, nasılsın?", records[0].UserTranslation);
        Assert.Equal(80, records[0].Score);
        Assert.Null(records[1].Score);
        Assert.Contains("nasılsın", File.ReadAllText(dir.File("data/attempts.jsonl")));
    }

    [Fact]
    public void SkipsCorruptLines()
    {
        using var dir = new TempDir();
        var path = dir.Write("attempts.jsonl", "{\"source\":\"Hi.\",\"timestamp\":\"2026-11-06T10:00:00+03:00\"}\nbozuk satır\n\n{\"source\":\"Run!\",\"timestamp\":\"2026-11-06T11:00:00+03:00\"}\n");
        var store = new ProgressStore(path);

        var records = store.LoadAll();

        Assert.Equal(2, records.Count);
        Assert.Equal(1, store.LastCorruptLineCount);
    }

    [Fact]
    public void MissingFileGivesEmptyList()
    {
        using var dir = new TempDir();
        Assert.Empty(new ProgressStore(dir.File("yok.jsonl")).LoadAll());
    }
}

public class ProgressCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 11, 6);

    private static IEnumerable<AttemptRecord> Day(int month, int day, int count, int goal = 3, int score = 80, TranslationDirection direction = TranslationDirection.TurkishToEnglish) =>
        Enumerable.Range(0, count).Select(i => new AttemptRecord
        {
            Timestamp = FixedTimeProvider.Local(2026, month, day, 10, i),
            Direction = direction,
            Source = $"s{month}-{day}-{i}",
            Score = score,
            IsCorrect = score >= 70,
            DailyGoal = goal,
        });

    private static ProgressSummary Calc(IEnumerable<AttemptRecord> attempts, int goal = 3) =>
        ProgressCalculator.Calculate(attempts, goal, Today, FixedTimeProvider.Istanbul);

    [Fact]
    public void EmptyHistory()
    {
        var summary = Calc([]);

        Assert.Equal(0, summary.TotalSentences);
        Assert.Equal(0, summary.TodayCount);
        Assert.False(summary.GoalReachedToday);
        Assert.Equal(3, summary.RemainingToday);
        Assert.Equal(0, summary.CurrentStreak);
        Assert.Null(summary.AverageScore);
        Assert.Equal(14, summary.RecentDays.Count);
        Assert.Equal(Today, summary.RecentDays[0].Date);
    }

    [Fact]
    public void CountsTodayAndGoal()
    {
        var summary = Calc(Day(11, 6, 2).Concat(Day(11, 5, 4)));

        Assert.Equal(6, summary.TotalSentences);
        Assert.Equal(2, summary.TodayCount);
        Assert.False(summary.GoalReachedToday);
        Assert.Equal(1, summary.RemainingToday);
        Assert.Equal(2, summary.ActiveDays);
    }

    [Fact]
    public void StreakContinuesFromYesterdayWhileTodayIsInProgress()
    {
        var attempts = Day(11, 3, 3).Concat(Day(11, 4, 3)).Concat(Day(11, 5, 3)).Concat(Day(11, 6, 1));

        var summary = Calc(attempts);

        Assert.Equal(3, summary.CurrentStreak);
        Assert.Equal(3, summary.BestStreak);
    }

    [Fact]
    public void StreakIncludesTodayWhenReached()
    {
        var summary = Calc(Day(11, 5, 3).Concat(Day(11, 6, 3)));

        Assert.True(summary.GoalReachedToday);
        Assert.Equal(2, summary.CurrentStreak);
    }

    [Fact]
    public void MissedDayBreaksStreakButBestIsKept()
    {
        var attempts = Day(10, 1, 3).Concat(Day(10, 2, 3)).Concat(Day(10, 3, 3)).Concat(Day(10, 4, 3))
            .Concat(Day(11, 4, 1))
            .Concat(Day(11, 5, 3));

        var summary = Calc(attempts);

        Assert.Equal(1, summary.CurrentStreak);
        Assert.Equal(4, summary.BestStreak);
        Assert.Equal(5, summary.DaysGoalReached);
    }

    [Fact]
    public void PastDaysUseTheirRecordedGoal()
    {
        // Dün hedef 2 idi ve 2 cümle yapıldı; bugün hedef 10'a çıkarıldı.
        var summary = Calc(Day(11, 5, 2, goal: 2), goal: 10);

        var yesterday = summary.RecentDays[1];
        Assert.Equal(2, yesterday.Goal);
        Assert.True(yesterday.GoalReached);
        Assert.Equal(10, summary.RecentDays[0].Goal);
        Assert.Equal(1, summary.CurrentStreak);
    }

    [Fact]
    public void UsesLocalDayNotUtcDay()
    {
        // İstanbul 6 Kasım 01:30 = UTC 5 Kasım 22:30 → yerel olarak bugüne sayılmalı.
        var attempt = new AttemptRecord { Timestamp = new DateTimeOffset(2026, 11, 5, 22, 30, 0, TimeSpan.Zero), Source = "x" };

        Assert.Equal(1, Calc([attempt]).TodayCount);
    }

    [Fact]
    public void AveragesAndDirectionCounts()
    {
        var attempts = Day(11, 6, 2, score: 60)
            .Concat(Day(11, 5, 2, score: 90, direction: TranslationDirection.EnglishToTurkish))
            .Append(new AttemptRecord { Timestamp = FixedTimeProvider.Local(2026, 11, 6), Source = "puansız" });

        var summary = Calc(attempts);

        Assert.Equal(75, summary.AverageScore);
        Assert.Equal(60, summary.TodayAverageScore);
        Assert.Equal(2, summary.CorrectCount);
        Assert.Equal(3, summary.TurkishToEnglishCount);
        Assert.Equal(2, summary.EnglishToTurkishCount);
    }
}

public class ProgressFormatterTests
{
    [Fact]
    public void ShowsTotalsTodayGoalAndHistory()
    {
        var today = new DateOnly(2026, 11, 6);
        var attempts = Enumerable.Range(0, 3).Select(i => new AttemptRecord
        {
            Timestamp = FixedTimeProvider.Local(2026, 11, 5, 10, i),
            Source = "s" + i,
            Score = 80,
            IsCorrect = true,
            DailyGoal = 3,
        });
        var summary = ProgressCalculator.Calculate(attempts, 5, today, FixedTimeProvider.Istanbul, 3);

        var text = ProgressFormatter.Format(summary, today);

        Assert.Contains("Toplam çevrilen cümle", text);
        Assert.Contains(": 3", text);
        Assert.Contains("0 / 5", text);
        Assert.Contains("hedefe 5 cümle kaldı", text);
        Assert.Contains("SON 3 GÜN", text);
        Assert.Contains("06.11.2026", text);
        Assert.Contains("devam ediyor", text);
        Assert.Contains("ULAŞILDI", text);
        Assert.Contains("ulaşılamadı", text);
        Assert.Contains("80,0", text);
        Assert.Contains("%100", text);
    }
}
