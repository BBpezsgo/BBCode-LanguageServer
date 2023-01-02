﻿using MediatR;

using Microsoft.Language.Xml;

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BBCodeLanguageServer.Managers
{
    class TextDocumentHandler : TextDocumentSyncHandlerBase
    {
        readonly ILanguageServerFacade Router;
        readonly Interface.Managers.BufferManager BufferManager;

        readonly DocumentSelector DocumentSelector = new(new DocumentFilter()
        {
            Pattern = "**/*.bbc",
        });

        public TextDocumentHandler(ILanguageServerFacade router, Interface.Managers.BufferManager bufferManager)
        {
            Router = router;
            BufferManager = bufferManager;
        }

        public TextDocumentChangeRegistrationOptions GetRegistrationOptions() => new()
        {
            DocumentSelector = DocumentSelector,
            SyncKind = TextDocumentSyncKind.Full,
        };

        public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri) => new(uri, "bbc");

        public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
        {
            Logger.Log($"OnDocumentOpen({request.TextDocument.Uri.ToUri()})");

            BufferManager.UpdateBuffer(request.TextDocument.Uri.ToUri(), new StringBuffer(request.TextDocument.Text));

            return Unit.Task;
        }

        public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
        {
            Logger.Log($"OnDocumentChange({request.TextDocument.Uri.ToUri()})");

            var uri = request.TextDocument.Uri.ToUri();
            var text = request.ContentChanges.FirstOrDefault()?.Text;

            BufferManager.UpdateBuffer(uri, new StringBuffer(text));

            return Unit.Task;
        }

        public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken)
        {
            Logger.Log($"OnDocumentSave({request.TextDocument.Uri.ToUri()})");

            BufferManager.Interface.OnDocumentSavedExternal(request);

            return Unit.Task;
        }

        public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
        {
            Logger.Log($"OnDocumentClose({request.TextDocument.Uri.ToUri()})");

            BufferManager.Interface.OnDocumentClosedExternal(request);

            return Unit.Task;
        }

        protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(SynchronizationCapability capability, ClientCapabilities clientCapabilities) => new()
        {
            DocumentSelector = DocumentSelector,
        };
    }
}