using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RiseOn.Utils;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RiseOn.Iap {
    public enum IapProductType {
        Consumable
      , [InspectorName("Non-Consumable")] NonConsumable
      , Subscription
    }

    /// <summary>
    /// The project's store catalog, edited in Project Settings > RiseOn > IAP and kept in a Resources asset.
    /// </summary>
    internal sealed class IapSettings : ScriptableObject {
        public const string ResourceName = "RiseOnIapSettings";

        [Serializable]
        public struct Entry {
            [SerializeField]
            [Tooltip("The product's value in IapProductId: random, so products two branches add do not meet on one number, and kept, so serialized fields keep their product when others are renamed or removed.")]
            private int number;

            [SerializeField]
            private string id;

            [SerializeField]
            private IapProductType type;

            // What the pack's fake store returns for the product; the stores return their own.
            [SerializeField]
            private string title;

            [SerializeField]
            private string description;

            [SerializeField]
            private float price;

            public Entry(int number, string id, IapProductType type, float price) {
                this.number = number;
                this.id     = id;
                this.type   = type;
                this.price  = price;
                title       = "";
                description = "";
            }

            public int Number => number;
            public string Id => id?.Trim() ?? "";
            public IapProductType Type => type;

            /// <summary>The fake store's title; the id when empty.</summary>
            public string Title => string.IsNullOrWhiteSpace(title) ? Id : title.Trim();

            public string Description => description?.Trim() ?? "";

            /// <summary>The fake store's price in US dollars.</summary>
            public decimal Price => Math.Round((decimal) Mathf.Max(0, price), 2);
        }

        // The sections that stay open come before those that fold.
        [SerializeField, SectionGroup("Stores")]
        [Tooltip("Put before every product id on the App Store, where ids are unique across a team's apps. Empty when the App Store ids are the catalog's.")]
        private string applePrefix;

        [SerializeField, SectionGroup("Stores"), LabelText("Google Play Key")]
        [Tooltip("Play Console > Monetize with Play > Monetization setup > Licensing: the base64 public key that checks the signature of every purchase.")]
        private string googlePlayKey;

        // The Editor puts the project's one manager class here, so the page offers no type to pick; a project without one
        // has none.
        [SerializeReference, SectionReference, ShowIf("@manager != null"), LabelText("Manager")]
        [Tooltip("The project's one partial IIapManager class, taken by the pack: it grants every paid purchase. Its fields follow.")]
        private IIapManager manager;

        [SerializeField, SectionList(Item = "product", Foldable = true, New = "NewEntry")]
        [Tooltip("Ids as in the Play Console; on the App Store each gets the Apple prefix. Code names them as IapProductId members: no_ads is IapProductId.NoAds.")]
        private List<Entry> products = new();

        private const string FakeStoreTip = "Builds buy from the pack's fake store while ticked, for testing before the products exist on the stores: it returns each product's Title, Price and "
                                          + "Description below, and a sheet asks how each purchase ends. It charges nothing and checks no receipt. The Editor always buys from it, ticked or not.";

        [SerializeField, SectionGroup("Fake Store", Toggle = nameof(fakeStore), OpenWhenOff = true, Tooltip = FakeStoreTip)]
        private bool fakeStore;

        // A place for the products' fake fields, which the Editor draws from the products list itself, so edits land there. Not the list:
        // Odin draws a second member holding one list as a reference to the first. The empty setter keeps Odin from greying it out as read-only.
        [ShowInInspector, SectionGroup("Fake Store"), HideLabel, FakeStoreTable]
        private int FakeStoreProducts {
            get => products.Count;
            set { }
        }

        /// <summary>
        /// The game's manager with the fields the page edited, kept by the Editor as an instance of the game's one manager class; the pack runs it.
        /// </summary>
        public IIapManager Manager => manager;

        public IReadOnlyList<Entry> Products => products;

        /// <summary>Put before every id on the App Store, where product ids are unique across a team's apps.</summary>
        public string ApplePrefix => applePrefix?.Trim() ?? "";

        /// <summary>Google Play Console > Monetization setup > Licensing, base64.</summary>
        public string GooglePlayKey => googlePlayKey?.Trim() ?? "";

        /// <summary>Builds buy from the pack's fake store, which charges nothing; the Editor always does.</summary>
        public bool FakeStore => fakeStore;

        /// <summary>The id the running store knows the product by.</summary>
        public string StoreId(string id) => Application.platform is RuntimePlatform.IPhonePlayer ? ApplePrefix + id : id;

#if UNITY_EDITOR
        /// <summary>
        /// A random positive number no product has.<br/>
        /// Random, as Localization's DistributedUIDGenerator keeps key ids apart across machines, so products that two branches add do not meet on one number; 31 bits, as Unity serializes enums of 32 bits or smaller.
        /// </summary>
        internal int NewNumber() {
            while (true) {
                var number = BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0) & int.MaxValue;
                if (number > 0 && products.All(entry => entry.Number != number)) return number;
            }
        }

        /// <summary>The catalog itself, for the Editor's Revert.</summary>
        internal List<Entry> EditableProducts => products;

        // The product the page's + adds: numbered, where Odin's list would add a 0 or copy the last number.
        private Entry NewEntry() => new(NewNumber(), "", IapProductType.Consumable, 0.99f);
#endif

        /// <summary>Marks the member the Editor draws as the fake store's table of the products.</summary>
        [Conditional("UNITY_EDITOR"), AttributeUsage(AttributeTargets.Property)]
        internal sealed class FakeStoreTableAttribute : Attribute { }

        /// <summary>The project's asset, or an empty catalog when the project never saved one.</summary>
        public static IapSettings Load() {
            var asset = Resources.Load<IapSettings>(ResourceName);
            return asset ? asset : CreateInstance<IapSettings>();
        }
    }
}