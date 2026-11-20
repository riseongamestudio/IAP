using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace RiseOn.Iap {
    /// <summary>
    /// The game's IAP manager: the one partial class deriving from this, which grants every paid purchase and which the rest of the game calls for purchases; Project Settings > RiseOn > IAP edits its fields.<br/>
    /// It wraps all of <see cref="IapSdk"/>. The pack makes it just before the first scene; a second class deriving from this does not compile. The pack's source generator adds <c>Ins</c>, the running instance.<br/>
    /// The pack runs every paid purchase the same way: it waits for <see cref="WaitUntilCanGrantAsync"/>, skips a transaction <see cref="IsGranted"/> knows, calls <see cref="GrantAsync"/> and <see cref="OnGranted"/>, confirms, and after the store's confirmation of a consumable calls <see cref="ForgetAsync"/>.
    /// A non-consumable the store returns as confirmed already, as Google Play does after a reinstall, goes the same way up to <see cref="OnGranted"/>, with nothing to confirm.<br/>
    /// Unity's thread only; every event comes on it.
    /// </summary>
    [Serializable]
    public abstract class IapManagerBase {
        #region State and events

        /// <summary>As <see cref="IapSdk.IsReady"/>.</summary>
        public bool IsReady => IapSdk.IsReady;

        /// <summary>As <see cref="IapSdk.WaitUntilReadyAsync"/>.</summary>
        public UniTask WaitUntilReadyAsync() => IapSdk.WaitUntilReadyAsync();

        /// <summary>As <see cref="IapSdk.IsPurchasing"/>.</summary>
        public bool IsPurchasing => IapSdk.IsPurchasing;

        /// <summary>As <see cref="IapSdk.Product"/>.</summary>
        public IapProduct Product(IapProductId product) => IapSdk.Product(product);

        /// <summary>As <see cref="IapSdk.Owns"/>.</summary>
        public bool Owns(IapProductId product) => IapSdk.Owns(product);

        /// <summary>As <see cref="IapSdk.Changed"/>, after <see cref="OnChanged"/>.</summary>
        public event Action Changed;

        /// <summary>As <see cref="IapSdk.Purchased"/>, after <see cref="OnPurchased"/>.</summary>
        public event Action<IapPurchase> Purchased;

        #endregion

        #region Buying

        /// <summary>As <see cref="IapSdk.PurchaseAsync"/>.</summary>
        public UniTask<IapResult> PurchaseAsync(IapProductId product, string placement = null) => IapSdk.PurchaseAsync(product, placement);

        /// <summary>As <see cref="IapSdk.RestoreAsync"/>.</summary>
        public UniTask<bool> RestoreAsync() => IapSdk.RestoreAsync();

        #endregion

        #region The game's answers

        /// <summary>
        /// Waits for what <see cref="IsGranted"/> and the grant read, the game's save above all: a purchase the store returns at launch arrives before the first scene.
        /// </summary>
        protected virtual UniTask WaitUntilCanGrantAsync() => UniTask.CompletedTask;

        /// <summary>The game's save holds this transaction, so its content was given.</summary>
        protected abstract bool IsGranted(string transactionId);

        /// <summary>
        /// Gives <see cref="IapOrder.Product"/>'s content and records <see cref="IapOrder.TransactionId"/> in the same save: a crash between two saves would grant twice or never.<br/>
        /// Throwing leaves the purchase unconfirmed for the next launch.
        /// </summary>
        protected abstract UniTask GrantAsync(IapOrder order);

        /// <summary>
        /// The store confirmed a consumable's transaction and never hands it again, so the save may drop its record.<br/>
        /// Never called for a non-consumable or a subscription: the store hands those back at every launch, and the record is what keeps them from being granted again.
        /// </summary>
        protected virtual UniTask ForgetAsync(string transactionId) => UniTask.CompletedTask;

        #endregion

        #region Hooks

        /// <summary>The pack made the manager, just before the first scene.</summary>
        protected virtual void OnAttached() { }

        /// <summary>The content is given and saved; the purchase is confirmed right after. Throwing is only logged.</summary>
        protected virtual void OnGranted(IapOrder order) { }

        /// <summary>As <see cref="IapSdk.Changed"/>.</summary>
        protected virtual void OnChanged() { }

        /// <summary>As <see cref="IapSdk.Purchased"/>.</summary>
        protected virtual void OnPurchased(in IapPurchase purchase) { }

        #endregion

        // The pack's events reach the hooks, then the game's own listeners, each alone.
        internal void Attach() {
            IapSdk.Changed += () => {
                Guard(OnChanged);
                IapSdk.Raise(Changed, handler => handler());
            };
            IapSdk.Purchased += purchase => {
                Guard(() => OnPurchased(purchase));
                IapSdk.Raise(Purchased, handler => handler(purchase));
            };

            Guard(OnAttached);
        }

        internal UniTask CallWaitUntilCanGrantAsync() => WaitUntilCanGrantAsync();
        internal bool CallIsGranted(string transactionId) => IsGranted(transactionId);
        internal UniTask CallGrantAsync(IapOrder order) => GrantAsync(order);
        internal void CallOnGranted(IapOrder order) => OnGranted(order);
        internal UniTask CallForgetAsync(string transactionId) => ForgetAsync(transactionId);

        private static void Guard(Action hook) {
            try {
                hook();
            } catch (Exception exception) {
                Debug.LogException(exception);
            }
        }
    }
}