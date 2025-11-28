using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using LanguageServer.Handlers;

namespace LanguageServer;

class OmniSharpService
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

    public async Task CreateAsync()
    {
        this.Server = await OmniSharp.Extensions.LanguageServer.Server.LanguageServer.From(Configure).ConfigureAwait(false);

        Logger.Log("Server is created, logger is active");

        await this.Server.WaitForExit.ConfigureAwait(false);
    }

    void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(new ConfigurationItem()
        {
            Section = "terminal",
        });
    }

    void Configure(LanguageServerOptions options)
    {
        options
            .WithInput(Console.OpenStandardInput())
            .WithOutput(Console.OpenStandardOutput());

        options
            .ConfigureLogging(
               x => x
                   .AddLanguageProtocolLogging()
                   .SetMinimumLevel(LogLevel.Information)
           );

        options
           .WithServices(x => x.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace)))
           .WithServices(this.ConfigureServices);

        options
           .WithHandler<TextDocumentSyncHandler>()
           .WithHandler<DocumentSymbolHandler>()
           .WithHandler<CodeLensHandler>()
           .WithHandler<CompletionHandler>()
           .WithHandler<DefinitionHandler>()
           .WithHandler<HoverHandler>()
           .WithHandler<DidChangeConfigurationHandler>()
           .WithHandler<SemanticTokensHandler>()
           .WithHandler<ReferencesHandler>()
           .WithHandler<SignatureHelpHandler>();

        options.OnInitialize((server, e, cancellationToken) =>
        {
            if (e.Capabilities?.TextDocument != null)
            {
                e.Capabilities.TextDocument.SemanticTokens = new SemanticTokensCapability()
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
                    ServerCancelSupport = false,
                };
            }
            this.ServiceProvider = (server as OmniSharp.Extensions.LanguageServer.Server.LanguageServer)?.Services;
            return Task.CompletedTask;
        });

        options.OnInitialized((server, e, result, cancellationToken) =>
        {
            server.Window.Log($"Initialized");
            return Task.CompletedTask;
        });

        options.OnStarted((server, cancellationToken) =>
        {
            server.Window.Log($"Started");
            return Task.CompletedTask;
        });
    }

    public void OnConfigChanged(DidChangeConfigurationParams e)
    {
        Config = e.Settings;
    }
}
