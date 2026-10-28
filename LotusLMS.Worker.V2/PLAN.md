# Kế hoạch refactor Worker V2 — task 7070

Nguồn: [refactor lại worker](https://task.aqy.vn/api/public/tasks/7a9aea8847704ab18d4a529870a3e013).

## 1. Mục tiêu và phạm vi

Triển khai hai bước đang có trong task:

1. Worker tự lấy danh sách account đang Treo từ backend client.
2. Tiếp nhận một account, gửi log, duy trì quyền sở hữu và chuẩn bị dữ liệu cho quy trình xử lý tiếp theo.

Theo yêu cầu mới nhất, mục tiêu chạy là **chương trình học**: dùng
`EnrolmentPlanIid`, `EnrolmentPlanName`. Không dùng `CourseIid` hoặc lấy IID học
viên từ kết quả đăng nhập để thay thế mã chương trình.

Giai đoạn này chưa chuyển toàn bộ nghiệp vụ học/ôn luyện của Worker cũ sang V2.
Quy trình nghiệp vụ sau khi tiếp nhận cần được mô tả trước khi triển khai phần đó.
Không đánh dấu `completed` chỉ vì đã nhận account thành công.

## 2. Những phần đã có

| Thành phần | Hiện trạng và cách sử dụng |
| --- | --- |
| `client` | Có hàng chờ account Treo, lọc proxy hết hạn, claim độc quyền bằng transaction, heartbeat, control, finish và log theo lượt chạy. |
| `LotusLMS.Client` | Có SDK và DTO cho toàn bộ API trên; cấu hình riêng bằng `IOptions<LotusClientOptions>`. |
| Claim response | Đã có credentials, device UUID, provider, `lms.base`, `lms.tenant`, `lms.origin`, proxy và chương trình học đã chọn. |
| `LotusLMS.Worker.V2` | Có host .NET 10, DI, console xanh và cấu hình debug VS Code; chưa nhận account. |
| `LotusLMS.Worker` | Có xử lý claim/lease/proxy để tham khảo, nhưng đang gộp nhiều trách nhiệm trong bridge. |
| `LotusLMS.Api`, `LotusLMS.Http` | Có API client và HTTP proxy; cần tạo cấu hình theo từng account khi kết nối nghiệp vụ LMS. |

Không tạo thêm API nhận việc hoặc SDK HTTP trùng với các phần đang có.

## 3. Trình tự thực hiện

### Bước 1 — Cấu hình và khởi động

- Giữ section `Client`: `BaseUrl`, `ApiKey`, `RequestTimeoutSeconds`.
- Thêm `WorkerOptions` riêng: `WorkerName`, `PollIntervalSeconds`, `HeartbeatIntervalSeconds`.
- Đề xuất polling và heartbeat mỗi 5 giây, phù hợp lease hiện tại 30 giây; validate cấu hình khi khởi động.
- `WorkerName` phải khớp tên đã đăng ký cho API key. Tạo `InstanceId` mới một lần cho mỗi tiến trình.
- Không yêu cầu nhập lại tài khoản LMS, tenant, origin, chương trình hoặc proxy trong appsettings Worker.
- Build baseline trước khi sửa; ghi nhận và tách các lỗi sẵn có khỏi thay đổi của task.

### Bước 2 — Lấy danh sách học viên

- Dùng `ILotusClient.GetAccountsAsync`, gọi `GET /api/worker/accounts`.
- Dùng bộ lọc sẵn có của backend: Treo, chưa có Worker giữ lượt và proxy hợp lệ.
- Chọn từng ứng viên theo thứ tự backend trả. Mỗi tiến trình xử lý một account tại một thời điểm.
- Không có account thì chờ đến chu kỳ tiếp theo; luôn hỗ trợ `CancellationToken`.
- Chỉ phân trang khi cần tìm ứng viên khác; lấy lại danh sách sau xung đột để tránh sử dụng trang cũ khi hàng chờ thay đổi.

### Bước 3 — Claim độc quyền

- Gọi `ClaimAsync` với `WorkerName`, `InstanceId`, `RequestId`.
- Chỉ xem account là đã tiếp nhận khi claim thành công; danh sách GET chưa cấp quyền xử lý.
- Nếu mất phản hồi, retry cùng account và cùng `RequestId` để nhận lại đúng lượt đã tạo.
- Với `ACCOUNT_CLAIMED` hoặc account đã dừng: bỏ ứng viên, lấy lại hàng chờ.
- Không xử lý mọi lỗi 409 giống nhau: `WORKER_INSTANCE_BUSY` phải đối chiếu lượt đang giữ; không tiếp tục claim account khác.
- Lưu quyền sở hữu gồm `RunId`, `InstanceId`, `LeaseToken` để gửi heartbeat, log và finish.

### Bước 4 — Chuẩn bị dữ liệu account và gửi log

Tạo context riêng cho lượt chạy từ **claim response**, không lấy dữ liệu nhạy cảm
từ danh sách account hoặc cấu hình cố định của Worker.

| Dữ liệu cần dùng | Nguồn |
| --- | --- |
| Tài khoản, mật khẩu, GUID thiết bị | `assignment.Credentials` |
| Tenant | `assignment.Lms.Tenant` |
| WebOrigin | `assignment.Lms.Origin` |
| Base URL API LMS | `assignment.Lms.Base` |
| Chương trình cần chạy | `assignment.EnrolmentPlanIid`, `assignment.EnrolmentPlanName` |
| Proxy của account | `assignment.Proxy`; null là kết nối trực tiếp |

- Kiểm tra context đủ dữ liệu, mã chương trình dương, proxy còn dùng được và danh tính lượt khớp yêu cầu claim.
- Account cũ chưa chọn chương trình: gửi log rõ lý do, kết thúc lượt `failed`; không tự chọn chương trình thay người dùng.
- Gửi log tiếp nhận bằng `SendLogAsync`, có account, worker, run và `EventId`. Backend đã ghi log claim; log Worker xác nhận phía Worker đã nhận phản hồi và kiểm tra context.
- Retry log giữ nguyên `EventId`, không claim lại account chỉ vì gửi log thất bại.
- Console tiếp tục dùng logger màu xanh. Không ghi mật khẩu, token hoặc toàn bộ assignment vào log.
- Khi tạo LMS client cho context, dùng options, cookie và HTTP handler riêng theo lượt; không sửa `IOptions.Value` dùng chung giữa các account. Kết nối backend client vẫn đi trực tiếp, tách khỏi proxy LMS.

### Bước 5 — Duy trì quyền sở hữu và dừng

- Chạy heartbeat độc lập với công việc của account; không để công việc dài làm hết lease.
- Dùng thời hạn server trả và đồng hồ đơn điệu để tính thời gian còn lại, có trừ thời gian request.
- Mất lease, backend yêu cầu dừng hoặc proxy không còn hợp lệ: hủy context và chặn request LMS tiếp theo.
- Lỗi mạng khi đã claim: không bắt đầu account khác khi chưa xác định quyền sở hữu. Chỉ retry heartbeat trong thời gian lease còn hợp lệ.
- Shutdown gửi log cuối và `FinishAsync` với timeout ngắn riêng; nếu backend không truy cập được, dừng cục bộ và để lease hết hạn.
- Khi có quy trình nghiệp vụ, quy trình đó quyết định `completed`/`failed`; nhận lệnh dừng dùng `stopped`.
- Khi hoàn tất, giải phóng tài nguyên của account cũ rồi quay lại hàng chờ. Trong giai đoạn chỉ nhận việc, giữ heartbeat chờ lệnh dừng, không giả lập hoàn thành nghiệp vụ.

### Bước 6 — Kiểm thử và tài liệu

- Hai Worker cùng claim một account: chỉ một Worker thành công.
- Mất phản hồi claim rồi retry: trả lại cùng lượt và token, không tạo lượt trùng.
- Account dừng giữa GET và claim; hàng chờ trống; backend timeout/401/403/409.
- Nhận đúng tenant, origin, GUID, chương trình và proxy của từng account; account sau không dùng cookie/options của account trước.
- Thiếu chương trình, proxy hết hạn, mất heartbeat hoặc nhấn Dừng: Worker ngừng đúng account và log có đúng run.
- Log retry không trùng sự kiện; lỗi gửi log không làm tạo claim mới.
- Build V2, chạy SDK/backend tests và smoke test với backend/SQLite tạm, LMS giả lập; kiểm tra F5/Shift+F5 trong VS Code.
- Cập nhật README về cấu hình tối thiểu, nhận việc, dừng và tra log.

## 4. Tổ chức code dự kiến

```text
LotusLMS.Worker.V2/
├── Worker.cs                     # Vòng lặp điều phối
├── WorkerOptions.cs              # Cấu hình vận hành và validation
├── Accounts/
│   ├── AccountReceiver.cs         # Lấy danh sách và claim
│   ├── AccountRunContext.cs       # Dữ liệu và quyền sở hữu của một lượt
│   ├── AccountLease.cs            # Heartbeat, hạn lease, hủy lượt
│   └── AccountLog.cs              # Gửi log có runId/eventId
├── Program.cs                    # Đăng ký DI
└── README.md
```

Chỉ thêm factory LMS khi nối context vào quy trình LMS; đặt cạnh nhóm Accounts
và tái sử dụng thư viện hiện có. Mỗi phần có trách nhiệm rõ ràng, tránh framework
hoặc lớp trung gian không cần thiết. Giữ mỗi hàm dưới 500 dòng; tách theo trách
nhiệm từ sớm thay vì chờ đạt giới hạn.

## 5. Tiêu chí hoàn thành giai đoạn này

- Worker V2 debug được bằng VS Code và tự tìm account Treo.
- Hai Worker không xử lý cùng một account trong cùng thời gian còn quyền sở hữu.
- Client hiển thị đúng tên Worker và log tiếp nhận do Worker gửi.
- Worker có context chứa đúng chương trình và cấu hình của account, không nhập lại cấu hình LMS thủ công.
- Heartbeat, mất lease và lệnh dừng hoạt động; Worker có thể nhận account tiếp theo sau khi lượt trước kết thúc.
- Không thay đổi hành vi Worker cũ; chưa triển khai thêm nghiệp vụ học/ôn luyện ngoài hai bước của task.

## 6. Kết quả triển khai

Đã triển khai hai bước nhận account, context riêng, log, heartbeat và kết thúc
lượt. Code nằm trong Worker.cs, WorkerOptions.cs và Accounts/. Hướng dẫn cấu
hình và debug: [README.md](README.md). Bộ kiểm thử Worker V2 độc lập và smoke
test hai tiến trình được thêm; toàn bộ dùng dữ liệu tạm và không gọi LMS.

Việc tiếp theo là mô tả quy trình nghiệp vụ sử dụng context đã nhận; không tự
chuyển luồng học/ôn luyện của Worker cũ trong giai đoạn này.
