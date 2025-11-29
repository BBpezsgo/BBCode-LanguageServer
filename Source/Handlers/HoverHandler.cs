using MediatR;

namespace LanguageServer.Handlers;

sealed class HoverHandler : IHoverHandler
{
    Task<Hover?> IRequestHandler<HoverParams, Hover?>.Handle(HoverParams e, CancellationToken cancellationToken) => Task.Run(() =>
    {
        Logger.Log($"HoverHandler.Handle({e})");

        if (OmniSharpService.Instance?.Server == null) return null;

        try
        {
            return OmniSharpService.Instance.Documents.Get(e.TextDocument)?.Hover(e);
        }
        catch (ServiceException error)
        {
            OmniSharpService.Instance?.Server?.Window?.ShowWarning($"ServiceException: {error.Message}");
            return null;
        }
    });

    public HoverRegistrationOptions GetRegistrationOptions(HoverCapability capability, ClientCapabilities clientCapabilities) => new()
    {
        DocumentSelector = TextDocumentSelector.ForLanguage(LanguageCore.LanguageConstants.LanguageId),
    };
}
