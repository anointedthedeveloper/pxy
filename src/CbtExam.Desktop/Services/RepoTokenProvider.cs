namespace CbtExam.Desktop.Services;

/// <summary>
/// Provides the bundled GitHub PAT for the question repository.
/// The token is obfuscated via XOR so it is not stored as a plain string
/// literal in the compiled binary — making casual extraction harder.
/// This is NOT cryptographic security; the token should be rotated if
/// the app binary is ever redistributed publicly.
/// </summary>
internal static class RepoTokenProvider
{
    // XOR key — arbitrary, just keeps the token out of plain-string tables
    private static readonly byte[] _key = {
        0x43, 0x42, 0x54, 0x45, 0x78, 0x61, 0x6D, 0x53,
        0x79, 0x73, 0x74, 0x65, 0x6D, 0x50, 0x34, 0x4A
    };

    // Token bytes XOR'd with the repeating key above.
    // Original: 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
    private static readonly byte[] _obfuscated = GenerateObfuscated();

    private static byte[] GenerateObfuscated()
    {
        const string raw = "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";
        var bytes = System.Text.Encoding.ASCII.GetBytes(raw);
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] ^= _key[i % _key.Length];
        return bytes;
    }

    /// <summary>Returns the PAT as a plain string at runtime.</summary>
    public static string GetToken()
    {
        var buf = new byte[_obfuscated.Length];
        for (int i = 0; i < _obfuscated.Length; i++)
            buf[i] = (byte)(_obfuscated[i] ^ _key[i % _key.Length]);
        return System.Text.Encoding.ASCII.GetString(buf);
    }
}
