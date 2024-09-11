using System.Collections.Immutable;
using System.IO;
using LanguageCore;
using LanguageCore.BBLang.Generator;
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

    static void AddDiagnostics<TDiagnostics>(
        this Dictionary<Uri, List<Diagnostic>> diagnostics,
        TDiagnostics value,
        Func<TDiagnostics, Diagnostic> converter)
        where TDiagnostics : IDiagnostics
    {
        if (value.File is null) return;
        List<Diagnostic> list = diagnostics.EnsureExistence(value.File, new List<Diagnostic>());
        Diagnostic converted = converter.Invoke(value);

        foreach (Diagnostic item in list)
        {
            if (item.Message == converted.Message &&
                item.Range == converted.Range)
            { return; }
        }

        list.Add(converted);
    }

    static void AddDiagnostics<TDiagnostics>(
        this Dictionary<Uri, List<Diagnostic>> diagnostics,
        IEnumerable<TDiagnostics> values,
        Func<TDiagnostics, Diagnostic> converter)
        where TDiagnostics : IDiagnostics
    {
        foreach (TDiagnostics value in values)
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
            document is DocumentBBLang documentBBLang)
        {
            tokens = documentBBLang.Tokens;
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
                if (document2 is DocumentBBLang documentBBLang2)
                { documentBBLang2.Tokens = tokenizerResult.Tokens; }
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
            document is DocumentBBLang documentBBLang)
        {
            parserResult = documentBBLang.AST;
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

            AnalysisCollection analysisCollection = new();

            Dictionary<int, ExternalFunctionBase> externalFunctions = Interpreter.GetExternalFunctions();

            CompilerResult compiled = Compiler.CompileFile(file, externalFunctions, settings, PreprocessorVariables.Normal, null, analysisCollection, TokenizerSettings, null, additionalImports);

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

        result.Diagnostics = new Dictionary<Uri, List<Diagnostic>>()
        {
            { file, new List<Diagnostic>() }
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
                    documentBBLang.AST = ast.Value.ParserResult;
                    documentBBLang.Tokens = ast.Value.Tokens;
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
