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
    "RegistrationApiKey": "<secret giống WORKER_REGISTRATION_KEY ở Client API>",
    "PollIntervalSeconds": 5,
    "HeartbeatIntervalSeconds": 5
  }
}
```

Tên lane gửi về Client có dạng `ServerName-WorkerName-01` đến
`ServerName-WorkerName-<MaxConcurrentAccounts>`, ví dụ `server-01-worker-v2-01`.
`Worker:ServerName` không bắt buộc; bỏ trống sẽ dùng `Environment.MachineName`.
Khi chạy Docker, giá trị tự lấy là hostname của container. Đặt `SERVER_NAME`
trong `.env` của Docker Compose để gửi tên máy chủ thật, hoặc truyền
`SERVER_NAME="$(hostname)" docker compose up -d worker` từ shell của máy chủ.
Tên đầy đủ, gồm cả dấu nối và số thứ tự, phải dài tối đa 100 ký tự.
Tên này được dùng xuyên suốt đăng ký, claim, log và hiển thị trên Client.
Đổi tên máy chủ sẽ tạo danh tính lane mới; dữ liệu lượt chạy cũ vẫn giữ tên cũ.
Worker tự gọi `POST /api/workers` cho
từng lane lúc khởi động. Client API tạo lane mới hoặc trả lại cùng API key khi
lane đã tồn tại; Worker giữ các key lane trong bộ nhớ và dùng key riêng khi
poll/claim. Bạn không cần đăng ký lane thủ công hay cấu hình `LaneApiKeys`.

`RegistrationApiKey` là khóa bootstrap chứng minh Worker được phép tự đăng ký.
Cấu hình cùng một secret (tối thiểu 32 ký tự) ở backend qua biến môi trường
`WORKER_REGISTRATION_KEY` và ở Worker qua User Secrets/biến môi trường. Backend
từ chối đăng ký nếu thiếu hoặc sai khóa bootstrap.

Có thể thay bằng User Secrets của project V2 hoặc biến môi trường:

```bash
dotnet user-secrets set "Worker:WorkerName" "worker-v2" --project LotusLMS.Worker.V2
dotnet user-secrets set "Worker:MaxConcurrentAccounts" "5" --project LotusLMS.Worker.V2
dotnet user-secrets set "Worker:RegistrationApiKey" "<secret bootstrap>" --project LotusLMS.Worker.V2
```

Base URL mặc định là localhost:3100; đổi `Client:BaseUrl` nếu backend ở nơi khác.
Biến môi trường có dạng `Worker__WorkerName`, `Worker__ServerName`,
`Worker__MaxConcurrentAccounts`, `Worker__RegistrationApiKey` và
`Client__BaseUrl`. Có thể chạy một lane bằng cách đặt concurrency là 1.
User Secrets được dùng trong môi trường Development. Không lưu secret bootstrap
hay API key lane vào appsettings hoặc Git.

`MaxConcurrentAccounts` từ 1–20 (mặc định 5), bootstrap key tối thiểu 32 ký tự,
`PollIntervalSeconds` từ 1–60;
`HeartbeatIntervalSeconds` từ 1–10. Backend hiện cấp lease 30 giây; mặc định
heartbeat mỗi 5 giây. Thiếu tên/bootstrap key hoặc cấu hình sai sẽ báo chính xác tên
trường cấu hình rồi thoát với mã 1, không để
`OptionsValidationException` kết thúc ứng dụng như exception chưa xử lý.
Nếu đã sửa file mà vẫn lỗi, kiểm tra User Secrets và biến môi trường vì chúng
được ưu tiên hơn appsettings.

## Vận hành và tra log

1. Chạy Client và Worker V2 từ thư mục gốc bằng `docker compose up -d` (hoặc
   chạy Client riêng trong thư mục `client` bằng `npm start`).
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

Nếu bật `StopOnDailyKpi` và LMS xác nhận đã đủ KPI trong ngày, Worker kết thúc
lượt hiện tại với outcome `deferred`; account vẫn ở trạng thái **Treo**, nhưng
được đưa khỏi hàng đợi đến một thời điểm ngẫu nhiên trong khung **00:00–08:00
ngày hôm sau theo giờ Việt Nam**. Backend chọn riêng cho từng account, lưu lịch
để giữ nguyên khi khởi động lại và ghi thời điểm cụ thể vào log. Lane được giải
phóng để nhận account khác. Nếu muốn chạy lại sớm, nhấn **Dừng treo** rồi nhấn
**Treo** để bỏ thời gian hoãn và đưa account vào hàng chờ ngay.

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

Log lỗi LMS ghi mã HTTP khi có phản hồi và phân biệt lỗi đọc JSON, timeout,
DNS/socket và TLS. Thiếu mã HTTP không tự được xem là lỗi mạng. Đường dẫn API
trong log không chứa query; không ghi nguyên văn exception có thể chứa dữ liệu phiên.
Lỗi đọc kiểu JSON vẫn giữ mã HTTP thực tế (ví dụ HTTP 200).

Log terminal trong mỗi lượt có tiền tố
`[lane=<tên lane> account=<accountId> run=<runId> plan=<enrolmentPlanIid>]`
và thời gian đến mili giây. `run` là lượt chạy ở Client API; không phải mã phiên
đăng nhập LMS. Không ghi mật khẩu, token, cookie hay nội dung đáp án.

- `PLAN_READ`: số mục và số mục phân biệt trong kế hoạch vừa lấy từ LMS.
  `PLAN_DUPLICATE` cảnh báo cùng course/item xuất hiện nhiều dòng trong kế hoạch.
- `THEORY_SELECT` / `PRACTICE_SELECT`: bài đang được xử lý, kèm course/item hoặc
  practice. Đây là log chọn bài, chưa phải request ghi dữ liệu.
- `THEORY_SEND` / `THEORY_ACK`: trước lần gọi lưu thời gian và sau khi API trả
  thành công. Cùng `sendId` là hai trạng thái của một lần gọi. `seq` là thứ tự lưu,
  `seconds` là số giây cộng thêm; `THEORY_PROGRESS` ghi tiến độ LMS trước/sau lưu.
- `PRACTICE_START_SEND` / `PRACTICE_START_ACK`: mở lượt ôn luyện, kèm số `attempt`
  và mã lượt làm bài `take` trả về.
- `PRACTICE_SEND` / `PRACTICE_ACK`: một lần lưu batch, có `sendId`, course,
  practice, take, attempt, số batch và danh sách ID câu hỏi. ACK ghi thêm
  `returnedTake`, `gainedKpi`, `overtime` và `serverFinished`; API thành công chưa
  đồng nghĩa được cộng KPI.
- `PRACTICE_REPEAT` ở mức Warning: cùng bài được chọn lại sau refresh trong
  luồng ôn luyện, với `attempt` tăng. Cảnh báo chỉ phục vụ chẩn đoán, không chặn bài.

Đối chiếu gửi lặp theo account, run, course, item/practice và take/batch/câu hỏi.
`SEND` không có `ACK` nghĩa là lần gọi chưa được xác nhận; không thể kết luận LMS
chưa ghi dữ liệu. `sendId` chỉ dùng nối log, không phải khóa chống trùng phía LMS.

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
- Worker nhận cấu hình chuẩn gồm URL HTTP/HTTPS/SOCKS5 và username/password riêng; đồng thời
  chuẩn hóa tương thích chuỗi nhà cung cấp `host:port:username:password` (HTTP).
  Dấu `:` bên trong mật khẩu được giữ nguyên; host IPv6 phải được đặt trong ngoặc vuông.
  Ví dụ SOCKS5: `socks5://160.250.166.18:11548`; dùng SocksPort, để trống
  username/password nếu nhà cung cấp không yêu cầu. Chuỗi nhà cung cấp có thể
  thêm tiền tố `socks5://` để giữ giao thức SOCKS5.
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
npm run test:worker-v2-isolation
node tests/worker-v2-startup-smoke.mjs
```

Bộ C# dùng HTTP giả và đồng hồ điều khiển được để kiểm tra claim retry, context,
lease và log. Smoke test tạo SQLite tạm, chạy hai tiến trình Worker thật để
kiểm tra cạnh tranh, mất phản hồi claim/log/finish, heartbeat, proxy hết hạn, Dừng,
account kế tiếp, thiếu chương trình, shutdown và API key sai. Không dùng học
viên thật hoặc gọi LMS. Worker cũ giữ nguyên.

`test:worker-v2-isolation` chạy một Worker thật với hai lane và LMS giả trên
localhost. Hai account cùng course/item nhưng có tiến độ riêng; kiểm tra token,
cookie, session và GUID thiết bị không bị dùng lẫn. Một request của account A
được giữ chờ đến khi B hoàn tất để xác nhận B không bị chặn bởi A.

Chạy `npm run test:worker-v2-isolation -- --different-pending-items` để kiểm tra
A còn ba bài, B đã hoàn thành bài đầu và chỉ còn hai bài. Kiểm thử đối chiếu
request ghi thực tế: A gửi ba ID, B chỉ gửi hai ID chưa đạt dù A đang chờ lưu bài
mà B đã hoàn thành. Cả hai tình huống dùng luồng lý thuyết và LMS giả trên localhost.
