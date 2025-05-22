using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Projection;

namespace MethodDependencyCopier
{
    public static class TextViewExtensions
    {
        public static IEnumerable<Document> GetRelatedDocuments(this ITextBuffer textBuffer)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (textBuffer == null)
                yield break;

            // Handle projection buffers
            if (textBuffer is IProjectionBuffer projectionBuffer)
            {
                foreach (var sourceBuffer in projectionBuffer.SourceBuffers)
                {
                    var doc = sourceBuffer.GetRelatedDocument();
                    if (doc != null)
                        yield return doc;
                }
            }
            else
            {
                var doc = textBuffer.GetRelatedDocument();
                if (doc != null)
                    yield return doc;
            }
        }

        public static Document GetRelatedDocument(this ITextBuffer textBuffer)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (textBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument textDocument))
            {
                var filePath = textDocument.FilePath;
                if (!string.IsNullOrEmpty(filePath))
                {
                    var workspace = textBuffer.GetWorkspace();
                    var documentId = workspace?.CurrentSolution?.GetDocumentIdsWithFilePath(filePath).FirstOrDefault();
                    return documentId != null ? workspace.CurrentSolution.GetDocument(documentId) : null;
                }
            }
            return null;
        }

        public static Workspace GetWorkspace(this ITextBuffer textBuffer)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var componentModel = (IComponentModel)Package.GetGlobalService(typeof(SComponentModel));
            return componentModel?.GetService<VisualStudioWorkspace>();
        }
    }
}
