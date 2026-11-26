using UnityEngine.Purchasing;

namespace RiseOn.Iap {
    /// <summary>
    /// A paid consumable for its product's <see cref="IIapGranter"/> to give.<br/>
    /// The pack confirms it to the store once the grant returns, and keeps its <see cref="TransactionId"/> until the store has, so that one handed again after a crash or a failed confirmation is not given twice.
    /// </summary>
    public sealed class IapOrder {
        /// <summary>The catalog id; the store's own id when the catalog no longer has the product.</summary>
        public string ProductId { get; }

        /// <summary>0, no member, when the catalog no longer has the product; <see cref="ProductId"/> names it.</summary>
        public IapProductId Product { get; }

        public IapProductType Type { get; }
        public int Quantity { get; }

        /// <summary>The same for every delivery of one purchase: Google Play's purchase token, the App Store's transaction id.</summary>
        public string TransactionId { get; }

        /// <summary>The answer to the purchase the pack is running, rather than one the store returned on its own.</summary>
        public bool Requested { get; }

        internal IapOrder(Order order, IapProductId product, string productId, IapProductType type, int quantity, bool requested) {
            Product       = product;
            ProductId     = productId;
            Type          = type;
            Quantity      = quantity;
            TransactionId = order.Info.TransactionID;
            Requested     = requested;
        }

        public override string ToString() {
            return $"{ProductId} x{Quantity} ({TransactionId})";
        }
    }

    public enum IapResult {
        /// <summary>A consumable's granter gave the purchase, or the store keeps a non-consumable's, and the pack confirmed it to the store.</summary>
        Purchased
      , Canceled

        /// <summary>Waiting for approval (Ask to Buy, a pending payment); the grant comes when the store approves, maybe in a later session.</summary>
      , Deferred
      , Failed
    }

    /// <summary>A purchase the store confirmed in this session, once.</summary>
    public readonly struct IapPurchase {
        public readonly string ProductId;
        public readonly int Quantity;

        /// <summary>The price the player paid for the whole quantity, in <see cref="Currency"/>.</summary>
        public readonly decimal Value;

        /// <summary>ISO 4217.</summary>
        public readonly string Currency;

        /// <summary>As <see cref="IapOrder.TransactionId"/>.</summary>
        public readonly string TransactionId;

        /// <summary>The id the store shows the player and its reports: Google Play's order id (GPA.…), the App Store's transaction id.</summary>
        public readonly string OrderId;

        internal IapPurchase(string productId, int quantity, decimal value, string currency, string transactionId, string orderId) {
            ProductId     = productId;
            Quantity      = quantity;
            Value         = value;
            Currency      = currency;
            TransactionId = transactionId;
            OrderId       = orderId;
        }

        public override string ToString() {
            return $"{ProductId} x{Quantity}: {Value} {Currency} ({OrderId})";
        }
    }
}