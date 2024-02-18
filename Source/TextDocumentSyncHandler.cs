using MediatR;

namespace LanguageServer.Handlers;

using LanguageServer;

class TextDocumentSyncHandler : TextDocumentSyncHandlerBase
{
    [SuppressMessage("CodeQuality", "IDE0052")]
    readonly ILanguageServerFacade Router;
    readonly Buffers Buffers;

    readonly TextDocumentSelector DocumentSelector = new(new TextDocumentFilter[] {
        new() { Pattern = "**/*.bbc" }
    });

    public TextDocumentSyncHandler(ILanguageServerFacade router, Buffers bufferManager)
    {
        Router = router;
        Buffers = bufferManager;
    }

    public TextDocumentChangeRegistrationOptions GetRegistrationOptions() => new()
    {
        DocumentSelector = DocumentSelector,
        SyncKind = TextDocumentSyncKind.Full,
    };

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri document)
        => new(document, document.Extension());

    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
    {
        Logger.Log($"OnDocumentOpen({request.TextDocument.Uri})");

        Microsoft.Language.Xml.StringBuffer buffer = Buffers.Update(request);

        OmniSharpService.Instance?.Documents.GetOrCreate(request.TextDocument, buffer).OnOpened(request);

        return Unit.Task;
    }

    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
    {
        Logger.Log($"OnDocumentChange({request.TextDocument.Uri})");

        Microsoft.Language.Xml.StringBuffer buffer = Buffers.Update(request);

        OmniSharpService.Instance?.Documents.GetOrCreate(request.TextDocument, buffer).OnChanged(request);

        return Unit.Task;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken)
    {
        Logger.Log($"OnDocumentSave({request.TextDocument.Uri})");

        // Microsoft.Language.Xml.StringBuffer? buffer = Buffers.Update(request);

        OmniSharpService.Instance?.Documents.GetOrCreate(request.TextDocument).OnSaved(request);

        return Unit.Task;
    }

    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
    {
        Logger.Log($"OnDocumentClose({request.TextDocument.Uri})");

        OmniSharpService.Instance?.Documents.Remove(request.TextDocument);

        return Unit.Task;
    }

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities) => new()
    {
        DocumentSelector = DocumentSelector,
    };
}