# Client quản lý tài khoản và Worker LotusLMS

Ứng dụng local dùng Node.js phục vụ cả backend API và giao diện web, lưu dữ liệu
bằng SQLite. Giao diện gồm bảng tài khoản, nhật ký worker, cấu hình proxy và
modal đọc response LMS raw.

## Chạy ứng dụng

Yêu cầu Node.js 24 trở lên. Từ thư mục repository:

```bash
cd client
npm ci
npm start
```

Mở `http://127.0.0.1:3100`. `npm run dev` dùng chế độ watch; `npm test` chạy kiểm
thử offline, `npm run check` kiểm tra cú pháp. Dữ liệu được tạo tự động ở
`client/data`; không có tài khoản hoặc mật khẩu mẫu được thêm vào database.

Cấu hình tùy chọn qua biến môi trường: `PORT` (3100), `CLIENT_HOST`
(`127.0.0.1`), `CLIENT_DATA_DIR` (đường dẫn thư mục dữ liệu),
`LMS_YENTHUY_API_URL`, `LMS_TRUONGSON_API_URL`. Hai URL LMS mặc định theo cấu hình
hiện tại của repository. Proxy được nhập và lưu trên trang “Cấu hình proxy”.

## Phạm vi

| Tính năng | Kết quả cần có |
| --- | --- |
| Tạo thông tin chung | Lưu căn cước, mật khẩu, nhà cung cấp, trạng thái và tên worker |
| Sửa thông tin | Cập nhật tài khoản; để trống mật khẩu trên form sửa sẽ giữ mật khẩu cũ |
| Thay đổi trạng thái | Chuyển giữa “Treo” và “Dừng treo” |
| Nhận log worker | Worker gửi log về backend, SQLite lưu theo tài khoản |
| Xem chi tiết tài khoản | Đăng nhập LMS, chọn chương trình học, hiển thị response danh sách khóa học dạng JSON trong modal |
| Cấu hình proxy | Lưu nhiều proxy HTTP/HTTPS, kiểm tra IP và chọn một cấu hình cho từng account |
| Bảng quản lý account | Tìm kiếm, lọc, phân trang; tạo, sửa, chọn proxy, đổi trạng thái, mở chi tiết và xem log |

## Kiến trúc

```mermaid
flowchart LR
    UI[Giao diện web local] --> API[Node.js backend]
    API --> DB[(SQLite local)]
    API --> LMS[API LotusLMS theo nhà cung cấp]
    WORKER[LotusLMS.Worker] -->|Gửi log / đọc trạng thái| API
```

- Backend Node.js phục vụ API tại `/api` và giao diện HTML/CSS/JavaScript cùng origin.
- Giao diện gọi backend; backend quản lý mật khẩu và phiên đăng nhập LMS.
- SQLite đặt tại `client/data/client.sqlite`.
- Cổng local mặc định: `http://127.0.0.1:3100`.
- Worker .NET có adapter gửi log và đọc trạng thái, mặc định tắt.

## Cấu trúc triển khai

```text
client/
├── docs/                   # Tài liệu tính năng, API và dữ liệu
├── src/
│   ├── server/
│   │   ├── index.js        # Khởi chạy HTTP server
│   │   ├── app.js          # Route API và static assets
│   │   ├── store.js        # SQLite, tài khoản, log, proxy và mã hóa
│   │   ├── lms.js          # Provider, login, cookie/session, proxy
│   │   └── signing.js      # Ký request, đối chiếu fixture C#
│   └── public/             # Giao diện HTML/CSS/JavaScript do Node.js phục vụ
└── data/                   # SQLite và khóa local; được bỏ qua trong Git
```

Giao diện dùng JavaScript trên trình duyệt; Node.js phục vụ giao diện và xử lý
mọi request LMS ở backend. SQLite dùng module tích hợp
[`node:sqlite`](https://nodejs.org/docs/latest-v24.x/api/sqlite.html); HTTP/proxy
dùng [`undici ProxyAgent`](https://github.com/nodejs/undici/blob/main/docs/docs/api/ProxyAgent.md).

## Chọn proxy cho từng account

1. Vào **Cấu hình proxy** → **Thêm**, nhập tên, host/port, thông tin xác thực và ngày hết hạn (nếu có), bật rồi lưu.
2. Vào **Tài khoản** → **Sửa** (hoặc thêm account mới).
3. Chọn cấu hình trong trường **Proxy** → **Lưu thay đổi**.

Bấm **Run/Treo** → chọn kết nối → **Tải chương trình** → chọn chương trình học
cần chạy → **Chạy tài khoản**. IID và tên chương trình được lưu vào account,
đồng thời trả cho Worker qua API danh sách/claim/heartbeat. Hủy modal không lưu. Danh sách chỉ
hiển thị proxy đang bật và còn hạn; ngày hết hạn có màu xanh/vàng/đỏ theo trạng
thái (vàng khi còn tối đa 3 ngày). Proxy hết hạn vẫn hiện trong trang quản lý
để gia hạn. Ngày hết hạn tính đến hết ngày theo giờ Việt Nam; để trống nghĩa
là chưa đặt hạn.

Chọn **Kết nối trực tiếp** để bỏ gán proxy. Bảng account hiển thị proxy đã chọn.
Proxy đang tắt không được gán mới và chặn đăng nhập của account đang sử dụng nó.
Muốn xóa cấu hình, gỡ khỏi các account trước. Mỗi account chọn một proxy; có thể
chia sẻ một cấu hình giữa nhiều account.

Lựa chọn áp dụng cho xem chi tiết LMS và Worker có `Client:Enabled=true`.
Khi thay đổi proxy lúc Worker đang chạy, Worker dừng ở lần đọc control kế tiếp
(tối đa khoảng 5 giây khi API phản hồi bình thường); khởi động lại để dùng proxy
mới. Worker độc lập tiếp tục dùng `Http:Proxy` của .NET.

Database cũ được sao lưu tự động trước khi chuyển proxy chung thành danh sách;
proxy cũ đang bật sẽ được gán cho các account hiện có. Chi tiết trong
[DATABASE.md](docs/DATABASE.md).

## Thư viện kết nối .NET

[`LotusLMS.Client`](../LotusLMS.Client/README.md) chứa `ILotusClient`, DTO và HTTP
transport dùng lại cho ứng dụng .NET khác. Worker đã dùng thư viện này cho nhận
account, heartbeat, log và finish. Không đổi API backend hoặc dữ liệu SQLite.

## Kết nối Worker .NET và nhận học viên

Sau khi nhấn **Run/Treo**, account vào hàng chờ. Worker gọi API danh sách, chọn
một học viên rồi xác nhận nhận việc. Backend khóa account trong SQLite và ghi
tên Worker thực tế; Worker khác nhận cùng account sẽ nhận `409 ACCOUNT_CLAIMED`.
Bảng account tự làm mới mỗi 5 giây, hiển thị Chờ Worker / Đã tiếp nhận / Đang xử
lý / Đang dừng cùng log gần nhất. “Xem log” xem lịch sử từng lượt chạy.

Đăng ký tên Worker và lấy API key một lần (giá trị minh họa):

```bash
curl http://127.0.0.1:3100/api/workers \
  -H 'Content-Type: application/json' \
  -d '{"workerName":"worker-node-01"}'
```

Tên đã tồn tại trả `409`; API key có trong file local
`client/data/worker-api-keys.json`. Không đưa key vào Git. Worker không cần nhập
căn cước/mật khẩu hoặc nhà cung cấp khi kết nối client; claim thành công trả
thông tin học viên và provider. Worker đọc `proxyId` từ chi tiết account rồi
lấy thông tin proxy qua `/api/proxies/:proxyId` bằng quyền sở hữu lượt chạy.

```text
Client__Enabled=true
Client__BaseUrl=http://127.0.0.1:3100
Client__WorkerName=worker-node-01
Client__ApiKey=<key lấy khi đăng ký Worker>
Client__AccountId=
```

Bỏ trống `AccountId` để tự lấy account đầu tiên có sẵn; có thể đặt UUID để chỉ
nhận một học viên cụ thể. Một tiến trình xử lý một account rồi thoát; dùng bộ
quản lý tiến trình nếu cần tự khởi động lại để nhận học viên tiếp theo. Chọn
`Worker:Mode` như luồng hiện có; mặc định `Idle` chỉ kiểm tra kết nối và gửi log.

Worker gửi heartbeat mỗi 5 giây, giữ quyền nhận 30 giây. Mất điều khiển hoặc
mất quyền sở hữu sẽ chặn request LMS mới, hủy request đang chạy và dừng host.
Backend chỉ đưa account đang Treo về hàng chờ khi hết lease hoặc nhận xác nhận
dừng. Không sửa thông tin account khi Worker còn giữ lượt chạy; dừng và chờ xác
nhận trước. Proxy hết hạn vẫn bị chặn như trước.

Log tự động lấy từ ILogger, kèm accountId, workerName và runId. Worker chỉ được
gửi log cho lượt mình sở hữu; eventId chống ghi trùng. Hàng đợi log memory tối
đa 5.000 sự kiện, không tồn tại sau khi tắt tiến trình. Bật tính năng này yêu cầu
Worker được build lại để dùng hợp đồng claim/heartbeat mới. `Client__Enabled=false`
giữ chế độ Worker độc lập.

```bash
dotnet run --project LotusLMS.RequestSigning.Tests -- --worker-client-only
```

Sau khi build solution, chạy `npm run test:worker` trong `client` để kiểm tra
Worker thật ở chế độ Idle nhận học viên và gửi log vào SQLite, không gọi LMS.
Chi tiết request/response: [WORKER_QUEUE.md](docs/WORKER_QUEUE.md).

## Đọc tài liệu

1. [Đặc tả tính năng, giao diện và nghiệm thu](docs/FEATURES.md).
2. [Hợp đồng API client và luồng gọi LMS](docs/API.md).
3. [Thiết kế dữ liệu và lưu trữ local](docs/DATABASE.md).
4. [Schema SQLite](docs/schema.sql).

## Mã nguồn tham chiếu

- [Các API đăng nhập, chương trình học và khóa học](../LotusLMS.Api/LotusLmsApiClient.cs).
- [Cấu hình tenant và API](../LotusLMS.Api/LotusLmsOptions.cs).
- [Thuật toán ký request](../LotusLMS.RequestSigning/README.md).
- [Fixture đối chiếu chữ ký](../LotusLMS.RequestSigning.Tests/Fixtures/cryptojs-vectors.json).
- [Fixture đối chiếu client sync token](../LotusLMS.RequestSigning.Tests/Fixtures/client-sync-vectors.json).

Các thông số LMS trong tài liệu được đối chiếu với mã nguồn hiện tại. URL API
được cấu hình phía backend theo nhà cung cấp để có thể cập nhật khi triển khai.

## Worker V2 tự nhận account

Worker V2 nhận một account Treo, gửi log tiếp nhận và duy trì heartbeat. Khi dừng
account hoặc proxy hết hạn, Worker giải phóng lượt rồi tiếp tục hàng chờ. Giai
đoạn này chuẩn bị context chương trình học, chưa chạy học/ôn luyện. Cấu hình API
key, tên Worker và debug VS Code: [LotusLMS.Worker.V2/README.md](../LotusLMS.Worker.V2/README.md).

Kiểm thử tích hợp bằng backend và SQLite tạm (build Worker V2 trước):

```bash
npm run test:worker-v2
```
