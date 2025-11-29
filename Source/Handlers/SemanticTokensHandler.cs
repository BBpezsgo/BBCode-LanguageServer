namespace LanguageServer.Handlers;

sealed class SemanticTokensHandler : SemanticTokensHandlerBase
{
    protected override Task Tokenize(SemanticTokensBuilder builder, ITextDocumentIdentifierParams identifier, CancellationToken cancellationToken)
    {
        Logger.Log($"SemanticTokensHandler.Tokenize({identifier})");

        OmniSharpService.Instance?.Documents.Get(identifier.TextDocument)?.GetSemanticTokens(builder, identifier);

        return Task.CompletedTask;
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
            Delta = false,
        },
        Range = false,
    };
}
