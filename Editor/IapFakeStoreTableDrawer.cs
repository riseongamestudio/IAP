using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace RiseOn.Iap.Editor {
    /// <summary>
    /// The Fake Store section's table: a row per product of the Products list, its id read-only, then the title and the price the fake store returns, and the
    /// description on the line below. Written through the products list's own value entries, so edits land in the catalog, with undo.
    /// </summary>
    internal sealed class IapFakeStoreTableDrawer : OdinAttributeDrawer<IapSettings.FakeStoreTableAttribute> {
        private const float Gap = 4;

        private static readonly GUIContent idHeader          = new("Id", "The product, as in the Products section");
        private static readonly GUIContent titleHeader       = new("Title", "Empty, the id");
        private static readonly GUIContent priceHeader       = new("Price", "In US dollars");
        private static readonly GUIContent descriptionLabel  = new("Description");
        private static readonly GUIContent noProduct         = new("No product yet: add one in the Products section.");

        protected override void DrawPropertyLayout(GUIContent label) {
            var products = Property.Tree.RootProperty.Children["products"];
            var entries  = products?.Children;
            if (entries == null || entries.Count == 0) {
                EditorGUI.LabelField(EditorGUILayout.GetControlRect(), noProduct, EditorStyles.miniLabel);
                return;
            }

            // The id's column fits the longest id, the description's label included; the price's, the type's column of the Products section.
            var idWidth = Mathf.Max(EditorStyles.label.CalcSize(descriptionLabel).x, EditorStyles.label.CalcSize(idHeader).x);
            foreach (var entry in entries) idWidth = Mathf.Max(idWidth, EditorStyles.label.CalcSize(new GUIContent(Id(entry))).x);
            idWidth += Gap;
            var priceWidth = IapEntryDrawer.TypeWidth;

            var header = EditorGUILayout.GetControlRect();
            var (idColumn, titleColumn, priceColumn) = Columns(header, idWidth, priceWidth);
            EditorGUI.LabelField(idColumn, idHeader, EditorStyles.miniBoldLabel);
            EditorGUI.LabelField(titleColumn, titleHeader, EditorStyles.miniBoldLabel);
            EditorGUI.LabelField(priceColumn, priceHeader, EditorStyles.miniBoldLabel);

            for (var i = 0; i < entries.Count; i++) {
                var entry = entries[i];
                if (i > 0) GUILayout.Space(EditorGUIUtility.standardVerticalSpacing * 2);

                var line = EditorGUILayout.GetControlRect();
                (idColumn, titleColumn, priceColumn) = Columns(line, idWidth, priceWidth);
                var id = Id(entry);
                using (new EditorGUI.DisabledScope(true)) EditorGUI.LabelField(idColumn, id.Length > 0 ? id : "(no id)");
                Text(titleColumn, entry.Children["title"]);
                Price(priceColumn, entry.Children["price"]);

                line = EditorGUILayout.GetControlRect();
                EditorGUI.LabelField(new Rect(line.x, line.y, idWidth, line.height), descriptionLabel);
                Text(Rect.MinMaxRect(line.x + idWidth, line.y, line.xMax, line.yMax), entry.Children["description"]);
            }
        }

        private static (Rect Id, Rect Title, Rect Price) Columns(Rect line, float idWidth, float priceWidth) {
            var id    = new Rect(line.x, line.y, idWidth, line.height);
            var price = new Rect(line.xMax - priceWidth, line.y, priceWidth, line.height);
            return (id, Rect.MinMaxRect(id.xMax, line.y, price.x - Gap, line.yMax), price);
        }

        private static string Id(InspectorProperty entry) => (entry.Children["id"].ValueEntry.WeakSmartValue as string ?? "").Trim();

        // Delayed, as the Products section's fields.
        private static void Text(Rect rect, InspectorProperty field) {
            EditorGUI.BeginChangeCheck();
            var value = EditorGUI.DelayedTextField(rect, field.ValueEntry.WeakSmartValue as string ?? "");
            if (EditorGUI.EndChangeCheck()) field.ValueEntry.WeakSmartValue = value;
        }

        // At 0 or above, to the cent.
        private static void Price(Rect rect, InspectorProperty field) {
            EditorGUI.BeginChangeCheck();
            var value = EditorGUI.DelayedFloatField(rect, field.ValueEntry.WeakSmartValue is float price ? price : 0);
            if (EditorGUI.EndChangeCheck()) field.ValueEntry.WeakSmartValue = (float) System.Math.Round(Mathf.Max(0, value), 2);
        }
    }
}