using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lingora.Core;
using Lingora.Core.Progress;

namespace Lingora.Desktop.Pages;

/// <summary>
/// Hedef penceresi: bugün x/hedef (büyük), ince ilerleme çubuğu, dört kısa özet ve son 7 gün.
/// </summary>
public partial class ProgressPage : UserControl
{
    private const double DayBarMaxHeight = 56;
    private readonly PracticeSession _session;

    public ProgressPage(PracticeSession session)
    {
        _session = session;
        InitializeComponent();
        Refresh();
    }

    internal ProgressSummary? Summary { get; private set; }

    /// <summary>Bugünkü ilerleme oranı (0–1), çubuğun dolu kısmı.</summary>
    internal double FillRatio { get; private set; }

    public void Refresh()
    {
        var summary = _session.GetProgress(recentDays: 7);
        var today = _session.Today;
        Summary = summary;

        TodayCount.Text = summary.TodayCount.ToString(Ui.Turkish);
        GoalText.Text = $"/ {summary.DailyGoal} cümle";
        RemainingText.Text = summary.GoalReachedToday ? "Hedef tamam" : $"{summary.RemainingToday} kaldı";
        RemainingText.Foreground = summary.GoalReachedToday ? Ui.Brush("AccentBrush") : Ui.Brush("TextMutedBrush");

        FillRatio = Math.Clamp((double)summary.TodayCount / summary.DailyGoal, 0, 1);
        BarFilled.Width = new GridLength(FillRatio, GridUnitType.Star);
        BarRest.Width = new GridLength(1 - FillRatio, GridUnitType.Star);

        Stats.Children.Clear();
        AddStat("Seri", $"{summary.CurrentStreak} gün");
        AddStat("En uzun seri", $"{summary.BestStreak} gün");
        AddStat("Toplam", summary.TotalSentences.ToString("N0", Ui.Turkish));
        AddStat("Ortalama puan", summary.AverageScore is { } avg ? avg.ToString("0", Ui.Turkish) : "–");

        Week.Children.Clear();
        foreach (var day in summary.RecentDays.Reverse())
        {
            Week.Children.Add(DayColumn(day, day.Date == today));
        }

        Footer.Text = summary.TotalSentences == 0
            ? "Henüz değerlendirilmiş çeviri yok. İlk cümleni gönderdiğinde ilerlemen burada görünecek."
            : $"Doğru kabul edilen: %{100.0 * summary.CorrectCount / summary.TotalSentences:0}   ·   "
              + $"TR → EN {summary.TurkishToEnglishCount}   ·   EN → TR {summary.EnglishToTurkishCount}";
    }

    private void AddStat(string label, string value)
    {
        var stack = new StackPanel();
        stack.Children.Add(Ui.Text(value, null, Brushes.White, 26));
        ((TextBlock)stack.Children[0]).FontWeight = FontWeights.Light;
        var caption = Ui.Text(label, "Muted");
        caption.Margin = new Thickness(0, 2, 0, 0);
        stack.Children.Add(caption);
        Stats.Children.Add(stack);
    }

    private static StackPanel DayColumn(DayStat day, bool isToday)
    {
        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

        // Günün hedefe göre doluluğunu gösteren ince dikey çubuk.
        var ratio = Math.Clamp((double)day.Count / Math.Max(1, day.Goal), 0, 1);
        var track = new Grid { Width = 6, Height = DayBarMaxHeight, HorizontalAlignment = HorizontalAlignment.Center };
        track.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = Ui.Brush("GlassBrush") });
        track.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(3),
            Height = Math.Max(day.Count > 0 ? 6 : 0, DayBarMaxHeight * ratio),
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = day.GoalReached ? Ui.Brush("LogoGradient") : Ui.Brush("TextMutedBrush"),
        });
        column.Children.Add(track);

        var count = Ui.Text(day.Count.ToString(Ui.Turkish), null, day.Count > 0 ? Brushes.White : Ui.Brush("TextMutedBrush"), 15);
        count.HorizontalAlignment = HorizontalAlignment.Center;
        count.Margin = new Thickness(0, 8, 0, 0);
        column.Children.Add(count);

        var name = Ui.Text(isToday ? "Bugün" : day.Date.ToString("ddd", Ui.Turkish), "Muted");
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.FontSize = 12;
        if (isToday)
        {
            name.Foreground = Ui.Brush("AccentBrush");
        }

        column.Children.Add(name);
        return column;
    }
}
