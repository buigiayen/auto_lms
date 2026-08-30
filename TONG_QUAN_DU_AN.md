# TỔNG QUAN DỰ ÁN — LotusLMSClient

## 1) Mục tiêu
Dự án là ứng dụng **Windows Forms (.NET 8)** để đăng nhập và làm việc với hệ thống LotusLMS qua API, gồm các phần chính:
- Đăng nhập tài khoản học viên.
- Xem dashboard khóa học.
- Mở danh sách khóa học và chi tiết tiến độ học.
- Tự động gửi thời gian học (`save-item-learning-time`, `track-time`).
- Ghi log hoạt động và xem log theo từng bài học.

---

## 2) Công nghệ và phụ thuộc
- **Ngôn ngữ/Nền tảng:** C#, .NET 8 (`net8.0-windows`), WinForms.
- **NuGet chính:**
  - `Newtonsoft.Json` (serialize/deserialize JSON).
  - `Microsoft.Web.WebView2` (đã khai báo package, hiện chưa thấy dùng trực tiếp trong source chính).
- **Solution:** `LotusLMSClient.sln`
- **Project:** `LotusLMSClient/LotusLMSClient.csproj`

---

## 3) Cấu trúc thư mục quan trọng
- `LotusLMSClient/Program.cs` — Entry point, mở `LoginForm`.
- `LotusLMSClient/Forms/`
  - `LoginForm.*` — form đăng nhập.
  - `MainForm.*` — form khung chính sau đăng nhập.
  - `LogViewerForm.cs` — form xem log theo file.
- `LotusLMSClient/UserControls/`
  - `DashboardControl.*` — dashboard khóa học.
  - `CourseListControl.*` — danh sách khóa học.
  - `CourseDetailControl.*` — chi tiết khóa học + auto-study/auto-save time.
  - `SessionListControl.*` — bảng bài học/phiên học + hiển thị thời gian + mở log.
  - `ProfileControl.*` — hồ sơ.
  - `LogControl.cs` — màn hình tổng log.
- `LotusLMSClient/Services/`
  - `ApiService.cs` — HTTP client chung + header mặc định.
  - `AuthService.cs` — nghiệp vụ login/logout và trạng thái user.
  - `CourseService.cs` — gọi API khóa học/time tracking, mapping dữ liệu.
- `LotusLMSClient/Models/`
  - `LoginResponse.cs`, `StudentCoursesResponse.cs`, `CourseModels.cs`, `SubjectDetails.cs`.
- `LotusLMSClient/Helpers/`
  - `ColorPalette.cs` — bảng màu UI.
  - `AppLog.cs` — ghi log file + lưu lịch sử in-memory.

Ngoài ra có:
- `dotnet-install.ps1` — script cài .NET SDK chính thức.
- `yenthuy.huelms.com/static/js/81777.d76fce6e.js` — file JS bundle ngoài luồng WinForms (không phải core C# app).

---

## 4) Luồng chạy chính
1. `Program.Main()` mở `LoginForm`.
2. User nhập CCCD/CMT + mật khẩu, `LoginForm` gọi `AuthService.LoginAsync`.
3. Login thành công -> mở `MainForm`.
4. `MainForm` điều hướng giữa các màn hình qua UserControl:
   - Dashboard: tải khóa học đang học.
   - Khóa học: chọn khóa học -> tải detail learning time -> mở `CourseDetailControl`.
5. Trong `CourseDetailControl`:
   - User chọn bài học.
   - Bộ đếm tự động chạy theo chu kỳ (mặc định 15s, có điều chỉnh theo thời gian còn thiếu).
   - Gọi `SaveItemLearningTimeAsync` định kỳ.
   - Mỗi 3 lần save gọi thêm `TrackTimeAsync`.
   - Đồng bộ lại dữ liệu từ server và refresh bảng thời gian.
6. Log được ghi qua `AppLog` và xem ở `LogControl`/`LogViewerForm`.

---

## 5) API chính đang dùng
Base API hiện đặt tại: `https://staging-api.lotuslms.com`

Các endpoint đáng chú ý:
- `POST /user/login`
- `POST /student/api/get-student-enrolment-plans`
- `POST /student/api/get-student-courses-in-enrolment-plan-table-view`
- `POST /time-tracking/course-time-tracking/get-detail-learning-time`
- `POST /course/api/save-item-learning-time`
- `POST /time-tracking/api/track-time`
- `POST /student/api/get-detail-learning-time` (hàm `ReportLearningTimeAsync`)

Headers/form-data có nhiều trường `_sand_*` để giả lập context web (token, session, domain, web_url, user_agent...).

---

## 6) Các thành phần nghiệp vụ quan trọng
- **AuthService**
  - Giữ trạng thái đăng nhập: token, user iid/id, name, cccd.
  - Set header `Authorization: Bearer` vào `ApiService`.
- **CourseService**
  - Lấy enrollment plan -> lấy danh sách khóa học -> map sang model hiển thị.
  - Quy ước trạng thái: `pf == 1` => Completed, ngược lại Learning.
  - Có logic parse cây học (`ParseLearningHierarchy`) và các hàm track time.
- **CourseDetailControl**
  - Là trung tâm auto-study/auto-save.
  - Ưu tiên item user chọn thủ công, nếu xong thì tìm item kế tiếp chưa đủ `min_learning_time`.
- **SessionListControl**
  - Tô màu thời gian học đạt/chưa đạt.
  - Mở log theo item qua `LogViewerForm`.
- **AppLog**
  - Ghi file log theo ngày hoặc theo `itemIid-userIid-date`.
  - Phát sự kiện realtime cho UI log.

---

## 7) Logging
- Thư mục log: `<AppBaseDirectory>/logs`
- Tên file:
  - Theo item: `{itemIid}-{userIid}-{dd-MM-yyyy}.log`
  - Hoặc chung app: `app_{dd-MM-yyyy}.log`
- LogControl có:
  - Xem log phiên hiện tại (in-memory + realtime event).
  - Mở file log lịch sử.
  - Mở thư mục log bằng Explorer.

---

## 8) Giao diện hiện có
- **Login**: màn hình đăng nhập full-custom không viền.
- **MainForm**: sidebar + header + content panel.
- **Dashboard**: card tiến độ học.
- **Khóa học**: danh sách card, bấm vào để load chi tiết.
- **Chi tiết khóa học**: danh sách bài học, progress thời gian, auto-save.
- **Hồ sơ**: thông tin user cơ bản.
- **Nhật ký**: xem log realtime/historical.

---

## 9) Điểm cần lưu ý kỹ thuật (quan trọng)
1. **Nhiều giá trị hard-code trong source** (`_sand_ri`, `_sand_rit`, session id, user agent, domain...).
2. **Base API là staging**, nhưng web origin/referer lại dùng domain thật `yenthuy.huelms.com`.
3. `App.config` đang có `supportedRuntime` kiểu .NET Framework 4.8, trong khi project là .NET 8 WinForms (khả năng là legacy artifact).
4. Một số chỗ `catch {}` nuốt lỗi hoàn toàn -> khó debug khi lỗi runtime/API.
5. `SubjectDetails.cs` rất lớn (model lồng sâu), khó bảo trì nếu schema API đổi.
6. Chưa thấy test tự động (unit/integration).

---

## 10) File nên ưu tiên đọc khi onboard nhanh
1. `LotusLMSClient/Program.cs`
2. `LotusLMSClient/Forms/LoginForm.cs`
3. `LotusLMSClient/Forms/MainForm.cs`
4. `LotusLMSClient/Services/ApiService.cs`
5. `LotusLMSClient/Services/AuthService.cs`
6. `LotusLMSClient/Services/CourseService.cs`
7. `LotusLMSClient/UserControls/CourseDetailControl.cs`
8. `LotusLMSClient/UserControls/SessionListControl.cs`
9. `LotusLMSClient/Helpers/AppLog.cs`
10. `LotusLMSClient/Models/StudentCoursesResponse.cs` + `SubjectDetails.cs`

---

## 11) Lệnh build/run cơ bản
```bash
dotnet restore "LotusLMSClient/LotusLMSClient.csproj"
dotnet build "LotusLMSClient.sln"
# run (nếu cần)
dotnet run --project "LotusLMSClient/LotusLMSClient.csproj"
```

---

## 12) Phạm vi đã rà soát
Đã đọc toàn bộ source chính trong các nhóm: `Forms`, `UserControls`, `Services`, `Models`, `Helpers`, cùng các file solution/project/config liên quan để lập tài liệu này.