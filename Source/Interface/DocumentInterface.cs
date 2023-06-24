using BBCodeLanguageServer.DocumentManagers;

using System;
using System.Collections.Generic;
using System.Linq;

namespace BBCodeLanguageServer.Interface
{
    internal interface IDocument
    {
        internal void OnChanged(DocumentItem document);
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
        readonly Dictionary<string, IDocument> Docs;

        internal DocumentInterface(IInterface @interface)
        {
            Docs = new();
            Interface = @interface;

            Interface.OnInitialize += Initialize;
            Interface.OnDocumentSymbols += DocumentSymbols;
            Interface.OnGotoDefinition += GotoDefinition;
            Interface.OnDocumentOpened += DidOpenTextDocument;
            Interface.OnDocumentChanged += DidChangeTextDocument;
            Interface.OnDocumentClosed += DidCloseTextDocument;
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

        IDocument GetDoc(Uri uri)
        {
            if (Docs.ContainsKey(uri.ToString()))
            {
                return Docs[uri.ToString()];
            }

            if (uri.Scheme == "file")
            {
                string path = System.Net.WebUtility.UrlDecode(uri.AbsolutePath);
                if (System.IO.File.Exists(path))
                {
                    var newDocItem = new DocumentItem(uri, System.IO.File.ReadAllText(path), path.Split('.').Last().ToLower());
                    Logger.Log($"Manual doc generating\n  path: {newDocItem.Uri}\n                {path}\n  language: {newDocItem.LanguageID}");
                    DidOpenTextDocument(new DocumentItemEventArgs(newDocItem));
                    return Docs[uri.ToString()];
                }
            }

            throw new ServiceException($"Document \"{uri}\" not found");
        }
        IDocument GetDoc(Document document) => GetDoc(document.Uri);

        internal void DidOpenTextDocument(DocumentItemEventArgs e)
        {
            if (!Docs.TryAdd(e.Document.Uri.ToString(), DocumentGenerator.GenerateDocument(e.Document, this)))
            { Docs[e.Document.Uri.ToString()].OnChanged(e.Document); }
        }
        internal void DidChangeTextDocument(DocumentItemEventArgs e)
        {
            if (!Docs.TryAdd(e.Document.Uri.ToString(), DocumentGenerator.GenerateDocument(e.Document, this)))
            { Docs[e.Document.Uri.ToString()].OnChanged(e.Document); }
        }

        internal SymbolInformationInfo[] DocumentSymbols(DocumentEventArgs e) => GetDoc(e.Document).Symbols(e);
        internal SingleOrArray<FilePosition>? GotoDefinition(DocumentPositionEventArgs e) => GetDoc(e.Document).GotoDefinition(e);
        internal void DidCloseTextDocument(DocumentEventArgs e) => Docs.Remove(e.Document.Uri.ToString());
        internal void DidChangeConfiguration(ConfigEventArgs e) { }
        internal CompletionInfo[] Completion(DocumentPositionContextEventArgs e) => GetDoc(e.Document).Completion(e);
        internal HoverInfo Hover(DocumentPositionEventArgs e) => GetDoc(e.Document).Hover(e);
        internal CodeLensInfo[] CodeLens(DocumentEventArgs e) => GetDoc(e.Document).CodeLens(e);
        internal FilePosition[] References(FindReferencesEventArgs e) => GetDoc(e.Document).References(e);
        internal SignatureHelpInfo SignatureHelp(SignatureHelpEventArgs e) => GetDoc(e.Document).SignatureHelp(e);
        internal SemanticToken[] OnSemanticTokensNeed(DocumentEventArgs e) => GetDoc(e.Document).GetSemanticTokens(e);
    }
}
