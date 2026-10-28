using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LotusLMS.RequestSigning;

/// <summary>
/// Encodes the current Unix timestamp using the login token, matching webpack
/// module 928962.u3. Authenticated requests require this in addition to ri/rit.
/// </summary>
public static class SandClientSyncTokenGenerator
{
    public const string FieldName = "_sand_client_sync_token";

    public static string Generate(string authenticationToken) =>
        Generate(authenticationToken, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    internal static string Generate(string authenticationToken, long timestampUnixSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationToken);
        if (timestampUnixSeconds is < 0 or > 9_007_199_254_740_991)
        {
            throw new ArgumentOutOfRangeException(nameof(timestampUnixSeconds));
        }

        // JavaScript writes the Number as a big-endian Float64, then maps the
        // sign bit so its two UInt32 words can be encoded in order.
        var bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(timestampUnixSeconds));
        var high = (uint)(bits >> 32);
        var low = (uint)bits;
        var negative = (high & 0x80000000U) != 0;
        var orderedHigh = negative ? ~high : high ^ 0x80000000U;
        var orderedLow = negative ? ~low : low;

        var key = Encoding.UTF8.GetBytes(authenticationToken);
        try
        {
            // 2^48 / 2^32 = 65536; both OPE parameters use its half (32768).
            var multiplier = 32768U + FirstHmacWord(key, "ope:multiplier") % 32768U;
            var offset = FirstHmacWord(key, "ope:offset") % 32768U;
            var encodedHigh = (ulong)orderedHigh * multiplier + offset;
            var encodedLow = (ulong)orderedLow * multiplier + offset;
            return "n:" + encodedHigh.ToString("x12", CultureInfo.InvariantCulture)
                + encodedLow.ToString("x12", CultureInfo.InvariantCulture);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static uint FirstHmacWord(byte[] key, string label) =>
        BinaryPrimitives.ReadUInt32BigEndian(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(label)));
}
