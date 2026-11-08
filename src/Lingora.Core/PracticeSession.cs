using Lingora.Core.Corpus;
using Lingora.Core.Game;
using Lingora.Core.Gemini;
using Lingora.Core.Journal;
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
/// <param name="Entry">Günlüğe yazılan kayıt (ayrıntılı açıklama istemek için).</param>
public sealed record SubmitOutcome(
    bool Success,
    string Text,
    Evaluation? Evaluation = null,
    VocabularyMergeResult? Words = null,
    JournalEntry? Entry = null);

/// <summary>Ayrıntılı açıklama sonucu. <paramref name="FromCache"/>: daha önce üretilmişti, AI çağrılmadı.</summary>
public sealed record ExplainOutcome(bool Success, string Text, bool FromCache = false);

/// <summary>Ders notu sonucu. <paramref name="FromCache"/>: daha önce üretilmişti, AI çağrılmadı.</summary>
public sealed record LessonOutcome(bool Success, LessonNote? Note, string? Error = null, bool FromCache = false);

/// <summary>Sesli ders sonucu. <paramref name="AudioPath"/>: MP3 dosyası.</summary>
public sealed record AudioLessonOutcome(bool Success, AudioLesson? Lesson, string? AudioPath, string? Error = null, bool FromCache = false);

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
    private readonly IStudyAssistant? _assistant;
    private readonly ISpeechSynthesizer? _speech;
    private readonly TimeProvider _time;
    private readonly Random? _random;
    private readonly List<AttemptRecord> _attempts;
    private SentencePicker _picker;

    public PracticeSession(
        AppPaths paths,
        ITranslationEvaluator evaluator,
        TimeProvider? time = null,
        ApiKeyStore? apiKeyStore = null,
        Random? random = null,
        IStudyAssistant? assistant = null,
        ISpeechSynthesizer? speech = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _assistant = assistant ?? evaluator as IStudyAssistant;
        _speech = speech ?? evaluator as ISpeechSynthesizer;
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

        Journal = new JournalStore(paths.JournalDirectory, paths.LessonsDirectory, _time.LocalTimeZone);
        Lessons = new LessonStore(paths.LessonsDirectory);
        AudioLessons = new AudioLessonStore(paths.LessonsDirectory);

        Corpus = SentenceCorpus.Empty;
        _picker = new SentencePicker(Corpus, _random);
        LoadCorpus();
        ShowCurrentPosition();
    }

    /// <summary>Günlük: her günün hataları, ipuçları ve istenen açıklamaları.</summary>
    public JournalStore Journal { get; }

    /// <summary>AI ile bir kez üretilip saklanan günlük ders notları.</summary>
    public LessonStore Lessons { get; }

    /// <summary>Günün kayıtlarından üretilen sesli dersler (metin + MP3).</summary>
    public AudioLessonStore AudioLessons { get; }

    /// <summary>
    /// Günün sesli dersi: kayıttan açılır; yoksa (ya da <paramref name="regenerate"/> ve o gün yeni çeviri varsa)
    /// AI önce yalnızca metin bir anlatım yazar, sonra metin parça parça seslendirilip MP3'e kaydedilir.
    /// </summary>
    public async Task<AudioLessonOutcome> GetOrCreateAudioLessonAsync(
        DateOnly date,
        IAudioEncoder encoder,
        bool regenerate = false,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        var saved = AudioLessons.Load(date);
        if (saved is not null && !regenerate)
        {
            return new AudioLessonOutcome(true, saved, AudioLessons.AudioFile(date), FromCache: true);
        }

        var entries = Journal.LoadDay(date);
        if (saved is not null && entries.Count <= saved.EntryCount)
        {
            return new AudioLessonOutcome(true, saved, AudioLessons.AudioFile(date), FromCache: true);
        }

        if (entries.Count == 0)
        {
            return new AudioLessonOutcome(false, null, null, "Bu gün için günlük kaydı yok; sesli ders üretilemez.");
        }

        if (_assistant is null || _speech is null)
        {
            return new AudioLessonOutcome(false, null, null, "Sesli ders bu yapılandırmada kullanılamıyor.");
        }

        var key = GetApiKey();
        if (!key.HasKey)
        {
            return new AudioLessonOutcome(false, null, null, key.Error ?? "API anahtarı girilmemiş. Ayarlar > API Anahtarı menüsünden ekleyin.");
        }

        string? temp = null;
        try
        {
            progress?.Report("Ders anlatımı yazılıyor");
            var script = (await _assistant.CreateAudioScriptAsync(date, entries, key.Key!, Settings.Model, cancellationToken).ConfigureAwait(false)).Trim();
            if (script.Length == 0)
            {
                return new AudioLessonOutcome(false, null, null, "Gemini boş bir anlatım döndürdü. Tekrar deneyin.");
            }

            var chunks = SpeechChunker.Split(script);
            var pcm = new MemoryStream();
            var sampleRate = 0;
            for (var i = 0; i < chunks.Count; i++)
            {
                progress?.Report(chunks.Count == 1 ? "Seslendiriliyor" : $"Seslendiriliyor ({i + 1}/{chunks.Count})");
                var audio = await _speech.SynthesizeAsync(chunks[i], Settings.Voice, Settings.TtsModel, key.Key!, cancellationToken).ConfigureAwait(false);
                if (sampleRate != 0 && audio.SampleRate != sampleRate)
                {
                    throw new GeminiException("Seslendirme parçaları farklı biçimde geldi. Tekrar deneyin.");
                }

                sampleRate = audio.SampleRate;
                pcm.Write(audio.Pcm);

                // Parçalar arasında yarım saniyelik sessizlik.
                pcm.Write(new byte[sampleRate]);
            }

            progress?.Report("MP3 kaydediliyor");
            var speech = new SpeechAudio(pcm.ToArray(), sampleRate);
            temp = AudioLessons.NewTempMp3Path(date);
            encoder.EncodeMp3(speech, temp);

            var lesson = new AudioLesson
            {
                Date = date,
                Script = script,
                EntryCount = entries.Count,
                Voice = Settings.Voice,
                TtsModel = Settings.TtsModel,
                Model = Settings.Model,
                DurationSeconds = Math.Round(speech.DurationSeconds, 1),
                CreatedAt = _time.GetLocalNow(),
            };
            AudioLessons.Save(lesson, temp);
            temp = null;
            return new AudioLessonOutcome(true, lesson, AudioLessons.AudioFile(date));
        }
        catch (GeminiException ex)
        {
            return new AudioLessonOutcome(false, null, null, ex.Message);
        }
        catch (OperationCanceledException)
        {
            return new AudioLessonOutcome(false, null, null, "İşlem iptal edildi.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            return new AudioLessonOutcome(false, null, null, "MP3 kaydedilemedi: " + ex.Message);
        }
        finally
        {
            if (temp is not null && File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>Hata veya ipucu satırını ayrıntılı açıklatır; daha önce açıklandıysa kayıttan döner.</summary>
    public async Task<ExplainOutcome> ExplainAsync(JournalEntry entry, FeedbackKind kind, int index, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.FindExplanation(kind, index) is { } cached)
        {
            return new ExplainOutcome(true, cached.Text, FromCache: true);
        }

        if (entry.GetItem(kind, index) is not { } item)
        {
            return new ExplainOutcome(false, "Açıklanacak satır bulunamadı.");
        }

        if (_assistant is null)
        {
            return new ExplainOutcome(false, "Ayrıntılı açıklama bu yapılandırmada kullanılamıyor.");
        }

        var key = GetApiKey();
        if (!key.HasKey)
        {
            return new ExplainOutcome(false, key.Error ?? "API anahtarı girilmemiş. Ayarlar > API Anahtarı menüsünden ekleyin.");
        }

        string text;
        try
        {
            var request = new ExplainRequest(entry.Direction, entry.Source, entry.UserTranslation, entry.CorrectedTranslation, kind, item);
            text = (await _assistant.ExplainAsync(request, key.Key!, Settings.Model, cancellationToken).ConfigureAwait(false)).Trim();
        }
        catch (GeminiException ex)
        {
            return new ExplainOutcome(false, ex.Message);
        }
        catch (OperationCanceledException)
        {
            return new ExplainOutcome(false, "İşlem iptal edildi.");
        }

        entry.Explanations.Add(new ItemExplanation { Kind = kind, Index = index, Item = item, Text = text, CreatedAt = _time.GetLocalNow() });
        try
        {
            Journal.Update(entry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ExplainOutcome(true, text + $"\n\n(Uyarı: günlüğe kaydedilemedi: {ex.Message})");
        }

        return new ExplainOutcome(true, text);
    }

    /// <summary>
    /// Günün ders notu: daha önce üretildiyse dosyadan okunur (AI çağrılmaz);
    /// yoksa o günün günlük kayıtlarından AI ile üretilip saklanır.
    /// <paramref name="regenerate"/>: not üretildikten sonra o gün yeni çeviri yapıldıysa notu yeniden üretir
    /// (yeni kayıt yoksa mevcut not döner, AI çağrılmaz).
    /// </summary>
    public async Task<LessonOutcome> GetOrCreateLessonAsync(DateOnly date, bool regenerate = false, CancellationToken cancellationToken = default)
    {
        var saved = Lessons.Load(date);
        if (saved is not null && !regenerate)
        {
            return new LessonOutcome(true, saved, FromCache: true);
        }

        var entries = Journal.LoadDay(date);
        if (saved is not null && entries.Count <= saved.EntryCount)
        {
            return new LessonOutcome(true, saved, FromCache: true);
        }

        if (entries.Count == 0)
        {
            return new LessonOutcome(false, null, "Bu gün için günlük kaydı yok; ders notu üretilemez.");
        }

        if (_assistant is null)
        {
            return new LessonOutcome(false, null, "Ders notu bu yapılandırmada kullanılamıyor.");
        }

        var key = GetApiKey();
        if (!key.HasKey)
        {
            return new LessonOutcome(false, null, key.Error ?? "API anahtarı girilmemiş. Ayarlar > API Anahtarı menüsünden ekleyin.");
        }

        LessonNote note;
        try
        {
            note = await _assistant.CreateLessonAsync(date, entries, key.Key!, Settings.Model, cancellationToken).ConfigureAwait(false);
        }
        catch (GeminiException ex)
        {
            return new LessonOutcome(false, null, ex.Message);
        }
        catch (OperationCanceledException)
        {
            return new LessonOutcome(false, null, "İşlem iptal edildi.");
        }

        note.Date = date;
        note.EntryCount = entries.Count;
        note.CreatedAt = _time.GetLocalNow();
        try
        {
            Lessons.Save(note);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new LessonOutcome(true, note, $"Ders notu kaydedilemedi: {ex.Message}");
        }

        return new LessonOutcome(true, note);
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

    /// <summary>Sesli ders sesi ve seslendirme modeli.</summary>
    public void UpdateSpeechSettings(string voice, string ttsModel)
    {
        Settings.Voice = voice;
        Settings.TtsModel = ttsModel;
        Settings.Normalize();
        SaveSettings();
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

        State = SessionState.Evaluating;
        var outcome = await EvaluateAndRecordAsync(item, text, cancellationToken).ConfigureAwait(false);
        if (outcome.Success)
        {
            _picker.MarkSeen(item.Direction, item.Source);
            State = SessionState.ShowingResult;
        }
        else
        {
            State = SessionState.AwaitingAnswer;
        }

        return outcome;
    }

    /// <summary>
    /// Oyun – resmi anlat: sahnenin kesin olgularıyla Gemini'ye gönderir. Sonuç günlük hedefe, günlüğe ve
    /// sözlüğe çeviri gibi sayılır; çalışma ekranındaki cümleyi ve durumu değiştirmez.
    /// </summary>
    public async Task<SubmitOutcome> SubmitSceneAsync(GameLibrary library, GameScene scene, string userSentence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(scene);
        var text = (userSentence ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return new SubmitOutcome(false, "Lütfen önce resmi anlatan cümleni yaz.");
        }

        return await EvaluateAndRecordAsync(SceneItem(library, scene), text, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Oyunda resmin anlatılacağı dil (TurkishToEnglish = İngilizce yaz). Kalıcıdır.</summary>
    public void SetGameDirection(TranslationDirection direction)
    {
        Settings.GameDirection = direction;
        SaveSettings();
    }

    /// <summary>Resmi anlat sahnesinin değerlendirme öğesi (kaynak: resim etiketi, referans: kanonik cümle).</summary>
    public PracticeItem SceneItem(GameLibrary library, GameScene scene)
    {
        var direction = Settings.GameDirection;
        var sourceSentence = direction == TranslationDirection.TurkishToEnglish ? scene.Tr : scene.En;
        return new PracticeItem(direction, "Resim: " + sourceSentence, [scene.Sentence(direction)], -1, library.DescribeFacts(scene, direction));
    }

    /// <summary>
    /// Oyun – doğru cümleyi seç: yerel değerlendirme (AI yok). Günlük hedefe sayılır; yanlışsa doğrusu
    /// günlüğe hata olarak yazılır.
    /// </summary>
    public JournalEntry RecordChoice(ChoiceQuestion question, int chosenIndex)
    {
        ArgumentNullException.ThrowIfNull(question);
        var correct = chosenIndex == question.CorrectIndex;
        var chosen = chosenIndex >= 0 && chosenIndex < question.Options.Count ? question.Options[chosenIndex] : string.Empty;
        var now = _time.GetLocalNow();
        var direction = question.Direction;
        var source = "Resim (seçmeli): " + (direction == TranslationDirection.TurkishToEnglish ? question.Scene.Tr : question.Scene.En);

        var record = new AttemptRecord
        {
            Timestamp = now,
            Direction = direction,
            Source = source,
            UserTranslation = chosen,
            Score = correct ? 100 : 0,
            IsCorrect = correct,
            DailyGoal = Settings.DailyGoal,
        };
        var entry = new JournalEntry
        {
            Timestamp = now,
            Direction = direction,
            Source = source,
            UserTranslation = chosen,
            Score = record.Score,
            IsCorrect = correct,
            CorrectedTranslation = question.Correct,
            NaturalTranslation = question.Correct,
        };
        if (!correct)
        {
            entry.Mistakes.Add($"{chosen} → {question.Correct}: resimle uyuşmuyor");
        }

        try
        {
            _progressStore.Append(record);
            Journal.Append(entry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warnings.Add($"Oyun sonucu kaydedilemedi: {ex.Message}");
        }

        _attempts.Add(record);
        return entry;
    }

    /// <summary>Değerlendirir ve sonucu ilerleme, sözlük ve günlüğe yazar (çalışma ekranı ve oyun ortak).</summary>
    private async Task<SubmitOutcome> EvaluateAndRecordAsync(PracticeItem item, string text, CancellationToken cancellationToken)
    {
        var key = GetApiKey();
        if (!key.HasKey)
        {
            return new SubmitOutcome(false, key.Error ?? "API anahtarı girilmemiş. Ayarlar > API Anahtarı menüsünden ekleyin.");
        }

        Evaluation evaluation;
        try
        {
            evaluation = await _evaluator
                .EvaluateAsync(item, text, key.Key!, Settings.Model, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GeminiException ex)
        {
            return new SubmitOutcome(false, ex.Message);
        }
        catch (OperationCanceledException)
        {
            return new SubmitOutcome(false, "İşlem iptal edildi.");
        }
        catch (Exception ex)
        {
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

        // Günlük: yalnızca yapılandırılmış değerlendirmeler (ham metin yanıtında hata/ipucu yok).
        JournalEntry? entry = null;
        if (evaluation.RawText is null)
        {
            entry = JournalEntry.From(item, text, evaluation, record.Timestamp);
            try
            {
                Journal.Append(entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                saveWarning += $"\n\n(Uyarı: günlüğe kaydedilemedi: {ex.Message})";
            }
        }

        _attempts.Add(record);
        return new SubmitOutcome(true, EvaluationFormatter.Format(item, text, evaluation) + saveWarning, evaluation, words, entry);
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
