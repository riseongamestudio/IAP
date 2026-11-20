using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Security;

namespace RiseOn.Iap {
    /// <summary>
    /// Unity IAP, connected before the first scene with the catalog from Project Settings > RiseOn > IAP: products fetched, then the account's purchases.<br/>
    /// Every paid purchase goes to the game's one <see cref="IapManagerBase"/> class to be granted, then the pack confirms it; with no such class nothing can be bought.<br/>
    /// Unity's thread only; every event comes on it.
    /// </summary>
    public static class IapSdk {
        // Unity IAP's name for its fake store, its default where no store runs; its own constant is internal.
        private const string FakeStoreName = "fake";

        private static readonly List<PendingOrder> held = new();

        // Transactions handed to a grant and not confirmed yet: the store hands one again on a restore or a
        // reconnection, and a grant must not run twice for it in one session.
        private static readonly HashSet<string> granting = new();

        // Confirmed transactions handed to a grant in this session, so that a second fetch of the purchases does not grant them again.
        private static readonly HashSet<string> regranting = new();

        private static readonly HashSet<string> ownedByStore = new();
        private static readonly HashSet<string> grantedHere  = new();

        private static IapSettings settings;
        private static Type managerType;
        private static Func<IapManagerBase> createManager;
        private static bool managerConflict;
        private static StoreController store;
        private static IapFakeStore fakeStore;
        private static CrossPlatformValidator validator;
        private static Running running;
        private static bool purchasesRequested;
        private static bool launched;
        private static bool ready;
        private static UniTaskCompletionSource readySource = new();

        /// <summary>Products or ownership changed: the store answered, a purchase was granted, an App Store refund went through.</summary>
        public static event Action Changed;

        /// <summary>
        /// The store confirmed a purchase handled in this session; each purchase once.<br/>
        /// The analytics pack logs its revenue from here when installed.
        /// </summary>
        public static event Action<IapPurchase> Purchased;

        private static IapSettings Settings => settings ??= IapSettings.Load();

        // Null before the first scene loads, or when the game has no manager class.
        internal static IapManagerBase Manager { get; private set; }

        /// <summary>The store returned the products and the account's purchases, or said it could not.</summary>
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
        /// The store says the account has this non-consumable or live subscription, or the game granted it in this session.<br/>
        /// False until the store answers, and offline: the game's own save is what decides what it gave.
        /// </summary>
        public static bool Owns(IapProductId product) {
            var entry = Entry(product);
            if (entry.Type is IapProductType.Consumable) throw new ArgumentException($"{nameof(IapSdk)}: {product} is consumable, and the store keeps no ownership of those");

            return ownedByStore.Contains(entry.Id) || grantedHere.Contains(entry.Id);
        }

        /// <summary>The running manager as its own class, for the manager's <c>Ins</c>.</summary>
        public static T GetManager<T>() where T : IapManagerBase {
            if (Manager is T manager) return manager;

            throw new InvalidOperationException(Manager != null
                ? $"{nameof(IapSdk)}: the running manager is {Manager.GetType().Name}, not {typeof(T).Name}"
                : launched
                    ? $"{nameof(IapSdk)}: no manager runs; the console says why"
                    : $"{nameof(IapSdk)}: {typeof(T).Name}.Ins is read before the pack starts, which is just before the first scene loads");
        }

        /// <summary>
        /// For the code the pack compiles into the game's manager class, which calls it before the first scene; the game never does.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void Register(Type type, Func<IapManagerBase> create) {
            if (managerType != null && managerType != type) {
                // Two assemblies that do not reference each other; the Editor reports it too.
                Debug.LogError($"{nameof(IapSdk)}: both {managerType.FullName} and {type.FullName} derive from {nameof(IapManagerBase)}, so neither runs and paid purchases wait unconfirmed; keep one");
                managerConflict = true;
                return;
            }

            managerType   = type;
            createManager = create;
        }

        // Hooks the manager to the events, then hands it the purchases paid before now.
        private static void Start(IapManagerBase manager) {
            manager.Attach();

            var waiting = held.ToList();
            held.Clear();
            foreach (var order in waiting) GrantAsync(order).Forget();
        }

        /// <summary>
        /// Opens the store's payment sheet.<br/>
        /// A paid purchase goes through the manager's grant and is confirmed before this returns <see cref="IapResult.Purchased"/>.<br/>
        /// Throws while another purchase runs, and when no manager runs to grant it; a product the store has not returned gives <see cref="IapResult.Failed"/>.<br/>
        /// <paramref name="placement"/> reaches the grant as <see cref="IapOrder.Placement"/>.
        /// </summary>
        public static async UniTask<IapResult> PurchaseAsync(IapProductId target, string placement = null) {
            var id = Entry(target).Id;
            if (Manager == null) throw new InvalidOperationException($"{nameof(IapSdk)}: no {nameof(IapManagerBase)} runs to grant {id}; the console says why");
            if (running != null) throw new InvalidOperationException($"{nameof(IapSdk)}: a purchase is running; {nameof(IsPurchasing)} tells when it ends");

            var product = store?.GetProductById(id);
            if (product is not { availableToPurchase: true }) {
                Debug.LogWarning($"{nameof(IapSdk)}: the store has not returned {id}{(ready ? ", check it in the store's console" : " yet")}");
                return IapResult.Failed;
            }

            var current = running = new Running(Settings.StoreId(id), placement);
            store.PurchaseProduct(product);
            return await current.Source.Task;
        }

        /// <summary>
        /// Asks the store again for every non-consumable and subscription the account owns, for the App Store's required Restore button.<br/>
        /// Ungranted ones reach the manager's grant; false when the store could not answer.
        /// </summary>
        public static UniTask<bool> RestoreAsync() {
            if (store == null) return UniTask.FromResult(false);

            // Unity IAP routes no restore to a custom store. The fake store's purchases come back confirmed, as Google Play's do, so the
            // manager's IsGranted guards them and none is confirmed or reported twice.
            if (fakeStore != null) {
                store.FetchPurchases();
                return UniTask.FromResult(true);
            }

            var source = new UniTaskCompletionSource<bool>();
            store.RestoreTransactions((done, error) => {
                if (!done) Debug.LogWarning($"{nameof(IapSdk)}: restore failed: {error}");
                source.TrySetResult(done);
            });
            return source.Task;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Launch() {
            // The Editor runs a copy, so that the manager's state neither dirties the asset nor outlives Play mode.
            var asset = IapSettings.Load();
            settings = Application.isEditor ? UnityEngine.Object.Instantiate(asset) : asset;
            launched = true;

            Manager = CreateManager();
            if (Manager != null) {
                Start(Manager);
            } else if (Settings.Products.Count > 0 && !managerConflict) {
                Debug.LogError($"{nameof(IapSdk)}: no partial class derives from {nameof(IapManagerBase)}, so paid purchases wait unconfirmed");
            }

            if (Settings.Products.Count == 0) {
                MarkReady();
                return;
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

        // The settings keep the fields the page edited; a class without them, never opened there, starts as written.
        private static IapManagerBase CreateManager() {
            if (managerConflict || managerType == null) return null;

            var saved = Settings.Manager;
            if (saved != null && saved.GetType() == managerType) return saved;

            if (saved != null) Debug.LogWarning($"{nameof(IapSdk)}: the settings hold the fields of {saved.GetType().Name}, not {managerType.Name}, so {managerType.Name} runs with its own defaults; open Project Settings > RiseOn > IAP to take it in");
            return createManager();
        }

        // Static state outlives a play session when the Editor skips the domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() {
            if (store != null) Listen(store, false);

            held.Clear();
            granting.Clear();
            regranting.Clear();
            ownedByStore.Clear();
            grantedHere.Clear();
            settings           = null;
            managerType        = null;
            createManager      = null;
            managerConflict    = false;
            store              = null;
            fakeStore          = null;
            validator          = null;
            running            = null;
            purchasesRequested = false;
            launched           = false;
            Manager            = null;
            ready              = false;
            readySource        = new UniTaskCompletionSource();
            Changed            = null;
            Purchased          = null;
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
            store.FetchProducts(Settings.Products.Select(entry => new ProductDefinition(entry.Id, Settings.StoreId(entry.Id), ToUnity(entry.Type))).ToList());
        }

        private static void OnStoreDisconnected(StoreConnectionFailureDescription failure) {
            Debug.LogWarning($"{nameof(IapSdk)}: store disconnected: {failure.Message}");
        }

        private static void OnProductsFetched(List<Product> products) {
            RaiseChanged();
            RequestPurchases();
        }

        private static void OnProductsFetchFailed(ProductFetchFailed failure) {
            var missing = string.Join(", ", failure.FailedFetchProducts.Select(definition => definition.storeSpecificId));
            Debug.LogError($"{nameof(IapSdk)}: the store did not return {missing} ({failure.FailureReason}); check the ids in App Store Connect or the Play Console");
            RaiseChanged();
            RequestPurchases();
        }

        // After the products, so that the purchases come back with their products known.
        private static void RequestPurchases() {
            if (purchasesRequested) return;

            purchasesRequested = true;
            store.FetchPurchases();
        }

        private static void OnPurchasesFetched(Orders orders) {
            ownedByStore.Clear();
            foreach (var order in orders.ConfirmedOrders) {
                var (_, id, type, _) = Describe(order);
                if (type is IapProductType.Consumable) continue;

                ownedByStore.Add(id);
                if (Manager != null) RegrantAsync(order).Forget();
            }

            MarkReady();
            RaiseChanged();
        }

        // Google Play hands a non-consumable confirmed before as confirmed, never as pending: after a reinstall or on a new device,
        // the game's save gets it back only through this grant. Nothing to confirm.
        private static async UniTaskVoid RegrantAsync(ConfirmedOrder confirmed) {
            var transaction = confirmed.Info.TransactionID;
            if (granting.Contains(transaction) || regranting.Contains(transaction) || !Verified(confirmed)) return;

            regranting.Add(transaction);

            var (product, id, type, quantity) = Describe(confirmed);
            var order = new IapOrder(confirmed, product, id, type, quantity, false, null);
            try {
                await Manager.CallWaitUntilCanGrantAsync();
                if (Manager.CallIsGranted(transaction)) return;

                await Manager.CallGrantAsync(order);
                Guard(() => Manager.CallOnGranted(order));
            } catch (Exception exception) {
                Debug.LogException(exception);
                Debug.LogError($"{nameof(IapSdk)}: the grant of {order}, which the store holds as bought, threw; it is tried again when the store returns the purchases next");
                regranting.Remove(transaction);
            }
        }

        private static void OnPurchasesFetchFailed(PurchasesFetchFailureDescription failure) {
            Debug.LogWarning($"{nameof(IapSdk)}: the store did not return the account's purchases: {failure.Message}");
            MarkReady();
        }

        private static void OnPurchasePending(PendingOrder order) {
            var transaction = order.Info.TransactionID;
            if (granting.Contains(transaction) || regranting.Contains(transaction) || held.Any(other => other.Info.TransactionID == transaction)) return;

            if (!Verified(order)) {
                if (Answers(order)) Finish(IapResult.Failed);
                return;
            }

            if (Manager == null) {
                held.Add(order);
            } else {
                GrantAsync(order).Forget();
            }
        }

        private static async UniTaskVoid GrantAsync(PendingOrder pending) {
            var transaction = pending.Info.TransactionID;
            var requested   = Answers(pending);
            var (product, id, type, quantity) = Describe(pending);
            var order       = new IapOrder(pending, product, id, type, quantity, requested, requested ? running.Placement : null);

            // The same order for every game: the save first, a transaction it holds is given already, and a failing
            // report after the content is saved must not leave the purchase unconfirmed.
            granting.Add(transaction);
            try {
                await Manager.CallWaitUntilCanGrantAsync();
                if (!Manager.CallIsGranted(transaction)) {
                    await Manager.CallGrantAsync(order);
                    Guard(() => Manager.CallOnGranted(order));
                }
            } catch (Exception exception) {
                Debug.LogException(exception);
                Debug.LogError($"{nameof(IapSdk)}: the grant of {order} threw, so it stays unconfirmed and comes back at the next launch");
                granting.Remove(transaction);
                if (requested && Answers(pending)) Finish(IapResult.Failed);
                return;
            }

            store.ConfirmPurchase(pending);
            if (type is not IapProductType.Consumable) {
                grantedHere.Add(id);
                RaiseChanged();
            }

            if (requested && Answers(pending)) Finish(IapResult.Purchased);
        }

        private static void OnPurchaseConfirmed(Order order) {
            var mine = granting.Remove(order.Info.TransactionID);
            switch (order) {
                case ConfirmedOrder when mine:
                    var (_, id, type, quantity) = Describe(order);
                    var metadata = order.CartOrdered.Items().FirstOrDefault()?.Product?.metadata;
                    var purchase = new IapPurchase(id, quantity, (metadata?.localizedPrice ?? 0) * quantity, metadata?.isoCurrencyCode ?? "", order.Info.TransactionID, order.Info.Google?.OrderId ?? order.Info.TransactionID);
                    Raise(Purchased, handler => handler(purchase));
                    if (type is IapProductType.Consumable) Forget(order.Info.TransactionID);
                    break;

                // Acknowledged before, by a backend or another device: a consumable the store will not hand again either; a non-consumable comes back with every fetch and keeps its record.
                case FailedOrder { FailureReason: PurchaseFailureReason.DuplicateTransaction }:
                    if (mine && Describe(order).type is IapProductType.Consumable) Forget(order.Info.TransactionID);
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
            grantedHere.Remove(id);
            RaiseChanged();
        }

        private static bool Verified(Order order) {
            if (validator == null) return true;

            try {
                validator.Validate(order.Info.Receipt);
                return true;
            } catch (IAPSecurityException exception) {
                Debug.LogError($"{nameof(IapSdk)}: {Describe(order).id} failed Google Play's signature check and is not granted: {exception.Message}");
                return false;
            }
        }

        private static void Forget(string transaction) {
            if (Manager == null) return;

            ForgetAsync().Forget();

            async UniTaskVoid ForgetAsync() {
                try {
                    await Manager.CallForgetAsync(transaction);
                } catch (Exception exception) {
                    Debug.LogException(exception);
                }
            }
        }

        private static void RaiseChanged() {
            Raise(Changed, handler => handler());
        }

        // Each handler alone, so that one failing in the game neither stops the others nor the pack's own bookkeeping.
        internal static void Raise<T>(T handlers, Action<T> call) where T : Delegate {
            if (handlers == null) return;

            foreach (var handler in handlers.GetInvocationList()) Guard(() => call((T) handler));
        }

        // A failing handler in the game must not stop the pack's own bookkeeping.
        private static void Guard(Action call) {
            try {
                call();
            } catch (Exception exception) {
                Debug.LogException(exception);
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

        // A product the catalog lost is granted as the store describes it; one the store does not describe is the
        // safest guess, consumable, which nothing counts as owned.
        private static IapProductType FromUnity(ProductType type) => type switch {
            ProductType.NonConsumable => IapProductType.NonConsumable
          , ProductType.Subscription  => IapProductType.Subscription
          , _                         => IapProductType.Consumable
        };

        private sealed class Running {
            public readonly string StoreId;
            public readonly string Placement;
            public readonly UniTaskCompletionSource<IapResult> Source = new();

            public Running(string storeId, string placement) {
                StoreId   = storeId;
                Placement = placement;
            }
        }
    }
}