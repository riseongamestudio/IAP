using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;

namespace RiseOn.Iap {
    /// <summary>
    /// The pack's fake store, through Unity IAP's custom store API: products as the catalog's Title, Description and Price,
    /// and each purchase ended on <see cref="IapFakeStoreSheet"/>.<br/>
    /// Owned products last for the session, as in Unity's own fake store.
    /// </summary>
    internal sealed class IapFakeStore : Store, IStoreWrapper {
        public const string Name     = "RiseOnFake";
        public const string Currency = "USD";

        private const string FakeReceipt = "RiseOnFakeReceipt";

        private readonly IapSettings settings;
        private readonly List<ConfirmedOrder> owned = new();
        private ConnectionState state;

        public IapFakeStore(IapSettings settings) => this.settings = settings;

        Store IStoreWrapper.instance => this;
        string IStoreWrapper.name => Name;
        public ConnectionState GetStoreConnectionState() => state;

        /// <summary>The price as the stores show it in the US, such as "$0.99".</summary>
        public static string PriceText(decimal price) => "$" + price.ToString("0.00", CultureInfo.InvariantCulture);

        public override void Connect() {
            state = ConnectionState.Connected;
            ConnectCallback?.OnStoreConnectionSucceeded();
        }

        public override void FetchProducts(IReadOnlyCollection<ProductDefinition> products) {
            ProductsCallback?.OnProductsFetched(products.Select(definition => new ProductDescription(definition.storeSpecificId, Metadata(definition))).ToList());
        }

        public override void FetchPurchases() {
            PurchaseFetchCallback?.OnAllPurchasesRetrieved(owned.Cast<Order>().ToList());
        }

        public override void Purchase(ICart cart) {
            var product = cart.Items().First().Product;
            IapFakeStoreSheet.Show(product.metadata, Type(product.definition), answer => {
                var info = new Info(Guid.NewGuid().ToString());
                switch (answer) {
                    case IapFakeStoreSheet.Answer.Buy:
                        PurchaseCallback?.OnPurchaseSucceeded(new PendingOrder(cart, info));
                        break;
                    case IapFakeStoreSheet.Answer.Defer:
                        PurchaseCallback?.OnPurchaseDeferred(new DeferredOrder(cart, info));
                        break;
                    case IapFakeStoreSheet.Answer.Decline:
                        PurchaseCallback?.OnPurchaseFailed(new FailedOrder(cart, PurchaseFailureReason.PaymentDeclined, "Declined on the fake store"));
                        break;
                    default:
                        PurchaseCallback?.OnPurchaseFailed(new FailedOrder(cart, PurchaseFailureReason.UserCancelled, "Cancelled on the fake store"));
                        break;
                }
            });
        }

        public override void FinishTransaction(PendingOrder order) {
            var transaction = order.Info.TransactionID;
            var definition  = order.CartOrdered.Items().First().Product.definition;
            if (definition.type is not ProductType.Consumable && owned.All(other => other.Info.TransactionID != transaction)) owned.Add(new ConfirmedOrder(order.CartOrdered, order.Info));

            ConfirmCallback?.OnConfirmOrderSucceeded(transaction);
        }

        public override void CheckEntitlement(ProductDefinition product) {
            EntitlementCallback?.OnCheckEntitlement(product, Owns(product.storeSpecificId) ? EntitlementStatus.FullyEntitled : EntitlementStatus.NotEntitled);
        }

        private bool Owns(string storeId) => owned.Any(order => order.CartOrdered.Items().Any(item => item.Product.definition.storeSpecificId == storeId));

        private ProductMetadata Metadata(ProductDefinition definition) {
            foreach (var entry in settings.Products) {
                if (settings.StoreId(entry.Id) == definition.storeSpecificId) return new ProductMetadata(PriceText(entry.Price), entry.Title, entry.Description, Currency, entry.Price);
            }

            return new ProductMetadata(PriceText(0), definition.id, "", Currency, 0);
        }

        private static IapProductType Type(ProductDefinition definition) => definition.type switch {
            ProductType.NonConsumable => IapProductType.NonConsumable
          , ProductType.Subscription  => IapProductType.Subscription
          , _                         => IapProductType.Consumable
        };

        // Unity IAP's own order info is internal; a fake order has no store's details.
        private sealed class Info : IOrderInfo {
            public Info(string transaction) => TransactionID = transaction;

            public IAppleOrderInfo Apple => null;
            public IGoogleOrderInfo Google => null;
            public IPaymentProvidersOrderInfo PaymentProviders => null;
            public List<IPurchasedProductInfo> PurchasedProductInfo { get; set; } = new();
            public string Receipt => FakeReceipt;
            public string TransactionID { get; }
        }
    }
}