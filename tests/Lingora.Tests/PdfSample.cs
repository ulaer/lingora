using Lingora.Core.Journal;

namespace Lingora.Tests;

/// <summary>Tanıtım görselleri ve örnek PDF için gerçekçi bir ders notu.</summary>
public static class PdfSample
{
    public static LessonNote SampleNote() => new()
    {
        Date = new DateOnly(2026, 11, 7),
        Title = "Yola çıkmak, dolaylı anlatım ve zaman uyumu",
        Summary = "Bugün 13 cümle çevirdin ve anlamı çoğunlukla doğru aktardın. Phrasal verb seçiminde ve dolaylı anlatımda zamanı geri kaydırmada zorlandın; bu iki konuyu tekrar etmek iyi olur.",
        Topics =
        [
            new() { Title = "Yolculuğa başlamak: set off / set out", Explanation = "\"Go out\" dışarı çıkmak demektir; bir yolculuğa başlamak için \"set off\" veya \"set out\" kullanılır. \"Leave\" de genel bir seçenektir.", Examples = ["We set off at dawn. (Şafakta yola çıktık.)", "They set out for Ankara. (Ankara'ya doğru yola koyuldular.)"] },
            new() { Title = "Dolaylı anlatımda zaman uyumu", Explanation = "Ana fiil geçmiş zamandaysa (said, told) aktarılan cümlenin fiili de bir zaman geri gider: know → knew, must → had to / was supposed to.", Examples = ["He said he knew the answer. (Cevabı bildiğini söyledi.)", "She said she had to leave. (Gitmesi gerektiğini söyledi.)"] },
        ],
        Mistakes =
        [
            new() { Wrong = "go out early", Right = "set off early", Why = "Yolculuğa başlamak anlamında \"go out\" kullanılmaz." },
            new() { Wrong = "what Mary must do", Right = "what Mary was supposed to do", Why = "Geçmişte beklenen görev; dolaylı anlatımda zaman kayar." },
        ],
        Practice = ["Sabah erkenden yola çıktılar.", "Ne yapması gerektiğini bilmediğini söyledi.", "Treni kaçırmamak için erken çıktık."],
        Model = "gemini-3.5-flash-lite",
    };
}
