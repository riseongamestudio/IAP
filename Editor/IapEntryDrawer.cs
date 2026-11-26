using RiseOn.Utils.Editor;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;

namespace RiseOn.Iap.Editor {
    /// <summary>
    /// A product of the Products list as a header line, the id in the label column and the type in the value column, whose arrow opens what goes with the product
    /// under it: a consumable's Granter as Odin draws it, and a Fake Store foldout with what the fake store returns for it. Every value keeps to the type's column.<br/>
    /// Written through the value entries, with undo; its number is for serialized fields only.
    /// </summary>
    internal sealed class IapEntryDrawer : OdinValueDrawer<IapSettings.Entry> {
        // The foldout's arrow, as a folding section's bar leaves for it.
        private const float Arrow = 14;

        private static readonly GUIContent granterLabel     = new("Granter", "The game's class that gives this consumable's content and saves it; its fields follow.");
        private static readonly GUIContent fakeStoreLabel   = new("Fake Store", "What the pack's fake store returns for the product; the stores return their own.");
        private static readonly GUIContent titleLabel       = new("Title", "The product's name in the shop; empty, the id");
        private static readonly GUIContent priceLabel       = new("Price", "In US dollars, shown as $0.99");
        private static readonly GUIContent descriptionLabel = new("Description");

        protected override void DrawPropertyLayout(GUIContent label) {
            var id      = Property.Children["id"];
            var type    = Property.Children["type"];
            var granter = Property.Children["granter"];
            var asset   = (Object) Property.Tree.WeakTargets[0];

            // The id as a section's title field: as wide as its text, at least as wide as most ids need so that the list's ids line up, and never closer than the inset
            // to the value column, even where the column is narrower than that least.
            const float least = 120;
            // Placed by the item rather than the layout, which moves it by a margin once the item opens.
            var line      = SectionRows.MarkFirstRow(EditorGUILayout.GetControlRect());
            var valueRect = Rect.MinMaxRect(line.x + EditorGUIUtility.labelWidth + Section.LabelGap, line.y, line.xMax, line.yMax);
            var text      = (id.ValueEntry.WeakSmartValue as string ?? "").Trim();
            var arrowRect = new Rect(line.x, line.y, Arrow, line.height);
            var idWidth   = Mathf.Max(0, Mathf.Min(Mathf.Max(EditorStyles.textField.CalcSize(new GUIContent(text)).x + Section.Inset, least), valueRect.x - Section.Inset - arrowRect.xMax));
            var idRect    = new Rect(arrowRect.xMax, line.y, idWidth, line.height);

            // Open while the Edit button of an issue on the page waits to reach its field, which may be under the line.
            var open = Property.State.Expanded || SettingsFocus.Asset == asset;
            if (EditorGUI.Foldout(arrowRect, open, GUIContent.none, true) != open) Property.State.Expanded = !open;

            // The label column's place past the id folds too, as a title does.
            var rest = Rect.MinMaxRect(idRect.xMax, line.y, valueRect.x, line.yMax);
            var e    = Event.current;
            if (e.type is EventType.MouseDown && e.button == 0 && rest.Contains(e.mousePosition)) {
                Property.State.Expanded = !open;
                e.Use();
            }

            // Delayed: the list is read once a typed id is done.
            EditorGUI.BeginChangeCheck();
            var newId = EditorGUI.DelayedTextField(idRect, text);
            if (EditorGUI.EndChangeCheck()) id.ValueEntry.WeakSmartValue = newId.Trim();
            GUI.Label(idRect, new GUIContent("", text.Length == 0 ? "The id, as in the Play Console" : $"The id, as in the Play Console; {nameof(IapProductId)}.{IapProductsFile.Member(text)} in code"));
            SettingsFocus.Drawn(asset, id.UnityPropertyPath, idRect);

            ChoiceFieldUtils.Enum(valueRect, type);

            // Odin's fade, as the Granter's own foldout and the page's sections open.
            // In by the arrow alone, so their arrows sit under the id: the drag handle already moves the whole row in.
            if (SirenixEditorGUI.BeginFadeGroup(Property, open)) {
                GUILayout.BeginHorizontal();
                GUILayout.Space(Arrow);
                GUILayout.BeginVertical();
                GUIHelper.PushLabelWidth(EditorGUIUtility.labelWidth - Arrow);
                // Other types keep a granter picked before, which the pack ignores, so a type changed back finds it again.
                if (type.ValueEntry.WeakSmartValue is IapProductType.Consumable) granter.Draw(granterLabel);

                FakeStore();
                GUIHelper.PopLabelWidth();
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }

            SirenixEditorGUI.EndFadeGroup();
        }

        // A foldout, its fold kept on the title as the row's own is on the product; its fields one level in, under its text.
        private void FakeStore() {
            var title = Property.Children["title"];
            var fold  = Line(out _);
            title.State.Expanded = EditorGUI.Foldout(Rect.MinMaxRect(fold.x, fold.y, fold.x + EditorGUIUtility.labelWidth, fold.yMax), title.State.Expanded, fakeStoreLabel, true);
            if (SirenixEditorGUI.BeginFadeGroup(title, title.State.Expanded)) {
                Text(title, titleLabel);
                Price(Property.Children["price"], priceLabel);
                Text(Property.Children["description"], descriptionLabel);
            }

            SirenixEditorGUI.EndFadeGroup();
        }

        // Delayed, as the id.
        private static void Text(InspectorProperty field, GUIContent label) {
            Line(out var rect, Section.Indent, label);
            EditorGUI.BeginChangeCheck();
            var value = EditorGUI.DelayedTextField(rect, field.ValueEntry.WeakSmartValue as string ?? "");
            if (EditorGUI.EndChangeCheck()) field.ValueEntry.WeakSmartValue = value;
        }

        // At 0 or above, to the cent.
        private static void Price(InspectorProperty field, GUIContent label) {
            Line(out var rect, Section.Indent, label);
            EditorGUI.BeginChangeCheck();
            var value = EditorGUI.DelayedFloatField(rect, field.ValueEntry.WeakSmartValue is float price ? price : 0);
            if (EditorGUI.EndChangeCheck()) field.ValueEntry.WeakSmartValue = (float) System.Math.Round(Mathf.Max(0, value), 2);
        }

        // A line: its label from the indent, and its value where a row's own label would end, in line with the page's values.
        private static Rect Line(out Rect value, float indent = 0, GUIContent label = null) {
            var line   = EditorGUILayout.GetControlRect();
            var valueX = line.x + EditorGUIUtility.labelWidth + Section.LabelGap;
            value = Rect.MinMaxRect(valueX, line.y, line.xMax, line.yMax);
            if (label != null) GUI.Label(Rect.MinMaxRect(line.x + indent, line.y, valueX, line.yMax), label, Section.CutLabel);
            return line;
        }
    }
}