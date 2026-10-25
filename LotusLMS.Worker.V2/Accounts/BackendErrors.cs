using System.Net;
using LotusLMS.Client;

namespace LotusLMS.Worker.V2.Accounts;

internal static class BackendErrors
{
    public static bool IsTemporary(LotusClientException error) => error.StatusCode is null
        or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int?)error.StatusCode >= 500;
}
