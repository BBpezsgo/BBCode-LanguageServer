using System.Collections.Immutable;
using System.IO;
using LanguageCore;
using LanguageCore.BBCode.Generator;
using LanguageCore.Compiler;
using LanguageCore.Parser;
using LanguageCore.Runtime;
using LanguageCore.Tokenizing;
using LanguageServer.DocumentManagers;

namespace LanguageServer;

public struct AnalysisResult
{
    public Dictionary<Uri, List<Diagnostic>> Diagnostics;
    public ImmutableArray<Token> Tokens;
    public ParserResult? AST;
    public CompilerResult? CompilerResult;

    public static AnalysisResult Empty => new()
    {
        Diagnostics = new Dictionary<Uri, List<Diagnostic>>(),
        Tokens = ImmutableArray<Token>.Empty,
        AST = null,
        CompilerResult = null,
    };
}

public readonly struct AnalyzedFile
{
    public readonly ParserResult AST;
    public readonly CompilerResult CompilerResult;

    public static AnalyzedFile Empty => new(ParserResult.Empty, CompilerResult.Empty);

    public AnalyzedFile(ParserResult ast, CompilerResult compilerResult)
    {
        AST = ast;
        CompilerResult = compilerResult;
    }
}

public static class Analysis
{
    static readonly TokenizerSettings TokenizerSettings = new(TokenizerSettings.Default)
    {
        TokenizeComments = true,
    };

    static void HandleCatchedExceptions(Dictionary<Uri, List<Diagnostic>> diagnostics, LanguageException exception, string? source)
    {
        diagnostics.AddDiagnostics(exception, v => v.ToOmniSharp(source));

        if (exception.InnerException is LanguageException innerException)
        {
            diagnostics.AddDiagnostics(innerException, v => v.ToOmniSharp(source));
        }
    }

    static void AddDiagnostics<TValue>(
        this Dictionary<Uri, List<Diagnostic>> diagnostics,
        TValue value,
        Func<TValue, Diagnostic> converter)
        where TValue : IInFile
    {
        if (value.FilePath is null) return;
        List<Diagnostic> list = diagnostics.EnsureExistence(value.FilePath, new List<Diagnostic>());
        Diagnostic converted = converter.Invoke(value);

        foreach (Diagnostic item in list)
        {
            if (item.Message == converted.Message &&
                item.Range == converted.Range)
            { return; }
        }

        list.Add(converted);
    }

    static void AddDiagnostics<TValue>(
        this Dictionary<Uri, List<Diagnostic>> diagnostics,
        IEnumerable<TValue> values,
        Func<TValue, Diagnostic> converter)
        where TValue : IInFile
    {
        foreach (TValue value in values)
        { diagnostics.AddDiagnostics(value, converter); }
    }

    static bool Tokenize(
        Dictionary<Uri, List<Diagnostic>> diagnostics,
        Uri file,
        bool force,
        [NotNullWhen(true)] out ImmutableArray<Token> tokens)
    {
        if (!force &&
            OmniSharpService.Instance is not null &&
            OmniSharpService.Instance.Documents.TryGet(file, out DocumentHandler? document) &&
            document is DocumentBBCode bbcDocument)
        {
            tokens = bbcDocument.Tokens;
            return true;
        }

        try
        {
            TokenizerResult tokenizerResult = AnyTokenizer.Tokenize(file, PreprocessorVariables.Normal, new TokenizerSettings(TokenizerSettings.Default)
            {
                TokenizeComments = true,
            });

            diagnostics.AddDiagnostics(tokenizerResult.Warnings, v => v.ToOmniSharp("Tokenizer"));

            if (OmniSharpService.Instance is not null)
            {
                DocumentHandler document2 = OmniSharpService.Instance.Documents.GetOrCreate(new TextDocumentIdentifier(file));
                if (document2 is DocumentBBCode bbcDocument2)
                { bbcDocument2.Tokens = tokenizerResult.Tokens; }
            }

            tokens = tokenizerResult.Tokens;
            return true;
        }
        catch (LanguageException exception)
        {
            Logger.Log($"{exception.GetType()}: {exception}");
            HandleCatchedExceptions(diagnostics, exception, "Tokenizer");
        }
        catch (Exception exception)
        {
            Logger.Log($"{exception.GetType()}: {exception}");
        }

        tokens = ImmutableArray<Token>.Empty;
        return false;
    }

    static bool Parse(
        Dictionary<Uri, List<Diagnostic>> diagnostics,
        ImmutableArray<Token> tokens,
        Uri file,
        bool force,
        [NotNullWhen(true)] out ParserResult parserResult)
    {
        if (!force &&
            OmniSharpService.Instance is not null &&
            OmniSharpService.Instance.Documents.TryGet(file, out DocumentHandler? document) &&
            document is DocumentBBCode bbcDocument)
        {
            parserResult = bbcDocument.AST;
            return true;
        }

        try
        {
            ParserResult ast = Parser.Parse(tokens, file);

            diagnostics.AddDiagnostics(ast.Errors, v => v.ToOmniSharp("Parser"));

            parserResult = ast;
            return ast.Errors.Length == 0;
        }
        catch (LanguageException exception)
        {
            Logger.Log($"{exception.GetType()}: {exception}");
            HandleCatchedExceptions(diagnostics, exception, "Parser");
        }
        catch (Exception exception)
        {
            Logger.Log($"{exception.GetType()}: {exception}");
        }

        parserResult = default;
        return false;
    }

    static bool Compile(
        Dictionary<Uri, List<Diagnostic>> diagnostics,
        Uri file,
        bool force,
        CompilerSettings settings,
        [NotNullWhen(true)] out CompilerResult compilerResult)
    {
        if (!force &&
            OmniSharpService.Instance is not null &&
            OmniSharpService.Instance.Documents.TryGet(file, out DocumentHandler? document) &&
            document is DocumentBBCode bbcDocument)
        {
            compilerResult = bbcDocument.CompilerResult;
            return true;
        }

        try
        {
            AnalysisCollection analysisCollection = new();

            Dictionary<int, ExternalFunctionBase> externalFunctions = Interpreter.GetExternalFunctions();

            CompilerResult compiled = Compiler.CompileFile(file, externalFunctions, settings, PreprocessorVariables.Normal, null, analysisCollection, TokenizerSettings, null);

            diagnostics.AddDiagnostics(analysisCollection.Warnings, v => v.ToOmniSharp("Compiler"));
            diagnostics.AddDiagnostics(analysisCollection.Errors, v => v.ToOmniSharp("Compiler"));

            compilerResult = compiled;
            return analysisCollection.Errors.Count == 0;
        }
        catch (LanguageException exception)
        {
            Logger.Log($"{exception.GetType()}: {exception}");
            HandleCatchedExceptions(diagnostics, exception, "Compiler");
        }
        catch (Exception exception)
        {
            Logger.Log($"{exception.GetType()}: {exception}");
        }

        compilerResult = default;
        return false;
    }

    static bool Generate(
        Dictionary<Uri, List<Diagnostic>> diagnostics,
        CompilerResult compilerResult,
        Uri file
        )
    {
        try
        {
            AnalysisCollection analysisCollection = new();

            CodeGeneratorForMain.Generate(
                compilerResult,
                new MainGeneratorSettings()
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
                analysisCollection);

            diagnostics.AddDiagnostics(analysisCollection.Hints, v => v.ToOmniSharp("CodeGenerator"));
            diagnostics.AddDiagnostics(analysisCollection.Informations, v => v.ToOmniSharp("CodeGenerator"));
            diagnostics.AddDiagnostics(analysisCollection.Warnings, v => v.ToOmniSharp("CodeGenerator"));
            diagnostics.AddDiagnostics(analysisCollection.Errors, v => v.ToOmniSharp("CodeGenerator"));

            if (analysisCollection.Errors.Count > 0)
            { return false; }

            Logger.Log($"Successfully compiled {file}");
            return true;
        }
        catch (LanguageException exception)
        {
            Logger.Log($"{exception.GetType()}: {exception}");
            HandleCatchedExceptions(diagnostics, exception, "CodeGenerator");
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
        string file = uri.LocalPath;
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
                DocumentHandler doc = OmniSharpService.Instance.Documents.GetOrCreate(new TextDocumentIdentifier(ast.Key));
                if (doc is DocumentBBCode docBBC)
                {
                    docBBC.AST = ast.Value.ParserResult;
                    docBBC.Tokens = ast.Value.Tokens;
                }
            }
        }

        result.CompilerResult = compilerResult;
        result.AST = compilerResult.Raw[file].ParserResult;
        result.Tokens = compilerResult.Raw[file].Tokens;

        if (!Generate(result.Diagnostics, compilerResult, file))
        { return result; }

        return result;
    }
}
