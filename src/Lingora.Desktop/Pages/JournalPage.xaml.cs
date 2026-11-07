using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Lingora.Core;
using Lingora.Core.Journal;
using Lingora.Core.Models;
using Microsoft.Win32;

namespace Lingora.Desktop.Pages;

/// <summary>
/// Günlük: her günün hataları, ipuçları ve açıklamaları; o günün AI ders notu
/// (bir kez üretilir, sonra kayıttan açılır) ve PDF olarak kaydetme.
/// </summary>
public partial class JournalPage : UserControl
{
    private readonly PracticeSession _session;
    private readonly DispatcherTimer _dots;
    private int _dotFrame;

    public JournalPage(PracticeSession session)
    {
        _session = session;
        InitializeComponent();
        _dots = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _dots.Tick += (_, _) =>
        {
            _dotFrame = (_dotFrame + 1) % 3;
            LessonStatus.Text = "Ders notu hazırlanıyor " + new string('.', _dotFrame + 1);
        };
        Refresh();
    }

    /// <summary>Seçili gün.</summary>
    internal DateOnly SelectedDay { get; private set; }

    /// <summary>Listelenen günler.</summary>
    internal IReadOnlyList<JournalDay> Days { get; private set; } = [];

    /// <summary>Gösterilen ders notu (yoksa null).</summary>
    internal LessonNote? ShownLesson { get; private set; }

    internal bool IsBusy { get; private set; }

    /// <summary>PDF kayıt yolu seçici. Testler bunu değiştirip iletişim kutusu açmadan kaydeder.</summary>
    internal Func<string, string?> SavePathPicker { get; set; } = DefaultSavePathPicker;

    /// <summary>Günleri yeniden okur; seçili günü korur (yoksa bugün).</summary>
    public void Refresh()
    {
        var today = _session.Today;
        var days = _session.Journal.Days().ToList();
        if (days.All(d => d.Date != today))
        {
            days.Insert(0, _session.Journal.Summarize(today));
        }

        Days = days;
        if (SelectedDay == default || days.All(d => d.Date != SelectedDay))
        {
            SelectedDay = today;
        }

        DayList.Children.Clear();
        foreach (var day in days)
        {
            DayList.Children.Add(DayButton(day, today));
        }

        ShowDay(SelectedDay);
    }

    internal void SelectDay(DateOnly date)
    {
        SelectedDay = date;
        foreach (var child in DayList.Children.OfType<RadioButton>())
        {
            child.IsChecked = child.Tag is DateOnly d && d == date;
        }

        ShowDay(date);
    }

    private RadioButton DayButton(JournalDay day, DateOnly today)
    {
        var title = day.Date == today ? "Bugün" : day.Date == today.AddDays(-1) ? "Dün" : day.Date.ToString("d MMMM, dddd", Ui.Turkish);
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = title, FontSize = 14.5, Foreground = Brushes.White });
        var details = day.Entries == 0
            ? "kayıt yok"
            : $"{day.Entries} çeviri · {day.Mistakes} hata"
              + (day.HasLesson ? "  ·  ✓ not" : string.Empty)
              + (day.HasAudio ? "  ·  ♪ ses" : string.Empty);
        content.Children.Add(new TextBlock
        {
            Text = details,
            FontSize = 12,
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = day.HasLesson || day.HasAudio ? Ui.Brush("AccentBrush") : Ui.Brush("TextMutedBrush"),
        });

        var button = new RadioButton
        {
            GroupName = "JournalDays",
            Style = (Style)FindResource("DayButton"),
            Content = content,
            Tag = day.Date,
            IsChecked = day.Date == SelectedDay,
        };
        button.Checked += (_, _) =>
        {
            if (button.Tag is DateOnly date && date != SelectedDay)
            {
                SelectedDay = date;
                ShowDay(date);
            }
        };
        return button;
    }

    private void ShowDay(DateOnly date)
    {
        var summary = _session.Journal.Summarize(date);
        DayTitle.Text = date.ToString("d MMMM yyyy, dddd", Ui.Turkish);
        DayStats.Text = summary.Entries == 0
            ? "Bu gün için kayıt yok."
            : $"{summary.Entries} çeviri  ·  {summary.Mistakes} hata  ·  {summary.Tips} ipucu  ·  {summary.Explanations} ayrıntılı açıklama";

        ShowLesson(_session.Lessons.Load(date), summary);
        ShowAudio(date, summary);
        ShowRecords(_session.Journal.LoadDay(date));
        ContentScroll.ScrollToTop();
    }

    // ---------- Ders notu ----------

    private void ShowLesson(LessonNote? note, JournalDay summary)
    {
        ShownLesson = note;
        LessonStatus.Visibility = Visibility.Collapsed;
        if (note is null)
        {
            LessonEmpty.Visibility = Visibility.Visible;
            LessonContent.Visibility = Visibility.Collapsed;
            LessonContent.Children.Clear();
            CreateLessonButton.IsEnabled = summary.Entries > 0 && !IsBusy;
            LessonIntro.Text = summary.Entries == 0
                ? "Bu gün için kayıt yok. Çeviri yaptıkça hataların, ipuçların ve istediğin açıklamalar buraya kaydedilir; sonra bunlardan bir ders notu oluşturabilirsin."
                : $"Bu günün {summary.Entries} çevirisinden; hataların, ipuçların ve istediğin açıklamalardan sana özel bir ders notu hazırlanır. Not bir kez oluşturulur ve kaydedilir; sonra her açışında hemen gelir.";
            return;
        }

        LessonEmpty.Visibility = Visibility.Collapsed;
        LessonContent.Visibility = Visibility.Visible;
        LessonContent.Children.Clear();
        RenderLesson(note, summary.Entries);
    }

    private void RenderLesson(LessonNote note, int currentEntries)
    {
        var header = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Ui.Text(note.Title, "Body", Brushes.White, 21);
        title.FontWeight = FontWeights.SemiBold;
        header.Children.Add(title);
        var pdf = new Button
        {
            Style = (Style)FindResource("SecondaryButton"),
            Content = "PDF olarak kaydet",
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(12, 0, 0, 0),
        };
        pdf.Click += (_, _) => SavePdf();
        Grid.SetColumn(pdf, 1);
        header.Children.Add(pdf);
        LessonContent.Children.Add(header);
        SavePdfButton = pdf;

        if (note.Summary.Length > 0)
        {
            var summary = Ui.Text(note.Summary, "Body", null, 15);
            LessonContent.Children.Add(new Border { Style = (Style)FindResource("HelpPanel"), Visibility = Visibility.Visible, Child = summary, Margin = new Thickness(0, 8, 0, 4) });
        }

        if (note.Topics.Count > 0)
        {
            Section("KONULAR");
            var number = 0;
            foreach (var topic in note.Topics)
            {
                number++;
                var topicTitle = Ui.Text($"{number}. {topic.Title}", "Body", Ui.Brush("AccentBrush"), 16.5);
                topicTitle.FontWeight = FontWeights.SemiBold;
                topicTitle.Margin = new Thickness(0, 6, 0, 4);
                LessonContent.Children.Add(topicTitle);
                LessonContent.Children.Add(Selectable(topic.Explanation, 15));
                foreach (var example in topic.Examples)
                {
                    var line = Selectable("•  " + example, 14, Ui.Brush("TextMutedBrush"));
                    line.FontStyle = FontStyles.Italic;
                    line.Margin = new Thickness(14, 4, 0, 0);
                    LessonContent.Children.Add(line);
                }
            }
        }

        if (note.Mistakes.Count > 0)
        {
            Section("GÜNÜN HATALARI");
            foreach (var mistake in note.Mistakes)
            {
                var card = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
                var line = new TextBlock { FontSize = 15, TextWrapping = TextWrapping.Wrap };
                line.Inlines.Add(new System.Windows.Documents.Run(mistake.Wrong) { Foreground = Ui.Brush("WarnBrush") });
                line.Inlines.Add(new System.Windows.Documents.Run("  →  ") { Foreground = Ui.Brush("TextMutedBrush") });
                line.Inlines.Add(new System.Windows.Documents.Run(mistake.Right) { Foreground = Brushes.White, FontWeight = FontWeights.SemiBold });
                card.Children.Add(line);
                if (mistake.Why.Length > 0)
                {
                    var why = Ui.Text(mistake.Why, "Muted");
                    why.FontSize = 13.5;
                    why.Margin = new Thickness(0, 3, 0, 0);
                    card.Children.Add(why);
                }

                LessonContent.Children.Add(card);
            }
        }

        if (note.Practice.Count > 0)
        {
            Section("ALIŞTIRMA");
            var number = 0;
            foreach (var exercise in note.Practice)
            {
                number++;
                var line = Selectable($"{number}.  {exercise}", 15);
                line.Margin = new Thickness(0, 0, 0, 6);
                LessonContent.Children.Add(line);
            }
        }

        var meta = $"{note.EntryCount} çeviriden {note.CreatedAt.ToLocalTime().ToString("d MMMM HH:mm", Ui.Turkish)} tarihinde oluşturuldu.";
        if (currentEntries > note.EntryCount)
        {
            meta += $" Bu nottan sonra {currentEntries - note.EntryCount} yeni çeviri eklendi.";
        }

        var metaText = Ui.Text(meta, "Muted");
        metaText.FontSize = 12;
        metaText.Margin = new Thickness(0, 18, 0, 6);
        LessonContent.Children.Add(metaText);

        // Not üretildikten sonra o gün yeni çeviri yapıldıysa yeniden üretilebilir.
        RegenerateLessonButton = null;
        if (currentEntries > note.EntryCount)
        {
            var regenerate = new Button
            {
                Style = (Style)FindResource("PrimaryButton"),
                Content = $"Ders notunu yenile  ({currentEntries - note.EntryCount} yeni çeviriyle)",
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 6, 0, 6),
                IsEnabled = !IsBusy,
            };
            regenerate.Click += async (_, _) => await CreateLessonAsync(regenerate: true);
            LessonContent.Children.Add(regenerate);
            RegenerateLessonButton = regenerate;
        }
    }

    /// <summary>Gösterilen notun "Ders notunu yenile" düğmesi (yeni çeviri yoksa null).</summary>
    internal Button? RegenerateLessonButton { get; private set; }

    /// <summary>Gösterilen notun "PDF olarak kaydet" düğmesi (not yoksa null).</summary>
    internal Button? SavePdfButton { get; private set; }

    private void Section(string text) =>
        LessonContent.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("SectionLabel") });

    private TextBox Selectable(string text, double size, Brush? brush = null) => new()
    {
        Text = text,
        Style = (Style)FindResource("ReadOnlyText"),
        FontSize = size,
        Foreground = brush ?? Ui.Brush("TextBrush"),
    };

    /// <summary>
    /// Seçili günün ders notunu üretir; daha önce üretildiyse kayıttan açar.
    /// <paramref name="regenerate"/>: o gün yeni çeviri yapıldıysa notu yeniden üretir.
    /// </summary>
    internal async Task CreateLessonAsync(bool regenerate = false)
    {
        if (IsBusy)
        {
            return;
        }

        var date = SelectedDay;
        IsBusy = true;
        CreateLessonButton.IsEnabled = false;
        if (RegenerateLessonButton is { } button)
        {
            button.IsEnabled = false;
        }

        LessonStatus.Visibility = Visibility.Visible;
        LessonStatus.Foreground = Ui.Brush("TextMutedBrush");
        LessonStatus.Text = "Ders notu hazırlanıyor .";
        _dots.Start();

        LessonOutcome outcome;
        try
        {
            outcome = await _session.GetOrCreateLessonAsync(date, regenerate);
        }
        catch (Exception ex)
        {
            outcome = new LessonOutcome(false, null, $"Beklenmeyen hata: {ex.Message}");
        }
        finally
        {
            _dots.Stop();
            IsBusy = false;
        }

        if (date != SelectedDay)
        {
            Refresh();
            return;
        }

        if (outcome.Success && outcome.Note is not null)
        {
            Refresh();
            if (outcome.Error is { } warning)
            {
                LessonStatus.Visibility = Visibility.Visible;
                LessonStatus.Foreground = Ui.Brush("WarnBrush");
                LessonStatus.Text = warning;
            }
        }
        else
        {
            CreateLessonButton.IsEnabled = true;
            if (RegenerateLessonButton is { } retry)
            {
                retry.IsEnabled = true;
            }

            LessonStatus.Foreground = Ui.Brush("WarnBrush");
            LessonStatus.Text = outcome.Error ?? "Ders notu oluşturulamadı.";
        }
    }

    private async void OnCreateLessonClick(object sender, RoutedEventArgs e) => await CreateLessonAsync();

    /// <summary>Gösterilen notu PDF olarak kaydeder; yol seçilmezse bir şey yapmaz.</summary>
    internal string? SavePdf()
    {
        if (ShownLesson is not { } note)
        {
            return null;
        }

        var path = SavePathPicker(LessonPdfExporter.SuggestedFileName(note));
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        LessonStatus.Visibility = Visibility.Visible;
        try
        {
            LessonPdfExporter.Export(note, path);
            LessonStatus.Foreground = Ui.Brush("AccentBrush");
            LessonStatus.Text = "PDF kaydedildi: " + path;
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LessonStatus.Foreground = Ui.Brush("WarnBrush");
            LessonStatus.Text = "PDF kaydedilemedi: " + ex.Message;
            return null;
        }
    }

    private static string? DefaultSavePathPicker(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Ders notunu PDF olarak kaydet",
            FileName = suggestedName,
            DefaultExt = ".pdf",
            Filter = "PDF dosyası (*.pdf)|*.pdf",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        return dialog.ShowDialog(Application.Current?.MainWindow) == true ? dialog.FileName : null;
    }

    // ---------- Günün kayıtları ----------

    private void ShowRecords(IReadOnlyList<JournalEntry> entries)
    {
        RecordsPanel.Children.Clear();
        if (entries.Count == 0)
        {
            var empty = Ui.Text("Bu gün için kayıt yok.", "Muted");
            empty.FontSize = 15;
            RecordsPanel.Children.Add(empty);
            return;
        }

        foreach (var entry in entries.OrderByDescending(e => e.Timestamp))
        {
            RecordsPanel.Children.Add(RecordCard(entry));
        }
    }

    private Border RecordCard(JournalEntry entry)
    {
        var body = new StackPanel();
        var head = $"{entry.Timestamp.ToLocalTime():HH:mm}  ·  {entry.Direction.DisplayName()}"
                   + (entry.Score is { } score ? $"  ·  {score} / 100" : string.Empty);
        var headText = Ui.Text(head, "Muted");
        headText.FontSize = 12;
        body.Children.Add(headText);

        var source = Selectable(entry.Source, 16, Brushes.White);
        source.Margin = new Thickness(0, 4, 0, 2);
        body.Children.Add(source);
        body.Children.Add(Selectable("Sen: " + entry.UserTranslation, 14, Ui.Brush("TextMutedBrush")));
        if (entry.CorrectedTranslation.Length > 0 && entry.CorrectedTranslation != entry.UserTranslation)
        {
            body.Children.Add(Selectable("Düzeltilmiş: " + entry.CorrectedTranslation, 14, Ui.Brush("AccentBrush")));
        }

        for (var i = 0; i < entry.Mistakes.Count; i++)
        {
            AddFeedback(body, "•  " + entry.Mistakes[i], entry.FindExplanation(FeedbackKind.Mistake, i), Ui.Brush("TextBrush"));
        }

        foreach (var minor in entry.MinorIssues)
        {
            AddFeedback(body, "•  " + minor + "  (küçük hata)", null, Ui.Brush("TextMutedBrush"));
        }

        if (entry.Tip.Length > 0)
        {
            AddFeedback(body, "İpucu: " + entry.Tip, entry.FindExplanation(FeedbackKind.Tip, 0), Ui.Brush("TextBrush"));
        }

        return new Border
        {
            Background = Ui.Brush("GlassBrush"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18, 12, 18, 14),
            Margin = new Thickness(0, 0, 0, 10),
            Child = body,
        };
    }

    private void AddFeedback(StackPanel body, string text, ItemExplanation? explanation, Brush brush)
    {
        var line = Selectable(text, 14.5, brush);
        line.Margin = new Thickness(0, 6, 0, 0);
        body.Children.Add(line);
        if (explanation is not null)
        {
            // Çalışma ekranındaki gibi: kutusuz, soluk ve hafif içeriden yazı.
            var detail = Selectable(explanation.Text, 13.5, Ui.Brush("TextMutedBrush"));
            detail.Margin = new Thickness(14, 4, 0, 4);
            body.Children.Add(detail);
        }
    }

    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        if (LessonPanel is null)
        {
            return; // InitializeComponent sırasında
        }

        LessonPanel.Visibility = TabLesson.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        AudioPanel.Visibility = TabAudio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        RecordsPanel.Visibility = TabRecords.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- Sesli ders ----------

    /// <summary>PCM → MP3 kodlayıcısı (testlerde değiştirilebilir).</summary>
    internal IAudioEncoder AudioEncoder { get; set; } = new Mp3Encoder();

    /// <summary>MP3 kayıt yolu seçici. Testler bunu değiştirip iletişim kutusu açmadan kaydeder.</summary>
    internal Func<string, string?> SaveMp3PathPicker { get; set; } = DefaultMp3PathPicker;

    /// <summary>Gösterilen sesli ders (yoksa null).</summary>
    internal AudioLesson? ShownAudio { get; private set; }

    internal string? ShownAudioPath { get; private set; }

    internal bool IsPlaying { get; private set; }

    private MediaPlayer? _player;
    private DispatcherTimer? _playTimer;

    private void ShowAudio(DateOnly date, JournalDay summary)
    {
        StopPlayback();
        var lesson = _session.AudioLessons.Load(date);
        ShownAudio = lesson;
        ShownAudioPath = lesson is null ? null : _session.AudioLessons.AudioFile(date);
        AudioStatus.Visibility = Visibility.Collapsed;

        if (lesson is null)
        {
            AudioEmpty.Visibility = Visibility.Visible;
            AudioContent.Visibility = Visibility.Collapsed;
            CreateAudioButton.IsEnabled = summary.Entries > 0 && !IsBusy;
            AudioIntro.Text = summary.Entries == 0
                ? "Bu gün için kayıt yok. Çeviri yaptıkça o günün hatalarından ve ipuçlarından dinleyebileceğin bir sesli ders hazırlanabilir."
                : $"Bu günün {summary.Entries} çevirisinden, öğretmen anlatımıyla 3-4 dakikalık bir sesli ders hazırlanır: "
                  + "önce yalnızca metin olarak yazılır, sonra Gemini sesiyle seslendirilip MP3 olarak kaydedilir. "
                  + "Bir kez oluşturulur; sonra her açışında hemen dinleyebilirsin.";
            return;
        }

        AudioEmpty.Visibility = Visibility.Collapsed;
        AudioContent.Visibility = Visibility.Visible;
        AudioTitle.Text = "Sesli ders · " + date.ToString("d MMMM yyyy", Ui.Turkish);
        AudioScriptText.Text = lesson.Script;
        AudioProgressFill.Width = 0;
        AudioTime.Text = $"0:00 / {Clock(lesson.DurationSeconds)}";
        PlayGlyph.Text = "";

        var voice = AppSettingsVoiceLabel(lesson.Voice);
        AudioMeta.Text = $"{lesson.EntryCount} çeviriden {lesson.CreatedAt.ToLocalTime().ToString("d MMMM HH:mm", Ui.Turkish)} tarihinde oluşturuldu · ses: {voice}"
                         + (summary.Entries > lesson.EntryCount ? $" · bu dersten sonra {summary.Entries - lesson.EntryCount} yeni çeviri eklendi." : ".");
        RegenerateAudioButton.Visibility = summary.Entries > lesson.EntryCount ? Visibility.Visible : Visibility.Collapsed;
        RegenerateAudioButton.Content = $"Sesli dersi yenile  ({summary.Entries - lesson.EntryCount} yeni çeviriyle)";
        RegenerateAudioButton.IsEnabled = !IsBusy;
    }

    private static string AppSettingsVoiceLabel(string voice)
    {
        var match = Lingora.Core.Models.AppSettings.Voices.FirstOrDefault(v => v.Name == voice);
        return match.Name is null ? voice : $"{match.Name} ({match.Description.ToLower(Ui.Turkish)})";
    }

    internal static string Clock(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));
        return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : $"{(int)span.TotalMinutes}:{span.Seconds:00}";
    }

    /// <summary>Seçili günün sesli dersini üretir; varsa kayıttan açar.</summary>
    internal async Task CreateAudioAsync(bool regenerate = false)
    {
        if (IsBusy)
        {
            return;
        }

        var date = SelectedDay;
        IsBusy = true;
        StopPlayback();
        CreateAudioButton.IsEnabled = false;
        RegenerateAudioButton.IsEnabled = false;
        AudioStatus.Visibility = Visibility.Visible;
        AudioStatus.Foreground = Ui.Brush("TextMutedBrush");

        var step = "Hazırlanıyor";
        var frame = 0;
        void Render() => AudioStatus.Text = step + " " + new string('.', frame % 3 + 1);
        Render();
        var dots = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        dots.Tick += (_, _) =>
        {
            frame++;
            Render();
        };
        dots.Start();
        var progress = new Progress<string>(text =>
        {
            step = text;
            Render();
        });

        AudioLessonOutcome outcome;
        try
        {
            outcome = await _session.GetOrCreateAudioLessonAsync(date, AudioEncoder, regenerate, progress);
        }
        catch (Exception ex)
        {
            outcome = new AudioLessonOutcome(false, null, null, $"Beklenmeyen hata: {ex.Message}");
        }
        finally
        {
            dots.Stop();
            IsBusy = false;
        }

        if (outcome.Success)
        {
            Refresh();
            TabAudio.IsChecked = true;
        }
        else
        {
            CreateAudioButton.IsEnabled = true;
            RegenerateAudioButton.IsEnabled = true;
            AudioStatus.Foreground = Ui.Brush("WarnBrush");
            AudioStatus.Text = outcome.Error ?? "Sesli ders oluşturulamadı.";
        }
    }

    private async void OnCreateAudioClick(object sender, RoutedEventArgs e) => await CreateAudioAsync();

    private async void OnRegenerateAudioClick(object sender, RoutedEventArgs e) => await CreateAudioAsync(regenerate: true);

    /// <summary>Oynat / duraklat.</summary>
    internal void TogglePlayback()
    {
        if (ShownAudioPath is null || !File.Exists(ShownAudioPath))
        {
            return;
        }

        if (_player is null)
        {
            _player = new MediaPlayer();
            _player.MediaEnded += (_, _) =>
            {
                StopPlayback();
                UpdatePlaybackUi();
            };
            _player.Open(new Uri(ShownAudioPath));
            _playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _playTimer.Tick += (_, _) => UpdatePlaybackUi();
        }

        if (IsPlaying)
        {
            _player.Pause();
            IsPlaying = false;
            _playTimer?.Stop();
        }
        else
        {
            _player.Play();
            IsPlaying = true;
            _playTimer?.Start();
        }

        UpdatePlaybackUi();
    }

    private void UpdatePlaybackUi()
    {
        PlayGlyph.Text = IsPlaying ? "" : "";
        PlayButton.ToolTip = IsPlaying ? "Duraklat" : "Dinle";
        if (_player is null || ShownAudio is null)
        {
            AudioProgressFill.Width = 0;
            return;
        }

        var total = _player.NaturalDuration.HasTimeSpan ? _player.NaturalDuration.TimeSpan.TotalSeconds : ShownAudio.DurationSeconds;
        var position = _player.Position.TotalSeconds;
        AudioTime.Text = $"{Clock(position)} / {Clock(total)}";
        var track = (AudioProgressFill.Parent as FrameworkElement)?.ActualWidth ?? 0;
        AudioProgressFill.Width = total > 0 ? Math.Clamp(position / total, 0, 1) * track : 0;
    }

    private void StopPlayback()
    {
        _playTimer?.Stop();
        _playTimer = null;
        if (_player is not null)
        {
            _player.Stop();
            _player.Close();
            _player = null;
        }

        IsPlaying = false;
    }

    private void OnPlayClick(object sender, RoutedEventArgs e) => TogglePlayback();

    /// <summary>Sesli dersin MP3'ünü seçilen yere kopyalar; yol seçilmezse bir şey yapmaz.</summary>
    internal string? SaveMp3()
    {
        if (ShownAudioPath is null || !File.Exists(ShownAudioPath))
        {
            return null;
        }

        var name = $"Lingora-sesli-ders-{SelectedDay:yyyy-MM-dd}.mp3";
        var path = SaveMp3PathPicker(name);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        AudioStatus.Visibility = Visibility.Visible;
        try
        {
            if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } folder)
            {
                Directory.CreateDirectory(folder);
            }

            File.Copy(ShownAudioPath, path, overwrite: true);
            AudioStatus.Foreground = Ui.Brush("AccentBrush");
            AudioStatus.Text = "MP3 kaydedildi: " + path;
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AudioStatus.Foreground = Ui.Brush("WarnBrush");
            AudioStatus.Text = "MP3 kaydedilemedi: " + ex.Message;
            return null;
        }
    }

    private void OnSaveMp3Click(object sender, RoutedEventArgs e) => SaveMp3();

    private static string? DefaultMp3PathPicker(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Sesli dersi MP3 olarak kaydet",
            FileName = suggestedName,
            DefaultExt = ".mp3",
            Filter = "MP3 ses dosyası (*.mp3)|*.mp3",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        };
        return dialog.ShowDialog(Application.Current?.MainWindow) == true ? dialog.FileName : null;
    }

    /// <summary>Pencere kapanırken çalan sesi durdurur.</summary>
    internal void StopAudio() => StopPlayback();
}
