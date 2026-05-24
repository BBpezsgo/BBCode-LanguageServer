using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using LanguageServer.Handlers;

namespace LanguageServer;

sealed class OmniSharpService
{
    public static OmniSharpService? Instance { get; private set; }

    public ILanguageServer? Server { get; private set; }
    public IServiceProvider? ServiceProvider { get; private set; }
    public Documents Documents { get; }
    public JToken? Config { get; private set; }

    public OmniSharpService()
    {
        Instance = this;
        Documents = new Documents();
    }

    public async Task RunAsync()
    {
        Server = await OmniSharp.Extensions.LanguageServer.Server.LanguageServer.From(Configure).ConfigureAwait(false);

        Logger.Debug("Created");

        await Server.WaitForExit.ConfigureAwait(false);
    }

    void Configure(LanguageServerOptions options)
    {
#pragma warning disable CA2000 // Dispose objects before losing scope
        options
            .WithInput(Console.OpenStandardInput())
            .WithOutput(Console.OpenStandardOutput());
#pragma warning restore CA2000 // Dispose objects before losing scope

        options
            .ConfigureLogging(
               x => x
                   .AddLanguageProtocolLogging()
                   .SetMinimumLevel(LogLevel.Information)
           );

        options
           .WithServices(x => x.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace)));

        options
           .WithHandler<CodeActionHandler>()
           .WithHandler<CodeLensHandler>()
           .WithHandler<CompletionHandler>()
           .WithHandler<DeclarationHandler>()
           .WithHandler<DefinitionHandler>()
           .WithHandler<DidChangeConfigurationHandler>()
           .WithHandler<DocumentHighlightHandler>()
           .WithHandler<DocumentSymbolHandler>()
           //.WithHandler<DocumentFormattingHandler>()
           .WithHandler<HoverHandler>()
           .WithHandler<ImplementationHandler>()
           .WithHandler<InlayHintsHandler>()
           .WithHandler<ReferencesHandler>()
           .WithHandler<SemanticTokensHandler>()
           .WithHandler<SignatureHelpHandler>()
           .WithHandler<TypeDefinitionHandler>()

           .WithHandler<TextDocumentSyncHandler>()
           //.WithHandler<NotebookDocumentSyncHandler>()
        ;

        options.OnInitialize((server, request, cancellationToken) =>
        {
            if (request.Capabilities?.TextDocument is not null)
            {
                request.Capabilities.TextDocument.SemanticTokens = new SemanticTokensCapability()
                {
                    TokenTypes = new Container<SemanticTokenType>(SemanticTokenType.Defaults),
                    TokenModifiers = new Container<SemanticTokenModifier>(SemanticTokenModifier.Defaults),
                    MultilineTokenSupport = false,
                    OverlappingTokenSupport = false,
                    Formats = new Container<SemanticTokenFormat>(SemanticTokenFormat.Defaults),
                    Requests = new SemanticTokensCapabilityRequests()
                    {
                        Full = new Supports<SemanticTokensCapabilityRequestFull?>(true),
                        Range = new Supports<SemanticTokensCapabilityRequestRange?>(false),
                    },
                    AugmentsSyntaxTokens = true,
                    ServerCancelSupport = false,
                };
            }
            ServiceProvider = (server as OmniSharp.Extensions.LanguageServer.Server.LanguageServer)?.Services;
            return Task.CompletedTask;
        });

        options.OnStarted((_, _) =>
        {
            Logger.Debug("Started");
            return Task.CompletedTask;
        });

        options.OnInitialized((_, _, _, _) =>
        {
            Logger.Debug("Initialized");
            return Task.CompletedTask;
        });
    }

    public void OnConfigChanged(DidChangeConfigurationParams e)
    {
        Config = e.Settings;
    }
}
