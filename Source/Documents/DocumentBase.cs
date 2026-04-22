namespace LanguageServer;

abstract class DocumentBase
{
    public Uri Uri => DocumentUri.ToUri();
    public DocumentUri DocumentUri { get; private set; }
    public string? Content { get; private set; }
    public string LanguageId { get; private set; }
    public DocumentVersion? Version { get; private set; }
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

    protected DocumentBase(DocumentUri uri, string? content, string languageId, Documents app)
    {
        DocumentUri = uri;
        Content = content;
        LanguageId = languageId;
        Documents = app;
    }

    public virtual void OnOpened(DidOpenTextDocumentParams e)
    {
        Logger.Debug($"[DocBuffer] Updated ({e.TextDocument}) ({e.TextDocument.Text.Length})");
        Content = e.TextDocument.Text;

        DocumentUri = e.TextDocument.Uri;
        LanguageId = e.TextDocument.Uri.GetExtension();
        Version = e.TextDocument.Version.HasValue ? DocumentVersion.Now(e.TextDocument.Version.Value) : null;
    }

    public virtual void OnChanged(DidChangeTextDocumentParams e)
    {
        foreach (TextDocumentContentChangeEvent change in e.ContentChanges)
        {
            if (change.Range is not null)
            {
                Logger.Debug($"[DocBuffer] Updated ({e.TextDocument}) ({change.Range}, {change.Text.Length})");
            }
            else
            {
                Logger.Debug($"[DocBuffer] Updated ({e.TextDocument}) ({change.Text.Length})");
                Content = change.Text;
            }
        }

        string? text = e.ContentChanges.FirstOrDefault()?.Text;

        if (text != null)
        {
            Logger.Debug($"[DocBuffer] Updated ({e.TextDocument}) ({text.Length})");
            Content = text;
        }

        DocumentUri = e.TextDocument.Uri;
        LanguageId = e.TextDocument.Uri.GetExtension();
        Version = e.TextDocument.Version.HasValue ? DocumentVersion.Now(e.TextDocument.Version.Value) : null;
    }

    public virtual void OnSaved(DidSaveTextDocumentParams e)
    {
        if (e.Text != null)
        {
            Logger.Debug($"[DocBuffer] Updated ({e.TextDocument}) ({e.Text.Length})");
            Content = e.Text;
        }

        DocumentUri = e.TextDocument.Uri;
        LanguageId = e.TextDocument.Uri.GetExtension();
    }

    public virtual Task<Hover?> Hover(HoverParams request, CancellationToken cancellationToken) => Task.FromResult<Hover?>(null);
    public virtual Task<IEnumerable<DocumentHighlight>?> DocumentHighlight(DocumentHighlightParams request, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<DocumentHighlight>?>(null);
    public virtual Task<IEnumerable<CodeLens>?> CodeLens(CodeLensParams request, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<CodeLens>?>(null);
    public virtual Task<IEnumerable<Location>?> References(ReferenceParams request, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<Location>?>(null);
    public virtual Task<SignatureHelp?> SignatureHelp(SignatureHelpParams request, CancellationToken cancellationToken) => Task.FromResult<SignatureHelp?>(null);
    public virtual Task GetSemanticTokens(SemanticTokensBuilder builder, ITextDocumentIdentifierParams request, CancellationToken cancellationToken) => Task.CompletedTask;
    public virtual Task<IEnumerable<CompletionItem>?> Completion(CompletionParams request, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<CompletionItem>?>(null);
    public virtual Task<LocationOrLocationLinks?> GotoDefinition(DefinitionParams request, CancellationToken cancellationToken) => Task.FromResult<LocationOrLocationLinks?>(null);
    public virtual Task<LocationOrLocationLinks?> GotoDeclaration(DeclarationParams request, CancellationToken cancellationToken) => Task.FromResult<LocationOrLocationLinks?>(null);
    public virtual Task<LocationOrLocationLinks?> GotoTypeDefinition(TypeDefinitionParams request, CancellationToken cancellationToken) => Task.FromResult<LocationOrLocationLinks?>(null);
    public virtual Task<LocationOrLocationLinks?> GotoImplementation(ImplementationParams request, CancellationToken cancellationToken) => Task.FromResult<LocationOrLocationLinks?>(null);
    public virtual Task<IEnumerable<SymbolInformationOrDocumentSymbol>?> Symbols(DocumentSymbolParams request, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<SymbolInformationOrDocumentSymbol>?>(null);
    public virtual Task<IEnumerable<InlineValueBase>?> InlineValues(InlineValueParams request, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<InlineValueBase>?>(null);
    public virtual Task<IEnumerable<InlayHint>?> InlayHints(InlayHintParams request, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<InlayHint>?>(null);
    public virtual Task<IEnumerable<CommandOrCodeAction>?> CodeAction(CodeActionParams request, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<CommandOrCodeAction>?>(null);
    public virtual Task<IEnumerable<TextEdit>?> DocumentFormatting(DocumentFormattingParams request, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<TextEdit>?>(null);

    public override string ToString() => $"{Path}";
}
