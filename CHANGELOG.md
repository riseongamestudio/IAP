# Lịch sử thay đổi

Mọi thay đổi đáng kể của `com.riseon.iap` được ghi ở đây. Định dạng theo
[Keep a Changelog](https://keepachangelog.com/vi/1.1.0/), đánh số theo
[Semantic Versioning](https://semver.org/lang/vi/).

## [Unreleased]

## [3.0.0] - 2026-11-26

### Đổi

- Không còn lớp manager: `IapManagerBase` bị bỏ, cùng mục Manager của trang settings,
  `IapSdk.GetManager<T>()` và `Ins` của lớp manager. Mỗi sản phẩm Consumable có ô
  **Granter** trên trang: một lớp `[Serializable]` của game cài `IIapGranter`, hàm
  `GrantAsync(IapOrder order)` cộng đồ và lưu xong rồi mới trả về, tự chờ save của game.
  Hàm là `protected internal`, nên lớp cài nó kiểu explicit
  (`UniTask IIapGranter.GrantAsync(IapOrder order)`). Thiếu granter, hoặc granter nằm
  trong assembly chỉ cho Editor, thì trang báo đỏ và build dừng.
- Pack tự giữ mã giao dịch đã cấp trong PlayerPrefs, tới khi cửa hàng xác nhận, để giao
  dịch cửa hàng trả lại không được cấp hai lần; save của game không còn lưu mã giao dịch.
- Món không tiêu hao và gói đăng ký không còn đi qua bước cấp: pack xác nhận, cửa hàng
  giữ quyền sở hữu. Game đọc `IapSdk.Owns` và nghe event mới `IapSdk.OwnershipChanged`
  (mua, Restore, lần cửa hàng trả lời lúc mở game, hoàn tiền, hết hạn), thay vì lưu
  quyền vào save.
- Game gọi thẳng `IapSdk.PurchaseAsync`, `IapSdk.Product`, `IapSdk.Owns`,
  `IapSdk.IsPurchasing`, `IapSdk.RestoreAsync` và nghe event của `IapSdk`.
- Hook `OnGranted` thành event `IapSdk.Granted`, chỉ cho món tiêu hao. Các hook
  `OnAttached`, `OnChanged`, `OnPurchased` và event `Changed`, `Purchased` trên manager bị
  bỏ; nghe `IapSdk.Changed`, `IapSdk.Purchased`.
- Bỏ `placement`: `PurchaseAsync` chỉ nhận sản phẩm, `IapOrder` không còn `Placement`.
- Trang settings: mỗi sản phẩm của mục Products là một dòng, mã ở cột nhãn (không còn
  nhãn **Id**), loại ở cột giá trị. Mũi tên đầu dòng mở ra dưới dòng ô **Granter** của
  sản phẩm Consumable cùng field của granter, rồi dòng **Fake Store** có mũi tên riêng,
  mở ra Title, Price, Description mà fake store trả về. Dữ liệu trong asset giữ nguyên.
- `IapSdk.IsReady` thành `true` khi cửa hàng trả lời về giao dịch của tài khoản. Có mạng
  thì lúc đó sản phẩm đã có, như trước; khi lấy sản phẩm thất bại thì giao dịch được lấy
  sớm (xem Sửa), nên `IsReady` có thể thành `true` trước khi có sản phẩm. Code đọc
  `IapSdk.Product(...)` sau `WaitUntilReadyAsync` thì cập nhật lại khi `IapSdk.Changed`
  báo.
- Bảng thanh toán của fake store vẽ như bảng thanh toán test của cửa hàng: kiểu App Store
  trên iOS và trong Editor khi build đang chọn là iOS, kiểu Google Play ở mọi chỗ khác,
  theo chế độ tối của máy trên Android và iOS (trong Editor theo giao diện của Editor);
  bảng Google Play có Default Icon của game trong Editor, chữ cái đầu tên game trong
  build. Kết quả mua chọn như trên cửa hàng test thật: Google Play qua bốn thẻ test ở
  trang Payment methods; App Store qua **Buy**, thêm dải **Fake Store** (Decline, Ask to
  Buy). Nút **Cancel** và **Buy for …** bỏ: hủy bằng chạm ra ngoài bảng hoặc nút X (App
  Store; Google Play khi màn hình nằm ngang). Nút **Deferred (Ask to Buy)** cũ trả
  `Deferred` rồi không bao giờ xong; giờ giao dịch hoãn được duyệt hay từ chối 8 giây sau,
  để thử luồng giao dịch tới ngoài `PurchaseAsync`. Trong lúc chờ, mua lại đúng món đó trả
  `Failed` ngay, không mở bảng.
- Phụ thuộc RiseOn lên bản phát hành cùng đợt: `com.riseon.utils` 2.0.0.

### Sửa

- Mở game khi không có mạng: Unity IAP lấy lại sản phẩm mãi mà không báo lỗi, nên giao
  dịch không bao giờ được lấy và người đã mua mất quyền sở hữu cả phiên. Giờ giao dịch
  được lấy thêm một lần ngay khi lần lấy sản phẩm đầu tiên thất bại, từ bản sao trên máy
  của cửa hàng; giao dịch chưa xác nhận trong lần đó chờ tới khi có sản phẩm.
- Lấy giao dịch của tài khoản thất bại: trước đây pack không lấy lại nữa. Giờ chưa có sản
  phẩm thì lấy lại cùng lần thử lấy sản phẩm sau; có rồi thì tự lấy lại sau 5 giây, chờ
  gấp đôi mỗi lần, tối đa một phút. `IapSdk.Changed` cũng báo khi lần lấy thất bại.
- Giao dịch của sản phẩm mà cửa hàng không trả về (như sản phẩm đã xóa khỏi danh sách)
  trước đây nằm im không log; giờ pack log lỗi kèm mã giao dịch ở lần lấy giao dịch sau
  khi có sản phẩm.

### Nâng cấp từ 2.0.0

1. Với mỗi sản phẩm Consumable, chuyển phần cộng đồ trong `GrantAsync` của lớp manager
   sang một lớp `[Serializable]` cài `IIapGranter` (xem README, mục Viết granter), cả
   phần chờ save load xong mà `WaitUntilCanGrantAsync` làm trước đây, rồi chọn lớp đó ở ô
   **Granter** của sản phẩm trên trang.
2. Xóa lớp manager, `IsGranted`, `ForgetAsync` và tập mã giao dịch trong save của game.
3. Món không tiêu hao, gói đăng ký: thay chỗ đọc quyền từ save bằng `IapSdk.Owns` và
   `IapSdk.OwnershipChanged`.
4. Đổi lời gọi qua manager sang `IapSdk` (`PurchaseAsync` không còn tham số `placement`),
   và handler của các hook sang event của `IapSdk`.

Giao dịch món tiêu hao mà bản 2.0.0 đã cấp nhưng cửa hàng chưa xác nhận lúc nâng cấp sẽ
được cấp thêm một lần: bản mới không đọc tập mã trong save cũ. Người chơi được thêm,
không mất.

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