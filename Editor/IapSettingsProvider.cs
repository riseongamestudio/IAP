using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RiseOn.Utils.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEngine;

namespace RiseOn.Iap.Editor {
    /// <summary>
    /// Project Settings > RiseOn > IAP: what blocks the build, then the catalog with each consumable's granter, and the stores' keys.<br/>
    /// Edits go straight to the asset, with undo; the products file waits for Apply or for the page to close, since writing it recompiles.<br/>
    /// The settings asset's Inspector only opens this page.
    /// </summary>
    internal static class IapSettingsProvider {
        internal const string PagePath = "Project/RiseOn/IAP";

        // Google Play Console's rule; the App Store also takes upper case and hyphens, so Google's is the one to keep.
        private static readonly Regex productIdPattern   = new("^[a-z0-9][a-z0-9_.]{0,39}$");
        private static readonly Regex applePrefixPattern = new("^[A-Za-z0-9_.-]*$");
        private const int AppleIdLimit = 100;

        // A recompile closes the page as the domain unloads: a write then would land after the compile and wait,
        // unseen, for the next one. The page comes back after the reload and still says the file waits.
        private static bool reloading;

        private static readonly SettingsTree tree = new();

        [InitializeOnLoadMethod]
        private static void WatchReload() {
            AssemblyReloadEvents.beforeAssemblyReload += () => reloading = true;
        }

        [MenuItem("RiseOn/IAP")]
        internal static void Open() {
            SettingsService.OpenProjectSettings(PagePath);
        }

        [SettingsProvider]
        private static SettingsProvider Create() {
            // Unity deactivates the page it rebuilds after a reload before it draws; only a drawn page has edits to write.
            var drawn = false;

            return new SettingsProvider(PagePath, SettingsScope.Project) {
                guiHandler = _ => {
                    using var page = SettingsPage.Begin();
                    IapProductsFile.held = true;
                    drawn = true;
                    Draw(LoadOrCreate());
                }
              , deactivateHandler = () => {
                    // Leaving or closing the window asks once, for every edit made here: one write and one recompile, or none.
                    IapProductsFile.held = false;
                    var settings = Find();
                    if (settings && drawn && !reloading && IapProductsFile.IsPending(settings)) AskOnLeave(settings);
                    drawn = false;
                    tree.Dispose();
                }
              , keywords = new[] { "iap", "purchase", "product", "store", "google play", "app store" }
            };
        }

        /// <summary>The whole page for one settings asset.</summary>
        internal static void Draw(IapSettings settings) {
            SettingsIssue.Draw(Issues(settings, true));
            DrawApply(settings);

            tree.Draw(settings);
        }

        // The page's edits until they are applied or reverted; otherwise Unity stopping before the write, or a file edited by hand.
        private static void DrawApply(IapSettings settings) {
            if (IapProductsFile.IsCurrent(settings)) {
                IapProductsFile.Remember(settings);
                return;
            }

            if (!IapProductsFile.IsPending(settings)) return;

            var renames = IapProductsFile.Renames(settings);
            var renamed = renames.Count == 0 ? "" : $" Apply also renames {string.Join(", ", renames.Select(rename => $"{nameof(IapProductId)}.{rename.From} to {rename.To}"))} in the project's scripts.";
            var revert  = IapProductsFile.RevertProblem();
            var picked  = HelpBoxUtils.Draw($"{IapProductsFile.Path} does not list the products yet, so {nameof(IapProductId)} is out of date and builds stop. Apply writes it; Revert takes the products back to the last time they matched it.{renamed}", MessageType.Warning,
                // Revert then Apply, as Unity's import settings order them.
                (new GUIContent("Revert", revert ?? "Takes the products back to the last time they matched the products file"), revert == null), (new GUIContent("Apply", $"Writes the products file; Unity recompiles {nameof(IapProductId)}"), true));
            if (picked == 1) IapProductsFile.Sync(settings);
            if (picked != 0) return;

            IapProductsFile.Revert(settings);
            GUIUtility.ExitGUI();
        }

        // Discard needs what the file last matched; without it the choice is to apply now or leave the file waiting, with builds stopped.
        private static void AskOnLeave(IapSettings settings) {
            var message = $"The products changed since {nameof(IapProductId)} was last written.";
            var revert  = IapProductsFile.RevertProblem();
            if (revert == null) {
                if (EditorUtility.DisplayDialog("IAP", message, "Apply", "Discard")) IapProductsFile.Sync(settings);
                else IapProductsFile.Revert(settings);
            } else if (EditorUtility.DisplayDialog("IAP", $"{message} They cannot be discarded: {revert.Substring(revert.IndexOf('\n') + 1)}", "Apply", "Later")) {
                IapProductsFile.Sync(settings);
            }
        }

        /// <summary>Everything that would leave purchases unpaid, ungranted or unconfirmed, for the page and the build.</summary>
        /// <param name="page">For the settings page, which also tells what only the Editor meets.</param>
        internal static IEnumerable<SettingsIssue> Issues(IapSettings settings, bool page = false) {
            if (SettingsAssetUtils.OutsideResources<IapSettings>(IapSettings.ResourceName) is { } path) {
                yield return new SettingsIssue(BuildTarget.NoTarget, true, $"{path} is outside a Resources folder, so the game cannot load it and sells nothing.", "Move it", () => SettingsAssetUtils.MoveToResources<IapSettings>(IapSettings.ResourceName));
            }

            var ids = settings.Products.Select(entry => entry.Id).ToList();
            foreach (var id in ids.Distinct()) {
                // The first product with the id, for Edit.
                var field = $"products.Array.data[{ids.IndexOf(id)}].id";
                if (id.Length == 0) {
                    yield return new SettingsIssue(BuildTarget.NoTarget, true, "A product has no id.", fieldAsset: settings, fieldPath: field);
                } else if (!productIdPattern.IsMatch(id) || id.StartsWith("android.test")) {
                    yield return new SettingsIssue(BuildTarget.NoTarget, true, $"Product {id}: Google Play takes 1 to 40 lower-case letters, digits, '_' and '.', starting with a letter or digit, and reserves android.test.",
                                                   fieldAsset: settings, fieldPath: field);
                } else if (settings.ApplePrefix.Length + id.Length > AppleIdLimit) {
                    yield return new SettingsIssue(BuildTarget.iOS, true, $"Product {settings.ApplePrefix}{id} is over the App Store's {AppleIdLimit} characters.", fieldAsset: settings, fieldPath: field);
                }
            }

            for (var i = 0; i < settings.Products.Count; i++) {
                var entry = settings.Products[i];
                if (entry.Type is not IapProductType.Consumable) continue;

                var field = $"products.Array.data[{i}].granter";
                var name  = entry.Id.Length > 0 ? entry.Id : "A consumable";
                if (entry.Granter == null) {
                    yield return new SettingsIssue(BuildTarget.NoTarget, true, $"{name} has no Granter, so its purchases would never be given nor confirmed, and Google Play refunds them after three days.", fieldAsset: settings, fieldPath: field);
                } else if (!InBuilds(entry.Granter.GetType())) {
                    yield return new SettingsIssue(BuildTarget.NoTarget, true, $"{name}'s Granter, {entry.Granter.GetType().Name}, is in {entry.Granter.GetType().Assembly.GetName().Name}, an Editor-only assembly: Play mode runs it, builds do not have it; move it to a runtime assembly.",
                                                   fieldAsset: settings, fieldPath: field);
                }
            }

            foreach (var issue in IapProductsFile.Issues(settings)) yield return issue;

            // The page shows its own Apply and Revert for it.
            if (!page && !IapProductsFile.IsCurrent(settings)) {
                yield return new SettingsIssue(BuildTarget.NoTarget, true, $"{IapProductsFile.Path} does not list the products, so {nameof(IapProductId)} is out of date: press Apply on the page.");
            }

            if (!applePrefixPattern.IsMatch(settings.ApplePrefix)) {
                yield return new SettingsIssue(BuildTarget.iOS, true, "The Apple prefix takes letters, digits, '_', '.' and '-', as App Store product ids do.", fieldAsset: settings, fieldPath: "applePrefix");
            }

            if (settings.FakeStore) {
                yield return new SettingsIssue(BuildTarget.NoTarget, false, "Fake Store is on: builds charge nothing and check no receipt. Turn it off before shipping.", "Turn off", () => TurnOffFakeStore(settings));
            } else if (settings.Products.Count > 0) {
                if (settings.GooglePlayKey.Length == 0) {
                    yield return new SettingsIssue(BuildTarget.Android, true, "The Google Play key is empty, so no purchase could be checked.", fieldAsset: settings, fieldPath: "googlePlayKey");
                } else if (!IsPublicKey(settings.GooglePlayKey)) {
                    yield return new SettingsIssue(BuildTarget.Android, true, "The Google Play key is not the base64 public key from Play Console > Monetization setup > Licensing.",
                                                   fieldAsset: settings, fieldPath: "googlePlayKey");
                }
            }
        }

        // TypeCache sees Editor assemblies too, and Play mode loads them, so only a build would miss the class.
        private static bool InBuilds(Type type) {
            playerAssemblies ??= new HashSet<string>(CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies).Select(assembly => assembly.name));
            return playerAssemblies.Contains(type.Assembly.GetName().Name);
        }

        // GetAssemblies takes some 150 ms and the page asks on every GUI event; a recompile reloads the domain, which
        // clears this with the assemblies it lists.
        private static HashSet<string> playerAssemblies;

        // Through a SerializedObject, so an undo brings it back.
        private static void TurnOffFakeStore(IapSettings settings) {
            var serialized = new SerializedObject(settings);
            serialized.FindProperty("fakeStore").boolValue = false;
            serialized.ApplyModifiedProperties();
        }

        // The Play Console's key is a base64 DER SubjectPublicKeyInfo, which starts with an ASN.1 sequence.
        private static bool IsPublicKey(string key) {
            try {
                var bytes = Convert.FromBase64String(key);
                return bytes.Length > 0 && bytes[0] == 0x30;
            } catch (FormatException) {
                return false;
            }
        }

        /// <summary>The project's asset wherever it is, Resources or not; null when the project never saved one.</summary>
        internal static IapSettings Find() => SettingsAssetUtils.Find<IapSettings>(IapSettings.ResourceName);

        internal static IapSettings LoadOrCreate() => SettingsAssetUtils.LoadOrCreate<IapSettings>(IapSettings.ResourceName);
    }

    [CustomEditor(typeof(IapSettings))]
    internal sealed class IapSettingsInspector : SettingsAssetInspector {
        protected override string PagePath => IapSettingsProvider.PagePath;
    }

    /// <summary>Stops a build whose purchases would go unpaid, ungranted or unconfirmed.</summary>
    internal sealed class IapBuildCheck : IPreprocessBuildWithReport {
        public int callbackOrder => int.MinValue;

        public void OnPreprocessBuild(BuildReport report) {
            var settings = IapSettingsProvider.Find();
            if (!settings) return;

            var issues = IapSettingsProvider.Issues(settings).Where(issue => issue.Concerns(report.summary.platform)).ToList();
            foreach (var warning in issues.Where(issue => !issue.Blocking && !issue.IsInfo)) Debug.LogWarning("IAP: " + warning.Message);

            var blocking = issues.Where(issue => issue.Blocking).Select(issue => issue.Message).ToList();
            if (blocking.Count > 0) throw new BuildFailedException("IAP: fix these before building, Project Settings > RiseOn > IAP has a button for some:\n- " + string.Join("\n- ", blocking));
        }
    }
}