using System.IO;
using System.Windows;
using System.Windows.Controls;
using Lingora.Core.Game;
using System.Windows.Input;
using Lingora.Core;
using Lingora.Core.Gemini;
using Lingora.Core.Models;
using Lingora.Desktop.Pages;

namespace Lingora.Desktop;

/// <summary>
/// Ana pencere: çalışma ekranı her zaman açık. Üst çubukta logo, kısa mod seçici,
/// İlerleme (hedef) ve Ayarlar butonları; bu ikisi küçük ek pencerede açılır.
/// </summary>
public partial class MainWindow : Window
{
    private readonly PracticeSession _session;
    private bool _initializing = true;

    public MainWindow(PracticeSession session, GeminiClient gemini)
    {
        _session = session;
        InitializeComponent();

        Practice = new PracticePage(session);
        Progress = new ProgressPage(session);
        Settings = new SettingsPage(session, gemini);
        Vocabulary = new VocabularyPage(session);
        Journal = new JournalPage(session);
        PageHost.Content = Practice;

        Practice.OpenDataSettingsRequested += (_, _) => OpenSettings(SettingsSection.DataFile);
        Practice.OpenApiSettingsRequested += (_, _) => OpenSettings(SettingsSection.ApiKey);
        Practice.Evaluated += (_, _) => UpdateToday();
        Vocabulary.Changed += (_, _) => UpdateToday();
        Settings.SessionChanged += (_, _) =>
        {
            Practice.Refresh();
            UpdateToday();
        };

        UpdateModeButtons();
        UpdateToday();
        _initializing = false;

        WindowFrame.Apply(this, 68);
        StateChanged += (_, _) => UpdateMaximizeButton();
        PreviewKeyDown += OnWindowPreviewKeyDown;
        Loaded += (_, _) => Practice.FocusInput();
    }

    internal PracticePage Practice { get; }

    internal ProgressPage Progress { get; }

    internal SettingsPage Settings { get; }

    internal VocabularyPage Vocabulary { get; }

    internal JournalPage Journal { get; }

    /// <summary>Oyun sayfası; ilk açılışta oluşturulur (sahne kütüphanesi o zaman yüklenir).</summary>
    internal GamePage? Game { get; private set; }

    /// <summary>Oyun kütüphanesinin klasörü (testler değiştirebilir); null ise uygulama klasöründe aranır.</summary>
    internal string? GameAssetsDirectory { get; set; }

    /// <summary>Oyunda sahne seçimi için rastgele üreteç (testlerde sabit tohum).</summary>
    internal Random? GameRandom { get; set; }

    /// <summary>Oyun verisi UI'yı bloklamadan arka planda yüklenir (testlerde kapalı: eşzamanlı).</summary>
    internal bool GameLoadInBackground { get; set; } = true;

    private GameLibrary? _loadedLibrary;

    private GameLibrary? LoadGameLibrary()
    {
        var directory = GameAssetsDirectory ?? GameLibrary.FindDirectory();
        if (directory is null)
        {
            return null;
        }

        try
        {
            var library = GameLibrary.Load(directory);
            library.Preload();
            return library;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or KeyNotFoundException)
        {
            return null;
        }
    }

    internal void OpenGame()
    {
        if (Game is null)
        {
            var library = _loadedLibrary ?? LoadGameLibrary();
            Game = new GamePage(_session, library, GameRandom);
            Game.Evaluated += (_, _) => UpdateToday();
        }

        Game.Refresh();
        ShowPage("Oyun", Game, 1080, 780);
    }

    private async void OnGameClick(object sender, RoutedEventArgs e)
    {
        if (Game is null && GameLoadInBackground)
        {
            var tip = GameButton.ToolTip;
            GameButton.IsEnabled = false;
            GameButton.ToolTip = "Yükleniyor…";
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            try
            {
                _loadedLibrary = await Task.Run(LoadGameLibrary);
            }
            finally
            {
                System.Windows.Input.Mouse.OverrideCursor = null;
                GameButton.ToolTip = tip;
                GameButton.IsEnabled = true;
            }
        }

        OpenGame();
    }

    /// <summary>Ek pencereyi gösterir. Testler bunu değiştirip pencereyi ekranda açmadan inceler.</summary>
    internal Func<PageWindow, bool?> ShowDialogHandler { get; set; } = window => window.ShowDialog();

    /// <summary>En son açılan ek pencere.</summary>
    internal PageWindow? LastDialog { get; private set; }

    internal void OpenSettings(SettingsSection section)
    {
        Settings.Refresh();
        Settings.ShowSection(section);
        ShowPage("Ayarlar", Settings, 900, 640);
    }

    internal void OpenProgress()
    {
        Progress.Refresh();
        ShowPage("Hedef", Progress, 660, 560);
    }

    internal void OpenVocabulary()
    {
        Vocabulary.Refresh();
        ShowPage("Sözlük", Vocabulary, 720, 640);
    }

    internal void OpenJournal()
    {
        Journal.Refresh();
        ShowPage("Günlük", Journal, 1040, 720);
        Journal.StopAudio();
    }

    private void ShowPage(string title, UserControl page, double width, double height)
    {
        var window = new PageWindow(title, page, width, height);
        if (IsVisible)
        {
            window.Owner = this;
        }

        LastDialog = window;
        try
        {
            ShowDialogHandler(window);
        }
        finally
        {
            window.Detach();
        }

        Practice.Refresh();
        UpdateToday();
        Practice.FocusInput();
    }

    private void OnProgressClick(object sender, RoutedEventArgs e) => OpenProgress();

    private void OnSettingsClick(object sender, RoutedEventArgs e) => OpenSettings(SettingsSection.ApiKey);

    private void OnVocabularyClick(object sender, RoutedEventArgs e) => OpenVocabulary();

    private void OnJournalClick(object sender, RoutedEventArgs e) => OpenJournal();

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void UpdateMaximizeButton()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "" : "";
        MaximizeButton.ToolTip = maximized ? "Önceki boyut" : "Büyüt";
    }

    private void OnModeChecked(object sender, RoutedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        var direction = sender == ModeEnTr ? TranslationDirection.EnglishToTurkish : TranslationDirection.TurkishToEnglish;
        if (_session.Settings.Direction == direction)
        {
            return;
        }

        if (Practice.IsBusy || Settings.IsBusy)
        {
            // Değerlendirme veya dosya yükleme sürerken mod değiştirilmez.
            UpdateModeButtons();
            return;
        }

        _session.SetDirection(direction);
        Practice.Refresh();
        Practice.FocusInput();
    }

    private void UpdateModeButtons()
    {
        var wasInitializing = _initializing;
        _initializing = true;
        ModeTrEn.IsChecked = _session.Settings.Direction == TranslationDirection.TurkishToEnglish;
        ModeEnTr.IsChecked = _session.Settings.Direction == TranslationDirection.EnglishToTurkish;
        _initializing = wasInitializing;
    }

    private void UpdateToday()
    {
        var today = _session.TodayCount;
        var goal = _session.Settings.DailyGoal;
        TodayText.Text = $"{today}/{goal}";
        TodayText.Foreground = today >= goal ? Ui.Brush("AccentBrush") : Ui.Brush("TextMutedBrush");
        var words = _session.Vocabulary.Count;
        VocabularyCount.Text = words == 0 ? string.Empty : words.ToString(Ui.Turkish);
        VocabularyCount.Visibility = words == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            Practice.Skip();
            e.Handled = true;
        }
    }
}
