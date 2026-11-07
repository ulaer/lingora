using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;
using Lingora.Core;
using Lingora.Core.Corpus;
using Lingora.Core.Gemini;
using Lingora.Core.Models;
using Lingora.Core.Storage;
using Microsoft.Win32;

namespace Lingora.Desktop.Pages;

public enum SettingsSection
{
    ApiKey,
    DataFile,
    General,
    SystemCheck,
}

/// <summary>Ayarlar: API anahtarı, veri dosyası (TXT) yükleme, genel ayarlar ve sistem kontrolü.</summary>
public partial class SettingsPage : UserControl
{
    private readonly PracticeSession _session;
    private readonly GeminiClient _gemini;
    private readonly Dictionary<Difficulty, RadioButton> _difficultyButtons = new();
    private readonly Dictionary<string, RadioButton> _voiceButtons = new();

    public SettingsPage(PracticeSession session, GeminiClient gemini)
    {
        _session = session;
        _gemini = gemini;
        InitializeComponent();

        foreach (var difficulty in Enum.GetValues<Difficulty>())
        {
            var option = new RadioButton
            {
                GroupName = "Difficulty",
                Content = difficulty.DisplayName(),
                Style = (Style)FindResource("Option"),
            };
            _difficultyButtons[difficulty] = option;
            DifficultyOptions.Children.Add(option);
        }

        foreach (var (name, description) in AppSettings.Voices)
        {
            var option = new RadioButton
            {
                GroupName = "Voice",
                Content = $"{name}  ·  {description}",
                Tag = name,
                Style = (Style)FindResource("Option"),
            };
            _voiceButtons[name] = option;
            VoiceOptions.Children.Add(option);
        }

        TtsModelHint.Text =
            $"Varsayılan: {AppSettings.DefaultTtsModel}. Sesli ders önce metin olarak yazılır, sonra bu modelle seslendirilip MP3'e kaydedilir. "
            + "Seslendirmenin ücretsiz kotası sınırlıdır; hata alırsan Bağlantıyı test et listesindeki bir 'tts' modelini yaz.";

        GoalHint.Text =$"cümle / gün  ({AppSettings.MinDailyGoal}–{AppSettings.MaxDailyGoal})";
        ModelHint.Text =
            $"Varsayılan: {AppSettings.DefaultModel} (her zaman güncel Flash modeli). "
            + "Not: 'gemini-1.5-flash' Google tarafından kullanımdan kaldırıldı. "
            + "Hesabınızda kullanılabilen modelleri API Anahtarı > Bağlantıyı test et gösterir.";

        ApiHelpPanel.Child = BuildHelp(HelpTopics.ApiKey);
        DataHelpPanel.Child = BuildHelp(HelpTopics.CorpusFile);

        TabApi.IsChecked = true;
        Refresh();
    }

    /// <summary>Yardım panelini açar/kapatır.</summary>
    internal void ToggleHelp(SettingsSection section, bool? show = null)
    {
        var panel = section == SettingsSection.DataFile ? DataHelpPanel : ApiHelpPanel;
        var visible = show ?? panel.Visibility != Visibility.Visible;
        panel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnHelpClick(object sender, RoutedEventArgs e) =>
        ToggleHelp(sender == DataHelpButton ? SettingsSection.DataFile : SettingsSection.ApiKey);

    private StackPanel BuildHelp(HelpTopic topic)
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Text(topic.Title, "Body", Ui.Brush("TextBrush"), 15));
        ((TextBlock)panel.Children[0]).FontWeight = FontWeights.SemiBold;

        for (var i = 0; i < topic.Steps.Count; i++)
        {
            var row = new Grid { Margin = new Thickness(0, i == 0 ? 12 : 8, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var number = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                Background = Ui.Brush("PrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = (i + 1).ToString(Ui.Turkish),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            row.Children.Add(number);
            var text = Ui.Text(topic.Steps[i]);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            panel.Children.Add(row);
        }

        var link = new TextBlock { Margin = new Thickness(30, 12, 0, 0), FontSize = 14 };
        var hyperlink = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(topic.LinkText + "  ↗"))
        {
            NavigateUri = new Uri(topic.LinkUrl),
        };
        hyperlink.RequestNavigate += OnHyperlink;
        link.Inlines.Add(hyperlink);
        panel.Children.Add(link);

        foreach (var note in topic.Notes)
        {
            var noteText = Ui.Text("•  " + note, "Muted");
            noteText.Margin = new Thickness(0, 10, 0, 0);
            panel.Children.Add(noteText);
        }

        return panel;
    }

    /// <summary>Oturumda pratik ekranını etkileyen bir değişiklik oldu (dosya, zorluk, anahtar).</summary>
    public event EventHandler? SessionChanged;

    /// <summary>Dosya yükleme veya bağlantı testi sürüyor.</summary>
    public bool IsBusy { get; private set; }

    public void ShowSection(SettingsSection section)
    {
        var tab = section switch
        {
            SettingsSection.DataFile => TabData,
            SettingsSection.General => TabGeneral,
            SettingsSection.SystemCheck => TabCheck,
            _ => TabApi,
        };
        tab.IsChecked = true;
        ShowPanelFor(tab);

        // İlk kurulumda (anahtar veya dosya yoksa) yardım kendiliğinden açık gelir.
        if (section == SettingsSection.ApiKey && !_session.GetApiKey().HasKey)
        {
            ToggleHelp(SettingsSection.ApiKey, show: true);
        }
        else if (section == SettingsSection.DataFile && _session.Corpus.Count == 0)
        {
            ToggleHelp(SettingsSection.DataFile, show: true);
        }
    }

    public void Refresh()
    {
        UpdateKeyInfo();
        UpdateCorpusInfo();

        var settings = _session.Settings;
        GoalBox.Text = settings.DailyGoal.ToString(Ui.Turkish);
        ModelBox.Text = settings.Model;
        foreach (var (difficulty, button) in _difficultyButtons)
        {
            button.IsChecked = difficulty == settings.Difficulty;
        }

        foreach (var (name, button) in _voiceButtons)
        {
            button.IsChecked = name == settings.Voice;
        }

        TtsModelBox.Text = settings.TtsModel;
        OrderSequential.IsChecked = settings.Order == SentenceOrder.Sequential;
        OrderRandom.IsChecked = settings.Order == SentenceOrder.Random;
        UpdatePositionInfo();
    }

    private void UpdatePositionInfo()
    {
        var total = _session.Corpus.Count;
        PositionInfo.Text = total == 0
            ? "Önce cümle dosyasını yükleyin."
            : $"Şu an {_session.CurrentLine.ToString("N0", Ui.Turkish)}. satırdasınız (toplam {total.ToString("N0", Ui.Turkish)}). "
              + "Örneğin ilk 10.000 satırı atlamak için 10001 yazın. Kaldığınız yer kaydedilir; uygulamayı kapatıp açınca oradan devam edersiniz.";
    }

    /// <summary>Kullanıcının yazdığı satırdan (1 tabanlı; "10.001" gibi noktalı yazım da olur) sıralı olarak başlar.</summary>
    internal void StartFromLine(string? text)
    {
        var digits = (text ?? string.Empty).Replace(".", string.Empty).Replace(",", string.Empty).Replace(" ", string.Empty);
        if (!int.TryParse(digits, out var line))
        {
            SetStatus(StartLineStatus, "Lütfen bir satır numarası yazın (ör. 10001).", isWarning: true);
            return;
        }

        try
        {
            _session.StartFromLine(line);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or InvalidOperationException)
        {
            var message = ex is ArgumentOutOfRangeException
                ? $"Satır 1 ile {_session.Corpus.Count.ToString("N0", Ui.Turkish)} arasında olmalı."
                : ex.Message;
            SetStatus(StartLineStatus, message, isWarning: true);
            return;
        }

        StartLineBox.Clear();
        OrderSequential.IsChecked = true;
        UpdatePositionInfo();
        SetStatus(StartLineStatus, $"{_session.CurrentLine.ToString("N0", Ui.Turkish)}. satırdan devam ediliyor.", isWarning: false);
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnStartLine(object sender, RoutedEventArgs e) => StartFromLine(StartLineBox.Text);

    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton tab)
        {
            ShowPanelFor(tab);
        }
    }

    private void ShowPanelFor(RadioButton tab)
    {
        if (ApiPanel is null)
        {
            return; // InitializeComponent sırasında
        }

        ApiPanel.Visibility = tab == TabApi ? Visibility.Visible : Visibility.Collapsed;
        DataPanel.Visibility = tab == TabData ? Visibility.Visible : Visibility.Collapsed;
        GeneralPanel.Visibility = tab == TabGeneral ? Visibility.Visible : Visibility.Collapsed;
        CheckPanel.Visibility = tab == TabCheck ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- API anahtarı ----------

    private void UpdateKeyInfo()
    {
        var lookup = _session.GetApiKey();
        CurrentKeyText.Text = lookup.Source switch
        {
            ApiKeySource.Settings => $"{ApiKeyStore.Mask(lookup.Key)}   (bu bilgisayarda şifreli)",
            ApiKeySource.Environment => $"{ApiKeyStore.Mask(lookup.Key)}   ({ApiKeyStore.EnvironmentVariable} ortam değişkeni)",
            _ => "Kayıtlı anahtar yok.",
        };
        CurrentKeyText.Foreground = lookup.HasKey ? Ui.Brush("TextBrush") : Ui.Brush("WarnBrush");
        ClearKeyButton.IsEnabled = lookup.Source == ApiKeySource.Settings;

        if (lookup.Error is not null)
        {
            SetStatus(ApiStatus, lookup.Error, isWarning: true);
        }
    }

    internal void SaveKey(string key)
    {
        if (ApiKeyStore.Validate(key) is { } error)
        {
            SetStatus(ApiStatus, error, isWarning: true);
            return;
        }

        _session.SetApiKey(key);
        KeyBox.Clear();
        UpdateKeyInfo();
        SetStatus(ApiStatus, "Anahtar şifrelenerek kaydedildi. İsterseniz 'Bağlantıyı test et' ile doğrulayın.", isWarning: false);
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSaveKey(object sender, RoutedEventArgs e) => SaveKey(KeyBox.Password);

    private async void OnTestKey(object sender, RoutedEventArgs e)
    {
        var key = string.IsNullOrWhiteSpace(KeyBox.Password) ? _session.GetApiKey().Key : KeyBox.Password.Trim();
        if (string.IsNullOrEmpty(key))
        {
            SetStatus(ApiStatus, "Test edilecek anahtar yok. Önce anahtarı yazın.", isWarning: true);
            return;
        }

        var model = _session.Settings.Model;
        SetBusy(true);
        SetStatus(ApiStatus, "Test ediliyor…", isWarning: false);
        try
        {
            var models = await _gemini.ListModelsAsync(key);
            var hasModel = models.Contains(model, StringComparer.OrdinalIgnoreCase);
            var flash = string.Join(", ", models.Where(m => m.Contains("flash", StringComparison.OrdinalIgnoreCase)).Take(6));
            SetStatus(
                ApiStatus,
                $"Bağlantı başarılı: {models.Count} model kullanılabilir.\n"
                + (hasModel
                    ? $"Ayarlı model '{model}' listede."
                    : $"Ayarlı model '{model}' listede görünmüyor (takma ad olabilir). Flash modeller: {flash}"),
                isWarning: false);
        }
        catch (GeminiException ex)
        {
            SetStatus(ApiStatus, "Başarısız: " + ex.Message, isWarning: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnClearKey(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            Window.GetWindow(this)!,
            "Kayıtlı API anahtarı silinsin mi?",
            "Anahtarı sil",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _session.ClearApiKey();
        UpdateKeyInfo();
        SetStatus(ApiStatus, "Kayıtlı anahtar silindi.", isWarning: false);
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnHyperlink(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    // ---------- Veri dosyası ----------

    private void UpdateCorpusInfo()
    {
        var count = _session.Corpus.Count;
        CorpusInfo.Text = count == 0
            ? "Henüz cümle dosyası yüklenmedi."
            : $"{count.ToString("N0", Ui.Turkish)} cümle çifti";
        CorpusInfo.Foreground = count == 0 ? Ui.Brush("WarnBrush") : Ui.Brush("TextBrush");
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Cümle dosyasını seçin",
            Filter = "Metin dosyaları (*.txt;*.tsv)|*.txt;*.tsv|Tüm dosyalar (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            PathBox.Text = dialog.FileName;
        }
    }

    internal async Task ImportAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            SetStatus(ImportStatus, "Önce dosya yolunu yazın veya 'Gözat…' ile seçin.", isWarning: true);
            return;
        }

        SetBusy(true);
        SetStatus(ImportStatus, "Yükleniyor… (büyük dosyalarda birkaç saniye sürebilir)", isWarning: false);
        try
        {
            var result = await Task.Run(() => _session.ImportCorpus(path));
            SetStatus(
                ImportStatus,
                $"Yüklendi: {result.Imported.ToString("N0", Ui.Turkish)} cümle çifti.\n"
                + $"Atlanan satır: {result.Skipped}  ·  Tekrar eden: {result.Duplicates}  ·  Kodlama: {result.EncodingName}",
                isWarning: false);
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            SetStatus(ImportStatus, "Yüklenemedi: " + ex.Message, isWarning: true);
        }
        finally
        {
            SetBusy(false);
            UpdateCorpusInfo();
        }
    }

    private async void OnImport(object sender, RoutedEventArgs e) => await ImportAsync(PathBox.Text);

    // ---------- Genel ----------

    internal void SaveGeneral(string goalText, Difficulty difficulty, string model, SentenceOrder? order = null, string? voice = null, string? ttsModel = null)
    {
        if (!int.TryParse(goalText?.Trim(), out var goal)
            || goal < AppSettings.MinDailyGoal
            || goal > AppSettings.MaxDailyGoal)
        {
            SetStatus(GeneralStatus, $"Günlük hedef {AppSettings.MinDailyGoal} ile {AppSettings.MaxDailyGoal} arasında bir sayı olmalı.", isWarning: true);
            return;
        }

        if (order is { } newOrder)
        {
            _session.SetOrder(newOrder);
        }

        _session.UpdateGeneralSettings(goal, difficulty, model);
        if (voice is not null || ttsModel is not null)
        {
            _session.UpdateSpeechSettings(voice ?? _session.Settings.Voice, ttsModel ?? _session.Settings.TtsModel);
        }

        Refresh();
        SetStatus(GeneralStatus, _session.Notice ?? "Ayarlar kaydedildi.", isWarning: false);
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSaveGeneral(object sender, RoutedEventArgs e)
    {
        var difficulty = _difficultyButtons.FirstOrDefault(p => p.Value.IsChecked == true).Key;
        var order = OrderRandom.IsChecked == true ? SentenceOrder.Random : SentenceOrder.Sequential;
        var voice = _voiceButtons.FirstOrDefault(p => p.Value.IsChecked == true).Key;
        SaveGeneral(GoalBox.Text, difficulty, ModelBox.Text, order, voice, TtsModelBox.Text);
    }

    // ---------- Sistem kontrolü ----------

    private async void OnRunCheck(object sender, RoutedEventArgs e)
    {
        var live = LiveCheckBox.IsChecked == true;
        SetBusy(true);
        CheckOutput.Text = live ? "Kontrol ediliyor (Gemini'ye test isteği gönderiliyor)…" : "Kontrol ediliyor…";
        try
        {
            var lines = await Diagnostics.RunAsync(_session.Paths, _gemini, live);
            var hasError = lines.Any(l => l.Level == CheckLevel.Error);
            CheckOutput.Text = Diagnostics.Format(lines)
                + Environment.NewLine
                + (hasError ? "Sonuç: HATA var." : "Sonuç: Çalışmaya hazır.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ---------- Ortak ----------

    private void SetBusy(bool busy)
    {
        IsBusy = busy;
        foreach (var button in new[] { SaveKeyButton, TestKeyButton, ImportButton, BrowseButton, SaveGeneralButton, StartLineButton, RunCheckButton })
        {
            button.IsEnabled = !busy;
        }
    }

    private static void SetStatus(TextBlock target, string text, bool isWarning)
    {
        target.Text = text;
        target.Foreground = isWarning ? Ui.Brush("WarnBrush") : Ui.Brush("TextBrush");
    }
}
