using System.Collections.Immutable;
using System.IO;
using System.Net.Http;
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
    static readonly ImmutableArray<string> AdditionalImports = ImmutableArray.Create<string>
    (
        "Primitives"
    );

    static readonly MainGeneratorSettings GeneratorSettings = new(MainGeneratorSettings.Default)
    {
        CheckNullPointers = false,
        DontOptimize = false,
        GenerateComments = false,
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
            string content;
            if (file.IsFile)
            {
                content = File.ReadAllText(file.AbsolutePath);
            }
            else
            {
                using HttpClient client = new();
                using HttpResponseMessage res = client.GetAsync(file, HttpCompletionOption.ResponseHeadersRead).Result;
                res.EnsureSuccessStatusCode();
                content = res.Content.ReadAsStringAsync().Result;
            }

            tokens = Tokenizer.Tokenize(content, tokenizerDiagnostics, PreprocessorVariables.Normal, file, new TokenizerSettings(TokenizerSettings.Default)
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
        string file,
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

        try
        {
            compilerResult = StatementCompiler.CompileFile(file, settings, _diagnostics);
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
            foreach (LanguageCore.Diagnostic item in _diagnostics.Diagnostics)
            { Logger.Log(item.Message); }
            foreach (DiagnosticWithoutContext item in _diagnostics.DiagnosticsWithoutContext)
            { Logger.Log(item.Message); }
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

    public static AnalysisResult Analyze(Uri file)
    {
        AnalysisResult result = AnalysisResult.Empty;

        result.Diagnostics = new Dictionary<Uri, List<OmniSharpDiagnostic>>()
        {
            { file, new List<OmniSharpDiagnostic>() }
        };

        List<IExternalFunction> externalFunctions = BytecodeProcessor.GetExternalFunctions(VoidIO.Instance);
        if (!Compile(result.Diagnostics, file.ToString(), true, new CompilerSettings(CodeGeneratorForMain.DefaultCompilerSettings)
        {
            ExternalFunctions = externalFunctions.ToImmutableArray(),
            PreprocessorVariables = PreprocessorVariables.Normal,
            AdditionalImports = AdditionalImports,
            SourceProviders = ImmutableArray.Create<ISourceProvider>(
                OmniSharpService.Instance!.Documents,
                new FileSourceProvider()
                {
                    ExtraDirectories = new string?[]
                    {
                        "/home/BB/Projects/BBLang/Core/StandardLibrary"
                    },
                }
            ),
            TokenizerSettings = new TokenizerSettings(TokenizerSettings.Default)
            {
                TokenizeComments = true,
            },
            CompileEverything = false,
        }, out CompilerResult compilerResult))
        {
            if (Tokenize(result.Diagnostics, file, true, out ImmutableArray<Token> tokens))
            { result.Tokens = tokens; }

            if (Parse(result.Diagnostics, tokens, file, true, out ParserResult parserResult))
            { result.AST = parserResult; }

            return result;
        }

        if (OmniSharpService.Instance is not null)
        {
            foreach (ParsedFile parsedFile in compilerResult.RawTokens)
            {
                DocumentHandler document = OmniSharpService.Instance.Documents.GetOrCreate(new TextDocumentIdentifier(parsedFile.File));
                if (document is DocumentBBLang documentBBLang)
                {
                    documentBBLang.AST = parsedFile.AST;
                    documentBBLang.Tokens = parsedFile.Tokens.Tokens;
                }
            }
        }

        result.CompilerResult = compilerResult;
        result.AST = compilerResult.RawTokens.First(v => v.File == file).AST;
        result.Tokens = compilerResult.RawTokens.First(v => v.File == file).Tokens.Tokens;

        if (!Generate(result.Diagnostics, compilerResult, file))
        { return result; }

        return result;
    }
}
