using System.IO;
using Lingora.Core.Journal;
using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Lingora.Desktop;

/// <summary>
/// PCM sesi Windows Media Foundation'ın MP3 kodlayıcısıyla MP3'e çevirir.
/// Kodlayıcı yalnızca 32/44,1/48 kHz kabul ettiği için Gemini'nin 24 kHz sesi önce 44,1 kHz'e çevrilir.
/// </summary>
internal sealed class Mp3Encoder : IAudioEncoder
{
    private const int TargetSampleRate = 44100;
    private const int BitRate = 96000;
    private static readonly object StartupLock = new();
    private static bool _started;

    public void EncodeMp3(SpeechAudio audio, string path)
    {
        ArgumentNullException.ThrowIfNull(audio);
        EnsureStarted();

        using var raw = new RawSourceWaveStream(new MemoryStream(audio.Pcm), new WaveFormat(audio.SampleRate, 16, 1));
        IWaveProvider source = audio.SampleRate == TargetSampleRate
            ? raw
            : new WdlResamplingSampleProvider(raw.ToSampleProvider(), TargetSampleRate).ToWaveProvider16();

        try
        {
            MediaFoundationEncoder.EncodeToMp3(source, path, BitRate);
        }
        catch (InvalidOperationException ex)
        {
            throw new NotSupportedException(
                "Bu Windows'ta MP3 kodlayıcısı bulunamadı (Windows N sürümlerinde 'Media Feature Pack' gerekir).", ex);
        }
    }

    private static void EnsureStarted()
    {
        lock (StartupLock)
        {
            if (!_started)
            {
                MediaFoundationApi.Startup();
                _started = true;
            }
        }
    }
}
