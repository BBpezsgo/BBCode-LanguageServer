using MediatR;

namespace LanguageServer.Handlers;

sealed class CompletionHandler : ICompletionHandler
{
    Task<CompletionList> IRequestHandler<CompletionParams, CompletionList>.Handle(CompletionParams e, CancellationToken cancellationToken) => Task.Run(() =>
    {
        Logger.Log($"CompletionHandler.Handle({e})");

        if (OmniSharpService.Instance?.Server == null) return new CompletionList();

        try
        {
            CompletionItem[]? result = OmniSharpService.Instance.Documents.Get(e.TextDocument)?.Completion(e);
            return new CompletionList(result ?? Array.Empty<CompletionItem>());
        }
        catch (ServiceException error)
        {
            OmniSharpService.Instance?.Server?.Window?.ShowWarning($"ServiceException: {error.Message}");
            return new CompletionList();
        }
    });

    public CompletionRegistrationOptions GetRegistrationOptions(CompletionCapability capability, ClientCapabilities clientCapabilities)
    {
        capability.ContextSupport = false;
        return new CompletionRegistrationOptions()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage(LanguageCore.LanguageConstants.LanguageId),
            ResolveProvider = false,
        };
    }
}
