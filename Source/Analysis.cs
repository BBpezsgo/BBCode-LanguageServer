using System.Collections.Immutable;
using LanguageCore;
using LanguageCore.BBLang.Generator;
using LanguageCore.Compiler;
using LanguageCore.Parser;
using LanguageCore.Runtime;
using LanguageCore.Tokenizing;
using LanguageServer.DocumentManagers;
using OmniSharpDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;

namespace LanguageServer;

public struct AnalysisResult
{
    public Dictionary<Uri, List<OmniSharpDiagnostic>> Diagnostics;
    public ImmutableArray<Token> Tokens;
    public ParserResult? AST;
    public CompilerResult? CompilerResult;

    public static AnalysisResult Empty => new()
    {
        Diagnostics = new Dictionary<Uri, List<OmniSharpDiagnostic>>(),
        Tokens = ImmutableArray<Token>.Empty,
        AST = null,
        CompilerResult = null,
    };
}

public static class Analysis
{
    static readonly TokenizerSettings TokenizerSettings = new(TokenizerSettings.Default)
    {
        TokenizeComments = true,
    };

    static readonly ImmutableArray<string> AdditionalImports = ImmutableArray.Create<string>
    (
        "../StandardLibrary/Primitives.bbc"
    );

    static readonly MainGeneratorSettings GeneratorSettings = new(MainGeneratorSettings.Default)
    {
        CheckNullPointers = false,
        DontOptimize = false,
        ExternalFunctionsCache = false,
        GenerateComments = false,
        GenerateDebugInstructions = false,
        PrintInstructions = false,
        CompileLevel = CompileLevel.All,
    };

    static void AddDiagnostics(
        this Dictionary<Uri, List<OmniSharpDiagnostic>> diagnostics,
        LanguageCore.Diagnostic value,
        string? source = null)
    {
        if (value.File is null) return;
        List<OmniSharpDiagnostic> list = diagnostics.EnsureExistence(value.File, new List<OmniSharpDiagnostic>());
        OmniSharpDiagnostic converted = value.ToOmniSharp(source);

        foreach (OmniSharpDiagnostic item in list)
        {
            if (item.Message == converted.Message &&
                item.Range == converted.Range)
            { return; }
        }

        list.Add(converted);
    }

    static void AddDiagnostics(
        this Dictionary<Uri, List<OmniSharpDiagnostic>> diagnostics,
        IEnumerable<LanguageCore.Diagnostic> value,
        string? source = null)
    {
        foreach (LanguageCore.Diagnostic diagnostic in value)
        { diagnostics.AddDiagnostics(diagnostic, source); }
    }

    static void AddDiagnostics(
        this Dictionary<Uri, List<OmniSharpDiagnostic>> diagnostics,
        DiagnosticsCollection value,
        string? source = null)
        => diagnostics.AddDiagnostics(value.Diagnostics, source);

    static bool Tokenize(
        Dictionary<Uri, List<OmniSharpDiagnostic>> diagnostics,
        Uri file,
        bool force,
        [NotNullWhen(true)] out ImmutableArray<Token> tokens)
    {
        if (!force &&
            OmniSharpService.Instance is not null &&
            OmniSharpService.Instance.Documents.TryGet(file, out DocumentHandler? document) &&
            document is DocumentBBLang documentBBLang)
        {
            tokens = documentBBLang.Tokens;
            return true;
        }

        DiagnosticsCollection tokenizerDiagnostics = new();

        try
        {
            tokens = AnyTokenizer.Tokenize(file, tokenizerDiagnostics, PreprocessorVariables.Normal, new TokenizerSettings(TokenizerSettings.Default)
            {
                TokenizeComments = true,
            }).Tokens;

            if (OmniSharpService.Instance is not null)
            {
                DocumentHandler document2 = OmniSharpService.Instance.Documents.GetOrCreate(new TextDocumentIdentifier(file));
                if (document2 is DocumentBBLang documentBBLang2)
                { documentBBLang2.Tokens = tokens; }
            }

            return !tokenizerDiagnostics.HasErrors;
        }
        catch (LanguageException exception)
        {
            Logger.Log($"{exception.GetType()}: {exception}");
            AddDiagnostics(diagnostics, (LanguageCore.Diagnostic)exception, "Tokenizer");
        }
        catch (Exception exception)
        {
            Logger.Log($"{exception.GetType()}: {exception}");
        }
        finally
        {
            diagnostics.AddDiagnostics(tokenizerDiagnostics, "Tokenizer");
        }

        tokens = ImmutableArray<Token>.Empty;
        return false;
    }

    static bool Parse(
        Dictionary<Uri, List<OmniSharpDiagnostic>> diagnostics,
        ImmutableArray<Token> tokens,
        Uri file,
        bool force,
        [NotNullWhen(true)] out ParserResult parserResult)
    {
        if (!force &&
            OmniSharpService.Instance is not null &&
            OmniSharpService.Instance.Documents.TryGet(file, out DocumentHandler? document) &&
            document is DocumentBBLang documentBBLang)
        {
            parserResult = documentBBLang.AST;
            return true;
        }

        DiagnosticsCollection parserDiagnostics = new();

        try
        {
            parserResult = Parser.Parse(tokens, file, parserDiagnostics);
            return !parserDiagnostics.HasErrors;
        }
        catch (LanguageException exception)
        {
            Logger.Log($"{exception.GetType()}: {exception}");
            AddDiagnostics(diagnostics, (LanguageCore.Diagnostic)exception, "Parser");
        }
        catch (Exception exception)
        {
            Logger.Log($"{exception.GetType()}: {exception}");
        }
        finally
        {
            diagnostics.AddDiagnostics(parserDiagnostics.Diagnostics, "Parser");
        }

        parserResult = default;
        return false;
    }

    static bool Compile(
        Dictionary<Uri, List<OmniSharpDiagnostic>> diagnostics,
        Uri file,
        bool force,
        CompilerSettings settings,
        [NotNullWhen(true)] out CompilerResult compilerResult)
    {
        if (!force &&
            OmniSharpService.Instance is not null &&
            OmniSharpService.Instance.Documents.TryGet(file, out DocumentHandler? document) &&
            document is DocumentBBLang documentBBLang)
        {
            compilerResult = documentBBLang.CompilerResult;
            return true;
        }

        DiagnosticsCollection _diagnostics = new();
        List<IExternalFunction> externalFunctions = BytecodeProcessorEx.GetExternalFunctions();

        try
        {
            compilerResult = Compiler.CompileFile(file, externalFunctions, settings, PreprocessorVariables.Normal, null, _diagnostics, TokenizerSettings, null, AdditionalImports);
            return !_diagnostics.HasErrors;
        }
        catch (LanguageException exception)
        {
            Logger.Log($"{exception.GetType()}: {exception}");
            AddDiagnostics(diagnostics, (LanguageCore.Diagnostic)exception, "Compiler");
        }
        catch (Exception exception)
        {
            Logger.Log($"{exception.GetType()}: {exception}");
        }
        finally
        {
            diagnostics.AddDiagnostics(_diagnostics.Diagnostics, "Compiler");
        }

        compilerResult = default;
        return false;
    }

    static bool Generate(
        Dictionary<Uri, List<OmniSharpDiagnostic>> diagnostics,
        CompilerResult compilerResult,
        Uri file
        )
    {
        DiagnosticsCollection _diagnostics = new();

        try
        {
            CodeGeneratorForMain.Generate(
                compilerResult,
                GeneratorSettings,
                null,
                _diagnostics);

            if (_diagnostics.HasErrors)
            { return false; }

            Logger.Log($"Successfully compiled {file}");
            return true;
        }
        catch (LanguageException exception)
        {
            Logger.Log($"{exception.GetType()}: {exception}");
            AddDiagnostics(diagnostics, (LanguageCore.Diagnostic)exception, "CodeGenerator");
        }
        catch (Exception exception)
        {
            Logger.Error($"{exception.GetType()}: {exception}");
        }
        finally
        {
            diagnostics.AddDiagnostics(_diagnostics.Diagnostics, "CodeGenerator");
        }

        return false;
    }

    static string? GetBasePath(Uri uri)
    {
        return "/home/BB/Projects/BBLang/Core/StandardLibrary";
    }

    public static AnalysisResult Analyze(Uri file)
    {
        AnalysisResult result = AnalysisResult.Empty;

        string? basePath = GetBasePath(file);

        result.Diagnostics = new Dictionary<Uri, List<OmniSharpDiagnostic>>()
        {
            { file, new List<OmniSharpDiagnostic>() }
        };

        if (!Compile(result.Diagnostics, file, true, new CompilerSettings() { BasePath = basePath }, out CompilerResult compilerResult))
        {
            if (Tokenize(result.Diagnostics, file, true, out ImmutableArray<Token> tokens))
            { result.Tokens = tokens; }

            if (Parse(result.Diagnostics, tokens, file, true, out ParserResult parserResult))
            { result.AST = parserResult; }

            return result;
        }

        if (OmniSharpService.Instance is not null)
        {
            foreach (CollectedAST ast in compilerResult.Raw)
            {
                DocumentHandler document = OmniSharpService.Instance.Documents.GetOrCreate(new TextDocumentIdentifier(ast.Uri));
                if (document is DocumentBBLang documentBBLang)
                {
                    documentBBLang.AST = ast.AST;
                    documentBBLang.Tokens = ast.Tokens.Tokens;
                }
            }
        }

        result.CompilerResult = compilerResult;
        result.AST = compilerResult.Raw.First(v => v.Uri == file).AST;
        result.Tokens = compilerResult.Raw.First(v => v.Uri == file).Tokens.Tokens;

        if (!Generate(result.Diagnostics, compilerResult, file))
        { return result; }

        return result;
    }
}
