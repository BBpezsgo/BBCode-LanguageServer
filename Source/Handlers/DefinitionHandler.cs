using MediatR;

namespace LanguageServer.Handlers;

sealed class DefinitionHandler : IDefinitionHandler
{
    Task<LocationOrLocationLinks?> IRequestHandler<DefinitionParams, LocationOrLocationLinks?>.Handle(DefinitionParams e, CancellationToken cancellationToken) => Task.Run(() =>
    {
        Logger.Log($"DefinitionHandler.Handle({e})");

        if (OmniSharpService.Instance?.Server == null) return null;

        try
        {
            return OmniSharpService.Instance.Documents.Get(e.TextDocument)?.GotoDefinition(e);
        }
        catch (ServiceException error)
        {
            OmniSharpService.Instance?.Server?.Window?.ShowWarning($"ServiceException: {error.Message}");
            return null;
        }
    });

    public DefinitionRegistrationOptions GetRegistrationOptions(DefinitionCapability capability, ClientCapabilities clientCapabilities) => new()
    {
        DocumentSelector = TextDocumentSelector.ForLanguage(LanguageCore.LanguageConstants.LanguageId),
    };
}
