namespace LanguageServer.Handlers;

sealed class SemanticTokensHandler : SemanticTokensHandlerBase
{
    readonly SemanticTokensLegend Legend = new()
    {
        TokenModifiers = new Container<SemanticTokenModifier>(
            SemanticTokenModifier.Defaults
            .Append(new SemanticTokenModifier("meow"))
        ),
        TokenTypes = new Container<SemanticTokenType>(SemanticTokenType.Defaults),
    };

    protected override Task Tokenize(SemanticTokensBuilder builder, ITextDocumentIdentifierParams identifier, CancellationToken cancellationToken)
    {
        Logger.Debug($"[Handler] SemanticTokens ({identifier.TextDocument})");
        if (OmniSharpService.Instance?.Server == null) return Task.CompletedTask;
        if (!OmniSharpService.Instance.Documents.TryGet(identifier.TextDocument.Uri, out DocumentBase? document))
        {
            Logger.Warn($"Document \"{identifier.TextDocument}\" not found");
            return Task.CompletedTask;
        }

        return document.GetSemanticTokens(builder, identifier, cancellationToken);
    }

    protected override Task<SemanticTokensDocument> GetSemanticTokensDocument(ITextDocumentIdentifierParams @params, CancellationToken cancellationToken)
        => Task.FromResult(new SemanticTokensDocument(Legend));

    protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(SemanticTokensCapability capability, ClientCapabilities clientCapabilities) => new()
    {
        DocumentSelector = TextDocumentSelector.ForLanguage(LanguageCore.LanguageConstants.LanguageId),
        Legend = Legend,
        Full = new SemanticTokensCapabilityRequestFull()
        {
            Delta = true,
        },
        Range = true,
    };
}
