# LotusLMS.Client

Thư viện .NET 10 kết nối backend Node.js trong `client/`. Dùng được từ Worker,
console app hoặc dịch vụ .NET khác. Project không phụ thuộc `LotusLMS.Worker`,
`LotusLMS.Api` hoặc luồng học LMS; chỉ giữ giao thức HTTP và DTO của backend client.

## Thêm vào ứng dụng

```xml
<ProjectReference Include="../LotusLMS.Client/LotusLMS.Client.csproj" />
```

Đăng ký DI:

```csharp
using LotusLMS.Client;

builder.Services.AddLotusClient(
    builder.Configuration.GetSection(LotusClientOptions.SectionName));
```

```json
{
  "Client": {
    "BaseUrl": "http://127.0.0.1:3100",
    "ApiKey": "<worker-api-key>",
    "RequestTimeoutSeconds": 5
  }
}
```

Có thể đặt `Client__BaseUrl`, `Client__ApiKey`, `Client__RequestTimeoutSeconds`
bằng environment/User Secrets. `Enabled`, `WorkerName`, `AccountId` là lựa chọn
điều phối của Worker, không phải thành phần bắt buộc của SDK.

Transport DI đi trực tiếp tới backend (`UseProxy=false`), không tự chuyển hướng,
không ghi request bằng logger mặc định. Claim có proxy snapshot để đối chiếu;
Worker đọc `proxyId` từ chi tiết account rồi gọi endpoint proxy kèm
runId/instanceId/leaseToken để lấy cấu hình và thông tin xác thực. Endpoint proxy
chỉ trả mật khẩu khi lượt chạy đang hoạt động và proxy thuộc account đã claim.
SDK không dùng proxy LMS để gọi backend. Khi cung cấp HttpClient thủ công, caller
cần cấu hình handler tương tự. BaseUrl hỗ trợ đường dẫn gốc như `/lotus/`.

Không dùng DI:

```csharp
using LotusLMS.Client;
using Microsoft.Extensions.Options;

using var backend = new LotusClient(
    new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }),
    Options.Create(new LotusClientOptions {
        BaseUrl = "http://127.0.0.1:3100",
        ApiKey = workerApiKey
    }));
```

`LotusClient` sở hữu HttpClient được truyền vào; dispose SDK khi dùng thủ công.
Trong DI, container quản lý vòng đời client. Tránh chia sẻ HttpClient thủ công
với dịch vụ khác vì SDK cấu hình BaseAddress và Timeout của nó.

## Các hàm kết nối

| Hàm trên `ILotusClient` | Chức năng |
| --- | --- |
| `GetAccountsAsync(query, ct)` | Danh sách học viên chờ, bộ lọc accountId và phân trang |
| `GetAccountAsync(accountId, ct)` | Đọc `proxyId` đã gán sau khi nhận account |
| `GetProxyAsync(proxyId, accountId, ownership, ct)` | Đọc thông tin proxy với quyền sở hữu lượt chạy |
| `ClaimAsync(accountId, request, ct)` | Tiếp nhận học viên, lấy run/token và cấu hình |
| `HeartbeatAsync(accountId, ownership, ct)` | Gia hạn quyền sở hữu và đọc điều khiển |
| `GetControlAsync(accountId, ownership, ct)` | Chỉ đọc điều khiển bằng header, không gia hạn |
| `SendLogAsync(log, ct)` | Gửi log kèm runId và eventId; trả thông tin chống trùng |
| `FinishAsync(accountId, request, ct)` | Xác nhận completed / failed / stopped |
| `RegisterWorkerAsync(workerName, ct)` | Đăng ký Worker trên backend local, lấy API key một lần |

`ClientAccount` có `ManagingTeacher`, `Execution`, proxy tóm tắt và chương trình
đã chọn: `EnrolmentPlanIid` (int?), `EnrolmentPlanName`. Hai trường này cũng có trên
`ClientRunControl` và `ClientAssignment` (kế thừa), dùng cho claim/heartbeat/control.
Đã bỏ `CourseIid`, `CourseName`. Mã null nghĩa là account cũ chưa chọn chương trình. `ClientAssignment`
có Credentials, Lms, Proxy riêng; không cần tham chiếu DTO của project Worker.

Ví dụ nhận một học viên, minh họa gọi SDK theo từng bước:

```csharp
ILotusClient backend = services.GetRequiredService<ILotusClient>();
var page = await backend.GetAccountsAsync(new ClientAccountQuery { PageSize = 20 }, ct);
var account = page.Data.FirstOrDefault();
if (account is null) return;

var claimRequest = new ClientClaimRequest {
    WorkerName = "worker-node-01",
    InstanceId = Guid.NewGuid().ToString(),
    RequestId = Guid.NewGuid().ToString()
};
var assignment = await backend.ClaimAsync(account.Id, claimRequest, ct);
var ownership = new ClientRunOwnership {
    RunId = assignment.RunId,
    InstanceId = assignment.InstanceId,
    LeaseToken = assignment.LeaseToken
};
var details = await backend.GetAccountAsync(account.Id, ct);
var proxy = details.ProxyId is { Length: > 0 } proxyId
    ? await backend.GetProxyAsync(proxyId, account.Id, ownership, ct)
    : null;
var control = await backend.HeartbeatAsync(account.Id, ownership, ct);
if (control.Status != "running") {
    // Dừng công việc trước khi xác nhận finish.
}
await backend.SendLogAsync(new ClientWorkerLog {
    AccountId = account.Id,
    WorkerName = assignment.WorkerName,
    RunId = ownership.RunId,
    InstanceId = ownership.InstanceId,
    LeaseToken = ownership.LeaseToken,
    EventId = Guid.NewGuid().ToString(),
    Level = "info",
    Message = "Đã tiếp nhận học viên",
    Context = new() { ["step"] = "accepted" }
}, ct);
// Chỉ finish sau khi công việc và các request LMS đã kết thúc.
await backend.FinishAsync(account.Id, new ClientFinishRequest {
    RunId = ownership.RunId,
    InstanceId = ownership.InstanceId,
    LeaseToken = ownership.LeaseToken,
    Outcome = "completed"
}, ct);
```

SDK không tự chạy vòng heartbeat, nhận account kế tiếp hoặc retry. Ứng dụng gọi
SDK phải gửi heartbeat mỗi 5 giây, dừng khi mất lease/nhận stopped, và giữ cùng
RequestId khi retry claim sau mất response. Log retry giữ nguyên EventId.
`ClientWorkerBridge` trong Worker hiện có điều phối đầy đủ, chặn request LMS khi
mất lease, gửi log ILogger và xác nhận kết thúc khi host dừng.

## Lỗi và bảo mật thông tin

`LotusClientException` có `Code`, `StatusCode` (có thể null), `Endpoint` (không
query). Ví dụ `ACCOUNT_CLAIMED`, `LEASE_LOST`, `CLIENT_TIMEOUT`,
`CLIENT_CONNECTION_FAILED`, `INVALID_RESPONSE`. Nội dung exception không chứa
response body, mật khẩu, API key hoặc lease token. DTO chứa secret dùng class
thường, không sinh ToString liệt kê giá trị. Không serialize/log toàn bộ claim.

Cancellation do caller được giữ nguyên `OperationCanceledException`. Timeout
của HttpClient chuyển thành `LotusClientException` với code CLIENT_TIMEOUT.
Các response HTTP lỗi vẫn giữ status và code hợp lệ của backend. Không tự retry
mutation; ứng dụng quyết định retry theo ý nghĩa nghiệp vụ.

## Kiểm tra

```bash
dotnet run --project LotusLMS.Client.Tests
dotnet run --project LotusLMS.RequestSigning.Tests -- --worker-client-only
cd client
npm run test:worker
```

Bộ SDK độc lập chỉ dùng HttpMessageHandler giả, không gọi mạng. Smoke test chạy
Worker thật ở chế độ Idle với backend và SQLite tạm, không gửi yêu cầu LMS.
Hợp đồng chi tiết: [WORKER_QUEUE.md](../client/docs/WORKER_QUEUE.md).
