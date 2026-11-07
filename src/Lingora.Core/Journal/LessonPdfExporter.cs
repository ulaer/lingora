using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace Lingora.Core.Journal;

/// <summary>Ders notunu A4 PDF olarak kaydeder (MigraDoc; Türkçe karakterler için Windows yazı tipleri).</summary>
public static class LessonPdfExporter
{
    private const string FontFamily = "Segoe UI";
    private static readonly Color Ink = new(0x1A, 0x22, 0x33);
    private static readonly Color Muted = new(0x5B, 0x6B, 0x84);
    private static readonly Color Accent = new(0x2E, 0x6B, 0xD6);
    private static readonly Color Violet = new(0x6A, 0x4F, 0xD8);
    private static readonly Color Soft = new(0xF2, 0xF5, 0xFB);
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly object FontLock = new();

    public static string SuggestedFileName(LessonNote note) =>
        $"Lingora-ders-notu-{note.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.pdf";

    public static void Export(LessonNote note, string path)
    {
        ArgumentNullException.ThrowIfNull(note);
        EnsureFonts();

        var document = Build(note);
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        renderer.PdfDocument.Save(path);
    }

    internal static Document Build(LessonNote note)
    {
        var document = new Document();
        document.Info.Title = note.Title.Length > 0 ? note.Title : "Lingora ders notu";
        document.Info.Author = "Lingora";
        document.Info.Subject = "Günlük ders notu " + note.Date.ToString("d MMMM yyyy", Turkish);

        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = FontFamily;
        normal.Font.Size = 10.5;
        normal.Font.Color = Ink;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(5);
        normal.ParagraphFormat.LineSpacingRule = LineSpacingRule.Multiple;
        normal.ParagraphFormat.LineSpacing = 1.2;

        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromCentimeter(2);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(2);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(2.2);
        section.PageSetup.RightMargin = Unit.FromCentimeter(2.2);

        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Alignment = ParagraphAlignment.Center;
        footer.Format.Font.Size = 8.5;
        footer.Format.Font.Color = Muted;
        footer.AddText("Lingora · " + note.Date.ToString("d MMMM yyyy", Turkish) + " · sayfa ");
        footer.AddPageField();

        var eyebrow = section.AddParagraph("LINGORA · DERS NOTU · " + note.Date.ToString("d MMMM yyyy, dddd", Turkish).ToUpper(Turkish));
        eyebrow.Format.Font.Size = 8.5;
        eyebrow.Format.Font.Bold = true;
        eyebrow.Format.Font.Color = Accent;
        eyebrow.Format.SpaceAfter = Unit.FromPoint(4);

        var title = section.AddParagraph(note.Title.Length > 0 ? note.Title : "Günün ders notu");
        title.Format.Font.Size = 20;
        title.Format.Font.Bold = true;
        title.Format.SpaceAfter = Unit.FromPoint(10);

        if (note.Summary.Length > 0)
        {
            var summary = section.AddParagraph(note.Summary);
            summary.Format.Shading.Color = Soft;
            summary.Format.Borders.Left.Color = Accent;
            summary.Format.Borders.Left.Width = Unit.FromPoint(2.5);
            summary.Format.Borders.DistanceFromLeft = Unit.FromPoint(8);
            summary.Format.LeftIndent = Unit.FromPoint(4);
            summary.Format.SpaceAfter = Unit.FromPoint(14);
        }

        if (note.Topics.Count > 0)
        {
            Heading(section, "Konular");
            var number = 0;
            foreach (var topic in note.Topics)
            {
                number++;
                var head = section.AddParagraph($"{number}. {topic.Title}");
                head.Format.Font.Size = 12.5;
                head.Format.Font.Bold = true;
                head.Format.Font.Color = Violet;
                head.Format.SpaceBefore = Unit.FromPoint(6);
                head.Format.KeepWithNext = true;
                section.AddParagraph(topic.Explanation);
                foreach (var example in topic.Examples)
                {
                    Bullet(section, example, italic: true);
                }
            }
        }

        if (note.Mistakes.Count > 0)
        {
            Heading(section, "Günün hataları");
            foreach (var mistake in note.Mistakes)
            {
                var line = section.AddParagraph();
                line.Format.LeftIndent = Unit.FromPoint(12);
                line.Format.SpaceAfter = Unit.FromPoint(1);
                line.Format.KeepWithNext = true;
                var wrong = line.AddFormattedText(mistake.Wrong);
                wrong.Font.Color = new Color(0xC2, 0x41, 0x3B);
                line.AddText("  →  ");
                var right = line.AddFormattedText(mistake.Right);
                right.Font.Bold = true;
                right.Font.Color = new Color(0x1F, 0x8A, 0x4C);
                if (mistake.Why.Length > 0)
                {
                    var why = section.AddParagraph(mistake.Why);
                    why.Format.LeftIndent = Unit.FromPoint(12);
                    why.Format.Font.Color = Muted;
                    why.Format.SpaceAfter = Unit.FromPoint(7);
                }
            }
        }

        if (note.Practice.Count > 0)
        {
            Heading(section, "Alıştırma");
            var number = 0;
            foreach (var exercise in note.Practice)
            {
                number++;
                var line = section.AddParagraph($"{number}. {exercise}");
                line.Format.LeftIndent = Unit.FromPoint(12);
                var blank = section.AddParagraph("_______________________________________________________________");
                blank.Format.LeftIndent = Unit.FromPoint(12);
                blank.Format.Font.Color = new Color(0xC9, 0xD2, 0xE0);
                blank.Format.SpaceAfter = Unit.FromPoint(8);
            }
        }

        var meta = section.AddParagraph(
            $"{note.EntryCount} çeviri kaydından üretildi · {note.CreatedAt.ToLocalTime().ToString("d MMMM yyyy HH:mm", Turkish)}"
            + (note.Model.Length > 0 ? $" · {note.Model}" : string.Empty));
        meta.Format.SpaceBefore = Unit.FromPoint(16);
        meta.Format.Font.Size = 8.5;
        meta.Format.Font.Color = Muted;
        return document;
    }

    private static void Heading(Section section, string text)
    {
        var heading = section.AddParagraph(text.ToUpper(Turkish));
        heading.Format.Font.Size = 9.5;
        heading.Format.Font.Bold = true;
        heading.Format.Font.Color = Accent;
        heading.Format.SpaceBefore = Unit.FromPoint(12);
        heading.Format.SpaceAfter = Unit.FromPoint(6);
        heading.Format.Borders.Bottom.Color = new Color(0xDD, 0xE4, 0xF0);
        heading.Format.Borders.Bottom.Width = Unit.FromPoint(0.75);
        heading.Format.Borders.DistanceFromBottom = Unit.FromPoint(3);
        heading.Format.KeepWithNext = true;
    }

    private static void Bullet(Section section, string text, bool italic)
    {
        var paragraph = section.AddParagraph("•  " + text);
        paragraph.Format.LeftIndent = Unit.FromPoint(14);
        paragraph.Format.FirstLineIndent = Unit.FromPoint(-9);
        paragraph.Format.SpaceAfter = Unit.FromPoint(2);
        paragraph.Format.Font.Italic = italic;
        paragraph.Format.Font.Color = Muted;
    }

    private static void EnsureFonts()
    {
        lock (FontLock)
        {
            if (GlobalFontSettings.FontResolver is null)
            {
                GlobalFontSettings.FontResolver = new WindowsFontResolver();
            }
        }
    }

    /// <summary>Segoe UI (yoksa Arial) dosyalarını Windows yazı tipi klasöründen okur.</summary>
    private sealed class WindowsFontResolver : IFontResolver
    {
        private static readonly string FontsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            var face = (isBold, isItalic) switch
            {
                (true, true) => "BoldItalic",
                (true, false) => "Bold",
                (false, true) => "Italic",
                _ => "Regular",
            };
            return new FontResolverInfo("Lingora-" + face);
        }

        public byte[]? GetFont(string faceName)
        {
            var candidates = faceName switch
            {
                "Lingora-Bold" => new[] { "segoeuib.ttf", "arialbd.ttf" },
                "Lingora-Italic" => new[] { "segoeuii.ttf", "ariali.ttf" },
                "Lingora-BoldItalic" => new[] { "segoeuiz.ttf", "arialbi.ttf" },
                _ => new[] { "segoeui.ttf", "arial.ttf" },
            };

            foreach (var file in candidates)
            {
                var path = Path.Combine(FontsDirectory, file);
                if (File.Exists(path))
                {
                    return File.ReadAllBytes(path);
                }
            }

            throw new FileNotFoundException("PDF için yazı tipi bulunamadı (Segoe UI / Arial).");
        }
    }
}
