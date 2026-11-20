using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RiseOn.Utils.Editor;
using UnityEditor;
using UnityEngine;

namespace RiseOn.Iap.Editor {
    /// <summary>
    /// The products as the pack's source generator reads them, one "number Member" line each; it compiles them into RiseOn.Iap as <see cref="IapProductId"/>.<br/>
    /// Written when Apply is pressed or chosen as the settings page closes, and when the asset is imported from elsewhere (a pull, a merge) while the page is closed; never by hand.<br/>
    /// A write renames the members the project's scripts name too, so they keep compiling.
    /// </summary>
    internal sealed class IapProductsFile : AssetPostprocessor {
        // Unity hands a project's *.{generator assembly}.additionalfile to that generator.
        internal const string Path = "Assets/RiseOn/IAP/Products.RiseOnIapGenerator.additionalfile";

        /// <summary>While the settings page draws: it saves the asset on every edit, and writes the file only when Apply is pressed or chosen as it closes.</summary>
        internal static bool held;

        // The products as they were the last time the page saw them match the file, for Revert. In Library, as only this machine kept them.
        private const string AppliedPath = "Library/RiseOn/IapProductsApplied.json";

        private static string  appliedJson;
        private static Applied applied;

        [Serializable]
        private sealed class Applied {
            public string                  file;
            public List<IapSettings.Entry> products;
        }

        /// <summary>
        /// Products that make the same member, one id listed twice or ids that differ only in case or separators, or that share a number, each with the id to edit: the later one's.
        /// </summary>
        internal static IEnumerable<SettingsIssue> Issues(IapSettings settings) {
            var products = settings.Products.Select((entry, index) => (entry, index)).ToList();
            foreach (var group in products.Where(product => product.entry.Id.Length > 0).GroupBy(product => Member(product.entry.Id)).Where(group => group.Count() > 1)) {
                var ids = group.Select(product => product.entry.Id).Distinct().ToList();
                yield return Issue(settings, ids.Count == 1 ? $"Product {ids[0]} is listed more than once." : $"Products {string.Join(", ", ids)} all become {nameof(IapProductId)}.{group.Key}; rename one.", group.Last().index);
            }

            // NewNumber never repeats a number of the list it adds to, so only two branches meet here, under one in a
            // hundred thousand for 200 products. Each number may be saved in enum fields on its branch, so only a
            // person can tell which product keeps it.
            foreach (var group in products.Where(product => product.entry.Number > 0).GroupBy(product => product.entry.Number).Where(group => group.Count() > 1)) {
                yield return Issue(settings, $"Products {string.Join(", ", group.Select(product => product.entry.Id))} share the number {group.Key}. Remove the one serialized enum fields do not mean and add it again for a new number.", group.Last().index);
            }
        }

        private static SettingsIssue Issue(IapSettings settings, string message, int index) => new(BuildTarget.NoTarget, true, message, fieldAsset: settings, fieldPath: $"products.Array.data[{index}].id");

        /// <summary>The file says what the products say.</summary>
        internal static bool IsCurrent(IapSettings settings) => FileText() == Text(settings);

        /// <summary>The asset's products are not the file's, and nothing stops the file from being written.</summary>
        internal static bool IsPending(IapSettings settings) => !Issues(settings).Any() && !IsCurrent(settings);

        /// <summary>The members whose name changed since the file was written, by number, as code names them.</summary>
        internal static List<(string From, string To)> Renames(IapSettings settings) {
            var written = new Dictionary<int, string>();
            foreach (var line in FileText().Split('\n')) {
                var parts = line.Split(' ');
                if (parts.Length == 2 && int.TryParse(parts[0], out var number)) written[number] = parts[1].Trim();
            }

            return settings.Products.Where(entry => entry.Id.Length > 0 && written.TryGetValue(entry.Number, out var member) && member != Member(entry.Id))
                           .Select(entry => (written[entry.Number], Member(entry.Id))).ToList();
        }

        /// <summary>
        /// Numbers the products, then writes the file when its text would change and nothing stops it, renaming the members the project's scripts name first; Unity recompiles after.
        /// </summary>
        internal static void Sync(IapSettings settings) {
            Number(settings);
            if (!IsPending(settings)) return;

            var renamed = CodeRename.Members(nameof(IapProductId), Renames(settings));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, Text(settings), new UTF8Encoding(false));
            Remember(settings);
            // One refresh, so the renamed scripts and the new members compile together.
            AssetDatabase.Refresh();
            if (renamed.Count > 0) Debug.Log($"IAP: renamed products in {string.Join(", ", renamed)}");
        }

        /// <summary>Keeps the products while the file matches them, so <see cref="Revert"/> can come back to them; the page calls it as it draws.</summary>
        internal static void Remember(IapSettings settings) {
            var json = JsonUtility.ToJson(new Applied { file = Text(settings), products = settings.EditableProducts });
            if (json == LoadAppliedJson()) return;

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(AppliedPath)!);
            File.WriteAllText(AppliedPath, json, new UTF8Encoding(false));
            appliedJson = json;
            applied     = null;
        }

        /// <summary>Why <see cref="Revert"/> cannot run, as a button's tooltip; null when it can.</summary>
        internal static string RevertProblem() {
            var last = LoadApplied();
            if (last == null) return "Revert is off.\nThis machine kept no products from the last time they matched the products file.";
            if (last.file != FileText()) return "Revert is off.\nThe products file changed elsewhere (a pull, a merge) after this machine last wrote it.";
            return null;
        }

        /// <summary>Takes the products back to when the file last matched them, with undo; check <see cref="RevertProblem"/> first.</summary>
        internal static void Revert(IapSettings settings) {
            var last = LoadApplied();
            Undo.RecordObject(settings, "Revert IAP Products");
            settings.EditableProducts.Clear();
            settings.EditableProducts.AddRange(last.products);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
        }

        private static string FileText() {
            return File.Exists(Path) ? File.ReadAllText(Path) : "";
        }

        private static string LoadAppliedJson() {
            appliedJson ??= File.Exists(AppliedPath) ? File.ReadAllText(AppliedPath) : "";
            return appliedJson;
        }

        private static Applied LoadApplied() {
            var json = LoadAppliedJson();
            if (json.Length == 0) return null;

            try {
                applied ??= JsonUtility.FromJson<Applied>(json);
            } catch (ArgumentException) {
                // A file cut short reads as none.
                return null;
            }

            return applied;
        }

        /// <summary>Snake case and dots to PascalCase: <c>no_ads</c> becomes <c>NoAds</c>.</summary>
        internal static string Member(string id) {
            var builder = new StringBuilder();
            var upper   = true;
            foreach (var c in id) {
                if (!char.IsLetterOrDigit(c)) {
                    upper = true;
                    continue;
                }

                builder.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }

            if (builder.Length == 0 || char.IsDigit(builder[0])) builder.Insert(0, '_');
            return builder.ToString();
        }

        /// <summary>Numbers the products a hand edit left without one; the page numbers the ones its + button adds.</summary>
        private static void Number(IapSettings settings) {
            var asset    = new SerializedObject(settings);
            var products = asset.FindProperty("products");
            for (var i = 0; i < products.arraySize; ++i) {
                var number = products.GetArrayElementAtIndex(i).FindPropertyRelative("number");
                if (number.intValue > 0) continue;

                number.intValue = settings.NewNumber();
                asset.ApplyModifiedPropertiesWithoutUndo();
            }

            AssetDatabase.SaveAssetIfDirty(settings);
        }

        private static string Text(IapSettings settings) {
            var text = new StringBuilder();
            foreach (var entry in settings.Products.Where(entry => entry.Id.Length > 0).OrderBy(entry => entry.Number)) {
                text.Append($"{entry.Number} {Member(entry.Id)}\n");
            }

            return text.ToString();
        }

        // A pull or a merge changed the settings, or someone deleted the file.
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom) {
            if (held) return;
            if (!imported.Any(path => System.IO.Path.GetFileNameWithoutExtension(path) is IapSettings.ResourceName) && !deleted.Contains(Path)) return;

            var settings = IapSettingsProvider.Find();
            if (settings) Sync(settings);
        }
    }
}