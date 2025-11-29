using MediatR;

namespace LanguageServer.Handlers;

sealed class ReferencesHandler : IReferencesHandler
{
    Task<LocationContainer?> IRequestHandler<ReferenceParams, LocationContainer?>.Handle(ReferenceParams e, CancellationToken cancellationToken) => Task.Run(() =>
    {
        Logger.Log($"ReferencesHandler.Handle({e})");

        if (OmniSharpService.Instance?.Server == null) return null;

        try
        {
            Location[]? result = OmniSharpService.Instance.Documents.Get(e.TextDocument)?.References(e);
            if (result == null) return null;
            return new LocationContainer(result);
        }
        catch (ServiceException error)
        {
            OmniSharpService.Instance?.Server?.Window?.ShowWarning($"ServiceException: {error.Message}");
            return null;
        }
    });

    public ReferenceRegistrationOptions GetRegistrationOptions(ReferenceCapability capability, ClientCapabilities clientCapabilities) => new()
    {
        DocumentSelector = TextDocumentSelector.ForLanguage(LanguageCore.LanguageConstants.LanguageId),
    };
}
