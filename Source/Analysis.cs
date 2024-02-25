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
    public ParserResult AST;
    public CompilerResult? CompilerResult;
}

public static class Analysis
{
    static Diagnostic[] GetDiagnosticInfos(Uri currentPath, string source, params Hint[] hints)
    {
        List<Diagnostic> diagnostics = new();

        for (int i = 0; i < hints.Length; i++)
        {
            Hint hint = hints[i];

            if (hint.File == null || hint.File != currentPath) continue;

            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Hint,
                Range = hint.Position.ToOmniSharp(),
                Message = hint.Message,
                Source = source,
            });
        }

        return diagnostics.ToArray();
    }

    static Diagnostic[] GetDiagnosticInfos(Uri currentPath, string source, params Information[] informations)
    {
        List<Diagnostic> diagnostics = new();

        for (int i = 0; i < informations.Length; i++)
        {
            Information information = informations[i];

            if (information.File == null || information.File != currentPath) continue;

            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Information,
                Range = information.Position.ToOmniSharp(),
                Message = information.Message,
                Source = source,
            });
        }

        return diagnostics.ToArray();
    }

    static Diagnostic[] GetDiagnosticInfos(Uri currentPath, string source, params Warning[] warnings)
    {
        List<Diagnostic> diagnostics = new();

        for (int i = 0; i < warnings.Length; i++)
        {
            Warning warning = warnings[i];

            if (warning.File == null || warning.File != currentPath) continue;

            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Warning,
                Range = warning.Position.ToOmniSharp(),
                Message = warning.Message,
                Source = source,
            });
        }

        return diagnostics.ToArray();
    }

    static Diagnostic[] GetDiagnosticInfos(Uri currentPath, string source, params Error[] errors)
    {
        List<Diagnostic> diagnostics = new();

        for (int i = 0; i < errors.Length; i++)
        {
            Error error = errors[i];

            if (error.File == null || error.File != currentPath) continue;

            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Range = error.Position.ToOmniSharp(),
                Message = error.Message,
                Source = source,
            });
        }

        return diagnostics.ToArray();
    }

    static void HandleCatchedExceptions(Dictionary<Uri, List<Diagnostic>> diagnostics, LanguageException exception, string source, Uri file, Uri? exceptionFile = null)
    {
        exceptionFile ??= exception.Uri;

        Logger.Log(exceptionFile?.ToString() ?? "null");

        if (exceptionFile == file)
        {
            diagnostics.GetOrAdd(exceptionFile, new List<Diagnostic>())
                .Add(new Diagnostic()
                {
                    Severity = DiagnosticSeverity.Error,
                    Range = exception.Position.ToOmniSharp(),
                    Message = exception.Message,
                    Source = source,
                });
        }

        if (exception.InnerException is LanguageException innerException)
        {
            if (exceptionFile == file)
            {
                diagnostics.GetOrAdd(exceptionFile, new List<Diagnostic>())
                    .Add(new Diagnostic()
                    {
                        Severity = DiagnosticSeverity.Error,
                        Range = innerException.Position.ToOmniSharp(),
                        Message = innerException.Message,
                        Source = source,
                    });
            }
        }
    }

    static Token[]? Tokenize(Dictionary<Uri, List<Diagnostic>> diagnostics, Uri file)
    {
        try
        {
            TokenizerResult tokenizerResult = AnyTokenizer.Tokenize(file);

            diagnostics.GetOrAdd(file, new List<Diagnostic>())
                .AddRange(GetDiagnosticInfos(file, "Tokenizer", tokenizerResult.Warnings));

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
            ParserResult ast = Parser.Parse(tokens);
            ast.SetFile(file);

            diagnostics.GetOrAdd(file, new List<Diagnostic>())
                .AddRange(GetDiagnosticInfos(file, "Parser", ast.Errors));

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

            Dictionary<string, ExternalFunctionBase> externalFunctions = new();
            new Interpreter().GenerateExternalFunctions(externalFunctions);

            CompilerResult compiled = Compiler.Compile(ast, externalFunctions, file, settings, null, analysisCollection);

            diagnostics.GetOrAdd(file, new List<Diagnostic>())
                .AddRange(GetDiagnosticInfos(file, "Compiler", analysisCollection.Warnings.ToArray()));

            diagnostics.GetOrAdd(file, new List<Diagnostic>())
                .AddRange(GetDiagnosticInfos(file, "Compiler", analysisCollection.Errors.ToArray()));

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

                diagnostics.GetOrAdd(file, new List<Diagnostic>())
                    .AddRange(GetDiagnosticInfos(file, "CodeGenerator", analysisCollection.Hints.ToArray()));

                diagnostics.GetOrAdd(file, new List<Diagnostic>())
                    .AddRange(GetDiagnosticInfos(file, "CodeGenerator", analysisCollection.Informations.ToArray()));

                diagnostics.GetOrAdd(file, new List<Diagnostic>())
                    .AddRange(GetDiagnosticInfos(file, "CodeGenerator", analysisCollection.Warnings.ToArray()));

                diagnostics.GetOrAdd(file, new List<Diagnostic>())
                    .AddRange(GetDiagnosticInfos(file, "CodeGenerator", analysisCollection.Errors.ToArray()));

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
            AST = ast ?? ParserResult.Empty,
            CompilerResult = compilerResult,
        };
    }
}
