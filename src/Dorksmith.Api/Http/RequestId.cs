using System.Security.Cryptography;

namespace Dorksmith.Api.Http;

/// <summary>ULID-style identifiers: 48-bit millisecond timestamp + 80 random bits in Crockford base32. Sortable, URL-safe.</summary>
public static class RequestId
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string New(TimeProvider? time = null)
    {
        var ms = (time ?? TimeProvider.System).GetUtcNow().ToUnixTimeMilliseconds();
        Span<byte> bytes = stackalloc byte[16];
        for (var i = 5; i >= 0; i--) { bytes[i] = (byte)(ms & 0xFF); ms >>= 8; }
        RandomNumberGenerator.Fill(bytes[6..]);

        Span<char> chars = stackalloc char[26];
        // 128 bits -> 26 base32 chars (first char carries 3 bits).
        ulong hi = 0, lo = 0;
        for (var i = 0; i < 8; i++) hi = (hi << 8) | bytes[i];
        for (var i = 8; i < 16; i++) lo = (lo << 8) | bytes[i];
        for (var i = 25; i >= 0; i--)
        {
            chars[i] = Alphabet[(int)(lo & 0x1F)];
            lo = (lo >> 5) | ((hi & 0x1F) << 59);
            hi >>= 5;
        }
        return new string(chars);
    }
}
