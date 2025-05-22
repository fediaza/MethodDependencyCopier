using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Threading;
using Task = System.Threading.Tasks.Task;

namespace MethodDependencyCopier
{
    internal sealed class CopyMethodWithDependenciesCommand
    {
        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            var commandId = new CommandID(PackageGuids.guidMethodDependencyCopierPackageCmdSet, PackageIds.CopyMethodWithDependenciesId);
            var command = new MenuCommand((s, e) => Execute(package), commandId);
            commandService.AddCommand(command);
        }

        private static async void Execute(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var textView = await GetActiveTextViewAsync(package);
                if (textView == null) return;


                var selectionSpan = textView.Selection.SelectedSpans.FirstOrDefault();
                if (selectionSpan.IsEmpty) return;

                var document = textView.TextBuffer.GetRelatedDocuments().FirstOrDefault();
                if (document == null) return;

                var semanticModel = await document.GetSemanticModelAsync();
                var syntaxRoot = await document.GetSyntaxRootAsync();

                // Convert SnapshotSpan to TextSpan
                var textSpan = new Microsoft.CodeAnalysis.Text.TextSpan(selectionSpan.Start, selectionSpan.Length);
                var selectedNode = syntaxRoot.FindNode(textSpan);

                var methodSymbol = await GetSelectedMethodSymbolAsync(semanticModel, selectedNode);
                if (methodSymbol == null) return;

                var dependencies = await FindAllMethodDependenciesAsync(methodSymbol, document.Project.Solution);
                var allMethods = new HashSet<IMethodSymbol>(dependencies) { methodSymbol };

                var sourceText = await GenerateSourceTextWithDependencies(allMethods, document.Project.Solution);
                Clipboard.SetText(sourceText);

                VsShellUtilities.ShowMessageBox(package,
                    "Method and dependencies copied to clipboard!",
                    "Method Dependency Copier",
                    OLEMSGICON.OLEMSGICON_INFO,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
            catch (Exception ex)
            {
                VsShellUtilities.ShowMessageBox(package,
                    $"Error: {ex.Message}",
                    "Method Dependency Copier",
                    OLEMSGICON.OLEMSGICON_WARNING,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        }

        private static async Task<string> GenerateSourceTextWithDependencies(HashSet<IMethodSymbol> methods, Solution solution)
        {
            var sb = new StringBuilder();

            foreach (var method in methods)
            {
                var location = method.Locations.FirstOrDefault(l => l.IsInSource);
                if (location == null) continue;

                var document = solution.GetDocument(location.SourceTree);
                if (document == null) continue;

                var syntaxRoot = await document.GetSyntaxRootAsync();
                var node = syntaxRoot.FindNode(location.SourceSpan);

                sb.AppendLine(node.ToFullString());
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static async Task<IEnumerable<IMethodSymbol>> FindAllMethodDependenciesAsync(IMethodSymbol methodSymbol, Solution solution)
        {
            var dependencies = new HashSet<IMethodSymbol>();
            var methodsToProcess = new Queue<IMethodSymbol>();
            methodsToProcess.Enqueue(methodSymbol);

            while (methodsToProcess.Count > 0)
            {
                var currentMethod = methodsToProcess.Dequeue();

                var references = await SymbolFinder.FindReferencesAsync(currentMethod, solution);
                foreach (var reference in references)
                {
                    foreach (var location in reference.Locations)
                    {
                        var document = solution.GetDocument(location.Document.Id);
                        if (document == null) continue;

                        var semanticModel = await document.GetSemanticModelAsync();
                        var syntaxRoot = await document.GetSyntaxRootAsync();
                        var node = syntaxRoot.FindNode(location.Location.SourceSpan);

                        var containingMethod = GetContainingMethod(semanticModel, node);
                        if (containingMethod != null && !dependencies.Contains(containingMethod))
                        {
                            dependencies.Add(containingMethod);
                            methodsToProcess.Enqueue(containingMethod);
                        }
                    }
                }
            }

            return dependencies;
        }

        private static IMethodSymbol GetContainingMethod(SemanticModel semanticModel, SyntaxNode node)
        {
            while (node != null)
            {
                var symbol = semanticModel.GetDeclaredSymbol(node);
                if (symbol is IMethodSymbol methodSymbol)
                {
                    return methodSymbol;
                }
                node = node.Parent;
            }
            return null;
        }

        //private static async Task<IMethodSymbol> GetSelectedMethodSymbolAsync(SemanticModel semanticModel, SyntaxNode selectedNode)
        //{
        //    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        //    while (selectedNode != null)
        //    {
        //        var symbol = semanticModel.GetDeclaredSymbol(selectedNode);
        //        if (symbol is IMethodSymbol methodSymbol)
        //        {
        //            return methodSymbol;
        //        }
        //        selectedNode = selectedNode.Parent;
        //    }
        //    return null;
        //}

        private static async Task<IMethodSymbol> GetSelectedMethodSymbolAsync(SemanticModel semanticModel, SyntaxNode selectedNode)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            while (selectedNode != null)
            {
                var symbolInfo = semanticModel.GetSymbolInfo(selectedNode);
                if (symbolInfo.Symbol is IMethodSymbol methodSymbol)
                {
                    return methodSymbol;
                }

                // Also check for method declarations
                var declaredSymbol = semanticModel.GetDeclaredSymbol(selectedNode);
                if (declaredSymbol is IMethodSymbol declaredMethodSymbol)
                {
                    return declaredMethodSymbol;
                }

                selectedNode = selectedNode.Parent;
            }
            return null;
        }

        private static async Task<IWpfTextView> GetActiveTextViewAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var serviceProvider = package as IServiceProvider;
            var textManager = serviceProvider.GetService(typeof(SVsTextManager)) as IVsTextManager;
            if (textManager == null) return null;

            textManager.GetActiveView(1, null, out IVsTextView textView);
            if (textView == null) return null;

            var userData = textView as IVsUserData;
            if (userData == null) return null;

            Guid guidViewHost = Microsoft.VisualStudio.Editor.DefGuidList.guidIWpfTextViewHost;
            userData.GetData(ref guidViewHost, out object holder);
            var viewHost = (IWpfTextViewHost)holder;

            return viewHost.TextView;
        }
    }
}
