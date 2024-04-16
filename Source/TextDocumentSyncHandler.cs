using MediatR;

namespace LanguageServer.Handlers;

using LanguageServer;

class TextDocumentSyncHandler : TextDocumentSyncHandlerBase
{
    [SuppressMessage("CodeQuality", "IDE0052")]
    readonly ILanguageServerFacade Router;
    readonly Buffers Buffers;

    static readonly TextDocumentSelector DocumentSelector = new(new TextDocumentFilter() { Pattern = "**/*.bbc" });

    public TextDocumentSyncHandler(ILanguageServerFacade router, Buffers bufferManager)
    {
        Router = router;
        Buffers = bufferManager;
    }

    public static TextDocumentChangeRegistrationOptions GetRegistrationOptions() => new()
    {
        DocumentSelector = DocumentSelector,
        SyncKind = TextDocumentSyncKind.Full,
    };

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri document)
        => new(document, document.Extension());

    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
    {
        Logger.Log($"Document opened: \"{request.TextDocument.Uri}\"");

        OmniSharpService.Instance?.Documents.GetOrCreate(request.TextDocument, Buffers.Update(request)).OnOpened(request);

        return Unit.Task;
    }

    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
    {
        Logger.Log($"Document changed: \"{request.TextDocument.Uri}\"");

        OmniSharpService.Instance?.Documents.GetOrCreate(request.TextDocument, Buffers.Update(request)).OnChanged(request);

        return Unit.Task;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken)
    {
        Logger.Log($"Document saved: \"{request.TextDocument.Uri}\"");

        OmniSharpService.Instance?.Documents.GetOrCreate(request.TextDocument, Buffers.Update(request)).OnSaved(request);

        return Unit.Task;
    }

    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
    {
        Logger.Log($"Document closed: \"{request.TextDocument.Uri}\"");

        OmniSharpService.Instance?.Documents.Remove(request.TextDocument);

        return Unit.Task;
    }

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities) => new()
    {
        DocumentSelector = DocumentSelector,
    };
}