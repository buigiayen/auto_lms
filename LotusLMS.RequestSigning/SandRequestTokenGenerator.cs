using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LotusLMS.RequestSigning;

/// <summary>
/// Implements the request-pair algorithm shipped in LotusLMS webpack module 185247.
/// Generate a new pair for every request; null means the logged-out browser state.
/// </summary>
public static class SandRequestTokenGenerator
{
    public const string RequestIdField = "_sand_ri";
    public const string RequestTokenField = "_sand_rit";

    public static SandRequestTokens Generate(int? userIid = null) =>
        Generate(
            userIid,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            RandomNumberGenerator.GetBytes(16));

    /// <summary>
    /// Copies a form/query and replaces captured signing fields. Supplying the login
    /// token also adds a current client-sync token. The caller's collection is unchanged.
    /// </summary>
    public static List<KeyValuePair<string, string>> Sign(
        IEnumerable<KeyValuePair<string, string>> fields,
        int? userIid = null,
        string? authenticationToken = null)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (authenticationToken is not null && userIid is null)
        {
            throw new ArgumentException("A user iid is required with an authentication token.", nameof(userIid));
        }
        var tokens = Generate(userIid);
        var signed = fields
            .Where(static field => field.Key is not RequestIdField and not RequestTokenField
                and not SandClientSyncTokenGenerator.FieldName)
            .ToList();
        signed.Add(new(RequestIdField, tokens.RequestId));
        signed.Add(new(RequestTokenField, tokens.RequestToken));
        if (authenticationToken is not null)
        {
            signed.Add(new(SandClientSyncTokenGenerator.FieldName,
                SandClientSyncTokenGenerator.Generate(authenticationToken)));
        }
        return signed;
    }

    // Fixed entropy is only available to the compatibility tests. Production always
    // uses a fresh UUID, UTC clock, and cryptographically random 16-byte IV above.
    internal static SandRequestTokens Generate(
        int? userIid,
        Guid requestUuid,
        long timestampMilliseconds,
        ReadOnlySpan<byte> iv)
    {
        if (userIid is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userIid), "Use a positive user iid or null for guests.");
        }

        if (iv.Length != 16)
        {
            throw new ArgumentException("AES-CBC requires a 16-byte IV.", nameof(iv));
        }

        var requestId = Md5Hex(
            requestUuid.ToString("D") + timestampMilliseconds.ToString(CultureInfo.InvariantCulture));

        // JavaScript's undefined info.iid yields NaN here, not 0. Use a long
        // intermediate to match JS Number arithmetic above Int32.MaxValue / 2.
        var userNumber = userIid.HasValue
            ? (2L * userIid.Value + 2451).ToString(CultureInfo.InvariantCulture)
            : "NaN";
        var secret = Md5Hex(userNumber + " t i a v s t");

        // CryptoJS hashes the UTF-8 lowercase MD5 hex text, not the 16 MD5 bytes.
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        try
        {
            using var aes = Aes.Create();
            aes.Key = key;
            var ciphertext = aes.EncryptCbc(Encoding.UTF8.GetBytes(requestId), iv, PaddingMode.PKCS7);
            return new SandRequestTokens(
                requestId,
                Convert.ToHexString(iv).ToLowerInvariant() + ":" + Convert.ToBase64String(ciphertext));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static string Md5Hex(string value) =>
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
