# RiseOn.Iap

Mua trong app bằng Unity IAP 5. Pack tự kết nối cửa hàng khi mở game và lấy sản phẩm
theo danh sách trong Project Settings. Món tiêu hao (Consumable) đi qua granter của sản
phẩm: lớp của game cộng đồ và lưu, xong thì pack mới xác nhận với cửa hàng. Món không
tiêu hao và gói đăng ký thì cửa hàng giữ: pack xác nhận, game hỏi `IapSdk.Owns` và nghe
`IapSdk.OwnershipChanged`. Game mua hàng qua lớp static `IapSdk`.

Package `com.riseon.iap`, namespace `RiseOn.Iap`.

## Mục lục

- [Yêu cầu](#yêu-cầu)
- [Cài đặt](#cài-đặt)
- [Viết granter](#viết-granter)
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
| `com.riseon.utils` 2.0.0 | Tự cài theo `package.json` | Các mục của trang cài đặt |
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
    "com.riseon.iap": "3.0.0"
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

## Viết granter

Mỗi sản phẩm Consumable trên trang có ô **Granter**: một lớp của game cài `IIapGranter`,
chọn theo tên lớp ngay trên trang, kèm field của nó (ví dụ số gems một gói). Một lớp
dùng được cho nhiều sản phẩm, mỗi sản phẩm một instance với số riêng. Sản phẩm
Non-Consumable và Subscription không có ô này: cửa hàng giữ quyền sở hữu, game đọc
`IapSdk.Owns` (xem [Gọi từ game](#gọi-từ-game)).

Lớp là `[Serializable]`, có constructor rỗng, nằm trong assembly vào build (không phải
thư mục `Editor`). Hàm `GrantAsync` là `protected internal`: chỉ pack gọi được, code
khác của game không gọi được, kể cả khi ép kiểu về `IIapGranter`, nên lớp cài nó kiểu
explicit. Namespace của pack là `RiseOn.Iap`:

```csharp
using System;
using Cysharp.Threading.Tasks;
using RiseOn.Iap;
using Sirenix.OdinInspector;
using UnityEngine;

[Serializable]
public sealed class GemsGranter : IIapGranter {
    [SerializeField, MinValue(1)]
    private int gems = 100;

    async UniTask IIapGranter.GrantAsync(IapOrder order) {
        await Save.WaitUntilLoadedAsync();    // giao dịch dở dang đến lúc mở game, có thể trước khi save load xong
        Save.Gems += gems * order.Quantity;
        await Save.WriteAsync();              // lưu xong mới trả về
    }
}
```

Field serialize của granter hiện dưới ô Granter trên trang, do Odin vẽ. Giới hạn giá trị
bằng `[MinValue]` của Odin, không dùng `[Min]` của Unity: `[Min]` khiến Odin để drawer
của Unity vẽ field, lệch khỏi cột nhãn của trang.

`GrantAsync` phải:

- **Tự chờ những gì nó đọc**, save của game trước hết: giao dịch chưa xác nhận của lần
  trước tới bất cứ lúc nào sau khi mở game, có thể trước khi save load xong.
- **Cộng đồ và lưu xong rồi mới trả về.** Sau khi nó trả về, pack ghi mã giao dịch vào
  PlayerPrefs rồi xác nhận với cửa hàng. Đồ chưa lưu mà game crash thì mất, vì cửa hàng
  không trả lại giao dịch đã xác nhận.
- **Ném lỗi khi không cấp được, và chỉ khi đó.** Pack không ghi mã, không xác nhận; giao
  dịch quay lại ở lần mở sau và granter chạy lần nữa. Nên đã lưu xong thì không được ném
  nữa, kể cả lỗi từ event của game chạy trong lúc lưu (như event báo save đã ghi): ném
  lúc đó thì đồ được cấp thêm lần nữa ở lần mở sau. Event như vậy bắt lỗi từng handler,
  như `SaveManager` của project mẫu.

Pack giữ mã giao dịch đã cấp trong PlayerPrefs, key `RiseOn.Iap.GrantedTransactions`,
từ lúc granter lưu xong tới lúc cửa hàng xác nhận. Cửa hàng trả lại một giao dịch đã có mã
(crash trước khi xác nhận, xác nhận lỗi mạng) thì pack chỉ xác nhận, không gọi granter lần
hai. Game không xóa key này: nút reset tiến trình gọi `PlayerPrefs.DeleteAll()` thì giao
dịch đang chờ xác nhận sẽ được cấp lần nữa, nên xóa từng key của game thay vì
`DeleteAll`. Game crash đúng lúc giữa lần lưu của
granter và lần ghi mã (vài mili giây) thì lần mở sau đồ được cấp lại: người chơi được
thêm, không bao giờ mất.

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
| `Purchased` | Món tiêu hao: granter đã cấp và lưu. Món không tiêu hao, gói đăng ký: đã thành sở hữu. Giao dịch đã gửi xác nhận |
| `Canceled` | Người chơi đóng bảng thanh toán |
| `Deferred` | Chờ duyệt (Ask to Buy, thanh toán chậm). Khi được duyệt, có thể ở phiên sau, giao dịch đi như mọi giao dịch khác |
| `Failed` | Cửa hàng chưa trả về sản phẩm, thanh toán lỗi, chữ ký sai, hoặc granter ném lỗi; Console có lý do |

`PurchaseAsync` ném lỗi khi sản phẩm không có trong danh sách, khi sản phẩm Consumable
chưa có granter, hoặc khi đang có giao dịch khác chạy. `IapSdk.IsPurchasing` cho biết
điều cuối, dùng để khóa nút mua.

**Quyền sở hữu** (Non-Consumable, Subscription) do cửa hàng giữ, nên game không lưu nó.
Google Play và StoreKit 2 (iOS 15 trở lên) trả lời từ bản sao trên máy, nên thường không
cần mạng: pack lấy giao dịch sau khi có sản phẩm, và lấy sớm một lần ngay khi lần lấy sản
phẩm đầu tiên thất bại.
`IapSdk.Owns` là `false` tới khi cửa hàng trả lời, một lúc sau khi mở game.
`IapSdk.OwnershipChanged` báo mỗi lần quyền đổi: mua, Restore, lần cửa hàng trả lời lúc mở
game, hoàn tiền, gói đăng ký hết hạn. Code bắt đầu nghe muộn thì đọc `Owns` một lần lúc
bắt đầu nghe. Ví dụ với `DefaultAdsManager` của `com.riseon.ads` (`using RiseOn.Ads;`):

```csharp
DefaultAdsManager.Ins.SetAdsRemoved(IapSdk.Owns(IapProductId.NoAds));
IapSdk.OwnershipChanged += (product, owned) => {
    if (product is IapProductId.NoAds) DefaultAdsManager.Ins.SetAdsRemoved(owned);
};
```

Trước câu trả lời đầu tiên đó, ad vẫn hiện với người đã mua remove-ads: banner tự hiện
và ad lúc mở game không chờ IAP. Game muốn tránh thì chờ `IapSdk.WaitUntilReadyAsync()`
trước khi bật banner hay hiện ad mở game, luôn kèm giới hạn thời gian của game: cửa hàng
không kết nối được thì hàm này chờ mãi.

Món mua trong phiên giữ sở hữu tới hết phiên, kể cả khi lần lấy giao dịch sau không còn
nó; chỉ App Store thu hồi (hoàn tiền) mới lấy lại ngay. Gói đăng ký test của Google Play
gia hạn vài phút một lần, nên muốn thấy nó hết hạn thì mở lại game.

App Store bắt có nút Restore: gọi `IapSdk.RestoreAsync()`. Pack hỏi lại cửa hàng các giao
dịch của tài khoản: món cửa hàng còn giữ thành sở hữu, món mua ở phiên trước mà cửa hàng
không còn giữ (hoàn tiền, hết hạn) thôi sở hữu.

Event của `IapSdk`, gọi trên thread của Unity. Một handler ném lỗi chỉ được log, các
handler khác vẫn chạy:

| Event | Khi nào |
|---|---|
| `Changed` | Sản phẩm hay quyền sở hữu có thể đã đổi: cửa hàng trả về sản phẩm, trả lời (hay không trả lời được) về giao dịch, một món không tiêu hao hay gói đăng ký được mua, một quyền hết. Mua món tiêu hao không báo event này; nghe `Granted` |
| `Granted` | Granter vừa cấp và lưu đồ của một `IapOrder` |
| `OwnershipChanged` | Một món không tiêu hao hay gói đăng ký thành sở hữu hoặc hết sở hữu |
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
| Stores: Fake Store | Build mua từ fake store của pack (xem [Fake store](#fake-store)): không trừ tiền, không kiểm biên lai, để thử trước khi sản phẩm có trên cửa hàng. Editor luôn dùng fake store, ô tick hay không |
| Products | Mỗi sản phẩm một dòng: ở cột nhãn là mã sản phẩm như trên Play Console (rê chuột lên ô để xem tên trong `IapProductId`), ở cột giá trị là loại: Consumable, Non-Consumable, Subscription. Mũi tên đầu dòng mở ra dưới dòng: **Granter** (chỉ Consumable), chọn lớp cài `IIapGranter` (xem [Viết granter](#viết-granter)), field của nó ngay dưới; rồi dòng **Fake Store** có mũi tên riêng, mở ra **Title**, **Price** (đô la Mỹ), **Description** mà fake store trả về, Title trống thì lấy mã. Cửa hàng thật trả dữ liệu của nó, không đọc các ô này. Đổi loại khỏi Consumable thì granter đã chọn vẫn giữ trong asset nhưng pack bỏ qua, đổi lại thì nó quay lại. Nút **+** trên thanh thêm sản phẩm (giá fake store 0.99), **×** xóa sản phẩm, kéo tay nắm để đổi thứ tự |

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

File sản phẩm có dòng đọc không được, thường do conflict khi merge, thì compile báo lỗi
`RISEONIAP002`: mở trang Project Settings > RiseOn > IAP, bấm **Apply** để ghi lại.

Trang báo đỏ (lỗi gắn với một dòng có nút **Edit** đưa tới dòng đó) và build dừng khi:

| Trường hợp | Nền tảng |
|---|---|
| Asset nằm ngoài thư mục Resources; nút **Move it** chuyển vào | Mọi nền tảng |
| Sản phẩm Consumable chưa có granter, hoặc granter nằm trong assembly chỉ cho Editor (Play mode chạy được, bản build không có) | Mọi nền tảng |
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

Pack có fake store riêng. Nó chạy trong Editor, trên nền tảng không có cửa hàng, và trong
build bật Fake Store. Sản phẩm mang Title, Description, Price của trang cài đặt, nên
`IapSdk.Product(...)` và shop của game hiện đúng chữ và giá như sẽ thấy trên cửa hàng (giá
kiểu `$0.99`, tiền tệ USD).

Mỗi lần mua, bảng thanh toán của fake store trượt lên từ đáy màn hình, trên mọi canvas,
vẽ như bảng thanh toán test của cửa hàng mà build mua, bảng mà tester gặp khi mua bằng
tài khoản test: kiểu App Store trên iOS và trong Editor khi build đang chọn là iOS, kiểu
Google Play ở mọi chỗ khác. Bảng theo chế độ tối của máy trên Android và iOS, luôn sáng
trên nền tảng khác; trong Editor theo giao diện tối hay sáng của Editor. Muốn xem kiểu
App Store trong Editor thì chuyển build target sang iOS; muốn xem bảng tối hay sáng thì
đổi Editor Theme trong Preferences. Icon trên bảng Google Play là **Default Icon** trong
Player Settings khi chạy trong Editor; trong build, hoặc khi Default Icon để trống (kể
cả khi game chỉ đặt icon riêng cho Android hay iOS), là chữ cái đầu tên game.

Trên bảng Google Play, dòng phương thức thanh toán mở trang **Payment methods** với bốn
thẻ test; thẻ chọn ở đó quyết định kết quả khi bấm **1-tap buy** (gói đăng ký:
**Subscribe**):

| Thẻ | `PurchaseAsync` trả về |
|---|---|
| **Test card, always approves** (chọn sẵn mỗi lần mở bảng) | `Purchased`, sau khi granter cấp đồ (món tiêu hao) và pack xác nhận |
| **Test card, always declines** | `Failed` |
| **Slow test card, approves after a few seconds** | `Deferred`; 8 giây sau giao dịch được duyệt và đi như mọi giao dịch tới ngoài `PurchaseAsync`: granter cấp đồ rồi `IapSdk.Granted` báo (món tiêu hao), hay món thành sở hữu và `IapSdk.OwnershipChanged` báo (món không tiêu hao, gói đăng ký); game cập nhật UI theo hai event đó |
| **Slow test card, declines after a few seconds** | `Deferred`; 8 giây sau giao dịch bị từ chối, không event nào báo |

Thẻ chậm trên Google Play thật chờ vài phút; ở fake store chỉ 8 giây. Trang thật còn thẻ
**Test card, approves then charges back**; fake store không có thẻ này. Muốn thử hoàn
tiền một món không tiêu hao hay gói đăng ký thì mua bằng thẻ đó với tài khoản test trên
Google Play thật rồi mở lại game: `IapSdk.Owns` của món là `false`. `RestoreAsync` trong
phiên đã mua không gỡ món đó.

Bảng App Store: bấm **Buy** (gói đăng ký: **Subscribe**) là mua (`Purchased`). Từ chối
và Ask to Buy nằm trên dải nền tối **Fake Store:** ngay phía trên bảng:

| Nút trên dải | `PurchaseAsync` trả về |
|---|---|
| **Decline** | `Failed` |
| **Ask to Buy: yes** | `Deferred`; 8 giây sau được duyệt, như thẻ chậm được duyệt ở trên |
| **Ask to Buy: no** | `Deferred`; 8 giây sau bị từ chối, như thẻ chậm bị từ chối ở trên |

Chạm ra ngoài bảng, hay nút X (App Store luôn có; Google Play chỉ khi màn hình nằm
ngang), trả `Canceled` và bảng trượt xuống ngay. Các lựa chọn khác hiện kết quả một lúc
rồi trượt xuống. Pack nhận câu trả lời lúc bảng bắt đầu trượt xuống; với `Purchased`,
`PurchaseAsync` còn chờ granter cấp và lưu xong mới trả về. Trong 8 giây một giao dịch
hoãn chờ trả lời, mua lại đúng món đó trả `Failed` ngay, không mở bảng.

Bảng và 8 giây chờ của giao dịch hoãn tính theo thời gian thực, không theo
`Time.timeScale`, nên vẫn chạy đúng hạn khi game dừng thời gian dưới shop. Scene không
có EventSystem thì bảng tự thêm một cái, dùng module của Input System khi project cài và
bật Input System.

Món không tiêu hao và gói đăng ký đã mua chỉ còn trong phiên chơi đó: mở lại game là
mất, `IapSdk.Owns` thành `false`. Restore trong cùng phiên trả lại chúng ở dạng đã xác
nhận.

## Luồng của pack

1. Trước scene đầu: kết nối cửa hàng. Mất kết nối thì tự thử lại với thời gian chờ tăng
   dần (`ExponentialBackOffRetryPolicy` của Unity IAP).
2. Lấy sản phẩm; lỗi thì thử lại mãi cùng cách đó. Lấy giao dịch của tài khoản sau khi có
   sản phẩm, và một lần sớm hơn, ngay khi lần lấy sản phẩm đầu tiên thất bại: món không
   tiêu hao và gói đăng ký cửa hàng giữ thành sở hữu (`OwnershipChanged` báo). Có câu trả
   lời về giao dịch, kể cả câu trả lời là lỗi, thì `IapSdk.IsReady` thành `true`,
   `IapSdk.WaitUntilReadyAsync` trả về, `IapSdk.Changed` báo. Có mạng thì sản phẩm đã có
   lúc đó; sản phẩm tới sau (lấy sớm khi không có mạng) thì `Changed` báo lần nữa, nên
   code đọc `IapSdk.Product(...)` cập nhật lại theo `Changed`. Giao dịch chưa xác
   nhận mà lần lấy sớm trả về thì chờ tới lần lấy sau khi có sản phẩm mới đi tiếp bước 3:
   chưa có sản phẩm thì Unity IAP không biết loại và giá của nó.
3. Mỗi giao dịch đã trả tiền đi qua kiểm chữ ký Google Play, rồi:
   - Món tiêu hao: mã đã có trong PlayerPrefs thì bỏ qua bước cấp; không thì granter
     `GrantAsync` → pack ghi mã → `IapSdk.Granted`. Rồi pack xác nhận với cửa hàng.
   - Món không tiêu hao, gói đăng ký: pack xác nhận, món thành sở hữu
     (`OwnershipChanged`).

   Trong một phiên, một giao dịch chỉ được xử lý một lần.
4. Cửa hàng xác nhận xong thì `IapSdk.Purchased` báo và, với món tiêu hao, pack xóa mã
   khỏi PlayerPrefs; mỗi giao dịch một lần. Có `com.riseon.analytics` và ô **Link RiseOn
   Iap** ở dòng Purchase của bảng Standard Events trên trang Analytics được tick (mặc
   định) thì doanh thu tự gửi tới các dịch vụ được tick từ đây, game không gọi
   `AnalyticsSdk.LogPurchase`; bỏ tick thì game tự gọi `LogPurchase`.

## Khi có lỗi

| Trường hợp | Pack làm gì |
|---|---|
| Không kết nối được cửa hàng lúc mở game | Thử kết nối lại mãi; sản phẩm trả `Available = false`, `Owns` trả `false`, `IapSdk.IsReady` giữ `false` và `IapSdk.WaitUntilReadyAsync` chờ tới khi kết nối được, nên game chờ nó thì luôn kèm giới hạn thời gian. Kết nối được thì `OwnershipChanged` báo các món đang sở hữu |
| Kết nối được nhưng không có mạng | Sản phẩm trả `Available = false` và được lấy lại tới khi có mạng; giao dịch vẫn được lấy từ bản sao trên máy, nên `Owns` và `IsReady` có câu trả lời. Máy iOS dưới 15 (StoreKit 1) chỉ trả giao dịch của sản phẩm đã có, nên ở đó `Owns` chờ tới khi có sản phẩm |
| Cửa hàng không trả giao dịch của tài khoản | Log cảnh báo; `Owns` giữ như trước (`false` lúc mở game), `IsReady` vẫn thành `true`, `Changed` báo. Chưa có sản phẩm thì pack lấy lại cùng lần thử lấy sản phẩm sau; có rồi thì tự lấy lại sau 5 giây, chờ gấp đôi mỗi lần, tối đa một phút, tới khi được |
| Cửa hàng không trả về một số sản phẩm | Log lỗi kèm mã, các sản phẩm khác vẫn bán |
| Chữ ký Google Play sai | Log lỗi, không cấp, không xác nhận; Google Play tự hoàn tiền sau ba ngày |
| Sản phẩm Consumable không có granter | Log lỗi, không xác nhận; giao dịch quay lại ở lần mở sau |
| Giao dịch của sản phẩm mà cửa hàng không trả về, như sản phẩm đã xóa khỏi danh sách | Log lỗi kèm mã giao dịch khi lấy giao dịch sau khi có sản phẩm, không xác nhận; giao dịch chờ tới khi sản phẩm có lại trong danh sách và trên cửa hàng |
| Granter ném lỗi khi cấp | Log exception, không ghi mã, không xác nhận; giao dịch quay lại ở lần mở sau |
| Cửa hàng không nhận xác nhận | Log cảnh báo; giao dịch quay lại ở lần mở sau, mã trong PlayerPrefs chặn cấp lần hai |
| Hoàn tiền một món không tiêu hao, gói đăng ký hết hạn | Món đang sở hữu trong phiên thì `Owns` thành `false`, `OwnershipChanged` và `Changed` báo: hoàn tiền App Store ngay khi thu hồi, kể cả món mua trong phiên; còn lại ở `RestoreAsync`, với món mua ở phiên trước. Mở lại game thì món không thành sở hữu: `Owns` là `false`, `OwnershipChanged` không báo món đó (`Changed` báo như mọi lần cửa hàng trả lời về giao dịch) |

## Lịch sử thay đổi

Xem [CHANGELOG.md](CHANGELOG.md).

## Giấy phép

MIT, xem [LICENSE.md](LICENSE.md).