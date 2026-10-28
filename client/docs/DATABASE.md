# Thiết kế SQLite local

Schema đầy đủ: [schema.sql](schema.sql). Bốn bảng là `accounts`, `worker_logs`, `proxies` và `worker_runs`. Mỗi account có
`proxy_id` nullable để chọn một cấu hình proxy đã lưu.

## 1. Vị trí lưu trữ

```text
client/
├── README.md
├── docs/
│   ├── FEATURES.md
│   ├── API.md
│   ├── DATABASE.md
│   └── schema.sql
└── data/                    # Được tạo khi triển khai/chạy ứng dụng
    ├── client.sqlite
    ├── client.sqlite-wal   # SQLite có thể tạo trong chế độ WAL
    ├── client.sqlite-shm
    ├── credential.key     # Khóa local mã hóa mật khẩu
    └── worker-api-keys.json # API key tự tạo theo tên worker
```

Thư mục `data` chứa dữ liệu runtime và không đưa vào Git hoặc Docker image.
Đường dẫn được giải quyết theo thư mục ứng dụng, không theo thư mục shell ngẫu
nhiên khi khởi chạy.

## 2. Bảng `accounts`

| Cột | Kiểu | Ý nghĩa |
| --- | --- | --- |
| `id` | TEXT, PK | UUID backend tự tạo |
| `identity_number` | TEXT | Căn cước 12 chữ số, tên đăng nhập LMS |
| `password_ciphertext` | TEXT | Mật khẩu được mã hóa để backend có thể dùng đăng nhập LMS |
| `provider` | TEXT | `yenthuy` hoặc `truongson` |
| `status` | TEXT | `running` hoặc `stopped`, mặc định `stopped` |
| `worker_name` | TEXT | Tên worker được gán cho tài khoản |
| `enrolment_plan_iid` | INTEGER nullable | IID chương trình học được chọn để chạy |
| `enrolment_plan_name` | TEXT | Tên chương trình để hiển thị, mặc định rỗng |
| `managing_teacher` | TEXT | Giáo viên quản lý tự nhập, mặc định rỗng, tối đa 200 ký tự |
| `proxy_id` | TEXT, FK nullable | Tham chiếu `proxies.id`; NULL nghĩa là kết nối trực tiếp |
| `device_uuid` | TEXT | GUID tự sinh và giữ ổn định cho tài khoản/nhà cung cấp |
| `created_at` | TEXT | Thời gian UTC ISO 8601 |
| `updated_at` | TEXT | Thời gian UTC ISO 8601, backend cập nhật khi sửa |

Ràng buộc duy nhất: `(provider, identity_number)`. Không đặt unique cho
`worker_name` vì một worker có thể xử lý nhiều tài khoản.

Mật khẩu phải giải mã được để gọi LMS nên lưu ciphertext thay vì hash một chiều.
Thiết kế dùng AES-256-GCM, IV mới cho mỗi lần mã hóa, đóng gói theo định dạng
`v1.<iv-base64>.<auth-tag-base64>.<ciphertext-base64>`. Backend tự tạo khóa 32 byte
trong `credential.key` ở lần chạy đầu và dùng lại ở các lần chạy sau. Khi mất
khóa, mật khẩu cũ không thể giải mã; việc sao lưu cần giữ cả SQLite và khóa.

Token LMS, cookie và `sessionId` của modal nằm trong bộ nhớ backend, không có cột
lưu trong SQLite. Đổi căn cước/mật khẩu/nhà cung cấp phải hủy phiên đang giữ.

## 3. Bảng `worker_logs`

| Cột | Kiểu | Ý nghĩa |
| --- | --- | --- |
| `id` | INTEGER, PK | Mã dòng log tăng tự động |
| `run_id` | TEXT, FK nullable | Lượt chạy trong `worker_runs`; NULL cho log trước nâng cấp |
| `event_id` | TEXT | UUID sự kiện do worker tạo |
| `account_id` | TEXT, FK | Tham chiếu `accounts.id` |
| `worker_name` | TEXT | Tên worker tại thời điểm gửi sự kiện |
| `level` | TEXT | `debug`, `info`, `warn`, `error` |
| `message` | TEXT | Nội dung log |
| `context_json` | TEXT | JSON object chứa dữ liệu chẩn đoán |
| `occurred_at` | TEXT | Thời điểm sự kiện ở worker, UTC |
| `received_at` | TEXT | Thời điểm backend nhận, UTC |

Unique `(account_id, worker_name, event_id)` chống trùng khi worker gửi lại.
`worker_name` trong log là thông tin lịch sử, giữ nguyên khi tài khoản được đổi
sang worker khác. Log sắp xếp theo `occurred_at DESC, id DESC` để ổn định khi có
nhiều sự kiện cùng thời điểm.

## 4. Quy tắc truy cập dữ liệu

- Bật `foreign_keys=ON` và `busy_timeout=5000` trên mỗi kết nối SQLite.
- Bật WAL để giao diện đọc log trong khi worker tiếp tục ghi.
- Dùng truy vấn có tham số cho mọi dữ liệu từ API.
- Lưu tài khoản và các thay đổi liên quan trong transaction.
- Không gửi `password_ciphertext` hoặc khóa mã hóa trong response API.
- Validate độ dài, enum, UUID và thời gian trước khi ghi; giữ ràng buộc SQL làm
  lớp kiểm tra bổ sung.
- Không có API xóa tài khoản trong 5 tính năng hiện tại; khóa ngoại log dùng
  `ON DELETE RESTRICT` để giữ lịch sử.

## 5. Bảng `proxies`

| Cột | Kiểu | Ý nghĩa |
| --- | --- | --- |
| `id` | TEXT, PK | UUID cấu hình |
| `name` | TEXT | Tên hiển thị, 1–100 ký tự |
| `enabled` | INTEGER | 0 hoặc 1, mặc định 1 |
| `address` | TEXT | URI HTTP/HTTPS có host và port |
| `username` | TEXT | Tài khoản proxy, mặc định rỗng |
| `password_ciphertext` | TEXT nullable | Mật khẩu proxy mã hóa AES-256-GCM |
| `expires_on` | TEXT nullable | Ngày cuối còn hiệu lực (YYYY-MM-DD), tính hết ngày UTC+7 |
| `created_at`, `updated_at` | TEXT | Thời gian UTC ISO 8601 |

`accounts.proxy_id` là FK `ON DELETE RESTRICT`, không unique: mỗi account chọn
một proxy hoặc trực tiếp; nhiều account có thể dùng chung cấu hình. API báo
`PROXY_IN_USE` nếu xóa proxy đã được gán. Tắt proxy vẫn giữ liên kết nhưng chặn
login của các account đó; không tự chuyển sang kết nối trực tiếp.

Sửa proxy chỉ hủy phiên xem chi tiết của account đang sử dụng nó. API giao diện
chỉ trả `hasPassword`; endpoint control yêu cầu API key đúng worker/account mới
trả thông tin xác thực proxy cho Worker.

## 6. Chuyển dữ liệu cấu hình cũ

Khi mở database cũ, backend sao lưu vào
`client/data/client.before-account-proxies.sqlite` trước khi chuyển đổi bằng
transaction. Cột `accounts.proxy_id` được thêm và bảng `proxy_settings` được
chuyển thành một bản ghi `proxies` tên “Proxy hiện tại”, giữ ciphertext cũ.
Nếu proxy cũ đang bật, các account có sẵn được gán cấu hình này để giữ cách kết
nối; nếu đang tắt thì account giữ kết nối trực tiếp. Cấu hình trống không tạo
bản ghi. Sau đó xóa bảng cũ và đặt `user_version=2`. Chạy lại không tạo bản sao.

Account, mật khẩu, GUID thiết bị, log và API key worker được giữ nguyên.
Sao lưu/khôi phục cần giữ cả file SQLite và `credential.key` tương ứng.
`worker-api-keys.json` chứa API key riêng theo tên Worker/lane. Worker V2 chỉ
nhận key lane qua endpoint đăng ký có xác thực bằng `WORKER_REGISTRATION_KEY`;
secret bootstrap không được lưu trong database hoặc file key này.

### Ngày hết hạn proxy (schema 3)

Database phiên bản trước tự thêm cột `proxies.expires_on` nullable; bản ghi cũ
được giữ `NULL`. Trước khi ALTER, backend sao lưu vào
`client/data/client.before-proxy-expiry.sqlite`. Bước thêm cột không đổi account,
mật khẩu hoặc liên kết proxy. `user_version` hiện tại là 3; khởi động lại không
tạo thêm cột/bản ghi. Cờ `isExpired` tính từ ngày hết hạn ở thời điểm đọc theo
UTC+7, nên không cần cron cập nhật dữ liệu và không mất hạn khi khởi động lại.

## 7. Quyền nhận account — `worker_runs` (schema 4)

Mỗi lượt giữ `id`, `account_id`, `worker_name`, `instance_id`, `request_id`,
`lease_token_ciphertext`, `state`, `accepted_at`, `last_heartbeat_at`,
`lease_expires_at`, `finished_at`. Token dùng AES-256-GCM với khóa local, không
xuất hiện trong API giao diện hoặc nội dung log. Chỉ response claim của đúng
Worker trả token để dùng cho heartbeat/log/finish.

Partial unique index theo `account_id` và theo `instance_id` với các trạng thái
`accepted`, `running`, `stopping` bảo đảm không nhận trùng. Unique theo
`(worker_name,instance_id,request_id)` giữ cùng kết quả khi retry claim. Claim,
heartbeat và finish dùng `BEGIN IMMEDIATE`. Sau 30 giây không heartbeat, lượt
được đánh dấu `lost` khi API tiếp theo kiểm tra; account còn Treo được nhận lại.
Các lượt kết thúc giữ nguyên để truy vết. Log cũ đến chậm vẫn gắn run cũ.

Database cũ thêm `worker_logs.run_id` nullable; sao lưu trước ALTER trong
`client.before-worker-queue.sqlite`, giữ account, cấu hình proxy, mật khẩu và
log cũ. Đây là migration schema 4. Thư mục dữ liệu phải nằm trên đĩa local của
một backend; các Worker trao đổi qua API, không dùng SQLite chia sẻ qua mạng.

## 8. Giáo viên quản lý (schema 5)

Thêm cột `accounts.managing_teacher` TEXT NOT NULL DEFAULT ''. Sao lưu database
cũ trước ALTER vào `client.before-managing-teacher.sqlite`; giữ nguyên account,
proxy, lượt nhận Worker và log. Đây là migration schema 5.

## 9. Chương trình học được chọn (schema 7)

Accounts lưu `enrolment_plan_iid` và `enrolment_plan_name`. Mã là số nguyên dương
không quá 2147483647 để khớp DTO int? của .NET. Account chưa chọn có mã NULL,
tên rỗng. Xác nhận Run lưu chương trình, proxy và trạng thái trong một transaction;
không đổi chương trình/proxy khi Worker đang giữ lượt khác.

Migration tự sao lưu trước ALTER vào `client.before-enrolment-plan-selection.sqlite`,
thêm trường chương trình còn thiếu và bỏ `course_iid`, `course_name` của schema 6.
Giữ nguyên `enrolment_plan_iid` đã lưu và dữ liệu tài khoản, mật khẩu, GUID, proxy,
lượt chạy, log. Tên chương trình cũ để rỗng cho đến khi chọn lại, không lấy tên
khóa học cũ. Mở lại ứng dụng giữ nguyên lựa chọn.

## 10. Hoãn account sau KPI trong ngày (schema 8)

Thêm `accounts.available_after` để giữ account ở trạng thái Treo nhưng tạm ẩn
khỏi hàng chờ đến thời điểm đó. Khi Worker kết thúc lượt với `deferred`, Backend
chọn ngẫu nhiên thời điểm nhận lại trong khung 00:00:00–08:00:00 ngày tiếp theo
theo giờ Việt Nam, rồi lưu dưới dạng UTC ISO 8601 trong `available_after`.
Lịch được chọn một lần cho mỗi lượt đạt KPI, giữ nguyên khi khởi động lại hay
gửi lại finish. Worker lane được giải phóng cho account khác. Run/Treo thủ công
xóa thời gian hoãn.

Migration sao lưu vào `client.before-worker-deferral.sqlite`, thêm cột
`available_after` và mở rộng trạng thái kết thúc của `worker_runs` bằng
`deferred`, giữ nguyên các lượt và log cũ.

## 11. Phần cần chạy (schema 9)

`accounts.run_sections_json` lưu mảng JSON gồm các giá trị `exam`, `review`,
`theory`, `practical`. Mảng phải có ít nhất một giá trị, không trùng và không có
giá trị ngoài danh sách. Mặc định là cả bốn phần để giữ hành vi account cũ.

Migration sao lưu vào `client.before-run-sections.sqlite` rồi thêm cột, giữ
nguyên thông tin đăng nhập, thiết bị, chương trình, lượt chạy và log.
Run lưu bộ lọc cùng proxy, chương trình và trạng thái trong một transaction.
Không được đổi bộ lọc khi còn lượt chạy đang hoạt động. Dừng account và lịch
hoãn KPI giữ nguyên bộ lọc. `user_version` hiện tại là 9.
