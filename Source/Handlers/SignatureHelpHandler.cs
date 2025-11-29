namespace LanguageServer.Handlers;

sealed class SignatureHelpHandler : ISignatureHelpHandler
{
    public Task<SignatureHelp?> Handle(SignatureHelpParams e, CancellationToken cancellationToken) => Task.Run(() =>
    {
        Logger.Log($"SignatureHelpHandler.Handle({e})");

        if (OmniSharpService.Instance?.Server == null) return null;

        try
        {
            return OmniSharpService.Instance.Documents.Get(e.TextDocument)?.SignatureHelp(e);
        }
        catch (ServiceException error)
        {
            OmniSharpService.Instance?.Server?.Window?.ShowWarning($"ServiceException: {error.Message}");
            return null;
        }
    });

    public SignatureHelpRegistrationOptions GetRegistrationOptions(SignatureHelpCapability capability, ClientCapabilities clientCapabilities) => new()
    {
        DocumentSelector = TextDocumentSelector.ForLanguage(LanguageCore.LanguageConstants.LanguageId),
        TriggerCharacters = new Container<string>("(", ","),
    };
}
