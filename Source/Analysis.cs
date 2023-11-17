﻿namespace LanguageServer
{
    using System.IO;
    using LanguageCore;
    using LanguageCore.BBCode.Compiler;
    using LanguageCore.Parser;
    using LanguageCore.Parser.Statement;
    using LanguageCore.Runtime;
    using LanguageCore.Tokenizing;

    public struct AnalysisResult
    {
        public Dictionary<string, List<Diagnostic>> Diagnostics;
        public CompiledFunction[] Functions;
        public CompiledGeneralFunction[] GeneralFunctions;
        public CompiledEnum[] Enums;
        public CompiledClass[] Classes;
        public CompiledStruct[] Structs;

        public ParserResult AST;

        public Token[] Tokens;
    }

    public static class Analysis
    {
        static Diagnostic[] GetDiagnosticInfos(string currentPath, string source, params Hint[] hints)
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

        static Diagnostic[] GetDiagnosticInfos(string currentPath, string source, params Information[] informations)
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

        static Diagnostic[] GetDiagnosticInfos(string currentPath, string source, params Warning[] warnings)
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

        static Diagnostic[] GetDiagnosticInfos(string currentPath, string source, params Error[] errors)
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

        static void HandleCatchedExceptions(Dictionary<string, List<Diagnostic>> diagnostics, LanguageException exception, string source, FileInfo file)
        {
            if (exception.File == file.FullName)
            {
                diagnostics.GetOrAdd(exception.File, new List<Diagnostic>())
                    .Add(new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Range = exception.Position.ToOmniSharp(),
                        Message = exception.Message,
                    });
            }

            if (exception.InnerException is LanguageException innerException)
            {
                if (exception.File == file.FullName)
                {
                    diagnostics.GetOrAdd(exception.File, new List<Diagnostic>())
                        .Add(new Diagnostic
                        {
                            Severity = DiagnosticSeverity.Error,
                            Range = innerException.Position.ToOmniSharp(),
                            Message = innerException.Message,
                        });
                }
            }
        }

        static Token[]? Tokenize(Dictionary<string, List<Diagnostic>> diagnostics, FileInfo file)
        {
            try
            {
                TokenizerResult tokenizerResult = StringTokenizer.Tokenize(File.ReadAllText(file.FullName));

                diagnostics.GetOrAdd(file.FullName, new List<Diagnostic>())
                    .AddRange(GetDiagnosticInfos(file.FullName, "Tokenizer", tokenizerResult.Warnings));

                return tokenizerResult.Tokens;
            }
            catch (LanguageException exception)
            {
                Logger.Log($"{exception.GetType()}: {exception}");
                HandleCatchedExceptions(diagnostics, exception, "Tokenizer", file);
            }
            catch (Exception exception)
            {
                Logger.Log($"{exception.GetType()}: {exception}");
            }

            return null;
        }

        static ParserResult? Parse(Dictionary<string, List<Diagnostic>> diagnostics, Token[] tokens, FileInfo file)
        {
            try
            {
                ParserResult ast = Parser.Parse(tokens);
                ast.SetFile(file.FullName);

                diagnostics.GetOrAdd(file.FullName, new List<Diagnostic>())
                    .AddRange(GetDiagnosticInfos(file.FullName, "Parser", ast.Errors));

                return ast;
            }
            catch (LanguageException exception)
            {
                Logger.Log($"{exception.GetType()}: {exception}");
                HandleCatchedExceptions(diagnostics, exception, "Parser", file);
            }
            catch (Exception exception)
            {
                Logger.Log($"{exception.GetType()}: {exception}");
            }

            return null;
        }

        static Compiler.Result? Compile(Dictionary<string, List<Diagnostic>> diagnostics, ParserResult ast, FileInfo file, string? basePath)
        {
            try
            {
                Compiler.Result compiled = Compiler.Compile(
                    ast,
                    new Dictionary<string, ExternalFunctionBase>(),
                    file,
                    null,
                    basePath);

                diagnostics.GetOrAdd(file.FullName, new List<Diagnostic>())
                    .AddRange(GetDiagnosticInfos(file.FullName, "Compiler", compiled.Warnings));

                diagnostics.GetOrAdd(file.FullName, new List<Diagnostic>())
                    .AddRange(GetDiagnosticInfos(file.FullName, "Compiler", compiled.Errors));

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
                    if (doc.RootElement.TryGetProperty("base", out var property))
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

        public static AnalysisResult Analyze(FileInfo file)
        {
            Dictionary<string, List<Diagnostic>> diagnostics = new();

            string? basePath = GetBasePath(file);

            Logger.Log($"Base path: \"{basePath ?? "null"}\"");

            CompiledFunction[]? functions = null;
            CompiledGeneralFunction[]? generalFunctions = null;
            CompiledEnum[]? enums = null;
            CompiledClass[]? classes = null;
            CompiledStruct[]? structs = null;

            Token[]? tokens = Tokenize(diagnostics, file);

            ParserResult? ast = tokens is null ? null : Parse(diagnostics, tokens, file);

            Compiler.Result? compilerResult = !ast.HasValue ? null : Compile(diagnostics, ast.Value, file, basePath);

            if (compilerResult.HasValue)
            {
                functions = compilerResult.Value.Functions;
                generalFunctions = compilerResult.Value.GeneralFunctions;

                enums = compilerResult.Value.Enums;

                classes = compilerResult.Value.Classes;
                structs = compilerResult.Value.Structs;

                try
                {
                    CodeGeneratorForMain.Result generated = CodeGeneratorForMain.Generate(
                        compilerResult.Value,
                        new Compiler.CompilerSettings()
                        {
                            CheckNullPointers = false,
                            DontOptimize = true,
                            ExternalFunctionsCache = false,
                            GenerateComments = false,
                            GenerateDebugInstructions = false,
                            PrintInstructions = false,
                            RemoveUnusedFunctionsMaxIterations = 0,
                        },
                        null,
                        Compiler.CompileLevel.All);

                    diagnostics.GetOrAdd(file.FullName, new List<Diagnostic>())
                        .AddRange(GetDiagnosticInfos(file.FullName, "CodeGenerator", generated.Hints));

                    diagnostics.GetOrAdd(file.FullName, new List<Diagnostic>())
                        .AddRange(GetDiagnosticInfos(file.FullName, "CodeGenerator", generated.Informations));

                    diagnostics.GetOrAdd(file.FullName, new List<Diagnostic>())
                        .AddRange(GetDiagnosticInfos(file.FullName, "CodeGenerator", generated.Warnings));

                    diagnostics.GetOrAdd(file.FullName, new List<Diagnostic>())
                        .AddRange(GetDiagnosticInfos(file.FullName, "CodeGenerator", generated.Errors));

                    Logger.Log($"Successfully compiled ({file.Name})");
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

                Functions = functions ?? Array.Empty<CompiledFunction>(),
                GeneralFunctions = generalFunctions ?? Array.Empty<CompiledGeneralFunction>(),
                Enums = enums ?? Array.Empty<CompiledEnum>(),
                Classes = classes ?? Array.Empty<CompiledClass>(),
                Structs = structs ?? Array.Empty<CompiledStruct>(),
            };
        }
    }
}
