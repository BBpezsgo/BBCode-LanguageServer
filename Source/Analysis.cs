namespace LanguageServer
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

        public static AnalysisResult Analyze(FileInfo file)
        {
            Dictionary<string, List<Diagnostic>> diagnostics = new();

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

            Logger.Log($"Base path: \"{basePath ?? "null"}\"");

            Token[] tokens = Array.Empty<Token>();

            CompiledFunction[] functions = Array.Empty<CompiledFunction>();
            CompiledGeneralFunction[] generalFunctions = Array.Empty<CompiledGeneralFunction>();

            CompiledEnum[] enums = Array.Empty<CompiledEnum>();

            CompiledClass[] classes = Array.Empty<CompiledClass>();
            CompiledStruct[] structs = Array.Empty<CompiledStruct>();
            ParserResult ast = ParserResult.Empty;

            try
            {
                TokenizerResult tokenizerResult = Tokenizer.Tokenize(
                    File.ReadAllText(file.FullName),
                    file.FullName);
                tokens = tokenizerResult.Tokens;

                diagnostics.GetOrAdd(file.FullName, new List<Diagnostic>())
                    .AddRange(GetDiagnosticInfos(file.FullName, "Tokenizer", tokenizerResult.Warnings));

                ast = Parser.Parse(tokens);
                ast.SetFile(file.FullName);

                diagnostics.GetOrAdd(file.FullName, new List<Diagnostic>())
                    .AddRange(GetDiagnosticInfos(file.FullName, "Parser", ast.Errors));

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

                CodeGeneratorForMain.Result generated = CodeGeneratorForMain.Generate(
                    compiled,
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

                functions = compiled.Functions;
                generalFunctions = compiled.GeneralFunctions;

                enums = compiled.Enums;

                classes = compiled.Classes;
                structs = compiled.Structs;

                Logger.Log($"Successfully compiled ({file.Name})");
            }
            catch (LanguageException exception)
            {
                Logger.Log($"{exception.GetType()}: {exception}");

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
            catch (Exception exception)
            {
                Logger.Error($"{exception.GetType()}: {exception}");
            }

            return new AnalysisResult()
            {
                Diagnostics = diagnostics,

                Tokens = tokens,

                AST = ast,

                Functions = functions,
                GeneralFunctions = generalFunctions,
                Enums = enums,
                Classes = classes,
                Structs = structs,
            };
        }
    }
}
