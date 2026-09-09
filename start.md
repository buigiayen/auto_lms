# Hướng dẫn chạy LotusLMS.Worker

Worker .NET tự động học + ôn luyện cho LotusLMS. Chạy trên macOS/Linux.

## 1. Yêu cầu

- .NET SDK **10.0** (project target `net10.0`).
- Kiểm tra: `dotnet --version`

## 2. Build

```bash
dotnet restore LotusLMS.Worker.sln
dotnet build LotusLMS.Worker.sln --no-restore
```

## 3. Cấu hình tài khoản (user-secrets)

Chạy từ thư mục gốc dự án (`tool-auto-study-huelms`):

```bash
# Bắt buộc
dotnet user-secrets set "LotusLmsCredentials:Username" "038090044831" \
  --project LotusLMS.Worker/LotusLMS.Worker.csproj
dotnet user-secrets set "LotusLmsCredentials:Password" "123456" \
  --project LotusLMS.Worker/LotusLMS.Worker.csproj

# Request context (khớp web client)
dotnet user-secrets set "LotusLmsCredentials:ClientSyncToken" "n:c1da89127987252489739834" \
  --project LotusLMS.Worker/LotusLMS.Worker.csproj

# Login ri/rit
dotnet user-secrets set "LotusLmsCredentials:RequestId" "8cbcfef31fd3b4c730a90124640619ad" \
  --project LotusLMS.Worker/LotusLMS.Worker.csproj
dotnet user-secrets set "LotusLmsCredentials:RequestToken" "ddfd1e7d986bb7743a479117dc72a574:1ebBJRNcQOy0/fovqo+ofL9ulu1gaNqOhkaK+L9TCUoVtMWHmFxib42uF9K1ymlb" \
  --project LotusLMS.Worker/LotusLMS.Worker.csproj

# Student (courses/học tập) ri/rit
dotnet user-secrets set "LotusLmsCredentials:StudentRequestId" "74eed31b414ccb4221cecd144939a82e" \
  --project LotusLMS.Worker/LotusLMS.Worker.csproj
dotnet user-secrets set "LotusLmsCredentials:StudentRequestToken" "0ea4a07800d0820795790aa8f336882c:ngv6rVZa30WCK43UTE0Rdp9WGqIwKlgBMZlDyhpXTZxPj33qqitrEdBoRv7R6W/V" \
  --project LotusLMS.Worker/LotusLMS.Worker.csproj

# Khuyến nghị: cố định device/session (khớp web client, tránh token_invalid)
dotnet user-secrets set "LotusLmsCredentials:DeviceUuid" "6587f9f2-7a8b-43f8-9c95-e94b7ae90382" \
  --project LotusLMS.Worker/LotusLMS.Worker.csproj
dotnet user-secrets set "LotusLmsCredentials:SessionId" "b0cd321a-7370-46af-b095-afa0e0340a6a" \
  --project LotusLMS.Worker/LotusLMS.Worker.csproj

# Kiểm tra lại
dotnet user-secrets list --project LotusLMS.Worker/LotusLMS.Worker.csproj
```

## 4. Chạy kiểm tra (an toàn — chỉ đọc)

Không gửi bất kỳ thao tác ghi nào (học thời gian, nộp bài):

```bash
Worker__Mode=ReadOnlyProbe \
  dotnet run --project LotusLMS.Worker/LotusLMS.Worker.csproj
```

Kết quả mong đợi: đăng nhập thành công, liệt kê kế hoạch + danh sách khóa học.

## 5. Chạy tự động học + ôn luyện (ghi thật)

> ⚠️ Gửi dữ liệu thật lên tài khoản (thời gian học + kết quả ôn luyện).
> Chỉ dùng cho tài khoản bạn được phép thao tác. Dừng bằng `Ctrl+C`.

```bash
Worker__Mode=AutoStudy \
  dotnet run --project LotusLMS.Worker/LotusLMS.Worker.csproj
```

Các tuỳ chọn (mặc định trong `LotusLMS.Worker/appsettings.json`):

| Khoá | Mặc định | Ý nghĩa |
|---|---|---|
| `Worker:SaveItemSeconds` | 15 | Chờ đủ số giây này rồi gửi một lần `save-item-learning-time` với cùng số giây |
| `Worker:TrackTimeSeconds` | 15 | Số giây gửi tới `track-time` khi bật chế độ dừng theo KPI |
| `Worker:KpiCheckEverySaves` | 3 | Khi bật dừng theo KPI, kiểm tra KPI và gửi `track-time` mỗi N lần lưu |
| `Worker:StopOnDailyKpi` | false | Nếu bật, dừng toàn bộ khi đạt KPI ngày; mặc định tiếp tục tuần tự đến môn kế tiếp |
| `Worker:IncludePractice` | false | Nếu bật, chạy thêm bước tự động ôn luyện; mặc định chỉ học video |
| `Worker:PracticeBatchSize` | 5 | Số câu hỏi mỗi batch `save-take` |
| `Worker:MarkPracticeFinished` | false | Giữ `finished=0` giống web client |
| `Worker:OnlyFirstPlan` / `OnlyFirstCoursePage` | true | Chỉ đọc kế hoạch đầu / trang 1 khóa học (khớp web client) |

Ghi đè qua biến môi trường, ví dụ:

```bash
Worker__Mode=AutoStudy \
Worker__StopOnDailyKpi=false \
  dotnet run --project LotusLMS.Worker/LotusLMS.Worker.csproj
```

## 6. API response logs

Mặc định mọi API response được nối thêm vào một file JSONL riêng theo URI trong
`logs/api-responses`. Ví dụ endpoint `/course/api/save-item-learning-time` được
ghi vào `course_api_save-item-learning-time.jsonl`. Tên file không chứa query
string; mỗi dòng gồm thời điểm, method, URI, status, headers và toàn bộ body.

Các file này có thể chứa token và dữ liệu tài khoản, đã được loại khỏi Git. Có
thể tắt hoặc đổi thư mục bằng `LotusLms__LogApiResponses=false` và
`LotusLms__ApiResponseLogDirectory=...`.

## 7. Proxy

Proxy nằm trong `LotusLMS.Worker/appsettings.json` (mục `Http:Proxy`), mặc định `Enabled: false`.

Bật proxy qua biến môi trường:

```bash
Http__Proxy__Enabled=true \
Http__Proxy__Address=http://113.160.166.31:10447 \
Worker__Mode=ReadOnlyProbe \
  dotnet run --project LotusLMS.Worker/LotusLMS.Worker.csproj
```

> **Lưu ý:** từ máy Mac hiện tại, proxy `113.160.166.31:10447` **không kết nối được**
> (`curl` trả exit 56 — connection reset). Nếu gặp lỗi login `HTTP status (null)`,
> hãy chạy trực tiếp (bỏ 2 dòng `Http__Proxy__*`).

## 8. Các vấn đề đã biết

1. **`token_invalid` giữa chừng** — token phiên bị server vô hiệu hóa khi chạy lâu.
   Khắc phục: đặt `DeviceUuid`/`SessionId` cố định (mục 3) để danh tính phiên ổn định.
2. **Server chỉ cộng ~1s/lần nếu lưu liên tục** — đã khắc phục bằng khoảng nghỉ
   `SaveItemSeconds` giây thực tế giữa các lần lưu. Không bỏ delay này.
3. **Proxy lỗi từ Mac** — xem mục 7.
