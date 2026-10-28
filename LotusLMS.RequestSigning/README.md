# LotusLMS.RequestSigning

Thư viện .NET 8, không cần NuGet bổ sung, dùng được từ Worker .NET 10 và
WinForms .NET 8. Sinh cặp `_sand_ri` / `_sand_rit` theo JavaScript của
`truongson.huelms.com`, module webpack `185247`, cùng token đồng bộ của module
`928962` (đối chiếu ngày 17/10/2026).
Trong các bundle đã khảo sát không thấy trường `ri_rit`; tên thực tế là
`_sand_ri` và `_sand_rit`.

## Quy tắc

```text
requestId = lowercaseHex(MD5(UTF8(UUID_v4 + unixMilliseconds)))
seed      = decimal(2 * userIid + 2451) + " t i a v s t"
secret    = lowercaseHex(MD5(UTF8(seed)))
aesKey    = SHA256(UTF8(secret))
iv        = randomBytes(16)
cipher    = AES-256-CBC-PKCS7(UTF8(requestId), aesKey, iv)
requestToken = lowercaseHex(iv) + ":" + Base64(cipher)
```

Khi chưa đăng nhập, `info.iid` là `undefined`, phép toán JavaScript tạo `NaN`.
Seed đăng nhập vì vậy là `"NaN t i a v s t"`. Trong thư viện, truyền `null`
cho trạng thái này. Sau đăng nhập phải dùng `result.iid`, không dùng tên đăng
nhập, CCCD, `course_iid`, `practice_iid`, hoặc `exam_sco_iid`.

`SHA256` nhận chuỗi hex MD5 gồm 32 ký tự, không nhận 16 byte MD5 gốc.
IV phải mới cho mỗi lần mã hóa. Hai trường phải thuộc cùng một cặp; không
ghép `_sand_ri` của request này với `_sand_rit` của request khác.

## Token đồng bộ của phiên

API sau đăng nhập còn yêu cầu `_sand_client_sync_token`. Token này dùng
`result.token` của login và thời gian Unix theo **giây**, khác với khóa AES
dẫn xuất từ `iid` ở trên. Trên cùng một phiên khảo sát, bỏ token đồng bộ trả
`err_code=12`; gửi token đúng của web thì danh sách đọc thành công.

```text
timestamp = floor(currentUnixSeconds)
(high, low) = two big-endian UInt32 words of Float64(timestamp)
orderedHigh = high XOR 0x80000000  // timestamp hiện tại không âm
orderedLow  = low
multiplier = 32768 + (UInt32BE(HMAC-SHA256(loginToken, "ope:multiplier")) % 32768)
offset     = UInt32BE(HMAC-SHA256(loginToken, "ope:offset")) % 32768
sync = "n:" + hex12(orderedHigh * multiplier + offset)
            + hex12(orderedLow  * multiplier + offset)
```

`UInt32BE` lấy bốn byte đầu của HMAC. Hai nhóm hex viết thường, mỗi nhóm đủ
12 ký tự. `SandClientSyncTokenGenerator.Generate(loginToken)` sinh giá trị
hiện tại; không cache token này hoặc sao chép từ tài khoản khác.

## Sử dụng

```csharp
using LotusLMS.RequestSigning;

var guest = SandRequestTokenGenerator.Generate();
var authenticated = SandRequestTokenGenerator.Generate(userIid: 12345);

var signedForm = SandRequestTokenGenerator.Sign(
    formFields, userIid: 12345, authenticationToken: loginToken);
using var content = new FormUrlEncodedContent(signedForm);
```

`Sign` giữ nguyên các trường nghiệp vụ (kể cả key lặp như `bank_iids[]`),
loại các trường ký cũ, thêm đúng một cặp mới và token đồng bộ khi có
`authenticationToken`, không sửa collection đầu vào. Khi login, gọi `Sign`
không truyền `userIid`/`authenticationToken`; token đồng bộ cũ được bỏ.
Không cache cặp này theo tài khoản hoặc bài ôn luyện.

## Tích hợp

- `LotusLMS.Api/LotusLmsApiTransport.cs` ký mọi POST ngay trước khi tạo content,
  dùng `session.UserIid` và `session.Token`; login dùng seed khách. GET gửi cặp
  mới, token đồng bộ và thông tin xác thực trong query.
- `LotusLMS.Worker` sử dụng tích hợp này qua API client. Luồng ôn luyện không
  còn dò `ri`/`rit` trong JSON để dùng lại làm request token.
  Bước ôn luyện đọc cây với `item_ntype=practice` để nhận được item trong `children`.
- `LotusLMSClient/Services/ApiService.cs` ký form, multipart và GET. `AuthService`
  đặt `UserIid` sau đăng nhập và xóa khi đăng xuất. Các hằng token cũ trong
  `AuthService` và `CourseService`, kể cả token đồng bộ cố định, đã được bỏ.
- Dockerfile chép project mới trong cả bước restore và publish.

API client mặc định bật `LotusLms:GenerateRequestTokens=true`. Các giá trị
`RequestId`, `RequestToken`, `StudentRequestId`, `StudentRequestToken`,
`ClientSyncToken` và
override ở phương thức ôn luyện được thay bằng cặp mới. Nếu cần phát lại một
capture cũ để chẩn đoán, đặt `GenerateRequestTokens=false`; lúc đó API client
giữ hành vi dùng các giá trị được truyền vào. WinForms luôn sinh token mới;
tham số `ri`/`rit` cũ được giữ để tương thích chữ ký hàm, không dùng để ký request.

`_sand_token`, cookie, tenant và device/session vẫn là các thành phần khác của
phiên. Token đồng bộ được tính từ `_sand_token`, không thay thế token đăng nhập.
File thử nghiệm có sẵn `LotusLMS.Api/Lotus.sandri.cs` được giữ nguyên và không
được dùng trong tích hợp mới.

## Kiểm chứng

```bash
dotnet run --project LotusLMS.RequestSigning.Tests
dotnet build LotusLMS.Worker.sln
```

Fixture được tạo bằng cách nạp bundle gốc, gọi trực tiếp module `185247.q`
với UUID, thời gian và IV cố định. Kiểm thử so sánh từng byte với fixture cho
khách, hai người dùng và `Int32.MaxValue`, ở ba locale. Các kiểm tra HTTP dùng
handler giả, bao gồm login, danh sách, chi tiết ôn luyện, sync, GET ngân hàng
câu hỏi và `save-take`; không gửi kết quả học tập lên LMS.
Fixture đồng bộ gồm 15 trường hợp từ module `928962.u3`, với token tổng hợp,
timestamp biên và ký tự UTF-8, cũng được đối chiếu ở ba locale.

API client C# đã xác minh live: đăng nhập, danh sách chương trình/khóa học, cây
ôn luyện, `get-practice-detail`, `get-sync-practice-progress` và GET
`get-question-bank-status` đều đọc thành công với các token tự sinh. Không gọi
`save-take`, ghi thời gian học hoặc tracking live.

Capture Chrome headless trước đó thực sự có logout với
`trigger_by=system_check_devtools`. Đây là cơ chế phát hiện debug của trang;
không phải bằng chứng rằng tài khoản không truy cập được ôn luyện. Chi tiết
nguồn và giới hạn ở [báo cáo khảo sát](../docs/request-signing-investigation.md).
