using System.Linq;
using System.Reflection;
using RiseOn.Utils.Editor;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace RiseOn.Iap.Editor {
    /// <summary>
    /// A product: its id and type, folding what the fake store returns for it under them, written through the value entries, with undo; its number is for serialized fields only.
    /// </summary>
    internal sealed class IapEntryDrawer : OdinValueDrawer<IapSettings.Entry> {
        private const string Fake = "What the fake store returns, in the Editor or with Fake Store on; the stores return their own.";

        private static readonly GUIContent idLabel          = new("Id");
        private static readonly GUIContent titleLabel       = new("Title", Fake + " Empty, the id.");
        private static readonly GUIContent descriptionLabel = new("Description", Fake);
        private static readonly GUIContent priceLabel       = new("Price", Fake + " In US dollars.");

        // The labels' column, so the fields of the three lines start together: the id's label after the fold arrow, the others
        // indented under it, as Unity indents what a foldout opens.
        private static float LabelWidth => Section.Indent + new[] { idLabel, titleLabel, descriptionLabel }.Max(content => EditorStyles.label.CalcSize(content).x) + 4;

        // The longest type name with the drop-down's arrow, so no type is cut.
        private static float TypeWidth => typeof(IapProductType).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => field.GetCustomAttribute<InspectorNameAttribute>()?.displayName ?? ObjectNames.NicifyVariableName(field.Name))
            .Max(name => EditorStyles.popup.CalcSize(new GUIContent(name)).x) + 8;

        protected override void DrawPropertyLayout(GUIContent label) {
            var id   = Property.Children["id"];
            var type = Property.Children["type"];

            var line      = EditorGUILayout.GetControlRect();
            var typeRect  = new Rect(line.xMax - TypeWidth, line.y, TypeWidth, line.height);
            var labelRect = new Rect(line.x, line.y, LabelWidth, line.height);
            var idRect    = Rect.MinMaxRect(labelRect.xMax, line.y, typeRect.x - 4, line.yMax);

            var text = (id.ValueEntry.WeakSmartValue as string ?? "").Trim();
            idLabel.tooltip = text.Length == 0 ? "As in the Play Console" : $"As in the Play Console; {nameof(IapProductId)}.{IapProductsFile.Member(text)} in code";
            Property.State.Expanded = EditorGUI.Foldout(labelRect, Property.State.Expanded, idLabel, true);

            // Delayed: the list is read once a typed id is done.
            EditorGUI.BeginChangeCheck();
            var newId = EditorGUI.DelayedTextField(idRect, text);
            if (EditorGUI.EndChangeCheck()) id.ValueEntry.WeakSmartValue = newId.Trim();
            SettingsFocus.Drawn((Object) Property.Tree.WeakTargets[0], id.UnityPropertyPath, idRect);

            ChoiceFieldUtils.Enum(typeRect, type);

            // With the fade the page's sections use.
            if (Section.BeginFade(Property, Property.State.Expanded)) DrawFake();
            Section.EndFade();
        }

        private void DrawFake() {
            // The price under the type, its label in the type's column.
            var line = EditorGUILayout.GetControlRect();
            var priceLabelRect = new Rect(line.xMax - TypeWidth, line.y, EditorStyles.label.CalcSize(priceLabel).x + 4, line.height);
            var priceRect      = Rect.MinMaxRect(priceLabelRect.xMax, line.y, line.xMax, line.yMax);
            Text(line, Property.Children["title"], titleLabel, line.xMax - TypeWidth - 4);
            EditorGUI.LabelField(priceLabelRect, priceLabel);

            var price = Property.Children["price"];
            EditorGUI.BeginChangeCheck();
            var newPrice = EditorGUI.DelayedFloatField(priceRect, price.ValueEntry.WeakSmartValue is float value ? value : 0);
            if (EditorGUI.EndChangeCheck()) price.ValueEntry.WeakSmartValue = (float) System.Math.Round(Mathf.Max(0, newPrice), 2);

            line = EditorGUILayout.GetControlRect();
            Text(line, Property.Children["description"], descriptionLabel, line.xMax);

            // A label in the labels' column, then a delayed text field up to right.
            static void Text(Rect line, InspectorProperty field, GUIContent label, float right) {
                var labelRect = Rect.MinMaxRect(line.x + Section.Indent, line.y, line.x + LabelWidth, line.yMax);
                EditorGUI.LabelField(labelRect, label);

                EditorGUI.BeginChangeCheck();
                var value = EditorGUI.DelayedTextField(Rect.MinMaxRect(labelRect.xMax, line.y, right, line.yMax), field.ValueEntry.WeakSmartValue as string ?? "");
                if (EditorGUI.EndChangeCheck()) field.ValueEntry.WeakSmartValue = value;
            }
        }
    }
}