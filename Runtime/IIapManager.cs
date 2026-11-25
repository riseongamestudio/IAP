using Cysharp.Threading.Tasks;

namespace RiseOn.Iap {
    /// <summary>
    /// The game's IAP manager: the one partial class implementing this, which grants every paid purchase; Project Settings > RiseOn > IAP edits its fields.<br/>
    /// The pack makes it just before the first scene; a second class implementing this does not compile.<br/>
    /// The pack's source generator seals it and adds <c>Ins</c>, the running instance.<br/>
    /// Its members are for the pack alone: the class implements them explicitly, and no other code can call them, not even through this interface.<br/>
    /// The pack runs every paid purchase the same way: it waits for <see cref="WaitUntilCanGrantAsync"/>, skips a transaction <see cref="IsGranted"/> knows, calls <see cref="GrantAsync"/>, raises <see cref="IapSdk.Granted"/>, confirms, and after the store's confirmation of a consumable calls <see cref="ForgetAsync"/>.<br/>
    /// A non-consumable the store returns as confirmed already, as Google Play does after a reinstall, goes the same way up to <see cref="IapSdk.Granted"/>, with nothing to confirm.<br/>
    /// Unity's thread only.
    /// </summary>
    public interface IIapManager {
        /// <summary>
        /// Waits for what <see cref="IsGranted"/> and the grant read, the game's save above all: a purchase the store returns at launch arrives before the first scene.
        /// </summary>
        protected internal UniTask WaitUntilCanGrantAsync();

        /// <summary>The game's save holds this transaction, so its content was given.</summary>
        protected internal bool IsGranted(string transactionId);

        /// <summary>
        /// Gives <see cref="IapOrder.Product"/>'s content and records <see cref="IapOrder.TransactionId"/> in the same save: a crash between two saves would grant twice or never.<br/>
        /// Throwing leaves the purchase unconfirmed for the next launch.
        /// </summary>
        protected internal UniTask GrantAsync(IapOrder order);

        /// <summary>
        /// The store confirmed a consumable's transaction and never hands it again, so the save may drop its record.<br/>
        /// Never called for a non-consumable or a subscription: the store hands those back at every launch, and the record is what keeps them from being granted again.
        /// </summary>
        protected internal UniTask ForgetAsync(string transactionId);
    }
}