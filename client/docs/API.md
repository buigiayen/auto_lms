# Hợp đồng API client

## 1. Quy ước

- Backend local: `http://127.0.0.1:3100`, API bắt đầu bằng `/api`.
- API client nhận/trả JSON UTF-8; các API LMS ở phần cuối dùng form riêng.
- `accountId`, `eventId`, `sessionId`: UUID. Các mã `iid` LMS là số nguyên dương.
- Thời gian request/response dùng UTC ISO 8601, ví dụ `2026-10-21T08:00:00.000Z`.
- Giao diện và backend cùng origin. Mặc định server chỉ lắng nghe localhost.
- Các giá trị trong ví dụ là dữ liệu minh họa, không phải thông tin tài khoản thật.

### Đăng nhập quản trị

Server yêu cầu `CLIENT_USERNAME` và `CLIENT_PASSWORD` trong môi trường/`.env`.
Các API quản trị tài khoản, log, proxy yêu cầu cookie phiên hợp lệ. Root `/`
chuyển hướng sang `/login` khi chưa đăng nhập; API trả `401 AUTH_REQUIRED`.

| Method | Endpoint | Kết quả |
| --- | --- | --- |
| GET | `/api/auth/session` | `{ data: { authenticated, username } }`; username là null khi chưa đăng nhập |
| POST | `/api/auth/login` | JSON `{ username, password }`; đối chiếu đúng thì cấp cookie và trả authenticated/username |
| POST | `/api/auth/logout` | Hủy phiên ở server, xóa cookie và trả 204 |

Cookie `lotus_admin_session` dùng HttpOnly, SameSite=Strict, Path=/ và hết hạn
sau 12 giờ. Đặt `CLIENT_SESSION_SECURE=true` khi sử dụng HTTPS. Token ngẫu nhiên
được lưu dạng digest trong bộ nhớ; restart vô hiệu hóa phiên cũ. Mười lần sai
trong 5 phút theo địa chỉ kết nối sẽ chặn đăng nhập thêm, trả 429 kèm Retry-After.
Các request trình duyệt vẫn được kiểm tra Origin.

`GET /api/health` công khai cho health check. Đăng nhập quản trị không thay thế
khóa worker: `/api/workers` dùng bootstrap key, `/api/worker/*` dùng API key.
Worker đọc `GET /api/accounts/:id` bằng API key chỉ khi được gán đúng account;
đọc `GET /api/proxies/:id` còn yêu cầu ownership của lượt chạy. Khóa worker
không cho phép gọi API quản trị danh sách/log/chỉnh sửa tài khoản.

Các endpoint `/api/worker/*` yêu cầu `Authorization: Bearer <worker-api-key>`.
Hợp đồng Worker: [WORKER_QUEUE.md](WORKER_QUEUE.md).
Backend cấu hình ánh xạ API key với `workerName`; không tin riêng `workerName`
trong payload. Khi triển khai nhận log từ máy khác, backend cần địa chỉ truy cập
được từ worker và HTTPS cho đường truyền đó.

Response lỗi chung:

```json
{
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "Căn cước phải gồm đúng 12 chữ số.",
    "fields": { "identityNumber": "INVALID_FORMAT" }
  }
}
```

| HTTP | Mã lỗi | Khi nào |
| --- | --- | --- |
| 400 | `VALIDATION_ERROR` | Dữ liệu không hợp lệ |
| 401 | `AUTH_REQUIRED` | Thiếu hoặc hết hạn phiên đăng nhập quản trị |
| 401 | `LOGIN_INVALID` | Sai tên đăng nhập hoặc mật khẩu |
| 429 | `LOGIN_RATE_LIMITED` | Quá nhiều lần đăng nhập sai trong 5 phút |
| 401 | `WORKER_UNAUTHORIZED` | Thiếu/sai API key worker |
| 401 | `WORKER_REGISTRATION_UNAUTHORIZED` | Thiếu/sai khóa bootstrap khi đăng ký Worker |
| 503 | `WORKER_REGISTRATION_DISABLED` | Client API chưa cấu hình `WORKER_REGISTRATION_KEY` |
| 403 | `RUN_OWNERSHIP_MISMATCH` | Sai Worker, instance, run hoặc lease token |
| 403 | `WORKER_PROXY_MISMATCH` | Proxy không được gán cho account mà Worker đang sở hữu |
| 409 | `ACCOUNT_CLAIMED` | Account đã được Worker tiếp nhận |
| 409 | `ACCOUNT_DEFERRED` | Account chưa đến lịch chạy lại, được chọn ngẫu nhiên trong khung 00:00–08:00 ngày hôm sau theo giờ Việt Nam |
| 409 | `LEASE_LOST` | Mất heartbeat hoặc lượt chạy đã kết thúc |
| 409 | `ACCOUNT_BUSY` | Chờ Worker dừng trước khi sửa thông tin |
| 404 | `ACCOUNT_NOT_FOUND` | Không tìm thấy tài khoản |
| 409 | `PROXY_EXPIRED` | Proxy hết hạn, không được gán mới, chạy hoặc gọi LMS |
| 409 | `ACCOUNT_EXISTS` | Trùng căn cước trong cùng nhà cung cấp |
| 409 | `LOG_EVENT_CONFLICT` | Dùng lại event ID nhưng thay đổi nội dung sự kiện |
| 410 | `LMS_SESSION_EXPIRED` | Phiên local đã đóng, hết hạn hoặc mất sau khi backend khởi động lại |
| 422 | `LMS_LOGIN_FAILED` | LMS từ chối thông tin đăng nhập |
| 502 | `LMS_REQUEST_FAILED` | LMS từ chối request sau đăng nhập hoặc trả dữ liệu không hợp lệ |
| 504 | `LMS_TIMEOUT` | Request LMS quá thời gian chờ |

Lỗi LMS thêm `error.upstreamCode` và `error.upstreamStatus` nếu có. HTTP 200 từ
LMS với `success=false` vẫn là lỗi nghiệp vụ. Ưu tiên đọc `message`, `error`,
`error_message`; đồng thời giữ lại mã `err_code` hoặc `error_code`.

## 2. Danh sách endpoint

| Method | Đường dẫn | Chức năng |
| --- | --- | --- |
| GET | `/api/accounts` | Danh sách tài khoản và bộ lọc |
| GET | `/api/accounts/:accountId` | Thông tin local của một tài khoản |
| POST | `/api/accounts` | Tạo tài khoản |
| PATCH | `/api/accounts/:accountId` | Sửa thông tin |
| PATCH | `/api/accounts/:accountId/status` | Treo/Dừng treo |
| GET | `/api/accounts/:accountId/logs` | Xem log theo tài khoản |
| GET | `/api/logs` | Xem log tất cả tài khoản, cùng bộ lọc/phân trang |
| POST | `/api/workers` | Đăng ký/tải lại API key Worker bằng khóa bootstrap |
| GET | `/api/worker/accounts` | Account đã Run, chưa có Worker nhận, proxy hợp lệ |
| POST | `/api/worker/accounts/:accountId/claim` | Xác nhận tiếp nhận và lấy thông tin học viên |
| POST | `/api/worker/accounts/:accountId/heartbeat` | Gia hạn quyền sở hữu và đọc điều khiển |
| POST | `/api/worker/accounts/:accountId/finish` | Xác nhận kết thúc và nhả khóa |
| POST | `/api/worker/logs` | Worker gửi log |
| GET | `/api/worker/accounts/:accountId/control` | Worker đọc trạng thái được yêu cầu |
| POST | `/api/accounts/:accountId/run-session` | Đăng nhập bằng proxy đang chọn để tải chương trình học trước Run |
| POST | `/api/accounts/:accountId/run` | Lưu IID chương trình học và đưa account vào hàng chờ |
| POST | `/api/accounts/:accountId/lms/session` | Đăng nhập LMS để mở chi tiết |
| GET | `/api/accounts/:accountId/today-gained-kpi-time` | Kiểm tra KPI học tập trong ngày |
| GET | `/api/accounts/:accountId/enrolment-plans` | Đọc danh sách chương trình học |
| GET | `/api/accounts/:accountId/enrolment-plans/:enrolmentPlanIid/courses` | Đọc response khóa học raw |
| DELETE | `/api/accounts/:accountId/lms/session` | Đóng phiên xem chi tiết |
| GET | `/api/proxies` | Danh sách cấu hình proxy, không trả mật khẩu |
| POST | `/api/proxies` | Tạo cấu hình proxy |
| GET / PATCH / DELETE | `/api/proxies/:proxyId` | Đọc, sửa hoặc xóa cấu hình chưa được gán |
| POST | `/api/proxies/test` | Thử cấu hình mới, không lưu |
| POST | `/api/proxies/:proxyId/test` | Thử cấu hình đang sửa, có thể giữ mật khẩu đã lưu |

`POST /api/workers` yêu cầu khóa bootstrap trong header Authorization. Tên mới trả HTTP 201; tên đã đăng ký trả HTTP 200 cùng API key ổn định, chỉ khi khóa bootstrap hợp lệ. Khóa bootstrap phải có tối thiểu 32 ký tự và được cấu hình trong tiến trình Client API; nếu thiếu thì endpoint trả 503.

## 3. Tạo tài khoản

`POST /api/accounts`

```json
{
  "identityNumber": "000000000001",
  "password": "example-password",
  "provider": "truongson",
  "status": "stopped",
  "workerName": "worker-01",
  "proxyId": null
}
```

`status` có thể bỏ qua để dùng mặc định `stopped`. Response `201`:

```json
{
  "data": {
    "id": "11111111-1111-4111-8111-111111111111",
    "identityNumber": "000000000001",
    "provider": "truongson",
    "status": "stopped",
    "workerName": "worker-01",
    "hasPassword": true,
    "proxyId": null,
    "proxy": null,
    "createdAt": "2026-10-21T08:00:00.000Z",
    "updatedAt": "2026-10-21T08:00:00.000Z"
  }
}
```

`proxyId` mặc định `null` (kết nối trực tiếp), hoặc UUID của một cấu hình đang bật.
Mỗi account chọn tối đa một proxy; nhiều account có thể chọn cùng cấu hình.
Khi được gán, `proxy` trả tóm tắt `{ id, name, address, enabled }` để hiển thị trong bảng.

Các response thông tin tài khoản dùng cùng cấu trúc `data` trên. Không trả
`password`, `passwordCiphertext`, `deviceUuid` hay token LMS.

## 4. Danh sách và sửa thông tin

`GET /api/accounts?search=worker-01&provider=truongson&status=stopped&page=1&pageSize=20`

Thêm `mood=sleeping|awake|celebrating|angry` để lọc trạng thái mascot (Đang nghỉ,
Thức giấc, Ăn mừng, Gặp lỗi). Bỏ qua hoặc để trống để xem tất cả. Bộ lọc kết hợp
với `search`, `provider`, `status` và được áp dụng trước phân trang; `pagination.total`
là tổng số kết quả phù hợp, còn `summary` vẫn thống kê toàn bộ tài khoản.
Trạng thái mascot khớp với hình hiển thị, bao gồm lỗi ở log gần nhất của lượt
đang chạy và trạng thái nghỉ khi chờ lịch chạy lại.

Các bộ lọc là tùy chọn. `page >= 1`; `pageSize` từ 1 đến 100. Tìm kiếm áp dụng
cho căn cước/tên worker. Response `200`:

```json
{
  "data": [],
  "pagination": { "page": 1, "pageSize": 20, "total": 0 }
}
```

`GET /api/accounts/:accountId` trả thông tin local để điền form.

`PATCH /api/accounts/:accountId` nhận một hoặc nhiều trường được phép:

```json
{
  "identityNumber": "000000000001",
  "provider": "yenthuy",
  "workerName": "worker-02",
  "proxyId": "22222222-2222-4222-8222-222222222222"
}
```

Gửi `proxyId: null` để kết nối trực tiếp; bỏ trường này để giữ lựa chọn cũ.
Proxy không tồn tại trả `404 PROXY_NOT_FOUND`; gán mới proxy đang tắt trả
`400 PROXY_DISABLED`. Sửa account hủy phiên LMS của riêng account đó.

Đổi mật khẩu bằng cách thêm `password` không rỗng. Bỏ trường này giữ nguyên mật
khẩu; gửi `password: ""` hoặc `null` bị từ chối. `status` và các trường kỹ thuật
không được cập nhật qua endpoint này. Response `200` là tài khoản đã lưu.

## 5. Thay đổi trạng thái

`PATCH /api/accounts/:accountId/status`

```json
{ "status": "running" }
```

Chấp nhận `running` hoặc `stopped`. Response `200` là tài khoản với trạng thái mới.
Gọi lại cùng giá trị không làm phát sinh thao tác LMS.

Khi chạy, có thể chọn proxy trong cùng request:

```json
{ "status": "running", "proxyId": "22222222-2222-4222-8222-222222222222" }
```

`proxyId: null` chọn kết nối trực tiếp; bỏ trường này giữ cấu hình đã gán.
Chỉ nhận `proxyId` khi `status=running`. Kiểm tra proxy còn bật/còn hạn trước khi
cập nhật cả trạng thái và lựa chọn bằng một câu UPDATE; nếu lỗi, không đổi dữ liệu.
Proxy đã hết hạn trả `409 PROXY_EXPIRED`, kể cả request không gửi lại `proxyId`.
Dừng account vẫn thực hiện được khi proxy đã hết hạn.

Worker nhận học viên qua danh sách → claim → heartbeat → finish. Các request
sau claim phải có `runId`, `instanceId`, `leaseToken` và API key của Worker.
`GET /api/worker/accounts/:accountId/control` chỉ đọc điều khiển (không gia hạn),
nhận ba trường sở hữu qua header `X-Worker-Run-Id`, `X-Worker-Instance-Id`,
`X-Worker-Lease-Token`. Worker .NET dùng POST heartbeat mỗi 5 giây để gia hạn.
Claim trả thông tin đăng nhập và cấu hình LMS/proxy cho Worker; API giao diện
không trả mật khẩu. Xem [WORKER_QUEUE.md](WORKER_QUEUE.md) cho hợp đồng đầy đủ.

## 6. Gửi và đọc log

`POST /api/worker/logs`, kèm API key của worker:

```json
{
  "eventId": "22222222-2222-4222-8222-222222222222",
  "accountId": "11111111-1111-4111-8111-111111111111",
  "workerName": "worker-01",
  "runId": "33333333-3333-4333-8333-333333333333",
  "instanceId": "44444444-4444-4444-8444-444444444444",
  "leaseToken": "<token từ claim>",
  "level": "info",
  "message": "Practice 11: submitted 5/58 questions.",
  "occurredAt": "2026-10-21T08:02:00.000Z",
  "context": { "practiceIid": 11, "submitted": 5, "total": 58 }
}
```

Response `201` khi tạo mới:

```json
{ "data": { "id": 1, "eventId": "22222222-2222-4222-8222-222222222222", "duplicate": false } }
```

Gửi lại cùng sự kiện trả `200`, cùng `id`, `duplicate=true`. So sánh nội dung
nghiệp vụ, không so `receivedAt`. Nội dung khác với cùng khóa sự kiện trả `409`.
`message` dài 1–10.000 ký tự; `context` là object JSON, mặc định `{}`.

`GET /api/accounts/:accountId/logs?level=error&from=2026-10-21T00:00:00Z&to=2026-10-22T00:00:00Z&page=1&pageSize=50`

Các bộ lọc tùy chọn. `from` bao gồm, `to` không bao gồm; yêu cầu `from < to` nếu
có cả hai. `pageSize` tối đa 100. Response gồm `data` chứa các bản ghi log có
`id`, các trường sự kiện và `receivedAt`, cùng `pagination` như API danh sách.

## 7. Mở phiên xem chi tiết

`POST /api/accounts/:accountId/lms/session`, không yêu cầu frontend gửi lại mật khẩu.

Backend đọc tài khoản, chọn cấu hình nhà cung cấp, giải mã mật khẩu và đăng nhập.
Response `201`:

```json
{
  "data": {
    "sessionId": "33333333-3333-4333-8333-333333333333",
    "expiresAt": "2026-10-21T08:18:00.000Z",
    "user": { "iid": 1, "displayName": "Tài khoản minh họa" }
  }
}
```

Các request đọc chi tiết sau đó dùng header:

```http
X-Lms-Session-Id: 33333333-3333-4333-8333-333333333333
```

Backend kiểm tra phiên thuộc đúng `accountId` và chưa hết hạn. Request hợp lệ
gia hạn thời gian không hoạt động thêm 15 phút. Thiếu header hoặc phiên không
còn hợp lệ trả `410 LMS_SESSION_EXPIRED`; phiên thuộc tài khoản khác bị từ chối
với `403 LMS_SESSION_ACCOUNT_MISMATCH`.

### 7.1. Kiểm tra KPI trong ngày

`GET /api/accounts/:accountId/today-gained-kpi-time`, kèm header phiên ở trên.
Backend gọi LMS `POST /time-tracking/learning-time-tracking/today-gained-kpi-time`
bằng `user_iid` của phiên và trả về hai giá trị KPI:

```json
{
  "data": {
    "totalGainedKpiTimeInSeconds": 123,
    "maxGainedKpiTimeInSeconds": 900
  }
}
```

Modal chi tiết hiển thị nút “Kiểm tra KPI 1 ngày”; sau khi kiểm tra, giao diện
hiển thị `totalGainedKpiTimeInSeconds / maxGainedKpiTimeInSeconds` (giây).
API LMS cần URL trang học làm ngữ cảnh request; client dùng `110` như
`CheckingTimeService` hiện tại. Tổng KPI là theo người dùng/ngày, không lấy
IID chương trình học làm IID khóa học.

## 8. Chọn chương trình và xem khóa học raw

`GET /api/accounts/:accountId/enrolment-plans?page=1&pageSize=12`, kèm header phiên.

```json
{
  "data": [ { "iid": 99, "name": "Chương trình học minh họa" } ],
  "pagination": { "page": 1, "pageSize": 12, "hasMore": false }
}
```

Giá trị dropdown là `iid`, nhãn là `name` và `iid`. Nếu thiếu tên, hiển thị
`Chương trình #99`. Giới hạn `pageSize` từ 1 đến 200; dùng thông tin tổng/phân
trang từ upstream nếu có, nếu không thì `hasMore = result.length === pageSize`.

Sau khi chọn `99`:

`GET /api/accounts/:accountId/enrolment-plans/99/courses?page=1&pageSize=20`

Backend kiểm tra `enrolmentPlanIid` thuộc danh sách chương trình đã đọc trong
phiên này trước khi gọi LMS. Nếu chưa được đọc hoặc không thuộc tài khoản, trả
`403 ENROLMENT_PLAN_NOT_AVAILABLE`. Cho phép phân trang response khóa học, mỗi
trang có `pageSize` từ 1 đến 200. Response `200`:

```json
{
  "data": {
    "accountId": "11111111-1111-4111-8111-111111111111",
    "enrolmentPlanIid": 99,
    "requestedAt": "2026-10-21T08:04:00.000Z",
    "page": 1,
    "pageSize": 20,
    "hasMore": false,
    "rawResponse": {
      "success": true,
      "result": [
        {
          "name": "Nhóm học phần",
          "course": null,
          "children": [ { "course": { "iid": 22, "name": "Học phần minh họa" } } ]
        }
      ]
    }
  }
}
```

`rawResponse` là toàn bộ body JSON upstream của trang đã yêu cầu, giữ nguyên
các trường và cấu trúc. Không chuyển qua DTO chỉ chứa `result`, không làm phẳng
`children` trước khi trả về modal. `hasMore` áp dụng quy tắc như danh sách
chương trình; trường hợp đủ đúng một trang có thể cần gọi trang kế tiếp để xác
nhận hết dữ liệu.

`DELETE /api/accounts/:accountId/lms/session`, kèm header phiên, trả `204`.
Chỉ xóa phiên local và cookie jar backend; không tự gọi `/user/logout` của LMS.
Gọi đóng lại một phiên đã xóa của cùng tài khoản vẫn trả `204`.

## 9. Ánh xạ sang API LMS hiện có

### 9.1. Cấu hình theo nhà cung cấp

| `provider` | `_sand_domain` | Web origin | API base URL theo cấu hình hiện tại |
| --- | --- | --- | --- |
| `yenthuy` | `yenthuy` | `https://yenthuy.huelms.com` | `https://staging-api.lotuslms.com` |
| `truongson` | `truongson` | `https://truongson.huelms.com` | `https://staging-api.lotuslms.com` |

Backend giữ allowlist hai nhà cung cấp; frontend không gửi URL LMS tùy ý. URL
API lấy theo cấu hình phía server, dựa trên snapshot mã nguồn hiện tại.

Nếu dùng proxy, cấu hình proxy ở HTTP transport backend và giữ cùng proxy,
cookie jar, device UUID và phiên trong toàn bộ chuỗi login → plans → courses.
Không dùng proxy mặc định của Worker như một thông số ngầm của client.

### 9.2. Đăng nhập

`POST {apiBaseUrl}/user/login`, form `application/x-www-form-urlencoded`.

| Trường | Nguồn |
| --- | --- |
| `lname` | `accounts.identity_number` |
| `pass` | Mật khẩu đã giải mã |
| `_sand_domain` | Nhà cung cấp |
| `_sand_web_url` | `{webOrigin}/user/login/?logout=1` |
| `_sand_device_uuid` | `accounts.device_uuid` |
| `_sand_session_id` | GUID mới cho phiên xem chi tiết |
| `_sand_ri`, `_sand_rit` | Chữ ký mới theo thuật toán dự án |

Giữ các browser flag và header theo `LotusLmsApiClient.LoginAsync`. Chỉ xem là
đăng nhập thành công khi response có `success` không bị từ chối,
`result.pass=1`, `result.token` và `result.iid` hợp lệ. Lưu token, user iid, user
id, device UUID, session ID và cookie jar trong bộ nhớ backend.

### 9.3. Lấy chương trình học

`POST {apiBaseUrl}/student/api/get-student-enrolment-plans`.

Query có `_sand_get_total=0`, `items_per_page=12`, `page=1` và các browser flag.
Body chứa `_sand_token`, `_sand_uiid`, `_sand_uid`. Transport thêm chữ ký mới và
`_sand_client_sync_token` cho request đã xác thực, đối chiếu cách làm trong
`GetStudentEnrolmentPlansAsync`.

Header `Origin` là web origin nhà cung cấp; `Referer` là `{webOrigin}/student`.
Backend lấy `result[]` và ghi nhận các `iid` chương trình thuộc phiên.

### 9.4. Lấy danh sách khóa học

`POST {apiBaseUrl}/student/api/get-student-courses-in-enrolment-plan-table-view`.

Query nghiệp vụ:

```text
_sand_get_total=0
enrolment_plan_iid=99
items_per_page=20
page=1
expand[]=syllabus
expand[]=score_by_rubric
```

Giữ hai khóa `expand[]` khi dựng query, không ghi đè một giá trị bằng giá trị
còn lại. Body chứa thông tin phiên và browser context như
`BuildAuthenticatedForm`; `Referer` là `{webOrigin}/student/ep/99`.

Node.js adapter phải đọc body JSON gốc để phục vụ modal. Hàm C# hiện tại
`GetStudentCoursesAsync` trả `data.Result` qua DTO nên không phải là response
raw đầy đủ để dùng trực tiếp cho yêu cầu này.

### 9.5. Ký request và xử lý lỗi

Port phần ký bằng Node.js crypto theo
[tài liệu thư viện hiện có](../../LotusLMS.RequestSigning/README.md), đối chiếu
hai bộ fixture crypto/client-sync trước khi gọi thật. Mỗi request sinh cặp
`_sand_ri`/`_sand_rit` mới; request sau đăng nhập cần client sync token tính từ
login token và thời gian hiện tại. Không lưu cặp chữ ký capture cũ vào tài khoản.

Kiểm tra cả HTTP status lẫn `success` trong body. Ví dụ lỗi backend trả cho modal:

```json
{
  "error": {
    "code": "LMS_REQUEST_FAILED",
    "message": "LMS từ chối request theo dõi phiên hoặc thiết bị.",
    "upstreamCode": "err_single_device_tracking",
    "upstreamStatus": 200
  }
}
```

Timeout mỗi request LMS mặc định 30 giây, có cấu hình phía backend. Không tự
retry đăng nhập hoặc tự đăng nhập lại khi phiên bị từ chối; giao diện cho người
dùng chủ động thử lại để tránh tạo thêm phiên ngoài ý muốn.

## 10. Danh sách proxy và lựa chọn theo account

`GET /api/proxies` trả `{ data: [...] }`. Mỗi cấu hình gồm `id`, `name`, `enabled`,
`address`, `username`, `hasPassword`, `accountCount`, `expiresOn`, `expiresAt`,
`isExpired`, `updatedAt`; không trả mật khẩu.
`GET /api/proxies?available=true` chỉ trả proxy đang bật và chưa hết hạn, dùng
cho danh sách khi nhấn Run/Treo. Danh sách quản lý mặc định vẫn trả tất cả để
người dùng gia hạn cấu hình cũ.
`GET /api/proxies/:proxyId` trả `{ data: {...} }` cùng cấu trúc.
Request từ giao diện không trả mật khẩu. Worker sau khi claim phải đọc
`proxyId` từ `GET /api/accounts/:accountId`, sau đó gọi endpoint proxy với
`Authorization` của Worker cùng các header `X-Worker-Account-Id`,
`X-Worker-Run-Id`, `X-Worker-Instance-Id` và `X-Worker-Lease-Token`. Backend
chỉ trả mật khẩu nếu lượt còn hoạt động, Worker sở hữu lượt đó và proxy được
gán đúng account; request thiếu/sai quyền sở hữu bị từ chối.

`POST /api/proxies` trả `201`; `PATCH /api/proxies/:proxyId` trả `200`. Cả hai
nhận đầy đủ `name`, `enabled`, `address`, `username` và mật khẩu tùy chọn:

```json
{
  "name": "Proxy Trường Sơn 01",
  "enabled": true,
  "address": "http://proxy.example:29087",
  "username": "proxy-user",
  "password": "example-proxy-password",
  "expiresOn": "2026-12-31"
}
```

`name` dài 1–100 ký tự. `address` có thể là URI HTTP/HTTPS/SOCKS5 có host và port (1–65535),
không chứa thông tin đăng nhập trong URL hoặc đường dẫn, hoặc chuỗi nhà cung cấp
`host:port:username:password` (cũng chấp nhận tiền tố `http://`, `https://` hoặc `socks5://`).
Chuỗi nhà cung cấp được chuẩn hóa thành URI theo giao thức đã nhập (mặc định HTTP) và
username/password tách riêng trước khi lưu; password có thể chứa dấu `:`.
Host IPv6 trong chuỗi này phải đặt trong ngoặc vuông, ví dụ
`[2001:db8::1]:29087:proxy-user:example-password`. Khi sửa, bỏ `password` để giữ
mật khẩu của **chính cấu hình đang sửa**. Cấu hình mới không kế thừa mật khẩu
cấu hình khác. Gửi `clearPassword: true` để xóa mật khẩu đang lưu.

Ví dụ proxy SOCKS5 không cần username/password: `address: "socks5://160.250.166.18:11548"`,
`username: ""` và bỏ trường `password` khi tạo mới. Dùng **SocksPort**, không dùng
cổng HTTP của nhà cung cấp. Khi sửa cấu hình đang có mật khẩu sang không xác thực,
gửi thêm `clearPassword: true`. Kiểm tra kết nối và các phiên LMS đều đi qua
SOCKS5, kể cả phân giải tên miền đích; kết quả kiểm tra trả IP đầu ra tại thời điểm đó.

`expiresOn` là ngày `YYYY-MM-DD` hoặc `null`. Khi sửa, bỏ trường này giữ ngày
cũ; gửi `null` để bỏ hạn. Ngày phải tồn tại trong lịch (không nhận 30/02).
Proxy dùng được đến hết ngày theo `Asia/Ho_Chi_Minh` (UTC+7). Ví dụ
`expiresOn: "2026-12-31"` hết hạn tại `2026-12-31T17:00:00.000Z` (00:00 ngày
01/01 ở Việt Nam). `isExpired` được tính lại khi đọc, không lưu cờ vào database.
Proxy cũ chưa có ngày giữ `null`, không tự coi là hết hạn.

UI hiển thị ngày kèm màu và chữ: xanh còn hạn, vàng khi còn tối đa 72 giờ,
đỏ đã hết hạn, xám chưa đặt hạn. Khi Run, proxy hết hạn không xuất hiện;
proxy hiện tại đã hết hạn không tự chuyển sang trực tiếp. Phải chủ động chọn
proxy khác hoặc kết nối trực tiếp. Backend kiểm tra lại khi xác nhận để chặn
cấu hình hết hạn trong lúc đang mở modal.

Các phiên LMS đang mở cũng kiểm tra hạn ở mỗi lần gọi API tiếp theo. Worker
control trả `enabled=false` cho proxy hết hạn (giữ nguyên cấu hình trong DB),
kèm `expiresAt`. Worker dừng ở lần kiểm tra kế tiếp, không chuyển sang trực tiếp.

`POST /api/proxies/test` nhận cùng payload cho cấu hình mới.
`POST /api/proxies/:proxyId/test` thử bản đang sửa, giữ mật khẩu cũ khi không gửi
`password`. Kiểm tra luôn thực hiện qua proxy đã nhập kể cả khi `enabled=false`,
không lưu, không đăng nhập LMS, không hủy phiên. Thành công trả
`{ "data": { "ip": "203.0.113.10", "viaProxy": true } }`; lỗi trả
`502 PROXY_CONNECTION_FAILED` hoặc `504 PROXY_TIMEOUT`.

Gán cho account qua `POST/PATCH /api/accounts...` với `proxyId`. Login, đọc chương
trình và đọc khóa học cùng dùng một proxy của account. Proxy đang tắt hoặc kết
nối lỗi không tự chuyển sang trực tiếp; đăng nhập với cấu hình đang tắt trả
`409 PROXY_DISABLED` trước khi gọi LMS. Sửa cấu hình proxy chỉ hủy các phiên của
account đang chọn cấu hình đó; account khác vẫn giữ phiên.

`DELETE /api/proxies/:proxyId` trả `204`. Nếu đang có account sử dụng, trả
`409 PROXY_IN_USE`; phải chọn proxy khác hoặc `proxyId: null` cho các account đó.
Worker có `Client:Enabled=true` nhận proxy account qua endpoint control; Worker
độc lập (`false`) tiếp tục dùng `Http:Proxy` trong cấu hình .NET.

## 11. Giáo viên quản lý account

POST/PATCH account nhận `managingTeacher` (chuỗi tùy chọn, tối đa 200 ký tự,
trim khoảng trắng). Bỏ trường khi PATCH để giữ tên cũ; gửi chuỗi rỗng để xóa.
GET account/danh sách trả `managingTeacher`; bảng hiển thị cột Giáo viên quản lý.
Có thể PATCH chỉ trường này khi Worker đang chạy, giữ quyền sở hữu và phiên LMS.
Account cũ mặc định chuỗi rỗng và hiển thị “Chưa nhập”.

## 12. Chọn chương trình học khi Chạy tài khoản

1. `POST /api/accounts/:id/run-session` body `{ "proxyId": null }` (hoặc UUID proxy)
   trả `201 data.sessionId`. Đây là phiên đọc LMS bằng kết nối đã chọn; chưa lưu
   proxy, chương trình học hoặc đổi trạng thái account. Worker đang giữ lượt chạy
   sẽ trả `409 ACCOUNT_BUSY` trước khi đăng nhập.
2. Với header `X-Lms-Session-Id`, gọi `GET /api/accounts/:id/enrolment-plans`.
   Danh sách `{ iid, name }` lấy từ `get-student-enrolment-plans` là các lựa chọn
   chương trình học. Hỗ trợ page/pageSize và pagination.hasMore để tải thêm.
   Bước Run không gọi API courses và không có lựa chọn khóa học.
3. `POST /api/accounts/:id/run` cùng header phiên:

```json
{
  "proxyId": null,
  "enrolmentPlanIid": 99
}
```

Backend chỉ nhận chương trình đã đọc trong đúng phiên Run, đúng account và cùng
proxy. Không nhận tên từ frontend; `enrolmentPlanName` lấy từ phản hồi LMS.
Chương trình chưa tải/không thuộc tài khoản trả `403 ENROLMENT_PLAN_NOT_AVAILABLE`.
Sai kiểu IID trả 400. Phiên khác account trả 403; phiên hết hạn/đã đóng trả 410.
Đổi proxy sau khi tải phải tải lại. Proxy hết hạn hoặc thay đổi cấu hình giữa
chừng sẽ bị chặn trước khi lưu. Phiên chi tiết thông thường không dùng để xác
nhận Run. Body chỉ nhận hai trường trên; không nhận `courseIid` hoặc `courseName`.

Thành công 200 trả account đã lưu `enrolmentPlanIid`, `enrolmentPlanName`,
`proxyId`, `status=running`. Trạng thái và lựa chọn được cập nhật cùng transaction;
đóng phiên LMS xem trước sau khi lưu. Hủy modal không thay đổi account.
API PATCH status cũ vẫn hỗ trợ Treo/Dừng và giữ lựa chọn hiện tại, không dùng
để tự đặt chương trình mới. Sửa căn cước/nhà cung cấp xóa lựa chọn của học viên cũ.

Các response account (`GET /api/accounts`, `GET /api/accounts/:id`) và Worker
(`GET /api/worker/accounts`, claim, heartbeat, control) trả:

```json
{
  "enrolmentPlanIid": 99,
  "enrolmentPlanName": "Chương trình A"
}
```

Các response trên không còn `courseIid` và `courseName`. Account cũ chưa chọn
chương trình trả null cho mã và chuỗi rỗng cho tên. Schema 6 nâng cấp giữ mã
chương trình đã lưu; tên để rỗng cho đến khi chọn lại, không dùng tên khóa cũ
làm tên chương trình. Worker đã nhận không được thay đổi chương trình giữa lượt.
SDK .NET có `EnrolmentPlanIid` dạng `int?` và `EnrolmentPlanName` trên
ClientAccount/ClientRunControl; ClientAssignment kế thừa các trường đó.
Thay đổi này chuẩn bị dữ liệu và DTO, chưa bổ sung logic thực thi cho Worker.
API courses phục vụ modal xem chi tiết vẫn giữ nguyên.
