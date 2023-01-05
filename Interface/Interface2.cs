using BBCodeLanguageServer.Interface.Managers;

using LanguageServer.Parameters.General;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using OmniSharp.Extensions.LanguageServer.Server;

using System;
using System.Threading.Tasks;

using DiagnosticSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;

namespace BBCodeLanguageServer.Interface
{
    internal class ServiceAppInterface2 : IInterface
    {
        internal ILanguageServer Server;
        internal IServiceProvider ServiceProvider;
        internal Managers.BufferManager BufferManager;
        Logger2 logger;

        internal static ServiceAppInterface2 Instance { get; private set; }

        event ServiceAppEvent OnInitialize;
        event ServiceAppEvent<DocumentItemEventArgs> OnDocumentChanged;
        event ServiceAppEvent<DocumentItemEventArgs> OnDocumentOpened;
        event ServiceAppEvent<DocumentEventArgs> OnDocumentClosed;
        event ServiceAppEvent<ConfigEventArgs> OnConfigChanged;
        event ServiceAppEvent<DocumentEventArgs, CodeLensInfo[]> OnCodeLens;
        event ServiceAppEvent<DocumentPositionContextEventArgs, CompletionInfo[]> OnCompletion;
        event ServiceAppEvent<DocumentEventArgs, SymbolInformationInfo[]> OnDocumentSymbols;
        event ServiceAppEvent<DocumentPositionEventArgs, SingleOrArray<FilePosition>?> OnGotoDefinition;
        event ServiceAppEvent<DocumentPositionEventArgs, HoverInfo> OnHover;
        event ServiceAppEvent<FindReferencesEventArgs, FilePosition[]> OnReferences;

        internal ServiceAppInterface2()
        {
            Instance = this;
        }

        event ServiceAppEvent IInterface.OnInitialize
        {
            add => OnInitialize += value;
            remove => OnInitialize -= value;
        }
        event ServiceAppEvent<DocumentItemEventArgs> IInterface.OnDocumentChanged
        {
            add => OnDocumentChanged += value;
            remove => OnDocumentChanged -= value;
        }
        event ServiceAppEvent<DocumentItemEventArgs> IInterface.OnDocumentOpened
        {
            add => OnDocumentOpened += value;
            remove => OnDocumentOpened -= value;
        }
        event ServiceAppEvent<DocumentEventArgs> IInterface.OnDocumentClosed
        {
            add => OnDocumentClosed += value;
            remove => OnDocumentClosed -= value;
        }
        event ServiceAppEvent<ConfigEventArgs> IInterface.OnConfigChanged
        {
            add => OnConfigChanged += value;
            remove => OnConfigChanged -= value;
        }
        event ServiceAppEvent<DocumentEventArgs, CodeLensInfo[]> IInterface.OnCodeLens
        {
            add => OnCodeLens += value;
            remove => OnCodeLens -= value;
        }
        event ServiceAppEvent<DocumentPositionContextEventArgs, CompletionInfo[]> IInterface.OnCompletion
        {
            add => OnCompletion += value;
            remove => OnCompletion -= value;
        }
        event ServiceAppEvent<DocumentEventArgs, SymbolInformationInfo[]> IInterface.OnDocumentSymbols
        {
            add => OnDocumentSymbols += value;
            remove => OnDocumentSymbols -= value;
        }
        event ServiceAppEvent<DocumentPositionEventArgs, SingleOrArray<FilePosition>?> IInterface.OnGotoDefinition
        {
            add => OnGotoDefinition += value;
            remove => OnGotoDefinition -= value;
        }
        event ServiceAppEvent<DocumentPositionEventArgs, HoverInfo> IInterface.OnHover
        {
            add => OnHover += value;
            remove => OnHover -= value;
        }
        event ServiceAppEvent<FindReferencesEventArgs, FilePosition[]> IInterface.OnReferences
        {
            add => OnReferences += value;
            remove => OnReferences -= value;
        }

        internal string GetDocumentContent(Uri uri)
        {
            Microsoft.Language.Xml.Buffer buffer = BufferManager.GetBuffer(uri);
            if (buffer == null) throw new ServiceException($"Buffer {uri} not found");
            string text = buffer.GetText(0, buffer.Length);
            if (text != null) return text;
            throw new ServiceException($"Document {uri} is not buffered");
        }

        internal async Task CreateAsync()
        {
            logger = new Logger2();
            Logger.Setup(logger);

            this.Server = await OmniSharp.Extensions.LanguageServer.Server.LanguageServer.From(Configure).ConfigureAwait(false);

            Logger.Log("Server is created, logger is active");

            await this.Server.WaitForExit.ConfigureAwait(false);
        }

        class Logger2 : ILogger
        {
            public void Send(LanguageServer.Parameters.Window.MessageType type, string message)
            {
                switch (type)
                {
                    case LanguageServer.Parameters.Window.MessageType.Error:
                        Instance?.Server?.Window.LogError(message);
                        break;
                    case LanguageServer.Parameters.Window.MessageType.Warning:
                        Instance?.Server?.Window.LogWarning(message);
                        break;
                    case LanguageServer.Parameters.Window.MessageType.Info:
                        Instance?.Server?.Window.LogInfo(message);
                        break;
                    case LanguageServer.Parameters.Window.MessageType.Log:
                        Instance?.Server?.Window.Log(message);
                        break;
                    default:
                        break;
                }
            }
        }

        void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton(provider =>
            {
                ILoggerFactory loggerFactory = provider.GetService<ILoggerFactory>();
                ILogger<Foo> logger = loggerFactory.CreateLogger<Foo>();

                logger.LogInformation("Configuring");

                return new Foo(logger);
            });
            services.AddSingleton(
                provider =>
                {
                    var loggerFactory = provider.GetService<ILoggerFactory>();
                    var logger = loggerFactory.CreateLogger<Foo>();

                    logger.LogInformation("Configuring");

                    return new Foo(logger);
                }
            );
            services.AddSingleton(
                new ConfigurationItem
                {
                    Section = "typescript",
                }
            ).AddSingleton(
                new ConfigurationItem
                {
                    Section = "terminal",
                }
            );
            services.AddSingleton<Managers.BufferManager>();
            services.AddSingleton<Managers.DiagnosticsHandler>();
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
               .WithHandler<BBCodeLanguageServer.Managers.TextDocumentHandler>()
               .WithHandler<Managers.DocumentSymbolHandler>()
               .WithHandler<Managers.CodeLensHandler>()
               .WithHandler<Managers.CompletionHandler>()
               .WithHandler<Managers.DefinitionHandler>()
               .WithHandler<Managers.HoverHandler>()
               .WithHandler<Managers.DidChangeConfigurationHandler>()
               .WithHandler<Managers.SemanticTokensHandler>()
               .WithHandler<Managers.ReferencesHandler>();

            options.OnInitialize((server, e, cancellationToken) =>
            {
                e.Capabilities.TextDocument.SemanticTokens = new OmniSharp.Extensions.LanguageServer.Protocol.Supports<OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.SemanticTokensCapability>(true, new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.SemanticTokensCapability()
                {
                    TokenTypes = new Container<SemanticTokenType>(SemanticTokenType.Defaults),
                    TokenModifiers = new Container<SemanticTokenModifier>(SemanticTokenModifier.Defaults),
                    MultilineTokenSupport = false,
                    OverlappingTokenSupport = false,
                    Formats = new Container<SemanticTokenFormat>(SemanticTokenFormat.Defaults),
                });
                server.Window.Log($"Initialize() ...");
                this.ServiceProvider = (server as OmniSharp.Extensions.LanguageServer.Server.LanguageServer).Services;
                this.BufferManager = ServiceProvider.GetService<Managers.BufferManager>();
                BufferManager.Interface = this;
                var diagnosticsHandler = ServiceProvider.GetService<Managers.DiagnosticsHandler>();
                diagnosticsHandler.Interface = this;
                return Task.CompletedTask;
            });

            options.OnInitialized((server, e, result, cancellationToken) =>
            {
                server.Window.Log($"Initialized()");
                OnInitialize?.Invoke();
                return Task.CompletedTask;
            });

            options.OnStarted((server, cancellationToken) =>
            {
                server.Window.Log($"OnStarted()");
                return Task.CompletedTask;
            });
        }

        internal class Foo
        {
            private readonly ILogger<Foo> _logger;

            public Foo(ILogger<Foo> logger)
            {
                logger.LogInformation("inside ctor");
                _logger = logger;
            }

            public void SayFoo()
            {
                _logger.LogInformation("Fooooo!");
            }
        }

        void IInterface.PublishDiagnostics(Uri uri, DiagnosticInfo[] diagnostics)
        {
            Diagnostic[] diagnostics_ = new Diagnostic[diagnostics.Length];

            for (int i = 0; i < diagnostics.Length; i++)
            {
                diagnostics_[i] = new Diagnostic()
                {
                    Message = diagnostics[i].message,
                    Severity = (DiagnosticSeverity)((int)diagnostics[i].severity),
                    Source = diagnostics[i].source,
                    Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range()
                    {
                        Start = new Position((int)diagnostics[i].range.start.line, (int)diagnostics[i].range.start.character),
                        End = new Position((int)diagnostics[i].range.end.line, (int)diagnostics[i].range.end.character),
                    },
                };
            }

            Server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
            {
                Uri = uri,
                Diagnostics = diagnostics_
            });
        }

        #region External Evens

        internal SymbolInformationInfo[] OnDocumentSymbolsExternal(DocumentEventArgs e) => OnDocumentSymbols?.Invoke(e);
        internal CodeLensInfo[] OnCodeLensExternal(DocumentEventArgs e) => OnCodeLens?.Invoke(e);
        internal CompletionInfo[] OnCompletionExternal(DocumentPositionContextEventArgs e) => OnCompletion?.Invoke(e);
        internal SingleOrArray<FilePosition>? OnGotoDefinitionExternal(DocumentPositionEventArgs e) => OnGotoDefinition?.Invoke(e);
        internal HoverInfo OnHoverExternal(DocumentPositionEventArgs e) => OnHover?.Invoke(e);
        internal void OnDocumentSavedExternal(DidSaveTextDocumentParams e)
        {
            if (Server == null) return;
        }
        internal void OnDocumentClosedExternal(DidCloseTextDocumentParams e)
        {
            if (Server == null) return;
            OnDocumentClosed?.Invoke(new DocumentEventArgs(new Document(e.TextDocument)));
        }
        internal void OnDocumentChangedExternal(Managers.BufferManager.DocumentEventArgs e)
        {
            if (Server == null) return;
            OnDocumentChanged?.Invoke(new DocumentItemEventArgs(new DocumentItem(e.Uri, GetDocumentContent(e.Uri), "bbc")));
        }
        internal void OnDocumentOpenedExternal(BufferManager.DocumentEventArgs e)
        {
            if (Server == null) return;
            OnDocumentOpened?.Invoke(new DocumentItemEventArgs(new DocumentItem(e.Uri, GetDocumentContent(e.Uri), "bbc")));
        }
        internal FilePosition[] OnReferencesExternal(ReferenceParams e) => OnReferences?.Invoke(new FindReferencesEventArgs(e));
        internal void OnConfigChangedExternal(DidChangeConfigurationParams e) => OnConfigChanged?.Invoke(new ConfigEventArgs(e));

        #endregion
    }

    namespace Managers
    {
        using MediatR;

        using OmniSharp.Extensions.LanguageServer.Protocol;
        using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
        using OmniSharp.Extensions.LanguageServer.Protocol.Document;
        using OmniSharp.Extensions.LanguageServer.Protocol.Models;
        using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;

        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;

        class DiagnosticsHandler
        {
            readonly ILanguageServerFacade Router;
            internal ServiceAppInterface2 Interface;

            public DiagnosticsHandler(ILanguageServerFacade router, BufferManager bufferManager)
            {
                Router = router;
            }

            public void PublishDiagnostics(Uri uri, Microsoft.Language.Xml.Buffer buffer)
            {
                Router.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
                {
                    Uri = uri,
                    Diagnostics = new List<Diagnostic>(),
                });
            }
        }

        class BufferManager
        {
            internal ServiceAppInterface2 Interface;
            readonly System.Collections.Concurrent.ConcurrentDictionary<Uri, Microsoft.Language.Xml.Buffer> Buffers = new();

            public void UpdateBuffer(Uri uri, Microsoft.Language.Xml.Buffer buffer)
            {
                Buffers.AddOrUpdate(uri, buffer, (k, v) => buffer);
            }
            public Microsoft.Language.Xml.Buffer GetBuffer(Uri uri)
            {
                return Buffers.TryGetValue(uri, out var buffer) ? buffer : null;
            }

            public class DocumentEventArgs : EventArgs
            {
                public Uri Uri { get; }

                public DocumentEventArgs(Uri uri)
                {
                    Uri = uri;
                }
            }
        }

        internal class DocumentSymbolHandler : IDocumentSymbolHandler
        {
            Task<SymbolInformationOrDocumentSymbolContainer> IRequestHandler<DocumentSymbolParams, SymbolInformationOrDocumentSymbolContainer>.Handle(DocumentSymbolParams e, CancellationToken cancellationToken) => Task.Run(() =>
            {
                Logger.Log($"Symbols()");

                if (ServiceAppInterface2.Instance.Server == null) return null;

                try
                {
                    SymbolInformationInfo[] result = ServiceAppInterface2.Instance.OnDocumentSymbolsExternal(new DocumentEventArgs(new Document(new DocumentItem(e.TextDocument.Uri.ToUri(), ServiceAppInterface2.Instance.GetDocumentContent(e.TextDocument.Uri.ToUri()), "bbc"))));
                    return new SymbolInformationOrDocumentSymbolContainer(result.Convert2());
                }
                catch (ServiceException error)
                {
                    ServiceAppInterface2.Instance?.Server?.Window?.ShowWarning($"ServiceException: {error.Message}");
                    return new SymbolInformationOrDocumentSymbolContainer();
                }
            });

            public DocumentSymbolRegistrationOptions GetRegistrationOptions(DocumentSymbolCapability capability, ClientCapabilities clientCapabilities)
            {
                capability.HierarchicalDocumentSymbolSupport = true;
                return new DocumentSymbolRegistrationOptions()
                {
                    DocumentSelector = DocumentSelector.ForLanguage("bbc"),
                };
            }
        }

        internal class CompletionHandler : ICompletionHandler
        {
            Task<CompletionList> IRequestHandler<CompletionParams, CompletionList>.Handle(CompletionParams e, CancellationToken cancellationToken) => Task.Run(() =>
            {
                Logger.Log($"CompletionHandler.Handle()");

                if (ServiceAppInterface2.Instance.Server == null) return null;

                try
                {
                    CompletionInfo[] result = ServiceAppInterface2.Instance.OnCompletionExternal(new DocumentPositionContextEventArgs(e, ServiceAppInterface2.Instance.GetDocumentContent(e.TextDocument.Uri.ToUri())));
                    return new CompletionList(result.Convert2());
                }
                catch (ServiceException error)
                {
                    ServiceAppInterface2.Instance?.Server?.Window?.ShowWarning($"ServiceException: {error.Message}");
                    return new CompletionList();
                }
            });

            public CompletionRegistrationOptions GetRegistrationOptions(CompletionCapability capability, ClientCapabilities clientCapabilities)
            {
                capability.ContextSupport = false;
                return new CompletionRegistrationOptions()
                {
                    DocumentSelector = DocumentSelector.ForLanguage("bbc"),
                    ResolveProvider = false,
                };
            }
        }

        internal class CodeLensHandler : ICodeLensHandler
        {
            Task<CodeLensContainer> IRequestHandler<CodeLensParams, CodeLensContainer>.Handle(CodeLensParams e, CancellationToken cancellationToken) => Task.Run(() =>
            {
                Logger.Log($"CodeLens()");

                if (ServiceAppInterface2.Instance.Server == null) return null;

                try
                {
                    var result = ServiceAppInterface2.Instance.OnCodeLensExternal(new DocumentEventArgs(new Document(new DocumentItem(e.TextDocument.Uri.ToUri(), ServiceAppInterface2.Instance.GetDocumentContent(e.TextDocument.Uri.ToUri()), "bbc"))));
                    return new CodeLensContainer(result.Convert2());
                }
                catch (ServiceException error)
                {
                    ServiceAppInterface2.Instance?.Server?.Window?.ShowWarning($"ServiceException: {error.Message}");
                    return new CodeLensContainer();
                }
            });

            public CodeLensRegistrationOptions GetRegistrationOptions(CodeLensCapability capability, ClientCapabilities clientCapabilities)
            {
                return new CodeLensRegistrationOptions()
                {
                    DocumentSelector = DocumentSelector.ForLanguage("bbc"),
                    ResolveProvider = false,
                };
            }
        }

        internal class DefinitionHandler : IDefinitionHandler
        {
            Task<LocationOrLocationLinks> IRequestHandler<DefinitionParams, LocationOrLocationLinks>.Handle(DefinitionParams e, CancellationToken cancellationToken) => Task.Run(() =>
            {
                Logger.Log($"DefinitionHandler.Handle()");

                if (ServiceAppInterface2.Instance.Server == null) return null;

                try
                {
                    var result = ServiceAppInterface2.Instance.OnGotoDefinitionExternal(new DocumentPositionEventArgs(e));
                    if (!result.HasValue) return new LocationOrLocationLinks();
                    return new LocationOrLocationLinks(result.Value.v.Convert2());
                }
                catch (ServiceException error)
                {
                    ServiceAppInterface2.Instance?.Server?.Window?.ShowWarning($"ServiceException: {error.Message}");
                    return new LocationOrLocationLinks();
                }
            });

            public DefinitionRegistrationOptions GetRegistrationOptions(DefinitionCapability capability, ClientCapabilities clientCapabilities)
            {
                return new DefinitionRegistrationOptions()
                {
                    DocumentSelector = DocumentSelector.ForLanguage("bbc"),
                };
            }
        }

        internal class HoverHandler : IHoverHandler
        {
            Task<Hover> IRequestHandler<HoverParams, Hover>.Handle(HoverParams e, CancellationToken cancellationToken) => Task.Run(() =>
            {
                Logger.Log($"DefinitionHandler.Handle()");

                if (ServiceAppInterface2.Instance.Server == null) return null;

                try
                {
                    var result = ServiceAppInterface2.Instance.OnHoverExternal(new DocumentPositionEventArgs(e));
                    if (result == null) return new Hover();
                    return result.Convert2();
                }
                catch (ServiceException error)
                {
                    ServiceAppInterface2.Instance?.Server?.Window?.ShowWarning($"ServiceException: {error.Message}");
                    return new Hover();
                }
            });

            public HoverRegistrationOptions GetRegistrationOptions(HoverCapability capability, ClientCapabilities clientCapabilities)
            {
                return new HoverRegistrationOptions()
                {
                    DocumentSelector = DocumentSelector.ForLanguage("bbc"),
                };
            }
        }

        internal class ReferencesHandler : IReferencesHandler
        {
            Task<LocationContainer> IRequestHandler<ReferenceParams, LocationContainer>.Handle(ReferenceParams e, CancellationToken cancellationToken) => Task.Run(() =>
            {
                Logger.Log($"ReferencesHandler.Handle()");

                if (ServiceAppInterface2.Instance.Server == null) return null;

                try
                {
                    var result = ServiceAppInterface2.Instance.OnReferencesExternal(e);
                    if (result == null) return new LocationContainer();
                    var resultConverted = new Location[result.Length];
                    for (int i = 0; i < result.Length; i++)
                    {
                        resultConverted[i] = result[i].Convert2().Location;
                    }
                    return new LocationContainer(resultConverted);
                }
                catch (ServiceException error)
                {
                    ServiceAppInterface2.Instance?.Server?.Window?.ShowWarning($"ServiceException: {error.Message}");
                    return new LocationContainer();
                }
            });

            public ReferenceRegistrationOptions GetRegistrationOptions(ReferenceCapability capability, ClientCapabilities clientCapabilities)
            {
                return new ReferenceRegistrationOptions()
                {
                    DocumentSelector = DocumentSelector.ForLanguage("bbc"),
                };
            }
        }

        internal class DidChangeConfigurationHandler : IDidChangeConfigurationHandler
        {
            Task<Unit> IRequestHandler<DidChangeConfigurationParams, Unit>.Handle(DidChangeConfigurationParams e, CancellationToken cancellationToken) => Task.Run(() =>
            {
                Logger.Log($"DidChangeConfiguration()");

                ServiceAppInterface2.Instance.OnConfigChangedExternal(e);

                return Unit.Value;
            });

            public void SetCapability(DidChangeConfigurationCapability capability, ClientCapabilities clientCapabilities)
            {

            }
        }

        internal class SemanticTokensHandler : SemanticTokensHandlerBase
        {
            public override async Task<SemanticTokens> Handle(SemanticTokensParams request, CancellationToken cancellationToken)
            {
                Logger.Info($"SemanticTokensHandler.Handle()");

                var result = await base.Handle(request, cancellationToken).ConfigureAwait(false);
                return result;
            }

            public override async Task<SemanticTokens> Handle(SemanticTokensRangeParams request, CancellationToken cancellationToken)
            {
                Logger.Info($"SemanticTokensHandler.Handle_Range()");

                var result = await base.Handle(request, cancellationToken).ConfigureAwait(false);
                return result;
            }

            public override async Task<SemanticTokensFullOrDelta> Handle(SemanticTokensDeltaParams request, CancellationToken cancellationToken)
            {
                Logger.Info($"SemanticTokensHandler.Handle_Delta()");

                var result = await base.Handle(request, cancellationToken).ConfigureAwait(false);
                return result;
            }

            protected override async Task Tokenize(SemanticTokensBuilder builder, ITextDocumentIdentifierParams identifier, CancellationToken cancellationToken)
            {
                Logger.Info($"SemanticTokensHandler.Tokenize()");

                using var typesEnumerator = RotateEnum(SemanticTokenType.Defaults).GetEnumerator();
                using var modifiersEnumerator = RotateEnum(SemanticTokenModifier.Defaults).GetEnumerator();
                // you would normally get this from a common source that is managed by current open editor, current active editor, etc.
                var content = await System.IO.File.ReadAllTextAsync(DocumentUri.GetFileSystemPath(identifier), cancellationToken).ConfigureAwait(false);
                await Task.Yield();

                foreach (var (line, text) in content.Split('\n').Select((text, line) => (line, text)))
                {
                    var parts = text.TrimEnd().Split(';', ' ', '.', '"', '(', ')');
                    var index = 0;
                    foreach (var part in parts)
                    {
                        typesEnumerator.MoveNext();
                        modifiersEnumerator.MoveNext();
                        if (string.IsNullOrWhiteSpace(part)) continue;
                        index = text.IndexOf(part, index, StringComparison.Ordinal);
                        builder.Push(line, index, part.Length, SemanticTokenType.Interface, modifiersEnumerator.Current);
                    }
                }
            }

            static IEnumerable<T> RotateEnum<T>(IEnumerable<T> values)
            {
                while (true)
                {
                    foreach (var item in values)
                        yield return item;
                }
            }

            protected override Task<SemanticTokensDocument> GetSemanticTokensDocument(ITextDocumentIdentifierParams @params, CancellationToken cancellationToken)
            {
                Logger.Info($"SemanticTokensHandler.GetSemanticTokensDocument()");

                return Task.FromResult(new SemanticTokensDocument(RegistrationOptions.Legend));
            }

            protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(SemanticTokensCapability capability, ClientCapabilities clientCapabilities)
            {
                Logger.Info($"SemanticTokensHandler.CreateRegistrationOptions()");

                SemanticTokensRegistrationOptions result = new()
                {
                    DocumentSelector = DocumentSelector.ForLanguage("bcc"),
                    Full = new SemanticTokensCapabilityRequestFull
                    {
                        Delta = true,
                    },
                    Range = true,
                };
                capability = new SemanticTokensCapability
                {
                    TokenTypes = new Container<SemanticTokenType>(SemanticTokenType.Defaults),
                    TokenModifiers = new Container<SemanticTokenModifier>(SemanticTokenModifier.Defaults),
                    MultilineTokenSupport = false,
                    OverlappingTokenSupport = false,
                    Formats = new Container<SemanticTokenFormat>(SemanticTokenFormat.Defaults),
                    Requests = new SemanticTokensCapabilityRequests()
                    {
                        Full = new Supports<SemanticTokensCapabilityRequestFull>(true),
                        Range = new Supports<SemanticTokensCapabilityRequestRange>(true),
                    },
                };
                result.Legend = new SemanticTokensLegend()
                {
                    TokenTypes = capability.TokenTypes,
                    TokenModifiers = capability.TokenModifiers,
                };
                return result;
            }
        }
    }
}
