using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Security;

namespace RiseOn.Iap {
    /// <summary>
    /// Unity IAP, connected before the first scene with the catalog from Project Settings > RiseOn > IAP: products fetched, then the account's purchases.<br/>
    /// A paid consumable goes to its product's <see cref="IIapGranter"/>, then the pack confirms it; a paid non-consumable or subscription the pack confirms
    /// itself, and the store keeps it, which <see cref="Owns"/> and <see cref="OwnershipChanged"/> tell.<br/>
    /// Unity's thread only; every event comes on it.
    /// </summary>
    public static class IapSdk {
        // Unity IAP's name for its fake store, its default where no store runs; its own constant is internal.
        private const string FakeStoreName = "fake";

        // Transactions this session handed to a grant or to the store's confirmation and not confirmed yet: the store hands one
        // again on a restore or a reconnection, and none may be granted or confirmed twice in one session.
        private static readonly HashSet<string> handling = new();

        // Pending transactions the store handed before their product was fetched, kept for the fetch after the products: Unity takes their type
        // as Unknown, so Google Play would only acknowledge a consumable rather than consume it and hand it again, and their price is unknown.
        // Unity hands a transaction to OnPurchasePending once a session, so that later fetch is where they go on.
        private static readonly HashSet<string> waiting = new();

        private static readonly HashSet<string> ownedByStore = new();
        private static readonly HashSet<string> boughtHere   = new();
        private static readonly HashSet<string> owned        = new();

        private static IapSettings settings;
        private static StoreController store;
        private static IapFakeStore fakeStore;
        private static CrossPlatformValidator validator;
        private static Running running;
        private static bool purchasesRequested;
        private static bool purchasesRequestedEarly;
        // The products came back, or their fetch gave up: no later fetch of them will ask for the purchases again.
        private static bool productsDone;
        private static int purchasesRetries;
        private static bool ready;
        private static UniTaskCompletionSource readySource = new();

        /// <summary>Products or ownership changed: the store answered, a purchase went through, an ownership ended.</summary>
        public static event Action Changed;

        /// <summary>
        /// The store confirmed a purchase handled in this session; each purchase once.<br/>
        /// The analytics pack logs its revenue from here when installed.
        /// </summary>
        public static event Action<IapPurchase> Purchased;

        /// <summary>A consumable's granter gave an order's content and saved it; the pack confirms it right after.</summary>
        public static event Action<IapOrder> Granted;

        /// <summary>
        /// A non-consumable or subscription became owned or stopped being: bought, restored, found among the account's purchases at launch, refunded, lapsed.<br/>
        /// Ownership starts false and the first answer of the store comes a moment after launch, so code that listens late reads <see cref="Owns"/> once as it starts listening.
        /// </summary>
        public static event Action<IapProductId, bool> OwnershipChanged;

        private static IapSettings Settings => settings ??= IapSettings.Load();

        /// <summary>The store returned the account's purchases, or said it could not, so <see cref="Owns"/> holds its answer; the products come before, or after a failed try that is retried meanwhile.</summary>
        public static bool IsReady => ready;

        /// <summary>A purchase is running; another one throws until it ends.</summary>
        public static bool IsPurchasing => running != null;

        public static UniTask WaitUntilReadyAsync() => readySource.Task;

        /// <summary>The product with the store's price once it answered.</summary>
        public static IapProduct Product(IapProductId product) {
            var entry = Entry(product);
            return new IapProduct(entry.Id, entry.Type, store?.GetProductById(entry.Id));
        }

        /// <summary>
        /// The account has this non-consumable or live subscription, as the store says, or it was bought in this session.<br/>
        /// False until the store answers, a moment after launch; <see cref="OwnershipChanged"/> tells when that changes it.
        /// </summary>
        public static bool Owns(IapProductId product) {
            var entry = Entry(product);
            if (entry.Type is IapProductType.Consumable) throw new ArgumentException($"{nameof(IapSdk)}: {product} is consumable, and the store keeps no ownership of those");

            return owned.Contains(entry.Id);
        }

        /// <summary>
        /// Opens the store's payment sheet.<br/>
        /// A paid consumable goes through its granter, and any paid purchase is confirmed, before this returns <see cref="IapResult.Purchased"/>.<br/>
        /// Throws while another purchase runs, and for a consumable with no granter; a product the store has not returned gives <see cref="IapResult.Failed"/>.
        /// </summary>
        public static async UniTask<IapResult> PurchaseAsync(IapProductId target) {
            var entry = Entry(target);
            if (entry.Type is IapProductType.Consumable && entry.Granter == null) throw new InvalidOperationException($"{nameof(IapSdk)}: {entry.Id} has no Granter in Project Settings > RiseOn > IAP, so nothing would give it");
            if (running != null) throw new InvalidOperationException($"{nameof(IapSdk)}: a purchase is running; {nameof(IsPurchasing)} tells when it ends");

            var product = store?.GetProductById(entry.Id);
            if (product is not { availableToPurchase: true }) {
                Debug.LogWarning($"{nameof(IapSdk)}: the store has not returned {entry.Id}{(ready ? ", check it in the store's console" : " yet")}");
                return IapResult.Failed;
            }

            var current = running = new Running(Settings.StoreId(entry.Id));
            store.PurchaseProduct(product);
            return await current.Source.Task;
        }

        /// <summary>
        /// Asks the store again for every non-consumable and subscription the account owns, for the App Store's required Restore button.<br/>
        /// What comes back is owned again, and what the store no longer holds stops being; false when the store could not answer.
        /// </summary>
        public static UniTask<bool> RestoreAsync() {
            if (store == null) return UniTask.FromResult(false);

            // Unity IAP routes no restore to a custom store. The fake store's purchases come back confirmed, as Google Play's do.
            if (fakeStore != null) {
                store.FetchPurchases();
                return UniTask.FromResult(true);
            }

            // Unity IAP fetches the purchases once a restore is done, which reads the ownership again.
            var source = new UniTaskCompletionSource<bool>();
            store.RestoreTransactions((done, error) => {
                if (!done) Debug.LogWarning($"{nameof(IapSdk)}: restore failed: {error}");
                source.TrySetResult(done);
            });
            return source.Task;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Launch() {
            // The Editor runs a copy, so that the granters' state neither dirties the asset nor outlives Play mode.
            var asset = IapSettings.Load();
            settings = Application.isEditor ? UnityEngine.Object.Instantiate(asset) : asset;

            if (Settings.Products.Count == 0) {
                MarkReady();
                return;
            }

            foreach (var entry in Settings.Products.Where(entry => entry.Type is IapProductType.Consumable && entry.Granter == null)) {
                Debug.LogError($"{nameof(IapSdk)}: {entry.Id} has no Granter in Project Settings > RiseOn > IAP, so its purchases wait unconfirmed");
            }

            if (Settings.FakeStore && !Application.isEditor) Debug.LogWarning($"{nameof(IapSdk)}: FAKE STORE, purchases charge nothing. Turn Fake Store off in Project Settings > RiseOn > IAP before shipping");

            // Google Play's signature check; StoreKit 2 verifies App Store transactions on the device itself.
            if (Application.platform is RuntimePlatform.Android && !Settings.FakeStore) validator = new CrossPlatformValidator(Convert.FromBase64String(Settings.GooglePlayKey), Application.identifier);

            // The pack's own fake store wherever Unity's would run: in the Editor, on platforms with no store, or when asked.
            if (Settings.FakeStore || UnityIAPServices.GetDefaultStore() is FakeStoreName) {
                fakeStore = new IapFakeStore(Settings);
                UnityIAPServices.AddNewCustomStore(fakeStore);
            }

            store = UnityIAPServices.StoreController(fakeStore != null ? IapFakeStore.Name : null);

            // Unity IAP retries product fetches on its own but not the connection; a player offline at launch
            // would otherwise never reach the store in that session.
            store.SetStoreReconnectionRetryPolicyOnDisconnection(new ExponentialBackOffRetryPolicy());

            Listen(store, true);
            ConnectAsync().Forget();

            static async UniTaskVoid ConnectAsync() {
                try {
                    await store.Connect();
                } catch (Exception exception) {
                    Debug.LogException(exception);
                }
            }
        }

        // Static state outlives a play session when the Editor skips the domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() {
            if (store != null) Listen(store, false);

            handling.Clear();
            waiting.Clear();
            ownedByStore.Clear();
            boughtHere.Clear();
            owned.Clear();
            GrantRecord.Reset();
            settings                = null;
            store                   = null;
            fakeStore               = null;
            validator               = null;
            running                 = null;
            purchasesRequested      = false;
            purchasesRequestedEarly = false;
            productsDone            = false;
            purchasesRetries        = 0;
            ready                   = false;
            readySource             = new UniTaskCompletionSource();
            Changed                 = null;
            Purchased               = null;
            Granted                 = null;
            OwnershipChanged        = null;
        }

        private static void Listen(StoreController target, bool on) {
            if (on) {
                target.OnStoreConnected       += OnStoreConnected;
                target.OnStoreDisconnected    += OnStoreDisconnected;
                target.OnProductsFetched      += OnProductsFetched;
                target.OnProductsFetchFailed  += OnProductsFetchFailed;
                target.OnPurchasesFetched     += OnPurchasesFetched;
                target.OnPurchasesFetchFailed += OnPurchasesFetchFailed;
                target.OnPurchasePending      += OnPurchasePending;
                target.OnPurchaseConfirmed    += OnPurchaseConfirmed;
                target.OnPurchaseFailed       += OnPurchaseFailed;
                target.OnPurchaseDeferred     += OnPurchaseDeferred;
                if (target.AppleStoreExtendedPurchaseService != null) target.AppleStoreExtendedPurchaseService.OnEntitlementRevoked += OnEntitlementRevoked;
            } else {
                target.OnStoreConnected       -= OnStoreConnected;
                target.OnStoreDisconnected    -= OnStoreDisconnected;
                target.OnProductsFetched      -= OnProductsFetched;
                target.OnProductsFetchFailed  -= OnProductsFetchFailed;
                target.OnPurchasesFetched     -= OnPurchasesFetched;
                target.OnPurchasesFetchFailed -= OnPurchasesFetchFailed;
                target.OnPurchasePending      -= OnPurchasePending;
                target.OnPurchaseConfirmed    -= OnPurchaseConfirmed;
                target.OnPurchaseFailed       -= OnPurchaseFailed;
                target.OnPurchaseDeferred     -= OnPurchaseDeferred;
                if (target.AppleStoreExtendedPurchaseService != null) target.AppleStoreExtendedPurchaseService.OnEntitlementRevoked -= OnEntitlementRevoked;
            }
        }

        private static void OnStoreConnected() {
            store.FetchProducts(Settings.Products.Select(entry => new ProductDefinition(entry.Id, Settings.StoreId(entry.Id), ToUnity(entry.Type))).ToList(), new ProductsRetry());
        }

        private static void OnStoreDisconnected(StoreConnectionFailureDescription failure) {
            Debug.LogWarning($"{nameof(IapSdk)}: store disconnected: {failure.Message}");
        }

        private static void OnProductsFetched(List<Product> products) {
            productsDone = true;
            RaiseChanged();
            RequestPurchases();
        }

        private static void OnProductsFetchFailed(ProductFetchFailed failure) {
            var missing = string.Join(", ", failure.FailedFetchProducts.Select(definition => definition.storeSpecificId));
            Debug.LogError($"{nameof(IapSdk)}: the store did not return {missing} ({failure.FailureReason}); check the ids in App Store Connect or the Play Console");
            productsDone = true;
            RaiseChanged();
            RequestPurchases();
        }

        // After the products, so that the purchases come back with their products known.
        private static void RequestPurchases() {
            if (purchasesRequested) return;

            purchasesRequested = true;
            store.FetchPurchases();
        }

        // Before them, while their fetch fails, for the ownership; what needs the products waits for the fetch after them.
        private static void RequestPurchasesEarly() {
            if (purchasesRequested || purchasesRequestedEarly) return;

            purchasesRequestedEarly = true;
            store.FetchPurchases();
        }

        private static void OnPurchasesFetched(Orders orders) {
            ownedByStore.Clear();
            foreach (var order in orders.ConfirmedOrders) {
                var (_, id, type, _) = Describe(order);
                if (type is not IapProductType.Consumable && Verified(order)) ownedByStore.Add(id);
            }

            // A record outlives its use once the store holds the transaction confirmed: the store never hands a confirmed consumable again.
            var open = orders.PendingOrders.Cast<Order>().Concat(orders.DeferredOrders).Select(order => order.Info.TransactionID).Concat(handling);
            GrantRecord.KeepOnly(open);

            foreach (var order in orders.PendingOrders.Where(order => waiting.Contains(order.Info.TransactionID) && !ProductUnknown(order)).ToList()) {
                waiting.Remove(order.Info.TransactionID);
                OnPurchasePending(order);
            }

            // With the products in, a purchase still without its product is of one the store did not return: it waits unconfirmed until it does.
            // Before them, as the early fetch is, every purchase is without one and goes on once they come.
            foreach (var order in orders.PendingOrders.Where(order => productsDone && ProductUnknown(order))) {
                Debug.LogError($"{nameof(IapSdk)}: purchase {order.Info.TransactionID} is of a product the store did not return, so it waits unconfirmed; keep the product in the Products section and in the store");
            }

            purchasesRetries = 0;

            UpdateOwned();
            MarkReady();
            RaiseChanged();
        }

        private static void OnPurchasesFetchFailed(PurchasesFetchFailureDescription failure) {
            Debug.LogWarning($"{nameof(IapSdk)}: the store did not return the account's purchases: {failure.Message}");
            // Asked again with the next try at the products, with their next fetch on a reconnection, and, the products in, on their own.
            purchasesRequested      = false;
            purchasesRequestedEarly = false;
            MarkReady();
            RaiseChanged();
            if (productsDone) RetryPurchasesAsync(store).Forget();
        }

        // Five seconds, doubling to a minute, until a fetch comes back; a new store from a reset drops the chain.
        private static async UniTaskVoid RetryPurchasesAsync(StoreController from) {
            var seconds = Math.Min(60, 5 << Math.Min(purchasesRetries++, 4));
            await UniTask.Delay(TimeSpan.FromSeconds(seconds), DelayType.Realtime);
            if (store != from || purchasesRequested) return;

            RequestPurchases();
        }

        private static void OnPurchasePending(PendingOrder order) {
            var transaction = order.Info.TransactionID;
            if (handling.Contains(transaction)) return;

            if (ProductUnknown(order)) {
                waiting.Add(transaction);
                return;
            }

            if (!Verified(order)) {
                if (Answers(order)) Finish(IapResult.Failed);
                return;
            }

            if (Describe(order).type is IapProductType.Consumable) {
                GrantAsync(order).Forget();
            } else {
                Confirm(order);
            }
        }

        // The same order for every game: a transaction the record holds was given already, the content is saved before the record,
        // and the record before the confirmation, so a crash anywhere loses nothing; one between the two saves gives it twice.
        private static async UniTaskVoid GrantAsync(PendingOrder pending) {
            var transaction = pending.Info.TransactionID;
            var requested   = Answers(pending);
            var (product, id, type, quantity) = Describe(pending);
            var order       = new IapOrder(pending, product, id, type, quantity, requested);

            handling.Add(transaction);
            try {
                if (!GrantRecord.Holds(transaction)) {
                    var granter = Granter(id) ?? throw new InvalidOperationException($"{nameof(IapSdk)}: nothing gives {id}: it has no Granter in Project Settings > RiseOn > IAP, or the catalog no longer has it");
                    await granter.GrantAsync(order);
                    GrantRecord.Add(transaction);
                    Raise(Granted, handler => handler(order));
                }
            } catch (Exception exception) {
                Debug.LogException(exception);
                Debug.LogError($"{nameof(IapSdk)}: the grant of {order} threw, so it stays unconfirmed and comes back at the next launch");
                handling.Remove(transaction);
                if (requested && Answers(pending)) Finish(IapResult.Failed);
                return;
            }

            store.ConfirmPurchase(pending);
            if (requested && Answers(pending)) Finish(IapResult.Purchased);
        }

        // A non-consumable or subscription: the store keeps it, so there is nothing to give, only to confirm.
        private static void Confirm(PendingOrder pending) {
            var requested = Answers(pending);
            handling.Add(pending.Info.TransactionID);
            store.ConfirmPurchase(pending);
            boughtHere.Add(Describe(pending).id);
            UpdateOwned();
            RaiseChanged();
            if (requested) Finish(IapResult.Purchased);
        }

        private static void OnPurchaseConfirmed(Order order) {
            var mine = handling.Remove(order.Info.TransactionID);
            switch (order) {
                case ConfirmedOrder when mine:
                    var (_, id, type, quantity) = Describe(order);
                    var metadata = order.CartOrdered.Items().FirstOrDefault()?.Product?.metadata;
                    var purchase = new IapPurchase(id, quantity, (metadata?.localizedPrice ?? 0) * quantity, metadata?.isoCurrencyCode ?? "", order.Info.TransactionID, order.Info.Google?.OrderId ?? order.Info.TransactionID);
                    Raise(Purchased, handler => handler(purchase));
                    if (type is IapProductType.Consumable) GrantRecord.Remove(order.Info.TransactionID);
                    break;

                // Acknowledged before, by a backend or another device: the store will not hand it again either.
                case FailedOrder { FailureReason: PurchaseFailureReason.DuplicateTransaction }:
                    GrantRecord.Remove(order.Info.TransactionID);
                    break;

                case FailedOrder failed:
                    Debug.LogWarning($"{nameof(IapSdk)}: the store did not confirm {Describe(order).id} ({failed.FailureReason}: {failed.Details}); it comes back at the next launch");
                    break;
            }
        }

        private static void OnPurchaseFailed(FailedOrder order) {
            if (order.FailureReason is not PurchaseFailureReason.UserCancelled) Debug.LogWarning($"{nameof(IapSdk)}: purchase of {Describe(order).id} failed ({order.FailureReason}: {order.Details})");
            if (Answers(order)) Finish(order.FailureReason is PurchaseFailureReason.UserCancelled ? IapResult.Canceled : IapResult.Failed);
        }

        private static void OnPurchaseDeferred(DeferredOrder order) {
            if (Answers(order)) Finish(IapResult.Deferred);
        }

        private static void OnEntitlementRevoked(string storeId) {
            var id = CatalogId(storeId);
            ownedByStore.Remove(id);
            boughtHere.Remove(id);
            UpdateOwned();
            RaiseChanged();
        }

        // What the store holds and what this session bought; each product whose ownership changes is told.
        private static void UpdateOwned() {
            var now     = new HashSet<string>(ownedByStore.Concat(boughtHere));
            var changed = owned.Concat(now).Where(id => owned.Contains(id) != now.Contains(id)).Distinct().ToList();
            owned.Clear();
            owned.UnionWith(now);

            foreach (var id in changed) {
                foreach (var entry in Settings.Products) {
                    if (entry.Id == id) Raise(OwnershipChanged, handler => handler((IapProductId) entry.Number, now.Contains(id)));
                }
            }
        }

        private static IIapGranter Granter(string id) {
            foreach (var entry in Settings.Products) {
                if (entry.Id == id) return entry.Granter;
            }

            return null;
        }

        private static bool Verified(Order order) {
            if (validator == null) return true;

            try {
                validator.Validate(order.Info.Receipt);
                return true;
            } catch (IAPSecurityException exception) {
                Debug.LogError($"{nameof(IapSdk)}: {Describe(order).id} failed Google Play's signature check and is not taken: {exception.Message}");
                return false;
            }
        }

        private static void RaiseChanged() {
            Raise(Changed, handler => handler());
        }

        // Each handler alone, so that one failing in the game neither stops the others nor the pack's own bookkeeping.
        private static void Raise<T>(T handlers, Action<T> call) where T : Delegate {
            if (handlers == null) return;

            foreach (var handler in handlers.GetInvocationList()) {
                try {
                    call((T) handler);
                } catch (Exception exception) {
                    Debug.LogException(exception);
                }
            }
        }

        private static bool Answers(Order order) {
            return running != null && StoreId(order) == running.StoreId;
        }

        private static void Finish(IapResult result) {
            var done = running;
            running = null;
            done?.Source.TrySetResult(result);
        }

        private static void MarkReady() {
            ready = true;
            readySource.TrySetResult();
        }

        private static bool ProductUnknown(Order order) {
            return order.CartOrdered.Items().FirstOrDefault()?.Product?.definition?.type is null or ProductType.Unknown;
        }

        private static string StoreId(Order order) {
            return order.CartOrdered.Items().FirstOrDefault()?.Product?.definition?.storeSpecificId ?? "";
        }

        private static (IapProductId product, string id, IapProductType type, int quantity) Describe(Order order) {
            var item = order.CartOrdered.Items().FirstOrDefault();
            var id   = CatalogId(item?.Product?.definition?.storeSpecificId ?? "");
            foreach (var entry in Settings.Products) {
                if (entry.Id == id) return ((IapProductId)entry.Number, id, entry.Type, item?.Quantity ?? 1);
            }

            return (0, id, FromUnity(item?.Product?.type ?? ProductType.Unknown), item?.Quantity ?? 1);
        }

        private static string CatalogId(string storeId) {
            foreach (var entry in Settings.Products) {
                if (Settings.StoreId(entry.Id) == storeId) return entry.Id;
            }

            return storeId;
        }

        // Only a cast number misses: every member comes from the catalog.
        private static IapSettings.Entry Entry(IapProductId product) {
            foreach (var entry in Settings.Products) {
                if (entry.Number == (int)product) return entry;
            }

            throw new ArgumentException($"{nameof(IapSdk)}: {product} is not in the catalog of Project Settings > RiseOn > IAP");
        }

        private static ProductType ToUnity(IapProductType type) => type switch {
            IapProductType.Consumable    => ProductType.Consumable
          , IapProductType.NonConsumable => ProductType.NonConsumable
          , IapProductType.Subscription  => ProductType.Subscription
          , _                            => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };

        // A product the catalog lost is taken as the store describes it; one the store does not describe is the
        // safest guess, consumable, which nothing counts as owned.
        private static IapProductType FromUnity(ProductType type) => type switch {
            ProductType.NonConsumable => IapProductType.NonConsumable
          , ProductType.Subscription  => IapProductType.Subscription
          , _                         => IapProductType.Consumable
        };

        // The consumable transactions given and not yet confirmed by the store, kept in PlayerPrefs, so that one the store hands again
        // after a crash or a failed confirmation is not given twice. Each goes once the store confirms it.
        private static class GrantRecord {
            private const string Key = "RiseOn.Iap.GrantedTransactions";

            private static HashSet<string> held;

            private static HashSet<string> Held => held ??= new HashSet<string>(PlayerPrefs.GetString(Key, "").Split('\n').Where(transaction => transaction.Length > 0));

            public static bool Holds(string transaction) => Held.Contains(transaction);

            public static void Add(string transaction) {
                if (Held.Add(transaction)) Save();
            }

            public static void Remove(string transaction) {
                if (Held.Remove(transaction)) Save();
            }

            public static void KeepOnly(IEnumerable<string> transactions) {
                var keep = new HashSet<string>(transactions);
                if (Held.RemoveWhere(transaction => !keep.Contains(transaction)) > 0) Save();
            }

            public static void Reset() => held = null;

            private static void Save() {
                PlayerPrefs.SetString(Key, string.Join("\n", Held));
                PlayerPrefs.Save();
            }
        }

        // Unity IAP retries a failed product fetch for as long as it fails, offline for the whole session, and says nothing meanwhile.
        // The ownership does not wait for it: both stores answer the purchases from their copy on the device.
        private sealed class ProductsRetry : IRetryPolicy {
            private readonly ExponentialBackOffRetryPolicy backOff = new();

            public Task<bool> ShouldRetry(IRetryPolicyInformation info) {
                RequestPurchasesEarly();
                return backOff.ShouldRetry(info);
            }
        }

        private sealed class Running {
            public readonly string StoreId;
            public readonly UniTaskCompletionSource<IapResult> Source = new();

            public Running(string storeId) {
                StoreId = storeId;
            }
        }
    }
}