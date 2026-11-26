using Cysharp.Threading.Tasks;

namespace RiseOn.Iap {
    /// <summary>
    /// What gives a consumable its content: one on each consumable product of Project Settings > RiseOn > IAP, picked there by class with its fields, such as the number of gems a pack gives.<br/>
    /// Non-consumables and subscriptions have none: the store keeps what the account owns, which <see cref="IapSdk.Owns"/> and <see cref="IapSdk.OwnershipChanged"/> tell.<br/>
    /// Its member is for the pack alone: the class implements it explicitly, and no other code can call it, not even through this interface.<br/>
    /// Unity's thread only.
    /// </summary>
    public interface IIapGranter {
        /// <summary>
        /// Gives <paramref name="order"/>'s content and saves it before it returns: the pack then records the transaction as given and confirms it, so content still unsaved would be lost in a crash.<br/>
        /// Waits itself for what it reads, the game's save above all: a purchase left unconfirmed comes at any time after launch, maybe before the save has loaded.<br/>
        /// Throwing leaves the purchase unconfirmed for the next launch.
        /// </summary>
        protected internal UniTask GrantAsync(IapOrder order);
    }
}