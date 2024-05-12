namespace LanguageServer;

using DocumentManagers;

public abstract class DocumentHandler
{
    public Uri Uri => DocumentUri.ToUri();
    public DocumentUri DocumentUri { get; private set; }
    public string Content { get; private set; }
    public string LanguageId { get; private set; }
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
    protected Documents Documents { get; }

    protected DocumentHandler(DocumentUri uri, string content, string languageId, Documents app)
    {
        DocumentUri = uri;
        Content = content;
        LanguageId = languageId;
        Documents = app;
    }

    public virtual void OnOpened(DidOpenTextDocumentParams e)
    {
        Logger.Log($"Document buffer updated ({e.TextDocument.Text.Length}): \"{e.TextDocument}\"");
        Content = e.TextDocument.Text;

        DocumentUri = e.TextDocument.Uri;
        LanguageId = e.TextDocument.Extension();
    }

    public virtual void OnChanged(DidChangeTextDocumentParams e)
    {
        string? text = e.ContentChanges.FirstOrDefault()?.Text;

        if (text != null)
        {
            Logger.Log($"Document buffer updated ({text.Length}): \"{e.TextDocument}\"");
            DocumentUri = text;
        }

        DocumentUri = e.TextDocument.Uri;
        LanguageId = e.TextDocument.Extension();
    }

    public virtual void OnSaved(DidSaveTextDocumentParams e)
    {
        if (e.Text != null)
        {
            Logger.Log($"Document buffer updated ({e.Text.Length}): \"{e.TextDocument}\"");
            Content = e.Text;
        }

        DocumentUri = e.TextDocument.Uri;
        Content = e.Text ?? string.Empty;
        LanguageId = e.TextDocument.Extension();
    }

    public abstract Hover? Hover(HoverParams e);
    public abstract CodeLens[] CodeLens(CodeLensParams e);
    public abstract Location[] References(ReferenceParams e);
    public abstract SignatureHelp? SignatureHelp(SignatureHelpParams e);
    public abstract void GetSemanticTokens(SemanticTokensBuilder builder, ITextDocumentIdentifierParams e);
    public abstract CompletionItem[] Completion(CompletionParams e);
    public abstract LocationOrLocationLinks? GotoDefinition(DefinitionParams e);
    public abstract SymbolInformationOrDocumentSymbol[] Symbols(DocumentSymbolParams e);

    public override string ToString() => $"{Path}";
}

public class Documents
{
    readonly List<DocumentHandler> _documents;

    public Documents()
    {
        _documents = new List<DocumentHandler>();
    }

    /// <exception cref="ServiceException"/>
    public static DocumentHandler GenerateDocument(DocumentUri uri, string content, string languageId, Documents documentInterface) => languageId switch
    {
        LanguageCore.LanguageConstants.LanguageId => new DocumentBBLang(uri, content, languageId, documentInterface),
        _ => throw new ServiceException($"Unknown language \"{languageId}\"")
    };

    public bool TryGet(DocumentUri uri, [NotNullWhen(true)] out DocumentHandler? document)
    {
        for (int i = 0; i < _documents.Count; i++)
        {
            if (_documents[i].Uri == uri)
            {
                document = _documents[i];
                return true;
            }
        }
        document = null;
        return false;
    }

    public void Remove(TextDocumentIdentifier documentId)
    {
        Logger.Log($"Unregister document: \"{documentId.Uri}\"");
        for (int i = _documents.Count - 1; i >= 0; i--)
        {
            if (_documents[i].Uri == documentId.Uri)
            {
                Logger.Log($"Document unregistered: \"{documentId.Uri}\"");
                _documents.RemoveAt(i);
            }
        }
    }

    public void RemoveDuplicates()
    {
        for (int i = _documents.Count - 1; i >= 0; i--)
        {
            for (int j = _documents.Count - 1; j >= i + 1; j--)
            {
                if (_documents[i].Uri == _documents[j].Uri)
                {
                    Logger.Log($"Unregister duplicated document: \"{_documents[i].Uri}\"");
                    _documents.RemoveAt(i);
                }
            }
        }
    }

    public DocumentHandler? Get(TextDocumentIdentifier documentId)
        => TryGet(documentId.Uri, out DocumentHandler? document) ? document : null;

    /// <exception cref="ServiceException"/>
    public DocumentHandler GetOrCreate(TextDocumentIdentifier documentId, string? content = null)
    {
        RemoveDuplicates();

        if (TryGet(documentId.Uri, out DocumentHandler? document))
        { return document; }

        Logger.Log($"Register document: \"{documentId.Uri}\"");

        if (documentId.Uri.Scheme == "file")
        {
            if (content is null)
            {
                string path = System.Net.WebUtility.UrlDecode(documentId.Uri.ToUri().AbsolutePath);
                if (!System.IO.File.Exists(path))
                { throw new ServiceException($"File not found: \"{path}\""); }
                content = System.IO.File.ReadAllText(path);
            }

            string extension = documentId.Extension();

            Logger.Log($"Document registered: \"{documentId.Uri}\"");
            document = GenerateDocument(documentId.Uri, content, extension, this);
            _documents.Add(document);

            return document;
        }

        throw new ServiceException($"Unknown document uri scheme: \"{documentId.Uri.Scheme}\"");
    }
}
