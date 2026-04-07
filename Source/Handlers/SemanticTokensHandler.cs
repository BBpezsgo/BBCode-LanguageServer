namespace LanguageServer.Handlers;

sealed class SemanticTokensHandler : SemanticTokensHandlerBase
{
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
        => Task.FromResult(new SemanticTokensDocument(RegistrationOptions.Legend));

    protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(SemanticTokensCapability capability, ClientCapabilities clientCapabilities) => new()
    {
        DocumentSelector = TextDocumentSelector.ForLanguage(LanguageCore.LanguageConstants.LanguageId),
        Legend = new SemanticTokensLegend()
        {
            TokenModifiers = capability.TokenModifiers,
            TokenTypes = capability.TokenTypes,
        },
        Full = new SemanticTokensCapabilityRequestFull()
        {
            Delta = true,
        },
        Range = true,
    };
}
