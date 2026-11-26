# RiseOn.Iap

Mua trong app bằng Unity IAP 5. Pack tự kết nối cửa hàng khi mở game, lấy sản phẩm
theo danh sách trong Project Settings, giữ lại những giao dịch cửa hàng trả về khi
game chưa sẵn sàng, rồi giao cho lớp manager của game. Manager cấp đồ xong thì pack mới
xác nhận với cửa hàng. Game mua hàng qua lớp static `IapSdk`; lớp manager chỉ cấp đồ.

Package `com.riseon.iap`, namespace `RiseOn.Iap`.

## Mục lục

- [Yêu cầu](#yêu-cầu)
- [Cài đặt](#cài-đặt)
- [Viết manager](#viết-manager)
- [Gọi từ game](#gọi-từ-game)
- [Cài đặt của project](#cài-đặt-của-project)
- [Fake store](#fake-store)
- [Luồng của pack](#luồng-của-pack)
- [Khi có lỗi](#khi-có-lỗi)
- [Lịch sử thay đổi](#lịch-sử-thay-đổi)
- [Giấy phép](#giấy-phép)

## Yêu cầu

| Phụ thuộc | Cách có | Dùng cho |
|---|---|---|
| Unity 6000.3 | | Bản đang dùng để phát triển |
| [Odin Inspector](https://odininspector.com) | Cài tay từ Asset Store | Các mục của trang cài đặt |
| `com.unity.purchasing` 5.4.3 | Tự cài theo `package.json` | Unity IAP 5 |
| `com.unity.ugui` 2.0.0 | Tự cài theo `package.json` | Sheet của fake store |
| `com.riseon.utils` 1.0.18 | Tự cài theo `package.json` | Các mục của trang cài đặt |
| `com.cysharp.unitask` 2.5.11 | Tự cài theo `package.json`, từ OpenUPM | Các hàm `async` |

"Tự cài" là Package Manager tự lấy theo version từ Unity Registry và các registry khai
trong manifest, cả khi cài pack bằng git URL. Odin không có trên UPM nên phải cài vào
project trước. Thiếu Odin thì project báo một lỗi từ `RiseOn.Iap.Requirements`.

## Cài đặt

**Registry RiseOn** (khuyên dùng): thêm registry RiseOn với scope `com.riseon`, và
OpenUPM với scope `com.cysharp`, vào `Packages/manifest.json`, rồi thêm package:

```json
{
  "scopedRegistries": [
    {
      "name": "RiseOn",
      "url": "https://upm.riseongamestudio.com",
      "scopes": ["com.riseon"]
    },
    {
      "name": "OpenUPM",
      "url": "https://package.openupm.com",
      "scopes": ["com.cysharp"]
    }
  ],
  "dependencies": {
    "com.riseon.iap": "2.0.0"
  }
}
```

**Git URL**: phụ thuộc vẫn lấy theo version từ registry, nên manifest cần OpenUPM như ví
dụ trên; `com.riseon.utils` lấy từ registry RiseOn, hoặc cài bằng git URL trước. Rồi
*Package Manager → + → Add package from git URL*:

```
https://github.com/riseongamestudio/IAP.git
```

**Thư mục local**: `"com.riseon.iap": "file:D:/path/to/IAP"`.

## Viết manager

Project có sản phẩm thì phải có đúng một lớp `partial` cài `IIapManager`. Pack tự tìm
lớp đó, tạo nó trước scene đầu và giữ field của nó trong settings; settings không có ô
chọn manager.
Chưa có lớp nào thì bấm **New script** trên trang Project Settings > RiseOn > IAP:
pack hỏi chỗ lưu và viết sẵn khung lớp.

Trên lớp đó, source generator của pack sinh thêm lúc compile, không ra file:

- `sealed`, để không lớp nào kế thừa nó thành manager thứ hai.
- `Ins`: instance pack đang chạy. Đọc trước scene đầu, hoặc khi pack không chạy được
  manager nào (Console có lý do), thì ném `InvalidOperationException`.
- `[Serializable]`, nếu lớp chưa tự gắn.
- Lệnh đăng ký với pack, để IL2CPP không cắt lớp.

Compile báo lỗi khi:

| Mã | Trường hợp | Dev làm gì |
|---|---|---|
| `RISEONIAP001` | Lớp cài `IIapManager` không `partial`, generic hoặc lồng trong lớp khác | Sửa lớp |
| `RISEONIAP002` | File sản phẩm có dòng đọc không được, thường do conflict khi merge | Mở trang Project Settings > RiseOn > IAP, bấm **Apply** |
| `RISEONIAP003` | Có hơn một lớp cài `IIapManager` | Giữ một lớp |

Dev điền phần cấp đồ. Bốn hàm của `IIapManager` là `protected internal`: chỉ pack gọi
được, code khác của game không gọi được, kể cả khi ép kiểu về `IIapManager`. Vì thế lớp
phải cài chúng kiểu explicit (`IIapManager.GrantAsync`), như khung **New script** viết.
Namespace của pack là `RiseOn.Iap`:

```csharp
using System;
using Cysharp.Threading.Tasks;
using RiseOn.Firebase.RemoteConfig;
using RiseOn.Iap;

public partial class IapManager : IIapManager {
    // Số gems một gói cho lấy từ remote config, nên chờ lần fetch lúc mở game.
    UniTask IIapManager.WaitUntilCanGrantAsync() => RemoteConfigInit.WaitUntilCompleteAsync();

    bool IIapManager.IsGranted(string transactionId) => Save.Granted.Contains(transactionId);

    UniTask IIapManager.GrantAsync(IapOrder order) {
        switch (order.Product) {
            case IapProductId.Gems:  Save.Gems += RemoteKeys.gems_per_pack.Value * order.Quantity; break;
            case IapProductId.NoAds: Save.AdsRemoved = true; break;
            default: throw new InvalidOperationException($"nothing to give for {order.ProductId}");
        }

        Save.Granted.Add(order.TransactionId);
        Save.Write();                     // đồ và mã giao dịch trong cùng một lần ghi
        return UniTask.CompletedTask;
    }

    UniTask IIapManager.ForgetAsync(string transactionId) {
        if (Save.Granted.Remove(transactionId)) Save.Write();
        return UniTask.CompletedTask;
    }
}
```

| Hàm của manager | Dev viết gì |
|---|---|
| `WaitUntilCanGrantAsync` | Chờ những gì `IsGranted` và `GrantAsync` cần đọc: save của game, remote config. Giao dịch cửa hàng trả về lúc mở game đến trước scene đầu. Không cần chờ gì thì trả `UniTask.CompletedTask` |
| `IsGranted` | Save đã có mã giao dịch này, tức là đồ đã cấp |
| `GrantAsync` | Cấp đồ theo `order.Product` và ghi `order.TransactionId` vào **cùng một lần ghi save**. Ghi hai lần thì crash ở giữa sẽ cấp hai lần hoặc mất đồ. Ném lỗi thì giao dịch chưa được xác nhận và quay lại ở lần mở sau |
| `ForgetAsync` | Món tiêu hao cửa hàng đã xác nhận và không bao giờ trả lại nữa, save có thể xóa mã đi. Pack không gọi nó cho món không tiêu hao: cửa hàng trả lại chúng ở mỗi lần mở game, và mã trong save là thứ chặn cấp lần hai. Không xóa thì trả `UniTask.CompletedTask` |

Đồ không tiêu hao (`NonConsumable`, `Subscription`) cấp bằng cách ghi trạng thái, ví dụ
`AdsRemoved = true`, không cộng dồn: nút Restore và máy mới trả lại các giao dịch đó.

## Gọi từ game

Game mua hàng và đọc sản phẩm qua `IapSdk`:

```csharp
var result = await IapSdk.PurchaseAsync(IapProductId.Gems);
var price  = IapSdk.Product(IapProductId.Gems).Price;   // "$0.99", trống khi cửa hàng chưa trả về
var noAds  = IapSdk.Owns(IapProductId.NoAds);           // ném lỗi với sản phẩm Consumable
```

`IapProductId` là enum pack sinh từ danh sách sản phẩm: `no_ads` thành
`IapProductId.NoAds`. Mỗi sản phẩm mang một số ngẫu nhiên cố định, nên field enum đã
lưu trong scene, prefab vẫn trỏ đúng sản phẩm khi sản phẩm khác đổi tên hay bị xóa.

`PurchaseAsync` trả về:

| Kết quả | Nghĩa |
|---|---|
| `Purchased` | Đồ đã cấp và lưu, giao dịch đã gửi xác nhận |
| `Canceled` | Người chơi đóng bảng thanh toán |
| `Deferred` | Chờ duyệt (Ask to Buy, thanh toán chậm). Khi được duyệt, có thể ở phiên sau, giao dịch đến manager như mọi giao dịch khác |
| `Failed` | Cửa hàng chưa trả về sản phẩm, thanh toán lỗi, chữ ký sai, hoặc manager ném lỗi khi cấp; Console có lý do |

`PurchaseAsync` ném lỗi khi sản phẩm không có trong danh sách, khi không có manager
nào chạy, hoặc khi đang có giao dịch khác chạy. `IapSdk.IsPurchasing` cho biết điều
cuối, dùng để khóa nút mua.

App Store bắt có nút Restore: gọi `IapSdk.RestoreAsync()`. Giao dịch trả về đi qua
`GrantAsync` như mọi giao dịch khác. Google Play tự trả lại giao dịch mỗi lần mở game;
món không tiêu hao đã xác nhận từ trước mà save không có (cài lại game, máy mới) cũng
đi qua `GrantAsync`, và pack không xác nhận lại.

Event của `IapSdk`, gọi trên thread của Unity. Một handler ném lỗi chỉ được log, các
handler khác vẫn chạy:

| Event | Khi nào |
|---|---|
| `Changed` | Sản phẩm hay quyền sở hữu đổi: cửa hàng trả lời, một giao dịch được cấp, App Store hoàn tiền |
| `Granted` | Manager vừa cấp và lưu đồ của một `IapOrder`, kể cả món cửa hàng trả về ở dạng đã xác nhận. Ví dụ: mua No Ads xong thì tắt banner đang hiện |
| `Purchased` | Cửa hàng xác nhận một giao dịch của phiên này, mỗi giao dịch một lần, kèm giá và tiền tệ |

## Cài đặt của project

Dev mở bằng menu **RiseOn > IAP**, hoặc Edit > Project Settings > RiseOn > IAP. Trang
này lưu vào `Assets/RiseOn/Resources/RiseOnIapSettings.asset` và
`Assets/RiseOn/IAP/Products.RiseOnIapGenerator.additionalfile` (file generator đọc để
sinh `IapProductId`). Inspector của asset chỉ có nút mở trang này.

| Mục | Ý nghĩa |
|---|---|
| Stores: Apple Prefix | Ghép trước mọi mã trên App Store, nơi mã sản phẩm không trùng giữa các app của một team. Trống khi mã App Store giống danh sách |
| Stores: Google Play Key | Play Console > Monetize with Play > Monetization setup > Licensing, chuỗi base64. Pack dùng nó kiểm chữ ký mọi giao dịch Google Play; App Store tự kiểm trên máy (StoreKit 2) |
| Manager | Lớp cài `IIapManager` của project, chỉ đọc, kèm field của nó |
| Products | Mỗi sản phẩm một dòng: **Id**, mã sản phẩm như trên Play Console (rê chuột lên nhãn để xem tên trong `IapProductId`), và loại: Consumable, Non-Consumable, Subscription. Nút **+** trên thanh thêm sản phẩm (giá fake store 0.99), **×** xóa sản phẩm, kéo tay nắm để đổi thứ tự |
| Fake Store | Ô trên thanh: build mua từ fake store của pack (xem [Fake store](#fake-store)): không trừ tiền, không kiểm biên lai, để thử trước khi sản phẩm có trên cửa hàng. Editor luôn dùng fake store, ô tick hay không. Bên trong, mỗi sản phẩm của mục Products một mục con gập mở theo thứ tự đó, thanh ghi mã sản phẩm, bên trong là những gì fake store trả về: **Title**, **Price** (đô la Mỹ), **Description**. Title trống thì fake store lấy mã. Cửa hàng thật trả dữ liệu của nó, không đọc các ô này. Các ô sửa được cả khi ô trên thanh tắt |

Sửa trên trang ghi thẳng vào asset; Ctrl+Z hoàn tác như mọi chỗ khác của Unity. Ghi
file sản phẩm thì Unity compile lại, nên khi danh sách khác file, trang báo vàng kèm
hai nút:

- **Apply**: ghi file ngay. Sản phẩm nào đã đổi mã thì `IapProductId.TenCu` trong
  script dưới `Assets` và trong package embedded đổi luôn thành tên mới, để code vẫn
  compile; hộp vàng liệt kê trước các tên sẽ đổi. Code gọi qua `using static`, hay
  nằm ở chỗ khác, dev tự sửa.
- **Revert**: đưa danh sách về lần cuối trang thấy nó khớp file; mọi sửa sau đó mất,
  Ctrl+Z lấy lại được. Bản đó lưu trong `Library/` của máy này, nên nút mờ khi máy
  chưa giữ bản nào, hoặc khi file đổi từ chỗ khác (pull, merge).

Rời trang lúc còn báo vàng thì Unity hỏi **Apply** hay **Discard** (Discard làm như
Revert); không Revert được thì hỏi **Apply** hay **Later**, và build dừng tới khi ghi.
Unity compile vì lý do khác thì không hỏi, file vẫn chờ.

Asset được import (pull, merge) lúc trang đóng thì pack tự ghi lại file sản phẩm, đổi
tên trong code như Apply, và cấp số cho sản phẩm chưa có số; trang đang mở thì việc đó
chờ tới lúc dev chọn trên trang.

Trang báo đỏ (lỗi gắn với một dòng có nút **Edit** đưa tới dòng đó) và build dừng khi:

| Trường hợp | Nền tảng |
|---|---|
| Asset nằm ngoài thư mục Resources; nút **Move it** chuyển vào | Mọi nền tảng |
| Không có lớp manager khi đã có sản phẩm (chưa có sản phẩm thì trang chỉ báo vàng), có hơn một lớp, lớp không có constructor rỗng, hoặc lớp nằm trong assembly chỉ cho Editor (Play mode chạy được, bản build không có) | Mọi nền tảng |
| Settings chưa giữ lớp manager, thường khi lớp ra đời lúc script đang lỗi compile; nút **Take it** nhận lớp | Mọi nền tảng |
| Mã sản phẩm trống, trùng, hoặc sai luật Google Play: 1 đến 40 ký tự chữ thường, số, `_`, `.`, bắt đầu bằng chữ hoặc số, không dùng `android.test` | Mọi nền tảng |
| Hai mã thành cùng một tên trong `IapProductId` | Mọi nền tảng |
| Hai sản phẩm cùng số (hai nhánh merge trúng cùng số ngẫu nhiên). Dev xóa sản phẩm mà field enum đã lưu không trỏ tới rồi thêm lại để có số mới | Mọi nền tảng |
| File sản phẩm chưa theo asset: trang còn mở, đã chọn Later, hoặc Unity tắt trước khi ghi. Bấm **Apply** hoặc **Revert**; trên trang lỗi này chỉ báo vàng | Mọi nền tảng |
| Apple Prefix có ký tự ngoài chữ, số, `_`, `.`, `-`, hoặc prefix cộng mã dài quá 100 ký tự | iOS |
| Google Play Key trống hoặc không phải khóa base64 của Play Console (khi Fake Store tắt và có sản phẩm) | Android |

Trang báo vàng, build chỉ log cảnh báo, khi:

| Trường hợp | Dev làm gì |
|---|---|
| Fake Store bật | Tắt trước khi phát hành (nút **Turn off** trên hộp báo). Build thử trước khi sản phẩm có trên cửa hàng cần fake store, nên build không dừng |

## Fake store

Pack có fake store riêng, đăng ký qua API store tùy biến của Unity IAP. Nó chạy trong
Editor, trên nền tảng không có cửa hàng, và trong build bật Fake Store. Sản phẩm mang
Title, Description, Price của trang cài đặt, nên `IapSdk.Product(...)` và shop của
game hiện đúng chữ và giá như sẽ thấy trên cửa hàng (giá kiểu `$0.99`, tiền tệ USD).

Mỗi lần mua, một sheet trượt lên từ đáy màn hình, trên mọi canvas: tên, loại, mô tả và
giá sản phẩm, cùng các nút kết thúc giao dịch:

| Nút | `PurchaseAsync` trả về |
|---|---|
| **Buy for $…** | `Purchased`, sau khi `GrantAsync` cấp đồ và pack xác nhận |
| **Cancel**, hoặc chạm ra ngoài sheet | `Canceled` |
| **Payment declined** | `Failed` |
| **Deferred (Ask to Buy)** | `Deferred`; giao dịch không bao giờ xong, như khi phụ huynh không duyệt |

Sheet dùng giờ không theo `Time.timeScale`, nên vẫn chạy khi game dừng thời gian dưới
shop. Scene không có EventSystem thì sheet tự thêm một cái, dùng module của Input System
khi project cài và bật Input System.

Món không tiêu hao và gói đăng ký đã mua chỉ còn trong phiên chơi đó, như fake store
của Unity: mở lại game là mất. Restore đưa chúng lại qua manager trong cùng phiên, như
món đã xác nhận Google Play trả về (`IsGranted` chặn cấp lần hai, pack không xác nhận
lại).

## Luồng của pack

1. Trước scene đầu: tạo manager, kết nối cửa hàng. Mất kết nối thì tự thử lại với thời
   gian chờ tăng dần (`ExponentialBackOffRetryPolicy` của Unity IAP).
2. Lấy sản phẩm, sau đó lấy giao dịch của tài khoản. Xong thì `IapSdk.IsReady` thành
   `true`, `IapSdk.WaitUntilReadyAsync` trả về, `IapSdk.Changed` báo.
3. Mỗi giao dịch đã trả tiền đi qua: kiểm chữ ký Google Play →
   `WaitUntilCanGrantAsync` → bỏ qua nếu `IsGranted` → `GrantAsync` → `IapSdk.Granted` →
   pack xác nhận với cửa hàng.
   Trong một phiên, một giao dịch chỉ đến manager một lần. Món không tiêu hao cửa hàng
   trả về ở dạng đã xác nhận (Google Play sau khi cài lại game) đi cùng đường đó tới
   `IapSdk.Granted`, không xác nhận lại.
4. Cửa hàng xác nhận xong thì `IapSdk.Purchased` báo và, với món tiêu hao,
   `ForgetAsync` chạy; mỗi giao dịch một lần. Có `com.riseon.analytics` và
   **Purchases from IAP** trên trang Analytics bật (mặc định) thì doanh thu tự gửi tới
   các dịch vụ analytics từ đây, game không gọi `AnalyticsSdk.LogPurchase`; tắt thì
   game tự gọi `LogPurchase`.

## Khi có lỗi

| Trường hợp | Pack làm gì |
|---|---|
| Không có mạng lúc mở game | Thử kết nối lại mãi; sản phẩm trả `Available = false`, `Owns` trả `false`, `IapSdk.IsReady` giữ `false` và `IapSdk.WaitUntilReadyAsync` chờ tới khi có mạng, nên màn boot không chờ nó. Đồ đã cấp nằm trong save của game, nên game dựa vào save chứ không dựa vào `Owns` |
| Cửa hàng không trả về một số sản phẩm | Log lỗi kèm mã, các sản phẩm khác vẫn bán |
| Chữ ký Google Play sai | Log lỗi, không cấp, không xác nhận; Google Play tự hoàn tiền sau ba ngày |
| Manager ném lỗi khi cấp | Log exception, không xác nhận; giao dịch quay lại ở lần mở sau |
| Cửa hàng không nhận xác nhận | Log cảnh báo; giao dịch quay lại ở lần mở sau, `IsGranted` chặn cấp lần hai |
| App Store hoàn tiền một món không tiêu hao | `Owns` thành `false`, `IapSdk.Changed` báo. Thu hồi đồ trong save là việc của game |

## Lịch sử thay đổi

Xem [CHANGELOG.md](CHANGELOG.md).

## Giấy phép

MIT, xem [LICENSE.md](LICENSE.md).