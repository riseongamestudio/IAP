using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace RiseOn.Iap.Editor {
    /// <summary>
    /// The game's manager classes, the settings' instance kept to the one there is, and the script the settings page writes for a game that has none.
    /// </summary>
    internal static class IapManagerScripts {
        /// <summary>Every class the pack could run; the source generator lets only one compile in an assembly.</summary>
        internal static List<Type> Candidates() {
            return TypeCache.GetTypesDerivedFrom<IapManagerBase>().Where(type => !type.IsAbstract).ToList();
        }

        internal static string Problem(Type type) {
            if (type.GetConstructor(Type.EmptyTypes) == null) return $"{type.Name} needs a constructor without parameters";
            if (!InBuilds(type)) return $"{type.Name} is in {type.Assembly.GetName().Name}, an Editor-only assembly: Play mode runs it, builds do not have it; move it to a runtime assembly";
            return null;
        }

        // TypeCache sees Editor assemblies too, and Play mode loads them, so only a build would miss the class.
        private static bool InBuilds(Type type) {
            playerAssemblies ??= new HashSet<string>(CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies).Select(assembly => assembly.name));
            return playerAssemblies.Contains(type.Assembly.GetName().Name);
        }

        // GetAssemblies takes some 150 ms and the page asks on every GUI event; a recompile reloads the domain, which
        // clears this with the assemblies it lists.
        private static HashSet<string> playerAssemblies;

        /// <summary>
        /// Makes the settings hold an instance of the game's one manager class, keeping the fields when it already does.<br/>
        /// Leaves them alone while scripts fail to compile, when a class could be missing only for now.
        /// </summary>
        internal static void Adopt(IapSettings settings) {
            if (EditorUtility.scriptCompilationFailed) return;

            var candidates = Candidates();
            if (candidates.Count != 1 || Problem(candidates[0]) != null || settings.Manager?.GetType() == candidates[0]) return;

            var asset = new SerializedObject(settings);
            if (SerializationUtility.HasManagedReferencesWithMissingTypes(settings)) SerializationUtility.ClearAllManagedReferencesWithMissingTypes(settings);
            asset.Update();
            asset.FindProperty("manager").managedReferenceValue = Activator.CreateInstance(candidates[0]);
            asset.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(settings);
        }

        /// <summary>Asks where and writes the script; the settings take its class once Unity compiles it.</summary>
        internal static void Create() {
            var path = EditorUtility.SaveFilePanelInProject("New IAP manager", "IapManager", "cs", "The class takes the file's name.");
            if (string.IsNullOrEmpty(path)) return;

            var name = Path.GetFileNameWithoutExtension(path);
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$")) {
                Debug.LogError($"{nameof(IapManagerScripts)}: {name} cannot be a class name");
                return;
            }

            File.WriteAllText(path, Template(name, EditorSettings.projectGenerationRootNamespace), new UTF8Encoding(true));
            AssetDatabase.ImportAsset(path);
        }

        // After every script reload: a manager class was written, renamed or replaced.
        [InitializeOnLoadMethod]
        private static void AdoptAfterReload() {
            EditorApplication.delayCall += () => {
                var settings = IapSettingsProvider.Find();
                if (settings) Adopt(settings);
            };
        }

        private static string Template(string name, string space) {
            var indent = string.IsNullOrEmpty(space) ? "" : "    ";
            var lines  = new List<string> {
                "using Cysharp.Threading.Tasks;"
              , "using RiseOn.Iap;"
              , ""
            };

            if (indent.Length > 0) lines.Add($"namespace {space} {{");
            lines.AddRange(new[] {
                $"{indent}// The project's one IAP manager. Partial: the IAP pack compiles {name}.Ins, the running instance, into it."
              , $"{indent}public sealed partial class {name} : IapManagerBase {{"
              , $"{indent}    // Wait for the game's save: a purchase the store returns at launch arrives before the first scene."
              , $"{indent}    protected override UniTask WaitUntilCanGrantAsync() => UniTask.CompletedTask;"
              , ""
              , $"{indent}    protected override bool IsGranted(string transactionId) => throw new System.NotImplementedException();"
              , ""
              , $"{indent}    // Give order.Product's content and record order.TransactionId in the same save."
              , $"{indent}    protected override UniTask GrantAsync(IapOrder order) => throw new System.NotImplementedException();"
              , $"{indent}}}"
            });
            if (indent.Length > 0) lines.Add("}");
            return string.Join("\r\n", lines);
        }
    }
}