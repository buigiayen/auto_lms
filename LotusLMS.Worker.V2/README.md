# LotusLMS.Worker.V2

Worker .NET 10 chạy nhiều lane độc lập trong cùng tiến trình. Mỗi lane tự nhận
một account đang Treo từ backend client, duy trì heartbeat, proxy, log và phiên
LMS riêng. Lane kết thúc account sẽ tiếp tục polling; lỗi ở một lane không hủy
các lane còn lại.

## Cấu hình tối thiểu

```json
{
  "Client": {
    "BaseUrl": "http://127.0.0.1:3100",
    "RequestTimeoutSeconds": 5
  },
  "Worker": {
    "WorkerName": "worker-v2",
    "MaxConcurrentAccounts": 5,
    "LaneApiKeys": {
      "worker-v2-01": "<key worker-v2-01>",
      "worker-v2-02": "<key worker-v2-02>",
      "worker-v2-03": "<key worker-v2-03>",
      "worker-v2-04": "<key worker-v2-04>",
      "worker-v2-05": "<key worker-v2-05>"
    },
    "PollIntervalSeconds": 5,
    "HeartbeatIntervalSeconds": 5
  }
}
```

`WorkerName` là tiền tố để tạo tên lane `WorkerName-01` đến
`WorkerName-<MaxConcurrentAccounts>`. Mỗi lane cần đăng ký riêng trên backend
và có API key riêng. Ví dụ gọi `POST /api/workers` lần lượt với tên
`worker-v2-01` ... `worker-v2-05`, rồi lấy `data.apiKey` từ mỗi response.
Nếu tên đã tồn tại, dùng key tương ứng trong `client/data/worker-api-keys.json`.
Worker xác thực đủ key trước khi bắt đầu claim account.

Có thể thay bằng User Secrets của project V2 hoặc biến môi trường:

```bash
dotnet user-secrets set "Worker:WorkerName" "worker-v2" --project LotusLMS.Worker.V2
dotnet user-secrets set "Worker:MaxConcurrentAccounts" "5" --project LotusLMS.Worker.V2
dotnet user-secrets set "Worker:LaneApiKeys:worker-v2-01" "<API key 01>" --project LotusLMS.Worker.V2
dotnet user-secrets set "Worker:LaneApiKeys:worker-v2-02" "<API key 02>" --project LotusLMS.Worker.V2
```

Base URL mặc định là localhost:3100; đổi `Client:BaseUrl` nếu backend ở nơi khác.
Biến môi trường có dạng `Worker__WorkerName`,
`Worker__MaxConcurrentAccounts`, `Worker__LaneApiKeys__worker-v2-01` và
`Client__BaseUrl`. Có thể chạy một lane bằng cách đặt concurrency là 1 và chỉ
cấu hình key `WorkerName-01`. User Secrets được dùng trong môi trường
Development. Không lưu API key thật vào appsettings hay Git.

`MaxConcurrentAccounts` từ 1–20 (mặc định 5), `PollIntervalSeconds` từ 1–60;
`HeartbeatIntervalSeconds` từ 1–10. Backend hiện cấp lease 30 giây; mặc định
heartbeat mỗi 5 giây. Thiếu tên/key hoặc cấu hình sai sẽ báo chính xác tên
trường cấu hình rồi thoát với mã 1, không để
`OptionsValidationException` kết thúc ứng dụng như exception chưa xử lý.
Nếu đã sửa file mà vẫn lỗi, kiểm tra User Secrets và biến môi trường vì chúng
được ưu tiên hơn appsettings.

## Vận hành và tra log

1. Chạy backend trong thư mục `client` bằng `npm start`.
2. Tạo account, chọn proxy còn hạn hoặc kết nối trực tiếp.
3. Nhấn **Run/Treo → Tải chương trình → chọn chương trình học → Chạy tài khoản**.
4. Chạy Worker đã cấu hình. Bảng account hiển thị tên Worker và log tiếp nhận.
5. Nhấn **Dừng treo** để dừng account; lane sở hữu account nhận lệnh ở heartbeat
   tiếp theo, gửi log cuối, xác nhận `stopped` rồi tiếp tục polling.

Khi API LMS trả về danh sách khóa học có mục học nhưng không còn mục nào chưa
đạt, Worker gửi log hoàn thành và gọi Client API để kết thúc lượt với outcome
`completed`; backend sẽ chuyển account sang trạng thái dừng và lane đó tiếp tục
nhận account khác. Danh sách khóa học rỗng được xem là dữ liệu không hợp lệ,
không tự báo hoàn thành.

Account cũ chưa chọn chương trình sẽ kết thúc `failed` kèm log yêu cầu chọn
chương trình và Run lại. Proxy tắt/hết hạn hoặc thay đổi trong khi giữ lượt làm
Worker dừng context hiện tại. Hết lease sẽ hủy context dù request mạng đang chờ.
SIGTERM/Ctrl+C dừng account đang giữ; log cuối và finish có timeout riêng.

Nếu mất phản hồi claim hoặc finish, Worker đối chiếu cùng account và cùng
`RequestId` trước khi lấy account khác. Retry log giữ nguyên `EventId` để không
ghi trùng. Lỗi account hoặc lane không làm dừng các lane khác. Lỗi kết nối tạm
thời được retry có giới hạn bởi thời hạn lease sau khi nhận account. Nếu không
thể xác nhận finish, lane dừng để tránh nhận account mới khi quyền sở hữu chưa
rõ. Lỗi xác minh IP proxy cũng kết thúc account `failed` và dừng lane đó.

## Context cho bước xử lý tiếp theo

`Accounts/AccountRunContext.cs` giữ `EnrolmentPlanIid`, tên chương trình, provider
và quyền sở hữu của một lượt. Các hàm dưới đây chỉ chuẩn bị cấu hình; không gửi
request:

- `CreateLmsOptions()`: Base URL, tenant, WebOrigin từ `assignment.Lms`.
- `CreateLoginRequest()`: thông tin đăng nhập, GUID thiết bị và provider từ account;
  API client dùng provider này cho login và toàn bộ request trong session.
- Proxy được nạp theo thứ tự danh sách account → claim → `GET /api/accounts/:id`
  để lấy `proxyId` → `GET /api/proxies/:proxyId` với ownership headers. Worker
  cấu hình HTTP transport từ proxy trả về; không dùng proxy trong appsettings.
  Account không gán proxy sẽ kết nối trực tiếp.
- Sau khi đăng nhập LMS thành công, nếu account có proxy Worker kiểm tra IP egress
  qua proxy và gửi IP đó về Client API. Nếu không xác minh được IP, Worker gửi
  log lỗi, kết thúc lượt `failed` và dừng lane đang xử lý account đó.

Mỗi lần tạo trả đối tượng riêng, không sửa `IOptions.Value` dùng chung. Các hàm
kiểm tra quyền sử dụng trước khi trả dữ liệu. Kết nối `ILotusClient` tới backend
luôn đi trực tiếp, không qua proxy của LMS.

Mỗi lane tạo `LotusLMS.Client` và phiên `LotusLMS.Api` riêng theo account.
`TimeProvider` được chia sẻ vì không chứa account state.
Không cần nhập lại tenant, WebOrigin, tài khoản, chương trình hoặc proxy trong
appsettings Worker. `LotusLMS.RequestSigning` được tham chiếu qua LotusLMS.Api.

Sau bước Practice, Worker tải lại tiến độ từ LMS rồi chạy `TheoryServices` cho
các mục `AutoStudy` chưa đạt. Theory xử lý video lần lượt theo course, chờ đủ
khoảng thời gian trước khi gọi `save-item-learning-time`, đọc lại thời lượng từ
LMS sau mỗi lần lưu và kiểm tra KPI theo cấu hình. Kết quả hoàn tất chỉ được
trả về sau khi LMS xác nhận lại toàn bộ chương trình đã đạt.

## Debug trong VS Code

1. Dùng file Development local hoặc cấu hình User Secrets như trên.
2. Chọn **Run and Debug → Debug LotusLMS.Worker.V2**.
3. Đặt breakpoint trong `Worker.ExecuteAsync`, `WorkerLane.RunAsync` hoặc
   `AccountReceiver.ReceiveAsync`, nhấn F5.
4. Shift+F5 dừng Worker. Console logger vẫn hiển thị thông điệp màu xanh lá.

Cấu hình VS Code build Debug trước khi chạy và đặt working directory tại project.
Cũng có thể chạy từ thư mục gốc:

```bash
dotnet run --project LotusLMS.Worker.V2
```

## Kiểm thử

```bash
dotnet run --project LotusLMS.Worker.V2.Tests
dotnet run --project LotusLMS.Client.Tests
dotnet build LotusLMS.Worker.V2 --configuration Debug
cd client
npm test
npm run test:worker-v2
node tests/worker-v2-startup-smoke.mjs
```

Bộ C# dùng HTTP giả và đồng hồ điều khiển được để kiểm tra claim retry, context,
lease và log. Smoke test tạo SQLite tạm, chạy hai tiến trình Worker thật để
kiểm tra cạnh tranh, mất phản hồi claim/log/finish, heartbeat, proxy hết hạn, Dừng,
account kế tiếp, thiếu chương trình, shutdown và API key sai. Không dùng học
viên thật hoặc gọi LMS. Worker cũ giữ nguyên.
