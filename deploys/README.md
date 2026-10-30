# Triển khai trên server từ GHCR

Thư mục này chạy độc lập với source code. Server cần Docker Engine và Docker
Compose v2 hỗ trợ `up --wait`. Image hỗ trợ Linux `amd64` và `arm64`.

Hai image của repository:

```text
ghcr.io/buigiayen/auto_lms-client
ghcr.io/buigiayen/auto_lms-worker-v2
```

## 1. Publish image từ GitHub

Commit các thay đổi cùng `.github/workflows/publish-images.yml` rồi push lên
GitHub. Workflow chạy kiểm tra client và Worker V2 trước khi build/push cả hai
image. Registry đúng là **ghcr.io**.

| Sự kiện | Image tag |
| --- | --- |
| Push vào nhánh mặc định (`main`) | `latest`, tên nhánh, `sha-<full commit SHA>` |
| Push vào nhánh khác | tên nhánh đã chuẩn hóa, `sha-<full commit SHA>` |
| Push Git tag `v1.2.3` | `v1.2.3`, `sha-<full commit SHA>` |
| Pull request vào `main`/`master` | kiểm tra và build, không publish |
| Actions → Publish container images → Run workflow | publish cho ref được chọn; `latest` chỉ cho nhánh mặc định |

Workflow dùng `GITHUB_TOKEN` có sẵn với quyền `packages: write`; không cần tạo
secret registry riêng. Danh sách tag và digest được ghi trong **Actions → run →
Summary**. Đợi cả hai job image thành công trước khi triển khai.

Nếu fork/đổi tên repository, sửa `IMAGE_PREFIX` trong `.env` trên server cho
khớp đường dẫn image ở Summary. Các Actions được ghim theo commit SHA.

## 2. Chuẩn bị trên server

Copy **toàn bộ thư mục `deploys`**, gồm `.env.example`, vào một thư mục trên
server, ví dụ `/opt/lotuslms`. Không cần copy source hay cài Node.js/.NET.

```bash
cd /opt/lotuslms
cp .env.example .env
chmod 600 .env
openssl rand -hex 32
```

Dán chuỗi vừa tạo vào `WORKER_REGISTRATION_KEY` trong `.env`. Client và worker
tự dùng cùng giá trị này. Mỗi server nên dùng khóa riêng.

Đặt `CLIENT_USERNAME` và `CLIENT_PASSWORD` để đăng nhập giao diện. Cả hai là
bắt buộc; backend chỉ đối chiếu trên server, không gửi mật khẩu vào trình duyệt.
Ví dụ phần cấu hình cần điền (thay mật khẩu bằng giá trị riêng của bạn):

```dotenv
CLIENT_USERNAME=admin
CLIENT_PASSWORD='your-private-login-password'
CLIENT_SESSION_SECURE=false
```

Cookie đăng nhập có thời hạn 12 giờ; đăng xuất hủy ngay, restart yêu cầu đăng
nhập lại. Khi đổi user/pass, sửa `.env` rồi chạy lại `sh deploy.sh` để tạo lại
client với cấu hình mới. Worker tiếp tục dùng bootstrap/API key độc lập.

Để Client hiển thị máy chủ đang chạy công việc, đặt trong `.env`:

```dotenv
SERVER_NAME=server-01
WORKER_NAME=worker-v2
```

Worker gửi tên lane dạng `server-01-worker-v2-01`, `server-01-worker-v2-02`…
Nếu không đặt `SERVER_NAME`, Worker dùng hostname của container. Có thể truyền
hostname thật từ shell của máy chủ bằng
`SERVER_NAME="$(hostname)" docker compose up -d --wait --wait-timeout 180`.

Mặc định `STOP_ON_DAILY_KPI=true`: khi đủ KPI ngày, tài khoản được hẹn chạy lại
vào một thời điểm ngẫu nhiên trong 00:00–08:00 ngày hôm sau theo giờ Việt Nam.

Mỗi account cũng có chu kỳ riêng: chạy 3 giờ, nghỉ ngẫu nhiên 15–30 phút rồi
tự được worker nhận lại với phiên LMS mới. Cấu hình trong `.env`:

```dotenv
WORK_REST_ENABLED=true
CONTINUOUS_WORK_MINUTES=180
MIN_REST_MINUTES=15
MAX_REST_MINUTES=30
```

Thời gian tính từ lúc worker nhận account; lịch nghỉ được lưu trong database.
Các account khác tiếp tục chạy. Dừng thủ công không tự chạy lại; lịch đủ KPI
vẫn được ưu tiên nếu đạt trước hạn. Đặt `WORK_REST_ENABLED=false` để tắt.
Triển khai cả image Client và Worker mới vì Client API cần hỗ trợ `resting`
và tự nâng schema 10 (có bản sao lưu migration).

Để nhận cảnh báo lỗi trên nhiều kênh/server Discord, thêm vào `.env`:

```dotenv
DISCORD_ENABLED=true
DISCORD_WEBHOOK_URLS='https://discord.com/api/webhooks/ID1/TOKEN1;https://discord.com/api/webhooks/ID2/TOKEN2'
DISCORD_EVENTS=worker_failed,lane_failed
```

Phân cách URL bằng `;`. Mỗi sự cố được gửi tới tất cả webhook hợp lệ. Thêm
`,account_failed` vào `DISCORD_EVENTS` để nhận cả lỗi account/LMS dù lane vẫn
tiếp tục chạy. Tin nhắn kèm file JSON chẩn đoán đã che dữ liệu nhạy cảm.
Giữ URL webhook trong `.env` riêng vì URL có token. Mặc định Discord tắt;
webhook/cấu hình lỗi không làm dừng worker. Sau khi sửa cấu hình và image đã có
thay đổi này, chạy lại `sh deploy.sh`.

Nếu image là private, đăng nhập bằng tài khoản được phép đọc package và GitHub
PAT **classic** có scope `read:packages`:

```bash
docker login ghcr.io -u YOUR_GITHUB_USERNAME
# Dán PAT ở lời nhắc Password.
```

Với package public, có thể pull mà không cần đăng nhập.

## 3. Pull và start

```bash
sh deploy.sh
```

Hoặc chạy trực tiếp:

```bash
docker compose pull
docker compose up -d --wait --wait-timeout 180
docker compose ps
docker compose logs --tail 100 -f client worker
```

Client phải healthy trước khi worker khởi động. Script giữ nguyên `.env` và
volume dữ liệu khi cập nhật. Log container được xoay vòng, tối đa 3 file × 10 MB
cho mỗi service.

## 4. Truy cập giao diện

Mặc định client chỉ mở tại `127.0.0.1:3100` trên server. Có thể tạo SSH tunnel
từ máy của bạn:

```bash
ssh -L 13100:127.0.0.1:3100 USER@SERVER_IP
```

Sau đó mở `http://127.0.0.1:13100` trên máy của bạn.

Nếu dùng reverse proxy trên cùng server, giữ địa chỉ bind `127.0.0.1` và đặt
`CLIENT_ALLOWED_HOSTS=lms.example.com` trong `.env`. Proxy chuyển tiếp tới
`http://127.0.0.1:3100`, giữ `Host` gốc. Với HTTPS, đặt
`CLIENT_SESSION_SECURE=true` để cookie chỉ được gửi qua kết nối bảo mật.
Với nhiều tên miền/IP, phân tách bằng dấu phẩy, không thêm scheme/port.

Nếu cần truy cập trực tiếp qua IP trong mạng riêng:

```dotenv
CLIENT_BIND_ADDRESS=0.0.0.0
CLIENT_ALLOWED_HOSTS=YOUR_SERVER_IP
CLIENT_PORT=3100
```

Chạy lại `sh deploy.sh`, rồi mở `http://YOUR_SERVER_IP:3100`.

## 5. Cập nhật và chọn phiên bản

Sau khi workflow publish xong, chạy lại `sh deploy.sh` để pull và cập nhật.
Để giữ hai service ở cùng một commit, đặt trong `.env`:

```dotenv
IMAGE_TAG=sha-FULL_COMMIT_SHA_FROM_ACTIONS_SUMMARY
```

Cũng có thể chọn `IMAGE_TAG=v1.2.3` khi Git tag đó đã được publish.
`IMAGE_TAG=latest` theo dõi nhánh mặc định. Đổi tag rồi chạy `sh deploy.sh`
để đổi phiên bản image. Nếu bản mới đã nâng schema, việc quay lại bản cũ cần
database tương thích hoặc bản sao lưu tương ứng.

```bash
docker compose stop
docker compose start
docker compose down
```

`down` giữ volume. Không thêm `-v` nếu muốn giữ dữ liệu.

## 6. Dữ liệu và khôi phục quyền file

Volume mặc định tên `lotuslms-client-data`, chứa database, khóa mã hóa và API
key worker. `CLIENT_DATA_VOLUME` cho phép chọn volume khác hoặc dùng volume
đã có. Khi sao lưu/khôi phục, giữ cả `client.sqlite`, các file WAL/SHM nếu có,
`credential.key` và `worker-api-keys.json` cùng nhau; dừng dịch vụ khi copy.

Image client chạy bằng user `node` có UID/GID `1000:1000`. Nếu dữ liệu được
copy từ máy khác và xuất hiện lỗi `EACCES` ở `/app/data/credential.key`, sửa
quyền trên đúng volume đang cấu hình:

```bash
docker compose stop worker client
docker compose run --rm --no-deps --user 0 --entrypoint sh client \
  -c 'chown -R 1000:1000 /app/data'
docker compose up -d --wait --wait-timeout 180
```

Lệnh này sửa chủ sở hữu, không tạo lại khóa mã hóa. File `.env` và thư mục
`backups/` được bỏ qua bởi Git và Docker build context.

Tham khảo: [GitHub Actions publish image](https://docs.github.com/en/actions/tutorials/publish-packages/publish-docker-images),
[đăng nhập/pull GHCR](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry),
[Docker Compose up](https://docs.docker.com/reference/cli/docker/compose/up/).
