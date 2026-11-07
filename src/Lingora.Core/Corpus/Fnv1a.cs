namespace Lingora.Core.Corpus;

/// <summary>Bağımlılıksız, hızlı 64-bit FNV-1a özeti (kriptografik değil).</summary>
internal static class Fnv1a
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    public static ulong Hash(ReadOnlySpan<byte> data)
    {
        var hash = OffsetBasis;
        foreach (var b in data)
        {
            hash ^= b;
            hash *= Prime;
        }

        return hash;
    }

    public static ulong Hash(string text)
    {
        var hash = OffsetBasis;
        foreach (var c in text)
        {
            hash ^= (byte)c;
            hash *= Prime;
            hash ^= (byte)(c >> 8);
            hash *= Prime;
        }

        return hash;
    }
}
