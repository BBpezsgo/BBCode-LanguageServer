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
    public Token[] Tokens;
    public ParserResult? AST;
    public CompilerResult? CompilerResult;
}

public static class Analysis
{
    [return: NotNullIfNotNull(nameof(warning))]
    public static Diagnostic? ToOmniSharp(this Warning? warning, string? source = null) => warning is null ? null : new Diagnostic()
    {
        Severity = DiagnosticSeverity.Warning,
        Range = warning.Position.ToOmniSharp(),
        Message = warning.Message,
        Source = source,
    };

    [return: NotNullIfNotNull(nameof(information))]
    public static Diagnostic? ToOmniSharp(this Information? information, string? source = null) => information is null ? null : new Diagnostic()
    {
        Severity = DiagnosticSeverity.Information,
        Range = information.Position.ToOmniSharp(),
        Message = information.Message,
        Source = source,
    };

    [return: NotNullIfNotNull(nameof(hint))]
    public static Diagnostic? ToOmniSharp(this Hint? hint, string? source = null) => hint is null ? null : new Diagnostic()
    {
        Severity = DiagnosticSeverity.Hint,
        Range = hint.Position.ToOmniSharp(),
        Message = hint.Message,
        Source = source,
    };

    [return: NotNullIfNotNull(nameof(error))]
    public static Diagnostic? ToOmniSharp(this Error? error, string? source = null) => error is null ? null : new Diagnostic()
    {
        Severity = DiagnosticSeverity.Error,
        Range = error.Position.ToOmniSharp(),
        Message = error.Message,
        Source = source,
    };

    [return: NotNullIfNotNull(nameof(error))]
    public static Diagnostic? ToOmniSharp(this LanguageException? error, string? source = null) => error is null ? null : new Diagnostic()
    {
        Severity = DiagnosticSeverity.Error,
        Range = error.Position.ToOmniSharp(),
        Message = error.Message,
        Source = source,
    };

    static IEnumerable<Diagnostic> ToOmniSharp(this IEnumerable<Hint> hints, Uri currentPath, string? source = null)
    {
        foreach (Hint hint in hints)
        {
            if (hint.Uri is null || hint.Uri != currentPath) continue;
            yield return hint.ToOmniSharp(source);
        }
    }

    static IEnumerable<Diagnostic> ToOmniSharp(this IEnumerable<Information> informations, Uri currentPath, string? source = null)
    {
        foreach (Information information in informations)
        {
            if (information.Uri is null || information.Uri != currentPath) continue;
            yield return information.ToOmniSharp(source);
        }
    }

    static IEnumerable<Diagnostic> ToOmniSharp(this IEnumerable<Warning> warnings, Uri currentPath, string? source = null)
    {
        foreach (Warning warning in warnings)
        {
            if (warning.Uri is null || warning.Uri != currentPath) continue;
            yield return warning.ToOmniSharp(source);
        }
    }

    static IEnumerable<Diagnostic> ToOmniSharp(this IEnumerable<Error> errors, Uri currentPath, string? source = null)
    {
        foreach (Error error in errors)
        {
            if (error.Uri is null || error.Uri != currentPath) continue;
            yield return error.ToOmniSharp(source);
        }
    }

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

    static Token[]? Tokenize(Dictionary<Uri, List<Diagnostic>> diagnostics, Uri file)
    {
        try
        {
            TokenizerResult tokenizerResult = AnyTokenizer.Tokenize(file, PreprocessorVariables.Normal);

            diagnostics.EnsureExistence(file, new List<Diagnostic>())
                .AddRange(tokenizerResult.Warnings.ToOmniSharp(file, "Tokenizer"));

            return tokenizerResult.Tokens;
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

        return null;
    }

    static ParserResult? Parse(Dictionary<Uri, List<Diagnostic>> diagnostics, Token[] tokens, Uri file)
    {
        try
        {
            ParserResult ast = Parser.Parse(tokens, file);
            ast.SetFile(file);

            diagnostics.EnsureExistence(file, new List<Diagnostic>())
                .AddRange(ast.Errors.ToOmniSharp(file, "Parser"));

            return ast;
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

        return null;
    }

    static CompilerResult? Compile(Dictionary<Uri, List<Diagnostic>> diagnostics, ParserResult ast, Uri file, CompilerSettings settings)
    {
        try
        {
            AnalysisCollection analysisCollection = new();

            Dictionary<int, ExternalFunctionBase> externalFunctions = Interpreter.GetExternalFunctions();

            CompilerResult compiled = Compiler.Compile(ast, externalFunctions, file, settings, PreprocessorVariables.Normal, null, analysisCollection);

            diagnostics.EnsureExistence(file, new List<Diagnostic>())
                .AddRange(analysisCollection.Warnings.ToOmniSharp(file, "Compiler"));

            diagnostics.EnsureExistence(file, new List<Diagnostic>())
                .AddRange(analysisCollection.Errors.ToOmniSharp(file, "Compiler"));

            return compiled;
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

        return null;
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
        Dictionary<Uri, List<Diagnostic>> diagnostics = new();

        string? basePath = GetBasePath(file);

        Logger.Log($"Base path: \"{basePath ?? "null"}\"");

        Token[]? tokens = Tokenize(diagnostics, file);

        ParserResult? ast = tokens is null ? null : Parse(diagnostics, tokens, file);

        CompilerResult? compilerResult = !ast.HasValue ? null : Compile(diagnostics, ast.Value, file, new CompilerSettings()
        {
            BasePath = basePath,
        });

        if (compilerResult.HasValue)
        {
            try
            {
                AnalysisCollection analysisCollection = new();

                CodeGeneratorForMain.Generate(
                    compilerResult.Value,
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

                Logger.Log($"Successfully compiled ({file})");
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
        }

        return new AnalysisResult()
        {
            Diagnostics = diagnostics,
            Tokens = tokens ?? Array.Empty<Token>(),
            AST = ast,
            CompilerResult = compilerResult,
        };
    }
}
