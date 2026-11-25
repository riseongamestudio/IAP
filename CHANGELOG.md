# Lịch sử thay đổi

Mọi thay đổi đáng kể của `com.riseon.iap` được ghi ở đây. Định dạng theo
[Keep a Changelog](https://keepachangelog.com/vi/1.1.0/), đánh số theo
[Semantic Versioning](https://semver.org/lang/vi/).

## [Unreleased]

### Sửa

- Trang settings: nhãn Title và Description của một sản phẩm không còn thụt vào dưới
  Id, mà bắt đầu ở đầu dòng.

## [2.0.0] - 2026-11-20

### Đổi

- `IapResult.Cancelled` đổi thành `Canceled`, theo chính tả Anh-Mỹ của các pack RiseOn.
  Code so kết quả mua với giá trị này thì đổi theo.
- Cần Unity 6000.3, bản đang dùng để phát triển, thay cho 6000.0 chưa từng được thử:
  `package.json` ghi `unity`, nên Package Manager không cài pack vào project Unity cũ
  hơn.
- Phụ thuộc RiseOn lên bản phát hành cùng đợt: `com.riseon.utils` 1.0.18.

## [1.0.1] - 2026-11-20

### Sửa

- Project chưa có Odin Inspector: mọi assembly của pack dùng Odin được bỏ qua, và chỉ còn một lỗi
  nói pack cần Odin; trước đó project báo thêm hàng loạt lỗi compile khó hiểu.

## [1.0.0] - 2026-11-20

Bản đầu tiên.