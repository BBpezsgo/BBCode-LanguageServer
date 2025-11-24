using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;

namespace LanguageServer.Handlers;

class DocumentSymbolHandler : IDocumentSymbolHandler
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

class CompletionHandler : ICompletionHandler
{
    Task<CompletionList> IRequestHandler<CompletionParams, CompletionList>.Handle(CompletionParams e, CancellationToken cancellationToken) => Task.Run(() =>
    {
        Logger.Log($"CompletionHandler.Handle({e})");

        if (OmniSharpService.Instance?.Server == null) return new CompletionList();

        try
        {
            CompletionItem[]? result = OmniSharpService.Instance.Documents.Get(e.TextDocument)?.Completion(e);
            return new CompletionList(result ?? Array.Empty<CompletionItem>());
        }
        catch (ServiceException error)
        {
            OmniSharpService.Instance?.Server?.Window?.ShowWarning($"ServiceException: {error.Message}");
            return new CompletionList();
        }
    });

    public CompletionRegistrationOptions GetRegistrationOptions(CompletionCapability capability, ClientCapabilities clientCapabilities)
    {
        capability.ContextSupport = false;
        return new CompletionRegistrationOptions()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage(LanguageCore.LanguageConstants.LanguageId),
            ResolveProvider = false,
        };
    }
}

class CodeLensHandler : ICodeLensHandler
{
    Task<CodeLensContainer?> IRequestHandler<CodeLensParams, CodeLensContainer?>.Handle(CodeLensParams e, CancellationToken cancellationToken) => Task.Run(() =>
    {
        Logger.Log($"CodeLensHandler.Handle({e})");

        if (OmniSharpService.Instance?.Server == null) return null;

        try
        {
            CodeLens[]? result = OmniSharpService.Instance.Documents.Get(e.TextDocument)?.CodeLens(e);
            return new CodeLensContainer(result ?? Array.Empty<CodeLens>());
        }
        catch (ServiceException error)
        {
            OmniSharpService.Instance?.Server?.Window?.ShowWarning($"ServiceException: {error.Message}");
            return null;
        }
    });

    public CodeLensRegistrationOptions GetRegistrationOptions(CodeLensCapability capability, ClientCapabilities clientCapabilities) => new()
    {
        DocumentSelector = TextDocumentSelector.ForLanguage(LanguageCore.LanguageConstants.LanguageId),
        ResolveProvider = false,
    };
}

class DefinitionHandler : IDefinitionHandler
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

class HoverHandler : IHoverHandler
{
    Task<Hover?> IRequestHandler<HoverParams, Hover?>.Handle(HoverParams e, CancellationToken cancellationToken) => Task.Run(() =>
    {
        Logger.Log($"HoverHandler.Handle({e})");

        if (OmniSharpService.Instance?.Server == null) return null;

        try
        {
            return OmniSharpService.Instance.Documents.Get(e.TextDocument)?.Hover(e);
        }
        catch (ServiceException error)
        {
            OmniSharpService.Instance?.Server?.Window?.ShowWarning($"ServiceException: {error.Message}");
            return null;
        }
    });

    public HoverRegistrationOptions GetRegistrationOptions(HoverCapability capability, ClientCapabilities clientCapabilities) => new()
    {
        DocumentSelector = TextDocumentSelector.ForLanguage(LanguageCore.LanguageConstants.LanguageId),
    };
}

class ReferencesHandler : IReferencesHandler
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

class SignatureHelpHandler : ISignatureHelpHandler
{
    public Task<SignatureHelp?> Handle(SignatureHelpParams e, CancellationToken cancellationToken) => Task.Run(() =>
    {
        Logger.Log($"SignatureHelpHandler.Handle({e})");

        if (OmniSharpService.Instance?.Server == null) return null;

        try
        {
            return OmniSharpService.Instance.Documents.Get(e.TextDocument)?.SignatureHelp(e);
        }
        catch (ServiceException error)
        {
            OmniSharpService.Instance?.Server?.Window?.ShowWarning($"ServiceException: {error.Message}");
            return null;
        }
    });

    public SignatureHelpRegistrationOptions GetRegistrationOptions(SignatureHelpCapability capability, ClientCapabilities clientCapabilities) => new()
    {
        DocumentSelector = TextDocumentSelector.ForLanguage(LanguageCore.LanguageConstants.LanguageId),
        TriggerCharacters = new Container<string>("(", ","),
    };
}

class DidChangeConfigurationHandler : IDidChangeConfigurationHandler
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

class SemanticTokensHandler : SemanticTokensHandlerBase
{
    protected override Task Tokenize(SemanticTokensBuilder builder, ITextDocumentIdentifierParams identifier, CancellationToken cancellationToken)
    {
        Logger.Log($"SemanticTokensHandler.Tokenize({identifier})");

        OmniSharpService.Instance?.Documents.Get(identifier.TextDocument)?.GetSemanticTokens(builder, identifier);

        return Task.CompletedTask;
    }

    protected override Task<SemanticTokensDocument> GetSemanticTokensDocument(ITextDocumentIdentifierParams @params, CancellationToken cancellationToken)
        => Task.FromResult(new SemanticTokensDocument(RegistrationOptions.Legend));

    protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(SemanticTokensCapability capability, ClientCapabilities clientCapabilities) => new()
    {
        DocumentSelector = TextDocumentSelector.ForLanguage(LanguageCore.LanguageConstants.LanguageId),
        Legend = new SemanticTokensLegend()
        {
            TokenModifiers = capability.TokenModifiers,
            TokenTypes = capability.TokenTypes,
        },
        Full = new SemanticTokensCapabilityRequestFull()
        {
            Delta = false,
        },
        Range = false,
    };
}
