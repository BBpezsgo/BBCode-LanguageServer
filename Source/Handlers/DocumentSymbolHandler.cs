using MediatR;

namespace LanguageServer.Handlers;

sealed class DocumentSymbolHandler : IDocumentSymbolHandler
{
    Task<SymbolInformationOrDocumentSymbolContainer?> IRequestHandler<DocumentSymbolParams, SymbolInformationOrDocumentSymbolContainer?>.Handle(DocumentSymbolParams e, CancellationToken cancellationToken) => Task.Run(() =>
    {
        Logger.Log($"DocumentSymbolHandler.Handle({e})");

        if (OmniSharpService.Instance?.Server == null) return null;

        try
        {
            SymbolInformationOrDocumentSymbol[]? result = OmniSharpService.Instance.Documents.Get(e.TextDocument)?.Symbols(e);
            return new SymbolInformationOrDocumentSymbolContainer(result ?? Array.Empty<SymbolInformationOrDocumentSymbol>());
        }
        catch (ServiceException error)
        {
            OmniSharpService.Instance?.Server?.Window?.ShowWarning($"ServiceException: {error.Message}");
            return null;
        }
    });

    public DocumentSymbolRegistrationOptions GetRegistrationOptions(DocumentSymbolCapability capability, ClientCapabilities clientCapabilities)
    {
        capability.HierarchicalDocumentSymbolSupport = true;
        return new DocumentSymbolRegistrationOptions()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage(LanguageCore.LanguageConstants.LanguageId),
        };
    }
}
