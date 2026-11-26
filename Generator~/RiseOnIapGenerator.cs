using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace RiseOn.Iap.Generator {
    /// <summary>
    /// Compiles into RiseOn.Iap the product enum the settings page lists.<br/>
    /// Into the game's assembly it compiles, for the one class implementing IIapManager, <c>sealed</c>, an <c>Ins</c> and the registration the pack starts it from.<br/>
    /// A second such class is an error, and so are an interface extending IIapManager and an abstract class implementing it.<br/>
    /// Nothing is written to disk, so nobody can edit it.
    /// </summary>
    [Generator]
    public sealed class RiseOnIapGenerator : ISourceGenerator {
        private const string PackAssembly = "RiseOn.Iap";
        private const string ManagerType  = "RiseOn.Iap.IIapManager";
        private const string ProductsFile = ".RiseOnIapGenerator.additionalfile";

        private static readonly DiagnosticDescriptor notPartial = new(
            "RISEONIAP001", "IAP manager not partial", "{0} implements IIapManager, so it must be a partial class, neither generic nor nested, for the IAP pack to find it and give it Ins",
            "RiseOn.Iap", DiagnosticSeverity.Error, true);

        private static readonly DiagnosticDescriptor notAlone = new(
            "RISEONIAP003", "Several IAP managers", "{0} implements IIapManager, and so does {1}; the IAP pack runs one manager, so only one class may",
            "RiseOn.Iap", DiagnosticSeverity.Error, true);

        private static readonly DiagnosticDescriptor badLine = new(
            "RISEONIAP002", "Unreadable product line", "{0} line {1} is not 'number member'; open Project Settings > RiseOn > IAP and press Apply to write it again",
            "RiseOn.Iap", DiagnosticSeverity.Error, true);

        private static readonly DiagnosticDescriptor subInterface = new(
            "RISEONIAP004", "Interface extends IIapManager", "{0} extends IIapManager; the manager class implements IIapManager itself, so no interface may extend it",
            "RiseOn.Iap", DiagnosticSeverity.Error, true);

        private static readonly DiagnosticDescriptor isAbstract = new(
            "RISEONIAP005", "Abstract IAP manager", "{0} implements IIapManager but is abstract; the IAP pack makes the manager itself, so it must be a class it can make",
            "RiseOn.Iap", DiagnosticSeverity.Error, true);

        public void Initialize(GeneratorInitializationContext context) {
            context.RegisterForSyntaxNotifications(() => new Receiver());
        }

        public void Execute(GeneratorExecutionContext context) {
            if (context.Compilation.AssemblyName is PackAssembly) {
                context.AddSource("IapProductId.g.cs", SourceText.From(ProductEnum(context), Encoding.UTF8));
                return;
            }

            var manager = context.Compilation.GetTypeByMetadataName(ManagerType);
            if (manager == null || context.SyntaxReceiver is not Receiver receiver) return;

            // Every class of this assembly that the pack could run, with the declaration that names it first.
            var found = new Dictionary<INamedTypeSymbol, ClassDeclarationSyntax>(SymbolEqualityComparer.Default);
            var mine  = new List<INamedTypeSymbol>();
            foreach (var declaration in receiver.Interfaces) {
                var model = context.Compilation.GetSemanticModel(declaration.SyntaxTree);
                if (model.GetDeclaredSymbol(declaration) is INamedTypeSymbol type && Implements(type, manager)) {
                    context.ReportDiagnostic(Diagnostic.Create(subInterface, declaration.Identifier.GetLocation(), type.ToDisplayString()));
                }
            }

            foreach (var declaration in receiver.Classes) {
                var model = context.Compilation.GetSemanticModel(declaration.SyntaxTree);
                if (model.GetDeclaredSymbol(declaration) is not INamedTypeSymbol type || !Implements(type, manager)) continue;
                if (type.IsAbstract) {
                    context.ReportDiagnostic(Diagnostic.Create(isAbstract, declaration.Identifier.GetLocation(), type.ToDisplayString()));
                    continue;
                }

                if (found.ContainsKey(type)) continue;

                found.Add(type, declaration);
                mine.Add(type);
            }

            // Assemblies this one references can hold one too; sibling assemblies the Editor checks.
            var all = mine.Concat(Referenced(context.Compilation, manager)).ToList();
            foreach (var type in mine) {
                var declaration = found[type];
                var other       = all.FirstOrDefault(each => !SymbolEqualityComparer.Default.Equals(each, type));
                if (other != null) {
                    context.ReportDiagnostic(Diagnostic.Create(notAlone, declaration.Identifier.GetLocation(), type.ToDisplayString(), other.ToDisplayString()));
                    continue;
                }

                var partial = type.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax()).OfType<ClassDeclarationSyntax>()
                    .All(each => each.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword)));
                if (!partial || type.IsGenericType || type.ContainingType != null) {
                    context.ReportDiagnostic(Diagnostic.Create(notPartial, declaration.Identifier.GetLocation(), type.Name));
                    continue;
                }

                context.AddSource($"{type.ToDisplayString().Replace('.', '_')}.IIapManager.g.cs", SourceText.From(Partial(type), Encoding.UTF8));
            }
        }

        // Only assemblies that reference the pack can hold a manager, which keeps the search off the engine's own.
        private static IEnumerable<INamedTypeSymbol> Referenced(Compilation compilation, INamedTypeSymbol manager) {
            foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols) {
                if (assembly.Name is PackAssembly || !assembly.Modules.Any(module => module.ReferencedAssemblySymbols.Any(each => each.Name is PackAssembly))) continue;

                foreach (var type in Types(assembly.GlobalNamespace)) {
                    if (type.TypeKind is TypeKind.Class && !type.IsAbstract && Implements(type, manager)) yield return type;
                }
            }
        }

        private static IEnumerable<INamedTypeSymbol> Types(INamespaceSymbol space) {
            foreach (var type in space.GetTypeMembers()) yield return type;
            foreach (var child in space.GetNamespaceMembers()) {
                foreach (var type in Types(child)) yield return type;
            }
        }

        // Lines of "number member", which the settings page writes from its product list.
        private static string ProductEnum(GeneratorExecutionContext context) {
            var code = new StringBuilder();
            code.Append("// <auto-generated/>\n");
            code.Append("namespace RiseOn.Iap {\n");
            code.Append("    /// <summary>The products of Project Settings > RiseOn > IAP; 0 is a product the catalog no longer has.</summary>\n");
            code.Append("    public enum IapProductId {\n");

            foreach (var file in context.AdditionalFiles.Where(file => file.Path.EndsWith(ProductsFile))) {
                var lines = file.GetText(context.CancellationToken)?.Lines;
                if (lines == null) continue;

                for (var i = 0; i < lines.Count; ++i) {
                    var line = lines[i].ToString().Trim();
                    if (line.Length == 0) continue;

                    var parts = line.Split(' ');
                    if (parts.Length != 2 || !int.TryParse(parts[0], out var number) || number <= 0 || !SyntaxFacts.IsValidIdentifier(parts[1])) {
                        context.ReportDiagnostic(Diagnostic.Create(badLine, Location.None, Path.GetFileName(file.Path), i + 1));
                        continue;
                    }

                    code.Append($"        {parts[1]} = {number},\n");
                }
            }

            code.Append("    }\n}\n");
            return code.ToString();
        }

        // Sealed, so that no class derives from the game's and turns into a second manager; Ins; and the registration that lets the
        // pack find the class and keeps IL2CPP from stripping it.
        private static string Partial(INamedTypeSymbol type) {
            var space  = type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString();
            var indent = space == null ? "" : "    ";
            var code   = new StringBuilder();
            code.Append("// <auto-generated/>\n");
            if (space != null) code.Append($"namespace {space} {{\n");
            // The settings hold it by [SerializeReference], which wants [Serializable] on the class itself.
            if (!IsSerializable(type)) code.Append($"{indent}[global::System.Serializable]\n");
            code.Append($"{indent}sealed partial class {type.Name} {{\n");
            code.Append($"{indent}    /// <summary>The instance the IAP pack runs; throws before the first scene loads.</summary>\n");
            code.Append($"{indent}    public static {type.Name} Ins => global::RiseOn.Iap.IapSdk.GetManager<{type.Name}>();\n\n");
            code.Append($"{indent}    [global::UnityEngine.RuntimeInitializeOnLoadMethod(global::UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded)]\n");
            code.Append($"{indent}    private static void RegisterWithRiseOnIap() => global::RiseOn.Iap.IapSdk.Register(typeof({type.Name}), () => new {type.Name}());\n");
            code.Append($"{indent}}}\n");
            if (space != null) code.Append("}\n");
            return code.ToString();
        }

        // A second [Serializable] would not compile.
        private static bool IsSerializable(INamedTypeSymbol type) {
            return type.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "System.SerializableAttribute");
        }

        private static bool Implements(INamedTypeSymbol type, INamedTypeSymbol root) {
            return type.AllInterfaces.Contains(root, SymbolEqualityComparer.Default);
        }

        private sealed class Receiver : ISyntaxReceiver {
            public readonly List<ClassDeclarationSyntax> Classes = new();
            public readonly List<InterfaceDeclarationSyntax> Interfaces = new();

            public void OnVisitSyntaxNode(SyntaxNode node) {
                switch (node) {
                    case ClassDeclarationSyntax { BaseList: not null } declaration:
                        Classes.Add(declaration);
                        break;
                    case InterfaceDeclarationSyntax { BaseList: not null } declaration:
                        Interfaces.Add(declaration);
                        break;
                }
            }
        }
    }
}