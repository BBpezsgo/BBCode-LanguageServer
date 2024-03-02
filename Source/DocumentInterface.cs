namespace LanguageServer;

using DocumentManagers;

public abstract class SingleDocumentHandler
{
    public Uri Uri => _uri.ToUri();
    public string Text => _text;
    public string LanguageId => _languageId;

    public string Path
    {
        get
        {
            string result = Uri.AbsolutePath;
            result = System.Net.WebUtility.UrlDecode(result);
            result = System.IO.Path.GetFullPath(result);
            return result;
        }
    }

    string _languageId;
    string _text;
    DocumentUri _uri;

    protected readonly Documents App;

    protected SingleDocumentHandler(DocumentUri uri, string content, string languageId, Documents app)
    {
        _uri = uri;
        _text = content;
        _languageId = languageId;
        App = app;
    }

    public virtual void OnOpened(DidOpenTextDocumentParams e)
    {
        _uri = e.TextDocument.Uri;
        _text = OmniSharpService.Instance?.GetDocumentContent(e.TextDocument) ?? string.Empty;
        _languageId = e.TextDocument.Extension();
    }

    public virtual void OnChanged(DidChangeTextDocumentParams e)
    {
        _uri = e.TextDocument.Uri;
        _text = OmniSharpService.Instance?.GetDocumentContent(e.TextDocument) ?? string.Empty;
        _languageId = e.TextDocument.Extension();
    }

    public virtual void OnSaved(DidSaveTextDocumentParams e)
    {
        _uri = e.TextDocument.Uri;
        _text = e.Text ?? string.Empty;
        _languageId = e.TextDocument.Extension();
    }

    public abstract Hover? Hover(HoverParams e);
    public abstract CodeLens[] CodeLens(CodeLensParams e);
    public abstract Location[] References(ReferenceParams e);
    public abstract SignatureHelp? SignatureHelp(SignatureHelpParams e);
    public abstract void GetSemanticTokens(SemanticTokensBuilder builder, ITextDocumentIdentifierParams e);
    public abstract CompletionItem[] Completion(CompletionParams e);
    public abstract LocationOrLocationLinks? GotoDefinition(DefinitionParams e);
    public abstract SymbolInformationOrDocumentSymbol[] Symbols(DocumentSymbolParams e);
}

public class Documents
{
    readonly OmniSharpService Interface;
    readonly List<SingleDocumentHandler> documents;

    public Documents(OmniSharpService @interface)
    {
        documents = new List<SingleDocumentHandler>();
        Interface = @interface;
    }

    /// <exception cref="ServiceException"/>
    public static SingleDocumentHandler GenerateDocument(DocumentUri uri, string content, string languageId, Documents documentInterface) => languageId switch
    {
        DocumentBBCode.LanguageIdentifier => new DocumentBBCode(uri, content, languageId, documentInterface),
        _ => throw new ServiceException($"Unknown language \"{languageId}\"")
    };

    public void Initialize()
    {

    }

    public bool TryGet(DocumentUri uri, [NotNullWhen(true)] out SingleDocumentHandler? document)
    {
        for (int i = 0; i < documents.Count; i++)
        {
            if (documents[i].Uri == uri)
            {
                document = documents[i];
                return true;
            }
        }
        document = null;
        return false;
    }

    public void Remove(TextDocumentIdentifier documentId)
    {
        Logger.Log($"Unregister document \"{documentId.Uri}\" ...");
        for (int i = documents.Count - 1; i >= 0; i--)
        {
            if (documents[i].Uri == documentId.Uri)
            {
                Logger.Log($"Document \"{documentId.Uri}\" unregistered");
                documents.RemoveAt(i);
            }
        }
    }

    public void RemoveDuplicates()
    {
        for (int i = documents.Count - 1; i >= 0; i--)
        {
            for (int j = documents.Count - 1; j >= i + 1; j--)
            {
                if (documents[i].Uri == documents[j].Uri)
                {
                    Logger.Log($"Unregister duplicated document \"{documents[i].Uri}\"");
                    documents.RemoveAt(i);
                }
            }
        }
    }

    public SingleDocumentHandler? Get(TextDocumentIdentifier documentId)
        => TryGet(documentId.Uri, out SingleDocumentHandler? document) ? document : null;

    /// <exception cref="ServiceException"/>
    public SingleDocumentHandler GetOrCreate(TextDocumentIdentifier documentId)
    {
        RemoveDuplicates();

        if (TryGet(documentId.Uri, out SingleDocumentHandler? document))
        { return document; }

        Logger.Log($"Register document \"{documentId.Uri}\" ...");

        if (documentId.Uri.Scheme == "file")
        {
            string path = System.Net.WebUtility.UrlDecode(documentId.Uri.ToUri().AbsolutePath);
            if (!System.IO.File.Exists(path))
            { throw new ServiceException($"File \"{path}\" not found"); }

            string extension = documentId.Extension();
            string content = System.IO.File.ReadAllText(path);

            Logger.Log($"Document \"{documentId.Uri}\" registered");
            document = GenerateDocument(documentId.Uri, content, extension, this);
            documents.Add(document);

            return document;
        }

        throw new ServiceException($"Unknown document uri scheme \"{documentId.Uri.Scheme}\"");
    }

    /// <exception cref="ServiceException"/>
    public SingleDocumentHandler GetOrCreate(TextDocumentIdentifier documentId, Microsoft.Language.Xml.StringBuffer buffer)
    {
        RemoveDuplicates();

        if (TryGet(documentId.Uri, out SingleDocumentHandler? document))
        { return document; }

        Logger.Log($"Register document \"{documentId.Uri}\" ...");

        if (documentId.Uri.Scheme == "file")
        {
            string extension = documentId.Extension();
            string content = buffer.GetText(0, buffer.Length);

            Logger.Log($"Document \"{documentId.Uri}\" registered");
            document = GenerateDocument(documentId.Uri, content, extension, this);
            documents.Add(document);

            return document;
        }

        throw new ServiceException($"Unknown document uri scheme \"{documentId.Uri.Scheme}\"");
    }
}
