using Lingora.Core.Corpus;
using Lingora.Core.Gemini;
using Lingora.Core.Lexicon;
using Lingora.Core.Models;
using Lingora.Core.Progress;
using Lingora.Core.Storage;

namespace Lingora.Core;

public enum SessionState
{
    /// <summary>Henüz cümle dosyası yüklenmemiş.</summary>
    NoCorpus,

    /// <summary>Cümle gösteriliyor, kullanıcının çevirisi bekleniyor.</summary>
    AwaitingAnswer,

    /// <summary>Gemini'den yanıt bekleniyor.</summary>
    Evaluating,

    /// <summary>Sonuç gösteriliyor; sonraki gönderim yeni cümleye geçer.</summary>
    ShowingResult,
}

/// <summary>Gönder butonunun sonucu.</summary>
/// <param name="Words">Bu değerlendirmeden sözlüğe eklenen/tekrar eden terimler.</param>
public sealed record SubmitOutcome(bool Success, string Text, Evaluation? Evaluation = null, VocabularyMergeResult? Words = null);

/// <summary>
/// Ana ekranın tüm akışı: cümle seç → çeviriyi gönder → Gemini ile değerlendir → kaydet
/// (ilerleme + sözlük). Arayüzden bağımsızdır; WPF katmanı yalnızca bunu çağırır.
/// </summary>
public sealed class PracticeSession
{
    private readonly AppPaths _paths;
    private readonly SettingsStore _settingsStore;
    private readonly ApiKeyStore _apiKeyStore;
    private readonly ProgressStore _progressStore;
    private readonly ITranslationEvaluator _evaluator;
    private readonly TimeProvider _time;
    private readonly Random? _random;
    private readonly List<AttemptRecord> _attempts;
    private SentencePicker _picker;

    public PracticeSession(
        AppPaths paths,
        ITranslationEvaluator evaluator,
        TimeProvider? time = null,
        ApiKeyStore? apiKeyStore = null,
        Random? random = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _time = time ?? TimeProvider.System;
        _apiKeyStore = apiKeyStore ?? new ApiKeyStore();
        _random = random;

        _paths.EnsureCreated();
        _settingsStore = new SettingsStore(paths.SettingsFile);
        Settings = _settingsStore.Load();
        if (_settingsStore.LastBackupPath is { } backup)
        {
            Warnings.Add($"Ayar dosyası bozuktu; varsayılanlar yüklendi. Yedek: {backup}");
        }

        _progressStore = new ProgressStore(paths.AttemptsFile);
        _attempts = _progressStore.LoadAll();
        if (_progressStore.LastCorruptLineCount > 0)
        {
            Warnings.Add($"İlerleme dosyasında {_progressStore.LastCorruptLineCount} bozuk satır atlandı.");
        }

        Vocabulary = new VocabularyBook(paths.VocabularyFile);
        if (Vocabulary.LoadWarning is { } vocabularyBackup)
        {
            Warnings.Add($"Sözlük dosyası bozuktu; boş sözlükle başlandı. Yedek: {vocabularyBackup}");
        }

        Corpus = SentenceCorpus.Empty;
        _picker = new SentencePicker(Corpus, _random);
        LoadCorpus();
        ShowCurrentPosition();
    }

    public AppSettings Settings { get; }

    /// <summary>Uygulama içi sözlük: Gemini'nin önerdiği eksik kelime/kalıp/tamlamalar.</summary>
    public VocabularyBook Vocabulary { get; }

    public SentenceCorpus Corpus { get; private set; }

    public PracticeItem? Current { get; private set; }

    public SessionState State { get; private set; }

    /// <summary>Kullanıcıya gösterilecek kısa bilgi (ör. zorluk geri dönüşü).</summary>
    public string? Notice { get; private set; }

    /// <summary>Açılışta oluşan uyarılar.</summary>
    public List<string> Warnings { get; } = new();

    public IReadOnlyList<AttemptRecord> Attempts => _attempts;

    public AppPaths Paths => _paths;

    /// <summary>Sıralı modda ekrandaki cümlenin 1 tabanlı satır numarası (rastgele modda da gösterilir).</summary>
    public int CurrentLine => Current is null ? 0 : Current.CorpusIndex + 1;

    /// <summary>Sonraki cümleye geçer: sıralı modda bir sonraki satır, rastgele modda rastgele bir cümle.</summary>
    public PracticeItem? NextSentence()
    {
        if (Settings.Order == SentenceOrder.Sequential)
        {
            var start = Current is null ? Settings.Position : Current.CorpusIndex + 1;
            return ShowInOrder(start);
        }

        Notice = null;
        return Apply(_picker.Next(Settings.Direction, Settings.Difficulty));
    }

    /// <summary>
    /// Sıralı modda kalınan yerdeki cümleyi gösterir (açılışta, mod/zorluk değişince).
    /// Rastgele modda yeni bir cümle seçer.
    /// </summary>
    public PracticeItem? ShowCurrentPosition() =>
        Settings.Order == SentenceOrder.Sequential ? ShowInOrder(Settings.Position) : NextSentence();

    /// <summary>Sıralı moda geçip verilen satırdan (1 tabanlı) başlar; ör. ilk 10.000 satırı atlamak için 10001.</summary>
    public PracticeItem? StartFromLine(int lineNumber)
    {
        if (Corpus.Count == 0)
        {
            throw new InvalidOperationException("Önce cümle dosyasını yükleyin.");
        }

        if (lineNumber < 1 || lineNumber > Corpus.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(lineNumber), $"Satır 1 ile {Corpus.Count:N0} arasında olmalı.");
        }

        Settings.Order = SentenceOrder.Sequential;
        Settings.Position = lineNumber - 1;
        SaveSettings();
        return ShowInOrder(Settings.Position);
    }

    public void SetOrder(SentenceOrder order)
    {
        if (Settings.Order == order)
        {
            return;
        }

        Settings.Order = order;
        SaveSettings();
        if (State != SessionState.Evaluating)
        {
            ShowCurrentPosition();
        }
    }

    /// <summary>Kullanıcının TXT dosyasını içe aktarır ve (sıralı modda) baştan başlar.</summary>
    public ImportResult ImportCorpus(string sourcePath, CancellationToken cancellationToken = default)
    {
        var result = CorpusImporter.Import(sourcePath, _paths.CorpusFile, cancellationToken);
        LoadCorpus();
        Settings.Position = 0;
        SaveSettings();
        Current = null;
        ShowCurrentPosition();
        return result;
    }

    public void SetDirection(TranslationDirection direction)
    {
        if (Settings.Direction == direction && Current is not null)
        {
            return;
        }

        Settings.Direction = direction;
        SaveSettings();

        // Sıralı modda aynı satırda kalınır, yalnızca yön değişir.
        ShowCurrentPosition();
    }

    public void UpdateGeneralSettings(int dailyGoal, Difficulty difficulty, string model)
    {
        var difficultyChanged = Settings.Difficulty != difficulty;
        Settings.DailyGoal = dailyGoal;
        Settings.Difficulty = difficulty;
        Settings.Model = model;
        SaveSettings();

        if (difficultyChanged && State != SessionState.Evaluating)
        {
            ShowCurrentPosition();
        }
    }

    private PracticeItem? ShowInOrder(int start)
    {
        Notice = null;
        var pick = _picker.NextInOrder(Settings.Direction, Settings.Difficulty, start);
        if (pick is not null)
        {
            if (Settings.Position != pick.Item.CorpusIndex)
            {
                Settings.Position = pick.Item.CorpusIndex;
                SaveSettings();
            }

            if (pick.Wrapped)
            {
                Notice = "Dosyanın sonuna gelindi; baştan başlandı.";
            }
        }

        return Apply(pick);
    }

    private PracticeItem? Apply(PickResult? pick)
    {
        if (pick is null)
        {
            Current = null;
            State = SessionState.NoCorpus;
            return null;
        }

        if (pick.UsedDifficultyFallback)
        {
            Notice = $"'{Settings.Difficulty.DisplayName()}' zorluğunda cümle yok; tüm cümlelerden seçildi.";
        }

        Current = pick.Item;
        State = SessionState.AwaitingAnswer;
        return Current;
    }

    public void SetApiKey(string apiKey)
    {
        _apiKeyStore.Set(Settings, apiKey);
        SaveSettings();
    }

    public void ClearApiKey()
    {
        _apiKeyStore.Clear(Settings);
        SaveSettings();
    }

    public ApiKeyLookup GetApiKey() => _apiKeyStore.Get(Settings);

    public async Task<SubmitOutcome> SubmitAsync(string userTranslation, CancellationToken cancellationToken = default)
    {
        if (State == SessionState.Evaluating)
        {
            return new SubmitOutcome(false, "Değerlendirme sürüyor, lütfen bekleyin.");
        }

        var item = Current;
        if (item is null)
        {
            return new SubmitOutcome(false, "Önce Ayarlar > Veri Dosyası menüsünden cümle dosyasını (TXT) yükleyin.");
        }

        var text = (userTranslation ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return new SubmitOutcome(false, "Lütfen önce çevirinizi yazın.");
        }

        var key = GetApiKey();
        if (!key.HasKey)
        {
            return new SubmitOutcome(false, key.Error ?? "API anahtarı girilmemiş. Ayarlar > API Anahtarı menüsünden ekleyin.");
        }

        State = SessionState.Evaluating;
        Evaluation evaluation;
        try
        {
            evaluation = await _evaluator
                .EvaluateAsync(item, text, key.Key!, Settings.Model, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GeminiException ex)
        {
            State = SessionState.AwaitingAnswer;
            return new SubmitOutcome(false, ex.Message);
        }
        catch (OperationCanceledException)
        {
            State = SessionState.AwaitingAnswer;
            return new SubmitOutcome(false, "İşlem iptal edildi.");
        }
        catch (Exception ex)
        {
            State = SessionState.AwaitingAnswer;
            return new SubmitOutcome(false, $"Beklenmeyen hata: {ex.Message}");
        }

        var record = new AttemptRecord
        {
            Timestamp = _time.GetLocalNow(),
            Direction = item.Direction,
            Source = item.Source,
            UserTranslation = text,
            Score = evaluation.Score,
            IsCorrect = evaluation.IsCorrect,
            DailyGoal = Settings.DailyGoal,
        };

        // Değerlendirme kullanıcıya her durumda gösterilir; kayıt hatası ayrıca bildirilir.
        string? saveWarning = null;
        try
        {
            _progressStore.Append(record);
        }
        catch (IOException ex)
        {
            saveWarning = $"\n\n(Uyarı: ilerleme kaydedilemedi: {ex.Message})";
        }
        catch (UnauthorizedAccessException ex)
        {
            saveWarning = $"\n\n(Uyarı: ilerleme kaydedilemedi: {ex.Message})";
        }

        var words = VocabularyMergeResult.Empty;
        try
        {
            words = Vocabulary.Merge(evaluation.Vocabulary, item.Source, record.Timestamp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            saveWarning += $"\n\n(Uyarı: sözlük kaydedilemedi: {ex.Message})";
        }

        _attempts.Add(record);
        _picker.MarkSeen(item.Direction, item.Source);
        State = SessionState.ShowingResult;
        return new SubmitOutcome(true, EvaluationFormatter.Format(item, text, evaluation) + saveWarning, evaluation, words);
    }

    public DateOnly Today => DateOnly.FromDateTime(_time.GetLocalNow().DateTime);

    public int TodayCount
    {
        get
        {
            var today = Today;
            return _attempts.Count(a =>
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(a.Timestamp, _time.LocalTimeZone).DateTime) == today);
        }
    }

    public ProgressSummary GetProgress(int recentDays = 14) =>
        ProgressCalculator.Calculate(_attempts, Settings.DailyGoal, Today, _time.LocalTimeZone, recentDays);

    /// <summary>Üst bilgi satırı: mod · bugün x/hedef · zorluk.</summary>
    public string StatusLine
    {
        get
        {
            var today = TodayCount;
            var goal = Settings.DailyGoal;
            var goalText = today >= goal ? "hedef tamam" : $"{goal - today} kaldı";
            return $"{Settings.Direction.DisplayName()}   ·   Bugün {today}/{goal} ({goalText})   ·   Zorluk: {Settings.Difficulty.DisplayName()}";
        }
    }

    private void LoadCorpus()
    {
        Corpus = SentenceCorpus.Load(_paths.CorpusFile);
        _picker = new SentencePicker(Corpus, _random);
        foreach (var attempt in _attempts)
        {
            _picker.MarkSeen(attempt.Direction, attempt.Source);
        }
    }

    private void SaveSettings() => _settingsStore.Save(Settings);
}
