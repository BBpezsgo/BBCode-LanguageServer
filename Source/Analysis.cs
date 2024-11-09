using System.Collections.Immutable;
using System.IO;
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

        try
        {
            DiagnosticsCollection tokenizerDiagnostics = new();
            TokenizerResult tokenizerResult = AnyTokenizer.Tokenize(file, tokenizerDiagnostics, PreprocessorVariables.Normal, new TokenizerSettings(TokenizerSettings.Default)
            {
                TokenizeComments = true,
            });

            diagnostics.AddDiagnostics(tokenizerDiagnostics, "Tokenizer");

            if (OmniSharpService.Instance is not null)
            {
                DocumentHandler document2 = OmniSharpService.Instance.Documents.GetOrCreate(new TextDocumentIdentifier(file));
                if (document2 is DocumentBBLang documentBBLang2)
                { documentBBLang2.Tokens = tokenizerResult.Tokens; }
            }

            tokens = tokenizerResult.Tokens;
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

        try
        {
            DiagnosticsCollection parserDiagnostics = new();
            ParserResult ast = Parser.Parse(tokens, file, parserDiagnostics);

            diagnostics.AddDiagnostics(parserDiagnostics.Diagnostics, "Parser");

            parserResult = ast;
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

        try
        {
            string[] additionalImports = new string[]
            {
                "../StandardLibrary/Primitives.bbc"
            };

            DiagnosticsCollection _diagnostics = new();

            List<IExternalFunction> externalFunctions = BytecodeProcessorEx.GetExternalFunctions();

            CompilerResult compiled = Compiler.CompileFile(file, externalFunctions, settings, PreprocessorVariables.Normal, null, _diagnostics, TokenizerSettings, null, additionalImports);

            diagnostics.AddDiagnostics(_diagnostics.Diagnostics, "Compiler");

            compilerResult = compiled;
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

        compilerResult = default;
        return false;
    }

    static bool Generate(
        Dictionary<Uri, List<OmniSharpDiagnostic>> diagnostics,
        CompilerResult compilerResult,
        Uri file
        )
    {
        try
        {
            DiagnosticsCollection _diagnostics = new();

            CodeGeneratorForMain.Generate(
                compilerResult,
                new MainGeneratorSettings(MainGeneratorSettings.Default)
                {
                    CheckNullPointers = false,
                    DontOptimize = true,
                    ExternalFunctionsCache = false,
                    GenerateComments = false,
                    GenerateDebugInstructions = false,
                    PrintInstructions = false,
                    CompileLevel = CompileLevel.All,
                },
                null,
                _diagnostics);

            diagnostics.AddDiagnostics(_diagnostics.Diagnostics, "CodeGenerator");

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

        return false;
    }

    static string? GetBasePath(Uri uri)
    {
        if (!uri.IsFile) return null;
        string file = uri.AbsolutePath;
        if (!File.Exists(file)) return null;
        return GetBasePath(new FileInfo(file));
    }
    static string? GetBasePath(FileInfo file)
    {
        string? basePath = null;
        string? configFile = file.DirectoryName != null ? Path.Combine(file.DirectoryName, "config.json") : null;

        if (configFile != null && File.Exists(configFile))
        {
            string configRaw = File.ReadAllText(configFile);

            System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(configRaw, new System.Text.Json.JsonDocumentOptions()
            {
                AllowTrailingCommas = true,
                CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
            });

            if (doc.RootElement.TryGetProperty("base", out System.Text.Json.JsonElement property))
            { basePath = property.GetString(); }
        }

        if (basePath != null && file.DirectoryName != null)
        {
            basePath = Path.GetFullPath(basePath, file.DirectoryName);
        }

        return basePath;
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
            foreach (KeyValuePair<Uri, CollectedAST> ast in compilerResult.Raw)
            {
                DocumentHandler document = OmniSharpService.Instance.Documents.GetOrCreate(new TextDocumentIdentifier(ast.Key));
                if (document is DocumentBBLang documentBBLang)
                {
                    documentBBLang.AST = ast.Value.AST;
                    documentBBLang.Tokens = ast.Value.Tokens.Tokens;
                }
            }
        }

        result.CompilerResult = compilerResult;
        result.AST = compilerResult.Raw[file].AST;
        result.Tokens = compilerResult.Raw[file].Tokens.Tokens;

        if (!Generate(result.Diagnostics, compilerResult, file))
        { return result; }

        return result;
    }
}
