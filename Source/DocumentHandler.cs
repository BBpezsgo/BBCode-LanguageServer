namespace LanguageServer;

abstract class DocumentHandler
{
    public Uri Uri => DocumentUri.ToUri();
    public DocumentUri DocumentUri { get; private set; }
    public string Content { get; private set; }
    public string LanguageId { get; private set; }
    public int? Version { get; private set; }
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
        Version = e.TextDocument.Version;
    }

    public virtual void OnChanged(DidChangeTextDocumentParams e)
    {
        foreach (var change in e.ContentChanges)
        {
            if (change.Range is not null)
            {
                Logger.Log($"Document buffer updated at {change.Range} to \"{change.Text.Length}\" (\"{e.TextDocument}\")");
            }
            else
            {
                Logger.Log($"Document buffer updated ({change.Text.Length}) (\"{e.TextDocument}\")");
                Content = change.Text;
            }
        }

        string? text = e.ContentChanges.FirstOrDefault()?.Text;

        if (text != null)
        {
            Logger.Log($"Document buffer updated ({text.Length}): \"{e.TextDocument}\"");
            Content = text;
        }

        DocumentUri = e.TextDocument.Uri;
        LanguageId = e.TextDocument.Extension();
        Version = e.TextDocument.Version;
    }

    public virtual void OnSaved(DidSaveTextDocumentParams e)
    {
        if (e.Text != null)
        {
            Logger.Log($"Document buffer updated ({e.Text.Length}): \"{e.TextDocument}\"");
            Content = e.Text;
        }

        DocumentUri = e.TextDocument.Uri;
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
