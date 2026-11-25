using System.Linq;
using System.Reflection;
using RiseOn.Utils.Editor;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace RiseOn.Iap.Editor {
    /// <summary>
    /// A product of the Products list: its id and type on one line, written through the value entries, with undo; what the fake store returns for it is in the
    /// Fake Store section, and its number is for serialized fields only.
    /// </summary>
    internal sealed class IapEntryDrawer : OdinValueDrawer<IapSettings.Entry> {
        private static readonly GUIContent idLabel = new("Id");

        // The longest type name with the drop-down's arrow, so no type is cut.
        internal static float TypeWidth => typeof(IapProductType).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => field.GetCustomAttribute<InspectorNameAttribute>()?.displayName ?? ObjectNames.NicifyVariableName(field.Name))
            .Max(name => EditorStyles.popup.CalcSize(new GUIContent(name)).x) + 8;

        protected override void DrawPropertyLayout(GUIContent label) {
            var id   = Property.Children["id"];
            var type = Property.Children["type"];

            var line      = EditorGUILayout.GetControlRect();
            var typeRect  = new Rect(line.xMax - TypeWidth, line.y, TypeWidth, line.height);
            var labelRect = new Rect(line.x, line.y, EditorStyles.label.CalcSize(idLabel).x + 4, line.height);
            var idRect    = Rect.MinMaxRect(labelRect.xMax, line.y, typeRect.x - 4, line.yMax);

            var text = (id.ValueEntry.WeakSmartValue as string ?? "").Trim();
            idLabel.tooltip = text.Length == 0 ? "As in the Play Console" : $"As in the Play Console; {nameof(IapProductId)}.{IapProductsFile.Member(text)} in code";
            EditorGUI.LabelField(labelRect, idLabel);

            // Delayed: the list is read once a typed id is done.
            EditorGUI.BeginChangeCheck();
            var newId = EditorGUI.DelayedTextField(idRect, text);
            if (EditorGUI.EndChangeCheck()) id.ValueEntry.WeakSmartValue = newId.Trim();
            SettingsFocus.Drawn((Object) Property.Tree.WeakTargets[0], id.UnityPropertyPath, idRect);

            ChoiceFieldUtils.Enum(typeRect, type);
        }
    }
}