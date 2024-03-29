using System.Collections.Immutable;
using System.IO;
using LanguageCore;
using LanguageCore.BBCode.Generator;
using LanguageCore.Compiler;
using LanguageCore.Parser;
using LanguageCore.Runtime;
using LanguageCore.Tokenizing;

namespace LanguageServer;

public struct AnalysisResult
{
    public Dictionary<Uri, List<Diagnostic>> Diagnostics;
    public ImmutableDictionary<Uri, ImmutableArray<Token>> Tokens;
    public ParserResult? AST;
    public CompilerResult? CompilerResult;

    public static AnalysisResult Empty => new()
    {
        Diagnostics = new Dictionary<Uri, List<Diagnostic>>(),
        Tokens = ImmutableDictionary<Uri, ImmutableArray<Token>>.Empty,
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

    static void HandleCatchedExceptions(Dictionary<Uri, List<Diagnostic>> diagnostics, LanguageException exception, string? source, Uri file, Uri? exceptionFile = null)
    {
        exceptionFile ??= exception.Uri;

        if (exceptionFile == file)
        {
            diagnostics.EnsureExistence(exceptionFile, new List<Diagnostic>())
                .Add(exception.ToOmniSharp(source));
        }

        if (exception.InnerException is LanguageException innerException)
        {
            if (exceptionFile == file)
            {
                diagnostics.EnsureExistence(exceptionFile, new List<Diagnostic>())
                    .Add(innerException.ToOmniSharp(source));
            }
        }
    }

    static bool Tokenize(
        Dictionary<Uri, List<Diagnostic>> diagnostics,
        Uri file,
        [NotNullWhen(true)] out Token[]? tokens)
    {
        try
        {
            TokenizerResult tokenizerResult = AnyTokenizer.Tokenize(file, PreprocessorVariables.Normal, new TokenizerSettings(TokenizerSettings.Default)
            {
                TokenizeComments = true,
            });

            diagnostics.EnsureExistence(file, new List<Diagnostic>())
                .AddRange(tokenizerResult.Warnings.ToOmniSharp(file, "Tokenizer"));

            tokens = tokenizerResult.Tokens;
            return true;
        }
        catch (LanguageException exception)
        {
            Logger.Log($"{exception.GetType()}: {exception}");
            HandleCatchedExceptions(diagnostics, exception, "Tokenizer", file, file);
        }
        catch (Exception exception)
        {
            Logger.Log($"{exception.GetType()}: {exception}");
        }

        tokens = null;
        return false;
    }

    static bool Parse(
        Dictionary<Uri, List<Diagnostic>> diagnostics,
        Token[] tokens,
        Uri file,
        [NotNullWhen(true)] out ParserResult parserResult)
    {
        try
        {
            ParserResult ast = Parser.Parse(tokens, file);
            ast.SetFile(file);

            diagnostics.EnsureExistence(file, new List<Diagnostic>())
                .AddRange(ast.Errors.ToOmniSharp(file, "Parser"));

            parserResult = ast;
            return ast.Errors.Length == 0;
        }
        catch (LanguageException exception)
        {
            Logger.Log($"{exception.GetType()}: {exception}");
            HandleCatchedExceptions(diagnostics, exception, "Parser", file, file);
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
        ParserResult ast,
        Uri file,
        CompilerSettings settings,
        [NotNullWhen(true)] out CompilerResult compilerResult)
    {
        try
        {
            AnalysisCollection analysisCollection = new();

            Dictionary<int, ExternalFunctionBase> externalFunctions = Interpreter.GetExternalFunctions();

            CompilerResult compiled = Compiler.Compile(ast, externalFunctions, file, settings, PreprocessorVariables.Normal, null, analysisCollection, TokenizerSettings);

            diagnostics.EnsureExistence(file, new List<Diagnostic>())
                .AddRange(analysisCollection.Warnings.ToOmniSharp(file, "Compiler"));

            diagnostics.EnsureExistence(file, new List<Diagnostic>())
                .AddRange(analysisCollection.Errors.ToOmniSharp(file, "Compiler"));

            compilerResult = compiled;
            return analysisCollection.Errors.Count == 0;
        }
        catch (LanguageException exception)
        {
            Logger.Log($"{exception.GetType()}: {exception}");
            HandleCatchedExceptions(diagnostics, exception, "Compiler", file);
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
                new GeneratorSettings()
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

            diagnostics.EnsureExistence(file, new List<Diagnostic>())
                .AddRange(analysisCollection.Hints.ToOmniSharp(file, "CodeGenerator"));

            diagnostics.EnsureExistence(file, new List<Diagnostic>())
                .AddRange(analysisCollection.Informations.ToOmniSharp(file, "CodeGenerator"));

            diagnostics.EnsureExistence(file, new List<Diagnostic>())
                .AddRange(analysisCollection.Warnings.ToOmniSharp(file, "CodeGenerator"));

            diagnostics.EnsureExistence(file, new List<Diagnostic>())
                .AddRange(analysisCollection.Errors.ToOmniSharp(file, "CodeGenerator"));

            if (analysisCollection.Errors.Count > 0)
            { return false; }

            Logger.Log($"Successfully compiled {file}");
            return true;
        }
        catch (LanguageException exception)
        {
            Logger.Log($"{exception.GetType()}: {exception}");
            HandleCatchedExceptions(diagnostics, exception, "CodeGenerator", file);
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

        try
        {
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
        }
        catch (Exception)
        { }

        if (basePath != null && file.DirectoryName != null)
        {
            basePath = Path.GetFullPath(basePath, file.DirectoryName);
        }

        return basePath;
    }

    public static AnalysisResult Analyze(Uri file)
    {
        string? basePath = GetBasePath(file);

        Logger.Log($"Base path: \"{basePath ?? "null"}\"");

        AnalysisResult result = AnalysisResult.Empty;

        if (!Tokenize(result.Diagnostics, file, out Token[]? tokens))
        { return result; }
        result.Tokens = new Dictionary<Uri, ImmutableArray<Token>>()
        {
            { file, tokens.ToImmutableArray() }
        }.ToImmutableDictionary();

        if (!Parse(result.Diagnostics, tokens, file, out ParserResult parserResult))
        { return result; }
        result.AST = parserResult;

        if (!Compile(result.Diagnostics, parserResult, file, new CompilerSettings() { BasePath = basePath }, out CompilerResult compilerResult))
        { return result; }
        result.CompilerResult = compilerResult;
        result.Tokens = compilerResult.Tokens;

        if (!Generate(result.Diagnostics, compilerResult, file))
        { return result; }

        return result;
    }
}
