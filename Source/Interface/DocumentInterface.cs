using ProgrammingLanguage.LanguageServer.DocumentManagers;

using System;
using System.Collections.Generic;
using System.Linq;

namespace ProgrammingLanguage.LanguageServer.Interface
{
    internal interface IDocument
    {
        internal Uri Uri { get; }

        internal void OnChanged(DocumentItem document);
        internal void OnSaved(Document document);

        internal HoverInfo Hover(DocumentPositionEventArgs e);
        internal CodeLensInfo[] CodeLens(DocumentEventArgs e);
        internal FilePosition[] References(FindReferencesEventArgs e);
        internal SignatureHelpInfo SignatureHelp(SignatureHelpEventArgs e);
        internal SemanticToken[] GetSemanticTokens(DocumentEventArgs e);
        internal CompletionInfo[] Completion(DocumentPositionContextEventArgs e);
        internal SingleOrArray<FilePosition>? GotoDefinition(DocumentPositionEventArgs e);
        internal SymbolInformationInfo[] Symbols(DocumentEventArgs e);
    }

    internal static class DocumentGenerator
    {
        internal static IDocument GenerateDocument(DocumentItem document, DocumentInterface documentInterface)
        {
            if (document.LanguageID == "bbc") return new DocumentBBCode(document, documentInterface);
            throw new Exception($"Unknown language {document.LanguageID}");
        }
    }

    public class DocumentInterface
    {
        internal readonly IInterface Interface;
        readonly List<IDocument> Documents;

        public DocumentInterface(IInterface @interface)
        {
            Documents = new List<IDocument>();
            Interface = @interface;

            Interface.OnInitialize += Initialize;
            Interface.OnDocumentSymbols += DocumentSymbols;
            Interface.OnGotoDefinition += GotoDefinition;
            Interface.OnDocumentOpened += DidOpenTextDocument;
            Interface.OnDocumentChanged += DidChangeTextDocument;
            Interface.OnDocumentClosed += DidCloseTextDocument;
            Interface.OnDocumentSaved += DidSaveTextDocument;
            Interface.OnConfigChanged += DidChangeConfiguration;
            Interface.OnCompletion += Completion;
            Interface.OnHover += Hover;
            Interface.OnReferences += References;
            Interface.OnCodeLens += CodeLens;
            Interface.OnSignatureHelp += SignatureHelp;
            Interface.OnSemanticTokensNeed += OnSemanticTokensNeed;
        }

        internal void Initialize()
        {

        }

        bool TryGetDocument(Uri uri, out IDocument document)
        {
            for (int i = 0; i < Documents.Count; i++)
            {
                if (Documents[i].Uri == uri)
                {
                    document = Documents[i];
                    return true;
                }
            }
            document = null;
            return false;
        }

        bool HasDocument(Uri uri) => TryGetDocument(uri, out _);

        void RemoveDocument(Uri uri)
        {
            Logger.Log($"Unregister document \"{uri}\" ...");
            for (int i = Documents.Count - 1; i >= 0; i--)
            {
                if (Documents[i].Uri == uri)
                {
                    Logger.Log($"Document \"{uri}\" unregistered");
                    Documents.RemoveAt(i);
                }
            }
        }

        IDocument GetDocument(Uri uri)
        {
            for (int i = Documents.Count - 1; i >= 0; i--)
            {
                if (Documents[i].Uri == uri)
                { return Documents[i]; }
            }
            return null;
        }

        void RemoveDuplicatedDocuments()
        {
            for (int i1 = Documents.Count - 1; i1 >= 0; i1--)
            {
                for (int i2 = Documents.Count - 1; i2 >= i1 + 1; i2--)
                {
                    if (Documents[i1].Uri == Documents[i2].Uri)
                    {
                        Logger.Log($"Unregister duplicated document \"{Documents[i1].Uri}\"");
                        Documents.RemoveAt(i1);
                    }
                }
            }
        }

        /// <exception cref="ServiceException"/>
        IDocument GetOrCreateDocument(Uri uri)
        {
            RemoveDuplicatedDocuments();

            if (TryGetDocument(uri, out IDocument document))
            { return document; }

            Logger.Log($"Register document \"{uri}\" ...");

            if (uri.Scheme == "file")
            {
                string path = System.Net.WebUtility.UrlDecode(uri.AbsolutePath);
                if (!System.IO.File.Exists(path))
                { throw new ServiceException($"File \"{path}\" not found"); }

                DocumentItem newDocItem = new(uri, System.IO.File.ReadAllText(path), path.Split('.').Last().ToLower());
                DidOpenTextDocument(new DocumentItemEventArgs(newDocItem));

                return GetDocument(uri);
            }

            throw new ServiceException($"Document \"{uri}\" not found");
        }
        IDocument GetOrCreateDocument(Document document) => GetOrCreateDocument(document.Uri);

        internal void DidOpenTextDocument(DocumentItemEventArgs e)
        {
            if (!HasDocument(e.Document.Uri))
            {
                Logger.Log($"Document \"{e.Document.Uri}\" registered");
                Documents.Add(DocumentGenerator.GenerateDocument(e.Document, this));
            }

            GetDocument(e.Document.Uri).OnChanged(e.Document);
        }
        internal void DidChangeTextDocument(DocumentItemEventArgs e)
        {
            if (!HasDocument(e.Document.Uri))
            {
                Logger.Log($"Document \"{e.Document.Uri}\" registered");
                Documents.Add(DocumentGenerator.GenerateDocument(e.Document, this));
            }

            GetDocument(e.Document.Uri).OnChanged(e.Document);
        }

        internal SymbolInformationInfo[] DocumentSymbols(DocumentEventArgs e) => GetOrCreateDocument(e.Document).Symbols(e);
        internal SingleOrArray<FilePosition>? GotoDefinition(DocumentPositionEventArgs e) => GetOrCreateDocument(e.Document).GotoDefinition(e);
        internal void DidCloseTextDocument(DocumentEventArgs e) => RemoveDocument(e.Document.Uri);
        internal void DidSaveTextDocument(DocumentEventArgs e) => GetDocument(e.Document.Uri).OnSaved(e.Document);
        internal void DidChangeConfiguration(ConfigEventArgs e) { }
        internal CompletionInfo[] Completion(DocumentPositionContextEventArgs e) => GetOrCreateDocument(e.Document).Completion(e);
        internal HoverInfo Hover(DocumentPositionEventArgs e) => GetOrCreateDocument(e.Document).Hover(e);
        internal CodeLensInfo[] CodeLens(DocumentEventArgs e) => GetOrCreateDocument(e.Document).CodeLens(e);
        internal FilePosition[] References(FindReferencesEventArgs e) => GetOrCreateDocument(e.Document).References(e);
        internal SignatureHelpInfo SignatureHelp(SignatureHelpEventArgs e) => GetOrCreateDocument(e.Document).SignatureHelp(e);
        internal SemanticToken[] OnSemanticTokensNeed(DocumentEventArgs e) => GetOrCreateDocument(e.Document).GetSemanticTokens(e);
    }
}
