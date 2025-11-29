using MediatR;

namespace LanguageServer.Handlers;

sealed class CodeLensHandler : ICodeLensHandler
{
    Task<CodeLensContainer?> IRequestHandler<CodeLensParams, CodeLensContainer?>.Handle(CodeLensParams e, CancellationToken cancellationToken) => Task.Run(() =>
    {
        Logger.Log($"CodeLensHandler.Handle({e})");

        if (OmniSharpService.Instance?.Server == null) return null;

        try
        {
            CodeLens[]? result = OmniSharpService.Instance.Documents.Get(e.TextDocument)?.CodeLens(e);
            return new CodeLensContainer(result ?? Array.Empty<CodeLens>());
        }
        catch (ServiceException error)
        {
            OmniSharpService.Instance?.Server?.Window?.ShowWarning($"ServiceException: {error.Message}");
            return null;
        }
    });

    public CodeLensRegistrationOptions GetRegistrationOptions(CodeLensCapability capability, ClientCapabilities clientCapabilities) => new()
    {
        DocumentSelector = TextDocumentSelector.ForLanguage(LanguageCore.LanguageConstants.LanguageId),
        ResolveProvider = false,
    };
}
