using System.Globalization;
using System.Security.Cryptography;
using System.Text;

public static class RequestHelper
{
    public const string keyTest= "b7ece353fd9ad8eb1d9c8beee1c4ada8";
    public static string CreateRequestId()
    {
        // Tương ứng giá trị UUID do s.A() tạo ra.
        string uuid = Guid.NewGuid().ToString("D");

        // Tương ứng Date.now().
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Nối UUID với timestamp, không thêm dấu cách.
        string input = uuid
            + timestamp.ToString(CultureInfo.InvariantCulture);

        // r = MD5(UUID + timestamp), trả về Hex viết thường.
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(input));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }
    public static string EncryptRequestToken(
        string plaintext,
        string secret)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(secret);

        Console.WriteLine($"plaintext: {plaintext}");
        Console.WriteLine($"secret: {secret}");

        // Tương đương Hex.parse(u(e)),
        // với u(e) = SHA256(e).toString(Hex).
        byte[] key = SHA256.HashData(
            Encoding.UTF8.GetBytes(secret));

        using Aes aes = Aes.Create();

        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;

        // IV ngẫu nhiên 16 byte, tương đương WordArray.random(16).
        aes.GenerateIV();

        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

        using ICryptoTransform encryptor = aes.CreateEncryptor();

        byte[] ciphertext = encryptor.TransformFinalBlock(
            plaintextBytes,
            0,
            plaintextBytes.Length);

        string ivHex = Convert.ToHexString(aes.IV).ToLowerInvariant();
        string ciphertextBase64 = Convert.ToBase64String(ciphertext);

        Console.WriteLine($"iv: {ivHex}");

        // Định dạng giống JavaScript: ivHex:ciphertextBase64.
        return $"{ivHex}:{ciphertextBase64}";
    }
}