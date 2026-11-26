using System.Collections.Generic;
using RiseOn.Utils.Editor;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace RiseOn.Iap.Editor {
    /// <summary>
    /// The Fake Store section's products: a foldout per product of the Products list, its id on the bar, with the title, the price and the description the
    /// fake store returns under it. Written through the products list's own value entries, so edits land in the catalog, with undo.
    /// </summary>
    internal sealed class IapFakeStoreTableDrawer : OdinAttributeDrawer<IapSettings.FakeStoreTableAttribute> {
        private static readonly GUIContent titleLabel       = new("Title", "The product's name in the shop; empty, the id");
        private static readonly GUIContent priceLabel       = new("Price", "In US dollars, shown as $0.99");
        private static readonly GUIContent descriptionLabel = new("Description");
        private static readonly GUIContent noProduct        = new("No product yet: add one in the Products section.");

        // The products whose fold this drawer gave back, so each opens as it was left this session.
        private readonly HashSet<string> restored = new();

        protected override void DrawPropertyLayout(GUIContent label) {
            var entries = Property.Tree.RootProperty.Children["products"]?.Children;
            if (entries == null || entries.Count == 0) {
                EditorGUI.LabelField(EditorGUILayout.GetControlRect(), noProduct, EditorStyles.miniLabel);
                return;
            }

            foreach (var entry in entries) {
                if (restored.Add(entry.Path)) Section.Restore(entry);

                var id = (entry.Children["id"].ValueEntry.WeakSmartValue as string ?? "").Trim();
                using var section = Section.Foldout(entry, id.Length > 0 ? id : "(no id)");
                if (!section.Open) continue;

                Text(entry.Children["title"], titleLabel);
                Price(entry.Children["price"], priceLabel);
                Text(entry.Children["description"], descriptionLabel);
            }
        }

        // Delayed, as the Products section's fields.
        private static void Text(InspectorProperty field, GUIContent label) {
            var rect = EditorGUI.PrefixLabel(EditorGUILayout.GetControlRect(), label);
            EditorGUI.BeginChangeCheck();
            var value = EditorGUI.DelayedTextField(rect, field.ValueEntry.WeakSmartValue as string ?? "");
            if (EditorGUI.EndChangeCheck()) field.ValueEntry.WeakSmartValue = value;
        }

        // At 0 or above, to the cent.
        private static void Price(InspectorProperty field, GUIContent label) {
            var rect = EditorGUI.PrefixLabel(EditorGUILayout.GetControlRect(), label);
            EditorGUI.BeginChangeCheck();
            var value = EditorGUI.DelayedFloatField(rect, field.ValueEntry.WeakSmartValue is float price ? price : 0);
            if (EditorGUI.EndChangeCheck()) field.ValueEntry.WeakSmartValue = (float) System.Math.Round(Mathf.Max(0, value), 2);
        }
    }
}