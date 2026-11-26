# Lịch sử thay đổi

Mọi thay đổi đáng kể của `com.riseon.iap` được ghi ở đây. Định dạng theo
[Keep a Changelog](https://keepachangelog.com/vi/1.1.0/), đánh số theo
[Semantic Versioning](https://semver.org/lang/vi/).

## [Unreleased]

### Đổi

- Manager của game là lớp `partial` cài interface `IIapManager`, thay cho kế thừa
  `IapManagerBase` (đã bỏ). Bốn hàm `WaitUntilCanGrantAsync`, `IsGranted`, `GrantAsync`,
  `ForgetAsync` là `protected internal`: chỉ pack gọi được, nên lớp cài chúng kiểu
  explicit (`UniTask IIapManager.GrantAsync(IapOrder order)`), và cài đủ cả bốn.
  Source generator gắn thêm `sealed` cho lớp; lớp đang ghi `sealed` vẫn compile. Compile
  báo lỗi khi một interface kế thừa `IIapManager` (`RISEONIAP004`) hay một lớp abstract
  cài nó (`RISEONIAP005`).
- Manager không còn bọc `IapSdk`: game gọi thẳng `IapSdk.PurchaseAsync`, `IapSdk.Product`,
  `IapSdk.Owns`, `IapSdk.IsPurchasing`, `IapSdk.RestoreAsync` và nghe event của `IapSdk`.
- Hook `OnGranted` thành event `IapSdk.Granted`. Các hook `OnAttached`, `OnChanged`,
  `OnPurchased` và event `Changed`, `Purchased` trên manager bị bỏ; nghe
  `IapSdk.Changed`, `IapSdk.Purchased`.
- Bỏ `placement`: `PurchaseAsync` chỉ nhận sản phẩm, `IapOrder` không còn `Placement`.

- Trang settings: mục Products chỉ còn mã và loại của mỗi sản phẩm, một dòng, không
  gập mở. Title, Price, Description mà fake store trả về chuyển sang mục mới **Fake
  Store** ở cuối trang: ô Fake Store (trước ở mục Stores) nằm trên thanh của mục, và
  bên trong mỗi sản phẩm một mục con gập mở, thanh ghi mã, sửa được cả khi ô tắt. Dữ
  liệu trong asset giữ nguyên.

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