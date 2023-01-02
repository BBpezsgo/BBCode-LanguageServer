using BBCodeLanguageServer.Interface;
using BBCodeLanguageServer.Interface.SystemExtensions;

using System.Collections.Generic;
using System.Linq;

namespace BBCodeLanguageServer
{
    public class DocumentInterface
    {
        internal readonly IInterface Interface;
        readonly Dictionary<string, Doc> Docs;

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
            Interface.OnCodeLens += CodeLens;
        }

        internal void Initialize()
        {

        }

        Doc GetDoc(System.Uri uri)
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
        Doc GetDoc(Document document) => GetDoc(document.Uri);

        internal void DidOpenTextDocument(DocumentItemEventArgs e)
        {
            if (!Docs.TryAdd(e.Document.Uri.ToString(), new Doc(e.Document, this)))
            { Docs[e.Document.Uri.ToString()].OnChanged(e.Document); }
        }
        internal void DidChangeTextDocument(DocumentItemEventArgs e)
        {
            if (!Docs.TryAdd(e.Document.Uri.ToString(), new Doc(e.Document, this)))
            { Docs[e.Document.Uri.ToString()].OnChanged(e.Document); }
        }

        internal SymbolInformationInfo[] DocumentSymbols(DocumentEventArgs e) => GetDoc(e.Document).Symbols(e);
        internal SingleOrArray<FilePosition>? GotoDefinition(DocumentPositionEventArgs e) => GetDoc(e.Document).GotoDefinition(e);
        internal void DidCloseTextDocument(DocumentEventArgs e) => Docs.Remove(e.Document.Uri.ToString());
        internal void DidChangeConfiguration(ConfigEventArgs e) { }
        internal CompletionInfo[] Completion(DocumentPositionContextEventArgs e) => GetDoc(e.Document).Completion(e);
        internal HoverInfo Hover(DocumentPositionEventArgs e) => GetDoc(e.Document).Hover(e);
        internal CodeLensInfo[] CodeLens(DocumentEventArgs e) => GetDoc(e.Document).CodeLens(e);
        internal FilePosition[] References(DocumentEventArgs e) => GetDoc(e.Document).References(e);
    }
}
