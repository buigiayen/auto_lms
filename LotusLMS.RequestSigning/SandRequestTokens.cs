namespace LotusLMS.RequestSigning;

/// <summary>A matching _sand_ri/_sand_rit pair for one HTTP request.</summary>
public sealed class SandRequestTokens
{
    internal SandRequestTokens(string requestId, string requestToken)
    {
        RequestId = requestId;
        RequestToken = requestToken;
    }

    public string RequestId { get; }

    public string RequestToken { get; }
}
