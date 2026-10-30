# LotusLMS.Worker.V2

Worker .NET 10 chạy nhiều lane độc lập trong cùng tiến trình. Mỗi lane tự nhận
một account đang Treo từ backend client, duy trì heartbeat, proxy, log và phiên
LMS riêng. Lane kết thúc account sẽ tiếp tục polling; lỗi ở một lane không hủy
các lane còn lại.

## Lọc phần cần chạy

Trong hộp thoại **Chạy tài khoản**, chọn một hoặc nhiều phần: Kiểm tra, Ôn luyện,
Lý thuyết, Thực hành (mô phỏng). Client lưu `runSections` theo account và gửi qua
claim/heartbeat/control. Worker truyền lựa chọn vào `CheckingTimeService.Runner`
và giữ nguyên bộ lọc trong mọi lần tải lại kế hoạch. Khi bỏ chọn lý thuyết,
worker cũng bỏ các request đọc chi tiết bài lý thuyết.

| `runSections` | Loại nội dung trong kế hoạch |
| --- | --- |
| `exam` | `Exam` |
| `review` | `PracticeOnly` |
| `theory` | `AutoStudy` |
| `practical` | `PracticeSpecialOnly` (mô phỏng các tình huống giao thông) |

Hiện worker thực thi ôn luyện thường và lý thuyết. Kiểm tra và mô phỏng chỉ được
lọc trong kế hoạch; nếu còn bài thuộc hai phần này, worker ghi log chưa hỗ trợ
và giữ kết quả chưa hoàn tất. Hoàn thành riêng các phần đã chọn trả kết quả
`SelectedSectionsCompleted`, với log khác hoàn thành cả chương trình.
Kiểm tra KPI trong ngày vẫn áp dụng cho mọi lựa chọn. Account cũ mặc định chọn
cả bốn phần; bộ lọc đã lưu được dùng lại khi chạy lần sau hoặc sau lịch hoãn KPI.

Triển khai lại cả client và worker để sử dụng bộ lọc này.

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

## Chu kỳ chạy/nghỉ cho từng tài khoản

Mặc định mỗi account chạy tối đa **180 phút**, tính từ lúc được worker nhận,
rồi nghỉ ngẫu nhiên **15–30 phút** (chọn theo giây). Khi đến hạn, worker hủy
công việc đang chờ, gửi log cuối, xác nhận `resting` với backend và nhả lane.
Account vẫn ở trạng thái Treo, hiển thị giờ chạy lại; lane có thể nhận account
khác trong quãng nghỉ. Đến hạn, account tự vào hàng chờ. Lần nhận tiếp theo tạo
context, HTTP client/cookie jar và phiên đăng nhập LMS mới, đọc lại tiến độ LMS
để tiếp tục công việc. Mỗi account có bộ đếm và thời gian nghỉ riêng.

Trong `.env` của Docker Compose:

```dotenv
WORK_REST_ENABLED=true
CONTINUOUS_WORK_MINUTES=180
MIN_REST_MINUTES=15
MAX_REST_MINUTES=30
```

Cấu hình .NET tương ứng: `Worker:WorkRestEnabled`, `ContinuousWorkMinutes`,
`MinRestMinutes` và `MaxRestMinutes`. Số phút phải từ 1 đến 1440, max không nhỏ
hơn min. Đặt `WORK_REST_ENABLED=false` để tắt chu kỳ này.

Lịch đã xác nhận được lưu trong SQLite, không bị đổi khi client/worker restart
hay retry finish. Một lượt nhận mới bắt đầu một chu kỳ chạy mới. Dừng thủ công
được ưu tiên và không tự bật lại account; Dừng rồi Run cho phép bỏ qua lịch nghỉ.
Nếu đã đủ KPI trước hạn, vẫn dùng lịch hoãn KPI 00:00–08:00 ngày hôm sau.
Nghỉ theo chu kỳ ghi log `info`, không phát cảnh báo lỗi Discord.

**Triển khai cả Client API và Worker mới cùng nhau**: API thêm outcome `resting`
và schema 10 để lưu thời gian nghỉ; worker cũ vẫn dùng các outcome cũ.

Kiểm thử đầy đủ với Client/SQLite và LMS giả lập cục bộ:
`npm run test:worker-v2-rest` trong thư mục `client` sau khi build worker.
Test dùng chu kỳ làm việc 1 phút và đẩy đồng hồ backend qua quãng nghỉ; không
dùng tài khoản LMS thật.

## Thông báo lỗi qua Discord

Worker gửi trực tiếp tới nhiều Discord webhook, độc lập với Client API và proxy
LMS. Trong `.env` của Docker Compose:

```dotenv
DISCORD_ENABLED=true
DISCORD_WEBHOOK_URLS='https://discord.com/api/webhooks/ID1/TOKEN1;https://discord.com/api/webhooks/ID2/TOKEN2'
DISCORD_EVENTS=worker_failed,lane_failed
```

Mỗi sự cố được gửi tới tất cả URL trong danh sách, phân cách bằng `;`. Chỉ nhận
URL webhook HTTPS của Discord; URL trùng được gộp. Với kênh forum/thread, giữ
`thread_id` trong URL webhook. Không commit URL thật vì URL chứa token.

| Sự kiện | Khi nào gửi |
| --- | --- |
| `worker_failed` | Cấu hình/khởi động/đăng ký lỗi, coordinator lỗi, hoặc toàn bộ lane đã dừng |
| `lane_failed` | Không tiếp nhận account an toàn, không xác nhận finish, xác minh IP proxy lỗi, hoặc lane thoát ngoài dự kiến |
| `account_failed` | Account kết thúc `failed` nhưng lane tiếp tục chạy; cần bật thêm trong `DISCORD_EVENTS` |

Để nhận cả lỗi HTTP LMS như `practice/api/save-take`, dùng:

```dotenv
DISCORD_EVENTS=worker_failed,lane_failed,account_failed
```

Thông báo có tên server, worker/lane, accountId, runId, giờ Việt Nam và lý do.
File JSON đính kèm chứa mã sự kiện và chi tiết HTTP/exception đã được che dữ
liệu nhạy cảm khi có. Không ping role/user hay `@everyone`. Khi tất cả lane lỗi,
worker gửi thêm một thông báo tổng kết riêng. Hoàn thành chương trình, đủ KPI
và dừng bình thường không tạo cảnh báo lỗi.

Cấu hình .NET tương ứng nằm trong section `Discord` (`Discord__Enabled`,
`Discord__WebhookUrls`, `Discord__Events` khi dùng biến môi trường). Mặc định
tắt. `RequestTimeoutSeconds=5`, `DeliveryTimeoutSeconds=15`, `MaxRetries=2`
(tối đa ba lần gửi). Một webhook lỗi không ngăn gửi tới các webhook khác.
Gặp 429, worker chờ `Retry-After`/`retry_after`; gặp 401/403/404 sẽ ngừng dùng
webhook đó đến lần restart. Cấu hình Discord sai được báo theo tên trường/vị
trí URL, vô hiệu hóa mục không hợp lệ và không làm dừng worker.

Worker chống phát trùng cùng loại/mã sự cố/lane/account/run trong 10 phút của
cùng tiến trình. Retry sau mất phản hồi vẫn có thể tạo tin nhắn trùng ở Discord.
Thông báo có giới hạn thời gian, không có hàng đợi lưu bền; OOM/SIGKILL hoặc mất
mạng có thể khiến không gửi được. Dùng giám sát bên ngoài nếu cần bao phủ các
trường hợp tiến trình bị kết thúc đột ngột.

Kiểm thử HTTP thật với stand-in cục bộ (Postman CLI từ 1.70):

```bash
postman simulation run postman/simulations/discord-notifications/discord-notifications.sim.yaml --port 14650 --output ndjson --no-history
```

Ở terminal khác, sau khi build project tests:

```bash
DISCORD_SIMULATION_BASE_URL=http://127.0.0.1:14650 dotnet LotusLMS.Worker.V2.Tests/bin/Debug/net10.0/LotusLMS.Worker.V2.Tests.dll
```

Chỉ mã kiểm thử đổi địa chỉ Discord sang localhost; production vẫn kiểm tra
URL Discord HTTPS và kết nối trực tiếp. Simulation chạy thật coordinator/lane,
giả lập lỗi polling và HTTP 502/429/404 của Discord; không dùng account LMS hoặc
webhook thật. Dừng simulation bằng Ctrl+C sau kiểm thử.

Tham khảo contract [Discord Execute Webhook](https://docs.discord.com/developers/resources/webhook#execute-webhook)
và [Discord Rate Limits](https://docs.discord.com/developers/topics/rate-limits).

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

Khi một HTTP request LMS thất bại, mở **Xem dữ liệu sự kiện → failure** trong
log lỗi của account. `failure.http` có loại lỗi (`application_rejected`,
`http_status`, `invalid_json`, `response_schema`, `timeout`, `network` hoặc
`response_read`), exception/message, thông báo LMS, thời gian request, method,
URL không có query, content type và các trường nghiệp vụ của request. Phản hồi
gồm HTTP status, request/correlation ID nếu có và bản xem trước body; `causes`
giữ loại exception bên trong, thông báo và đường dẫn/vị trí JSON khi có lỗi đọc
dữ liệu. `failure.stackTrace` chỉ vị trí phát sinh exception trong worker/API.
Terminal ghi cùng dữ liệu với nhãn `HTTP_REQUEST_FAILED` và scope lane/account/run.

Thông tin này được ghi khi lỗi xảy ra, không cần bật `LogApiResponses`.
Body/chuỗi dài được rút gọn; body JSON hỏng được bỏ và giữ vị trí lỗi parser.
Thông tin xác thực, token, cookie, phiên, thiết bị
và nội dung đáp án được che hoặc bỏ khỏi chẩn đoán. Không ghi toàn bộ request
form/header. Log cũ chỉ có mô tả chung không thể khôi phục lại body đã bị bỏ.

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
