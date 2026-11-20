using UnityEngine.Purchasing;

namespace RiseOn.Iap {
    /// <summary>A product of the catalog, with what the store said about it once it answered.</summary>
    public readonly struct IapProduct {
        public readonly string Id;
        public readonly IapProductType Type;

        /// <summary>The store returned the product and sells it; buy only when this is true.</summary>
        public readonly bool Available;

        /// <summary>The store's localized price, such as "$0.99"; empty until <see cref="Available"/>.</summary>
        public readonly string Price;

        public readonly decimal Value;

        /// <summary>ISO 4217.</summary>
        public readonly string Currency;

        public readonly string Title;
        public readonly string Description;

        internal IapProduct(string id, IapProductType type, Product product) {
            Id   = id;
            Type = type;

            var metadata = product?.metadata;
            Available   = product is { availableToPurchase: true } && metadata != null;
            Price       = metadata?.localizedPriceString ?? "";
            Value       = metadata?.localizedPrice ?? 0;
            Currency    = metadata?.isoCurrencyCode ?? "";
            Title       = metadata?.localizedTitle ?? "";
            Description = metadata?.localizedDescription ?? "";
        }

        public override string ToString() => Available ? $"{Id} ({Type}): {Price}" : $"{Id} ({Type}): not on the store";
    }
}