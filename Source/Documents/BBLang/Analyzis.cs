using System.Collections.Immutable;
using System.Diagnostics;
using LanguageCore;
using LanguageCore.BBLang.Generator;
using LanguageCore.Compiler;
using LanguageCore.Parser;
using LanguageCore.Tokenizing;
using LanguageCore.Workspaces;
using Diagnostic = LanguageCore.Diagnostic;

namespace LanguageServer.DocumentManagers;

enum CompilationReason
{
    None,
    Unknown,
    Saved,
    Opened,
    Requested,
}

partial class DocumentBBLang
{
    static readonly Dictionary<Uri, CacheItem> Cache = new();

    public ImmutableArray<Token> Tokens;
    public ParserResult AST;
    public CompilerResult CompilerResult;

    public CompilerSettings CompilerSettings;

    Task? CompilationTask;
    DocumentVersion CompiledVersion;
    DocumentVersion CurrentlyCompilingVersion;
    CompilationReason CompilationReason;
    DocumentVersion DesiredCompiledVersion;

    void RequestCompilation(DocumentVersion? version, CompilationReason reason) => RequestCompilation(version ?? DocumentVersion.Zero(0), reason);
    void RequestCompilation(DocumentVersion version, CompilationReason reason)
    {
        if (CompilationTask is not null && !CompilationTask.IsCompleted) return;
        if (CompiledVersion == version) return;
        if (DesiredCompiledVersion == version) return;

        Logger.Debug($"Requesting compilation for {version} because of {reason}");

        DesiredCompiledVersion = version;

        CompilationTask = Task.Run(async () =>
        {
            await Task.Delay(150).ConfigureAwait(false);

            if (CompiledVersion == DesiredCompiledVersion) return;

            CompilationReason = reason;
            await CompileAsync().ConfigureAwait(false);
            CompilationReason = CompilationReason.None;

            if (DesiredCompiledVersion != CompiledVersion)
            {
                RequestCompilation(DesiredCompiledVersion, reason);
            }
        });
    }

    Task AwaitForCompilation(DocumentVersion? version, CancellationToken cancellationToken, bool force = true) => AwaitForCompilation(version ?? DocumentVersion.Zero(0), cancellationToken, force);
    Task AwaitForCompilation(DocumentVersion version, CancellationToken cancellationToken, bool force = true)
    {
        if (!force && (CompilationReason is CompilationReason.Requested or CompilationReason.None))
        {
            Logger.Trace($"Skipping validation");
            return Task.CompletedTask;
        }
        RequestCompilation(version, CompilationReason.Requested);
        if (CompilationTask is null) return Task.CompletedTask;
        return CompilationTask.WaitAsync(cancellationToken);
    }

    async Task CompileAsync()
    {
        CurrentlyCompilingVersion = Version ?? DocumentVersion.Zero(0);

        Logger.Debug($"Validating {CurrentlyCompilingVersion}");

        OmniSharpService.Instance?.Server?.SendNotification<CompilerStatusNotificationArgs>("bblang/compiler/status", new()
        {
            Status = "working",
            Details = $"Compiling {System.IO.Path.GetFileName(Uri.LocalPath)} version {CurrentlyCompilingVersion}"
        });

        try
        {
            DiagnosticsCollection diagnostics = new();

            Configuration config = Configuration.Empty;
            BBLangProject? project = null;
            Uri? projectRoot = null;

            Logger.Debug($"  Compiling configuraton");

            if (ConfigurationManager.Search(Uri, Documents, out Uri? configurationPath, out _))
            {
                projectRoot = new(configurationPath, ".");
                Logger.Trace($"    Parsing configuration `{configurationPath}`");
                config = Configuration.Parse(configurationPath, diagnostics, new Logger());
                Logger.Trace($"    Configuation parsed");

                if (BBLangProject.Projects.TryGetValue(configurationPath, out project))
                {
                    project.Configuration = config;
                }
                else
                {
                    BBLangProject.Projects[configurationPath] = project = new BBLangProject()
                    {
                        Configuration = config,
                    };

                    if (config.IsProject)
                    {
                        if (configurationPath.IsFile)
                        {
                            foreach (string item in System.IO.Directory.EnumerateFiles(projectRoot.LocalPath, "*.bbc"))
                            {
                                project.Files.Add(new Uri(item, UriKind.Absolute));
                            }
                        }

                        foreach (DocumentBase item in Documents.OpenedDocuments)
                        {
                            if (projectRoot.IsBaseOf(item.Uri))
                            {
                                project.Files.Add(item.Uri);
                            }
                        }
                    }
                }
            }

            if (project is not null)
            {
                OmniSharpService.Instance?.Server?.SendNotification<ProjectStatusNotificationArgs>("bblang/project/status", new()
                {
                    ProjectType = project.Configuration.IsProject ? "project" : "file",
                    ContextFile = Uri.ToString(),
                    IndexedFiles = project.Files.Count,
                    Root = projectRoot!.ToString(),
                });
            }
            else
            {
                OmniSharpService.Instance?.Server?.SendNotification<ProjectStatusNotificationArgs>("bblang/project/status", new()
                {
                    ProjectType = null,
                    ContextFile = Uri.ToString(),
                });
            }

            diagnostics.Clear();

            CompilerSettings compilerSettings = CompilerSettings = new(CodeGeneratorForMain.DefaultCompilerSettings)
            {
                Optimizations = OptimizationSettings.None,
                CompileEverything = true,
                PreprocessorVariables = PreprocessorVariables.Normal,
                SourceProviders = [
                    Documents,
                    new FileSourceProvider()
                    {
                        ExtraDirectories = config.ExtraDirectories,
                    },
                ],
                AdditionalImports = config.AdditionalImports,
                ExternalFunctions = config.ExternalFunctions.As<LanguageCore.Runtime.IExternalFunction>(),
                ExternalConstants = config.ExternalConstants,
                TokenizerSettings = new TokenizerSettings(TokenizerSettings.Default)
                {
                    TokenizeComments = true,
                },
                Cache = Cache,
                OptimizationDiagnostics = true,
            };
            HashSet<Uri> compiledFiles;
            if (DocumentUri.Scheme == "file")
            {
                CompilerResult compilerResult = CompilerResult.MakeEmpty(Uri);
                try
                {
                    string[] files;
                    if (project is null)
                    {
                        files = [.. Documents.OpenedDocuments.Select(v => v.Uri.ToString())];
                    }
                    else if (project.Configuration.IsProject)
                    {
                        files = [.. project.Files.Select(v => v.ToString())];
                    }
                    else
                    {
                        files = [Uri.ToString()];
                    }

                    Logger.Debug($"  Compiling {string.Join(", ", files)}");
                    compilerResult = StatementCompiler.CompileFiles(files, compilerSettings, diagnostics);
                    Logger.Debug($"  Compiled");
                }
                catch (LanguageExceptionAt languageException)
                {
                    diagnostics.Add(languageException.ToDiagnostic());
                }
                catch (LanguageException languageException)
                {
                    diagnostics.Add(languageException.ToDiagnostic());
                }

                ParsedFile raw = compilerResult.RawTokens.FirstOrDefault(v => v.File == Uri);
                if (raw.Index == null)
                {
                    Logger.Warn($"Compiled file not found");
                }
                Tokens = !raw.AST.Tokens.IsDefault ? raw.AST.Tokens : !raw.Tokens.Tokens.IsDefault ? Tokens : ImmutableArray<Token>.Empty;
                AST = raw.AST.IsNotEmpty ? raw.AST : AST;
                CompilerResult = compilerResult;

                compiledFiles = new(compilerResult.RawTokens.Select(v => v.File));
                Logger.Info($"Validated {CurrentlyCompilingVersion} ({(diagnostics.HasErrors ? "failed" : "ok")})");
            }
            else if (Content is not null)
            {
                TokenizerResult tokens = Tokenizer.Tokenize(Content, diagnostics, Uri, compilerSettings.PreprocessorVariables, compilerSettings.TokenizerSettings);
                ParserResult ast = Parser.Parse(tokens.Tokens, Uri, diagnostics);
                Tokens = !ast.Tokens.IsDefault ? ast.Tokens : !tokens.Tokens.IsDefault ? tokens.Tokens : ImmutableArray<Token>.Empty;
                AST = ast.IsNotEmpty ? ast : AST;

                compiledFiles = new() { Uri };
                Logger.Info($"Validated {CurrentlyCompilingVersion} ({(diagnostics.HasErrors ? "failed" : "ok")}) (fallback)");
            }
            else
            {
                compiledFiles = new();
            }

            foreach (Diagnostic item in diagnostics.DiagnosticsWithoutContext)
            {
                Logger.Error(item.ToString());
            }

            Dictionary<Uri, List<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>> diagnosticsPerFile = new();

            static string GetFullMessage(Diagnostic diagnostic, int indent)
            {
                string result = $"{diagnostic.Message}";
                foreach (Diagnostic item in diagnostic.SubErrors)
                {
                    result += $"\n{new string(' ', indent)} -> {GetFullMessage(item, indent + 2)}";
                }
                return result;
            }

            static void CompileDiagnostic(Diagnostic diagnostic, Dictionary<Uri, List<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>> diagnosticsPerFile, DiagnosticsLevel parentLevel = 0)
            {
                if (diagnostic is DiagnosticAt diagnosticWithPosition)
                {
                    if (diagnosticWithPosition.File is null)
                    {
                        Logger.Error(diagnosticWithPosition.ToString());
                        return;
                    }

                    if (!diagnosticsPerFile.TryGetValue(diagnosticWithPosition.File, out List<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>? container))
                    { container = diagnosticsPerFile[diagnosticWithPosition.File] = new(); }

                    container.Add(new OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic()
                    {
                        Severity = (parentLevel > diagnosticWithPosition.Level ? parentLevel : diagnosticWithPosition.Level) switch {
                            DiagnosticsLevel.Error => DiagnosticSeverity.Error,
                            DiagnosticsLevel.Warning => DiagnosticSeverity.Warning,
                            DiagnosticsLevel.Information => DiagnosticSeverity.Information,
                            DiagnosticsLevel.Hint => DiagnosticSeverity.Hint,
                            DiagnosticsLevel.OptimizationNotice => DiagnosticSeverity.Information,
                            DiagnosticsLevel.FailedOptimization => DiagnosticSeverity.Information,
                            _ => throw new UnreachableException(),
                        },
                        Range = diagnosticWithPosition.Position.ToOmniSharp(),
                        Message = GetFullMessage(diagnosticWithPosition, 0),
                        Source = diagnosticWithPosition.File.ToString(),
                        RelatedInformation = Diagnostic.EnumerateAll(diagnostic)
                            .Where(v =>
                                v is not DiagnosticAt w
                                || (w.Location.File == diagnosticWithPosition.Location.File
                                    && w.Location.Position.Union(diagnosticWithPosition.Location.Position).Equals(diagnosticWithPosition.Location.Position)
                                ))
                            .SelectMany(v => v.RelatedInformation)
                            .OfType<DiagnosticRelatedInformationAt>()
                            .Select(v => new OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticRelatedInformation()
                            {
                                Location = v.Location.ToOmniSharp(),
                                Message = v.Message,
                            })
                            .ToArray(),
                        Tags = diagnosticWithPosition.Tag switch
                        {
                            LanguageCore.DiagnosticTag.None => null,
                            LanguageCore.DiagnosticTag.Unnecessary => new(OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticTag.Unnecessary),
                            _ => null,
                        },
                    });

                    foreach (Diagnostic item in diagnostic.SubErrors)
                    {
                        if (item is DiagnosticAt diagnosticWithPosition2
                            && diagnosticWithPosition2.Location.File == diagnosticWithPosition.Location.File
                            && diagnosticWithPosition2.Location.Position.Union(diagnosticWithPosition.Location.Position).Equals(diagnosticWithPosition.Location.Position))
                        { continue; }
                        CompileDiagnostic(item, diagnosticsPerFile, diagnostic.Level);
                    }
                }
                else
                {
                    foreach (Diagnostic item in diagnostic.SubErrors)
                    {
                        CompileDiagnostic(item, diagnosticsPerFile, diagnostic.Level);
                    }
                }
            }

            foreach (DiagnosticAt diagnostic in diagnostics.Diagnostics)
            {
                if (diagnostic.Level == DiagnosticsLevel.OptimizationNotice) continue;
                if (diagnostic.Level == DiagnosticsLevel.FailedOptimization) continue;

                CompileDiagnostic(diagnostic, diagnosticsPerFile);
            }

            foreach (Uri file in compiledFiles)
            {
                diagnosticsPerFile.TryAdd(file, new());
            }

            foreach ((Uri file, List<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic> fileDiagnostics) in diagnosticsPerFile)
            {
                OmniSharpService.Instance?.Server?.PublishDiagnostics(new PublishDiagnosticsParams()
                {
                    Uri = file,
                    Diagnostics = fileDiagnostics,
                    Version = Documents.TryGet(file, out DocumentBase? document) ? document.Version?.Version : null,
                });
            }
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
        {
            Logger.Error(ex);
            OmniSharpService.Instance?.Server?.Window?.ShowError($"BBLang {ex.GetType().Name}: {ex.Message}");
            OmniSharpService.Instance?.Server?.SendNotification<CompilerStatusNotificationArgs>("bblang/compiler/status", new()
            {
                Status = "failed",
                Details = $"{ex.GetType().Name}: {ex.Message}",
            });
        }
        finally
        {
            CompiledVersion = CurrentlyCompilingVersion;
            OmniSharpService.Instance?.Server?.SendNotification<CompilerStatusNotificationArgs>("bblang/compiler/status", new()
            {
                Status = "done",
            });
        }
    }
}
