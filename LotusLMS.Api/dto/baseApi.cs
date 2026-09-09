namespace LotusLMS.Api;

public sealed class BaseApi<T> where T : class
{
    public T? Result { get; set; }

    public bool Success { get; set; }
    public string? Ts { get; set; }
    public int ServerTs { get; set; }
    public string? RequestId { get; set; }
    public string? ServerIp { get; set; }
    public int Translate { get; set; }
}
