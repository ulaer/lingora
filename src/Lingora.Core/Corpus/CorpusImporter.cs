using System.Text;

namespace Lingora.Core.Corpus;

/// <summary>İçe aktarma özeti.</summary>
/// <param name="Imported">Kaydedilen benzersiz cümle çifti sayısı.</param>
/// <param name="Skipped">Biçime uymadığı için atlanan satır sayısı.</param>
/// <param name="Duplicates">Tekrar ettiği için atlanan satır sayısı.</param>
/// <param name="EncodingName">Dosyanın okunduğu karakter kodlaması.</param>
public sealed record ImportResult(int Imported, int Skipped, int Duplicates, string EncodingName);

/// <summary>
/// Kullanıcının TXT dosyasını okuyup uygulamanın veri klasörüne sade bir
/// <c>İngilizce&lt;TAB&gt;Türkçe</c> dosyası olarak yazar. Hata olursa eski veri korunur.
/// </summary>
public static class CorpusImporter
{
    private const int TurkishWindowsCodePage = 1254;

    public static ImportResult Import(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        var path = NormalizeUserPath(sourcePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Dosya bulunamadı: {path}", path);
        }

        try
        {
            // Önce katı UTF-8 dene; geçersiz bayt varsa Türkçe Windows (1254) kodlamasıyla yeniden oku.
            return ImportWith(path, destinationPath, new UTF8Encoding(false, throwOnInvalidBytes: true), cancellationToken);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return ImportWith(path, destinationPath, Encoding.GetEncoding(TurkishWindowsCodePage), cancellationToken);
        }
    }

    /// <summary>Kullanıcının yapıştırdığı yoldaki tırnakları ve boşlukları temizler.</summary>
    public static string NormalizeUserPath(string? path)
    {
        var trimmed = (path ?? string.Empty).Trim().Trim('"', '\'').Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Dosya yolu boş.", nameof(path));
        }

        return Environment.ExpandEnvironmentVariables(trimmed);
    }

    private static ImportResult ImportWith(string sourcePath, string destinationPath, Encoding encoding, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = destinationPath + ".tmp";
        var seen = new HashSet<ulong>();
        int imported = 0, skipped = 0, duplicates = 0;

        try
        {
            using (var reader = new StreamReader(sourcePath, encoding, detectEncodingFromByteOrderMarks: true))
            using (var writer = new StreamWriter(temp, append: false, new UTF8Encoding(false)))
            {
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!CorpusParser.TryParseLine(line, out var pair))
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            skipped++;
                        }

                        continue;
                    }

                    var normalized = pair.English + "\t" + pair.Turkish;
                    if (!seen.Add(Fnv1a.Hash(normalized)))
                    {
                        duplicates++;
                        continue;
                    }

                    writer.Write(normalized);
                    writer.Write('\n');
                    imported++;
                }
            }

            if (imported == 0)
            {
                throw new InvalidDataException(
                    $"Dosyada geçerli cümle çifti bulunamadı. Beklenen satır biçimi: {CorpusParser.ExpectedFormat}");
            }

            File.Move(temp, destinationPath, overwrite: true);
            return new ImportResult(imported, skipped, duplicates, encoding.WebName);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }
}
