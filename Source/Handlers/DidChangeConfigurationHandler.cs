using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;

namespace LanguageServer.Handlers;

sealed class DidChangeConfigurationHandler : IDidChangeConfigurationHandler
{
    Task<Unit> IRequestHandler<DidChangeConfigurationParams, Unit>.Handle(DidChangeConfigurationParams e, CancellationToken cancellationToken) => Task.Run(() =>
    {
        Logger.Log($"DidChangeConfigurationHandler.Handle({e})");

        OmniSharpService.Instance?.OnConfigChanged(e);

        return Unit.Value;
    });

    public void SetCapability(DidChangeConfigurationCapability capability, ClientCapabilities clientCapabilities)
    { }
}
