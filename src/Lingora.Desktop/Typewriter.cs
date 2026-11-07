using System.Windows;
using System.Windows.Threading;

namespace Lingora.Desktop;

/// <summary>
/// Metinleri sırayla, harf harf ve hızlıca yazar. Her parça kendi kabıyla birlikte
/// sırası gelince görünür hale gelir. <see cref="Complete"/> hepsini anında bitirir.
/// </summary>
internal sealed class Typewriter
{
    private readonly List<(UIElement Container, Action<string> Set, string Text)> _parts = new();
    private readonly DispatcherTimer _timer;
    private int _part;
    private int _chars;

    public Typewriter(TimeSpan interval, int charsPerTick)
    {
        CharsPerTick = Math.Max(1, charsPerTick);
        _timer = new DispatcherTimer { Interval = interval };
        _timer.Tick += (_, _) => Step();
    }

    public int CharsPerTick { get; }

    public bool IsRunning => _timer.IsEnabled;

    public void Add(UIElement container, Action<string> set, string text) => _parts.Add((container, set, text));

    public void Start()
    {
        foreach (var (container, set, _) in _parts)
        {
            container.Visibility = Visibility.Collapsed;
            set(string.Empty);
        }

        _part = 0;
        _chars = 0;
        if (_parts.Count > 0)
        {
            _timer.Start();
        }
    }

    /// <summary>Animasyonu durdurur ve tüm metinleri tam haliyle gösterir.</summary>
    public void Complete()
    {
        _timer.Stop();
        foreach (var (container, set, text) in _parts)
        {
            container.Visibility = Visibility.Visible;
            set(text);
        }

        _parts.Clear();
    }

    /// <summary>Animasyonu durdurur, metinlere dokunmaz.</summary>
    public void Cancel()
    {
        _timer.Stop();
        _parts.Clear();
    }

    /// <summary>Zamanlayıcıyı durdurup animasyonu elle bir adım ilerletir (demo kaydı için). Bitmediyse true.</summary>
    internal bool StepOnce()
    {
        _timer.Stop();
        Step();
        return _parts.Count > 0;
    }

    private void Step()
    {
        while (_part < _parts.Count)
        {
            var (container, set, text) = _parts[_part];
            container.Visibility = Visibility.Visible;
            if (_chars < text.Length)
            {
                _chars = Math.Min(text.Length, _chars + CharsPerTick);
                set(text[.._chars]);
                return;
            }

            _part++;
            _chars = 0;
        }

        _timer.Stop();
        _parts.Clear();
    }
}
