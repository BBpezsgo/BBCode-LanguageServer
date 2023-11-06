using System;
using System.Collections.Generic;
using System.Linq;
using LanguageCore.BBCode;
using LanguageCore.BBCode.Compiler;
using LanguageCore.Parser;
using LanguageCore.Parser.Statement;
using LanguageCore.Runtime;
using LanguageCore.Tokenizing;

namespace LanguageServer.DocumentManagers
{
    using Interface;
    using LanguageCore;

    internal class DocumentBBCode : IDocument
    {
        public Uri Uri { get; private set; }

        readonly DocumentInterface App;
        string Text;
        string LanguageId;

        Token[] Tokens;

        CompiledClass[] Classes;
        CompiledStruct[] Structs;
        CompiledEnum[] Enums;
        CompiledFunction[] Functions;
        CompiledGeneralFunction[] GeneralFunctions;

        public DocumentBBCode(DocumentItem document, DocumentInterface app)
        {
            App = app;
            OnChanged(document);
        }

        public void OnChanged(DocumentItem e)
        {
            Text = e.Content;
            Uri = e.Uri;
            LanguageId = e.LanguageID;

            Validate(new Document(e));
        }

        public void OnSaved(Document e)
        {
            Validate(e);
        }

        Token GetTokenAt(SinglePosition position)
        {
            if (Tokens == null) return null;
            if (Tokens.Length == 0) return null;

            foreach (var token in Tokens)
            { if (token.Position.Range.Contains(position)) return token; }

            return null;
        }

        static (Error[] errors, Warning[] warnings, Information[] informations, Hint[] hints) CollectErrors(LanguageCore.BBCode.EasyCompiler.Result compilerResult)
        {
            List<Error> errors = new();
            List<Warning> warnings = new();
            List<Information> informations = new();
            List<Hint> hints = new();

            if (compilerResult.CompilerResult.Warnings != null)
            { warnings.AddRange(compilerResult.CompilerResult.Warnings); }

            if (compilerResult.CompilerResult.Errors != null)
            { errors.AddRange(compilerResult.CompilerResult.Errors); }

            if (compilerResult.CodeGeneratorResult.Warnings != null)
            { warnings.AddRange(compilerResult.CodeGeneratorResult.Warnings); }

            if (compilerResult.CodeGeneratorResult.Errors != null)
            { errors.AddRange(compilerResult.CodeGeneratorResult.Errors); }

            if (compilerResult.CodeGeneratorResult.Informations != null)
            { informations.AddRange(compilerResult.CodeGeneratorResult.Informations); }

            if (compilerResult.CodeGeneratorResult.Hints != null)
            { hints.AddRange(compilerResult.CodeGeneratorResult.Hints); }

            return (errors.ToArray(), warnings.ToArray(), informations.ToArray(), hints.ToArray());
        }

        static DiagnosticInfo[] GetDiagnosticInfos(string currentPath, params Hint[] hints)
        {
            List<DiagnosticInfo> diagnostics = new();

            for (int i = 0; i < hints.Length; i++)
            {
                Hint hint = hints[i];

                if (hint.File == null || hint.File != currentPath) continue;

                diagnostics.Add(new DiagnosticInfo
                {
                    severity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Hint,
                    range = hint.Position,
                    message = hint.Message,
                });
            }

            return diagnostics.ToArray();
        }

        static DiagnosticInfo[] GetDiagnosticInfos(string currentPath, params Information[] informations)
        {
            List<DiagnosticInfo> diagnostics = new();

            for (int i = 0; i < informations.Length; i++)
            {
                Information information = informations[i];

                if (information.File == null || information.File != currentPath) continue;

                diagnostics.Add(new DiagnosticInfo
                {
                    severity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Information,
                    range = information.Position,
                    message = information.Message,
                });
            }

            return diagnostics.ToArray();
        }

        static DiagnosticInfo[] GetDiagnosticInfos(string currentPath, params Warning[] warnings)
        {
            List<DiagnosticInfo> diagnostics = new();

            for (int i = 0; i < warnings.Length; i++)
            {
                Warning warning = warnings[i];

                if (warning.File == null || warning.File != currentPath) continue;

                diagnostics.Add(new DiagnosticInfo
                {
                    severity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Warning,
                    range = warning.Position,
                    message = warning.Message,
                });
            }

            return diagnostics.ToArray();
        }

        static DiagnosticInfo[] GetDiagnosticInfos(string currentPath, params Error[] errors)
        {
            List<DiagnosticInfo> diagnostics = new();

            for (int i = 0; i < errors.Length; i++)
            {
                Error error = errors[i];

                if (error.File == null || error.File != currentPath) continue;

                diagnostics.Add(new DiagnosticInfo
                {
                    severity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Error,
                    range = error.Position,
                    message = error.Message,
                });
            }

            return diagnostics.ToArray();
        }

        static DiagnosticInfo[] GetDiagnosticInfos(Error[] errors, Warning[] warnings, Information[] informations, Hint[] hints, string currentPath)
        {
            List<DiagnosticInfo> diagnostics = new();

            for (int i = 0; i < errors.Length; i++)
            {
                Error error = errors[i];

                if (error.File == null || error.File != currentPath) continue;

                diagnostics.Add(new DiagnosticInfo
                {
                    severity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Error,
                    range = error.Position,
                    message = error.Message,
                });
            }

            for (int i = 0; i < warnings.Length; i++)
            {
                Warning warning = warnings[i];

                if (warning.File == null || warning.File != currentPath) continue;

                diagnostics.Add(new DiagnosticInfo
                {
                    severity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Warning,
                    range = warning.Position,
                    message = warning.Message,
                });
            }

            for (int i = 0; i < informations.Length; i++)
            {
                Information information = informations[i];

                if (information.File == null || information.File != currentPath) continue;

                diagnostics.Add(new DiagnosticInfo
                {
                    severity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Information,
                    range = information.Position,
                    message = information.Message,
                });
            }

            for (int i = 0; i < hints.Length; i++)
            {
                Hint hint = hints[i];

                if (hint.File == null || hint.File != currentPath) continue;

                diagnostics.Add(new DiagnosticInfo
                {
                    severity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Hint,
                    range = hint.Position,
                    message = hint.Message,
                });
            }

            return diagnostics.ToArray();
        }

        List<DiagnosticInfo> Compile(System.IO.FileInfo file)
        {
            List<DiagnosticInfo> diagnostics = new();

            string basePath = null;
            try
            {
                string configFile = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(file.FullName), "config.json");
                if (System.IO.File.Exists(configFile))
                {
                    string configRaw = System.IO.File.ReadAllText(configFile);
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

            try
            {
                TokenizerResult tokenizerResult = Tokenizer.Tokenize(
                    System.IO.File.ReadAllText(file.FullName),
                    file.FullName);
                Tokens = tokenizerResult.Tokens;

                diagnostics.AddRange(GetDiagnosticInfos(file.FullName, tokenizerResult.Warnings));

                ParserResult ast = Parser.Parse(Tokens);

                diagnostics.AddRange(GetDiagnosticInfos(file.FullName, ast.Errors));

                Compiler.Result compiled = Compiler.Compile(
                    ast,
                    new Dictionary<string, ExternalFunctionBase>(),
                    file,
                    null,
                    basePath);

                diagnostics.AddRange(GetDiagnosticInfos(file.FullName, compiled.Warnings));
                diagnostics.AddRange(GetDiagnosticInfos(file.FullName, compiled.Errors));

                CodeGenerator.Result generated = CodeGenerator.Generate(
                    compiled,
                    Compiler.CompilerSettings.Default,
                    null,
                    Compiler.CompileLevel.All);

                diagnostics.AddRange(GetDiagnosticInfos(file.FullName, generated.Hints));
                diagnostics.AddRange(GetDiagnosticInfos(file.FullName, generated.Informations));
                diagnostics.AddRange(GetDiagnosticInfos(file.FullName, generated.Warnings));
                diagnostics.AddRange(GetDiagnosticInfos(file.FullName, generated.Errors));

                Functions = compiled.Functions;
                GeneralFunctions = compiled.GeneralFunctions;

                Enums = compiled.Enums;

                Classes = compiled.Classes;
                Structs = compiled.Structs;

                Logger.Log($"Successfully compiled ({file.Name})");
            }
            catch (LanguageException exception)
            {
                Position range = exception.Position;

                Logger.Log($"Exception: {exception}\n  at {range.ToStringRange()}");

                diagnostics.Add(new DiagnosticInfo
                {
                    severity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Error,
                    range = range,
                    message = exception.Message,
                });

                if (exception.InnerException is LanguageException innerException)
                {
                    range = innerException.Position;

                    diagnostics.Add(new DiagnosticInfo
                    {
                        severity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Error,
                        range = range,
                        message = innerException.Message,
                    });
                }
            }
            catch (System.Exception exception)
            {
                Logger.Log($"System Exception: {exception}");
            }

            return diagnostics;
        }

        void Validate(Document e)
        {
            string path = System.Net.WebUtility.UrlDecode(e.Uri.AbsolutePath);

            Logger.Log($"Validate({path})");

            if (e.Uri.Scheme != "file") return;

            if (!System.IO.File.Exists(path))
            {
                Logger.Log($"{path} not found");
                return;
            }

            System.IO.FileInfo file = new(path);

            List<DiagnosticInfo> diagnostics = Compile(file);

            for (int i = diagnostics.Count - 1; i >= 0; i--) if (diagnostics[i].range.ToStringRange() == "0:0") diagnostics.RemoveAt(i);

            App.Interface.PublishDiagnostics(e.Uri, diagnostics.ToArray());
        }

        CompletionInfo[] IDocument.Completion(DocumentPositionContextEventArgs e)
        {
            Logger.Log($"Completion()");

            List<CompletionInfo> result = new();

            foreach (CompiledFunction function in Functions)
            {
                if (function.Context != null) continue;

                result.Add(new CompletionInfo()
                {
                    Deprecated = false,
                    Detail = function.ReadableID(),
                    Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Function,
                    Label = function.Identifier.Content,
                    Preselect = false,
                });
            }

            foreach (CompiledEnum @enum in Enums)
            {
                result.Add(new CompletionInfo()
                {
                    Deprecated = false,
                    Detail = null,
                    Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Enum,
                    Label = @enum.Identifier.Content,
                    Preselect = false,
                });
            }

            foreach (CompiledClass @class in Classes)
            {
                result.Add(new CompletionInfo()
                {
                    Deprecated = false,
                    Detail = null,
                    Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Class,
                    Label = @class.Name.Content,
                    Preselect = false,
                });
            }

            foreach (CompiledStruct @struct in Structs)
            {
                result.Add(new CompletionInfo()
                {
                    Deprecated = false,
                    Detail = null,
                    Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Struct,
                    Label = @struct.Name.Content,
                    Preselect = false,
                });
            }

            SinglePosition position = e.Position;
            foreach (CompiledFunction function in Functions)
            {
                if (function.Block == null) continue;
                if (function.Block.Position.Range.Contains(position))
                {
                    foreach (var parameter in function.Parameters)
                    {
                        result.Add(new CompletionInfo()
                        {
                            Deprecated = false,
                            Detail = null,
                            Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Variable,
                            Label = parameter.Identifier.Content,
                            Preselect = false,
                        });
                    }

                    break;
                }
            }

            return result.ToArray();
        }

        HoverInfo IDocument.Hover(DocumentPositionEventArgs e)
        {
            Logger.Log($"Hover({e.Position.ToStringMin()})");

            List<HoverContent> result = new();
            Range<SinglePosition> range = new(e.Position);

            return new HoverInfo()
            {
                Contents = result.ToArray(),
                Range = range,
            };
        }

        CodeLensInfo[] IDocument.CodeLens(DocumentEventArgs e)
        {
            if (Functions == null) return Array.Empty<CodeLensInfo>();

            List<CodeLensInfo> result = new();

            foreach (CompiledFunction function in Functions)
            {
                if (function.FilePath != e.Document.Uri.AbsolutePath)
                { continue; }

                if (function.CompiledAttributes.ContainsKey("CodeEntry"))
                { result.Add(new CodeLensInfo($"This is the code entry", function.Identifier)); }

                result.Add(new CodeLensInfo($"{function.TimesUsedTotal} reference", function.Identifier));
            }

            return result.ToArray();
        }

        SingleOrArray<FilePosition>? IDocument.GotoDefinition(DocumentPositionEventArgs e)
        {
            Logger.Log($"GotoDefinition({e.Position.ToStringMin()})");

            return null;
        }

        SymbolInformationInfo[] IDocument.Symbols(DocumentEventArgs e)
        {
            Logger.Log($"Symbols()");

            List<SymbolInformationInfo> result = new();

            foreach (var function in Functions)
            {
                if (function.FilePath != null && function.FilePath.Replace('\\', '/') != e.Document.Uri.ToString().Replace("file:///", string.Empty).Replace('\\', '/')) continue;

                result.Add(new SymbolInformationInfo()
                {
                    Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Function,
                    Name = function.Identifier.Content,
                    Location = new DocumentLocation()
                    {
                        Range = function.Position,
                        Uri = function.FilePath is null ? e.Document.Uri : new Uri($"file:///{function.FilePath.Replace('\\', '/')}", UriKind.Absolute),
                    },
                });
            }

            foreach (var @class in Classes)
            {
                if (@class.FilePath != null && @class.FilePath.Replace('\\', '/') != e.Document.Uri.ToString().Replace("file:///", string.Empty).Replace('\\', '/')) continue;

                result.Add(new SymbolInformationInfo()
                {
                    Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Class,
                    Name = @class.Name.Content,
                    Location = new DocumentLocation()
                    {
                        Range = @class.Position,
                        Uri = @class.FilePath is null ? e.Document.Uri : new Uri($"file:///{@class.FilePath.Replace('\\', '/')}", UriKind.Absolute),
                    },
                });
            }

            foreach (var @struct in Structs)
            {
                if (@struct.FilePath != null && @struct.FilePath.Replace('\\', '/') != e.Document.Uri.ToString().Replace("file:///", string.Empty).Replace('\\', '/')) continue;

                result.Add(new SymbolInformationInfo()
                {
                    Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Struct,
                    Name = @struct.Name.Content,
                    Location = new DocumentLocation()
                    {
                        Range = @struct.Position,
                        Uri = @struct.FilePath is null ? e.Document.Uri : new Uri($"file:///{@struct.FilePath.Replace('\\', '/')}", UriKind.Absolute),
                    },
                });
            }

            foreach (var @enum in Enums)
            {
                if (@enum.FilePath != null && @enum.FilePath.Replace('\\', '/') != e.Document.Uri.ToString().Replace("file:///", string.Empty).Replace('\\', '/')) continue;

                result.Add(new SymbolInformationInfo()
                {
                    Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Enum,
                    Name = @enum.Identifier.Content,
                    Location = new DocumentLocation()
                    {
                        Range = @enum.Position,
                        Uri = @enum.FilePath is null ? e.Document.Uri : new Uri($"file:///{@enum.FilePath.Replace('\\', '/')}", UriKind.Absolute),
                    },
                });
            }

            return result.ToArray();
        }

        FilePosition[] IDocument.References(FindReferencesEventArgs e)
        {
            Logger.Log($"References({e.Position.ToStringMin()})");

            return Array.Empty<FilePosition>();
        }

        SignatureHelpInfo IDocument.SignatureHelp(SignatureHelpEventArgs e)
        {
            return null;
        }

        SemanticToken[] IDocument.GetSemanticTokens(DocumentEventArgs e)
        {
            if (Tokens == null) return Array.Empty<SemanticToken>();

            List<SemanticToken> result = new();

            foreach (var token in Tokens)
            {
                switch (token.AnalyzedType)
                {
                    case TokenAnalyzedType.Attribute:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Type));
                        break;
                    case TokenAnalyzedType.Type:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Type));
                        break;
                    case TokenAnalyzedType.Struct:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Struct));
                        break;
                    case TokenAnalyzedType.Keyword:
                        break;
                    case TokenAnalyzedType.FunctionName:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Function));
                        break;
                    case TokenAnalyzedType.VariableName:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Variable));
                        break;
                    case TokenAnalyzedType.ParameterName:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Parameter));
                        break;
                    case TokenAnalyzedType.Namespace:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Namespace));
                        break;
                    case TokenAnalyzedType.Library:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Namespace));
                        break;
                    case TokenAnalyzedType.Class:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Class));
                        break;
                    case TokenAnalyzedType.Statement:
                        break;
                    case TokenAnalyzedType.BuiltinType:
                        break;
                    case TokenAnalyzedType.Enum:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Enum));
                        break;
                    case TokenAnalyzedType.EnumMember:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.EnumMember));
                        break;
                    case TokenAnalyzedType.TypeParameter:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.TypeParameter));
                        break;
                    case TokenAnalyzedType.Hash:
                    case TokenAnalyzedType.HashParameter:

                    case TokenAnalyzedType.None:
                    case TokenAnalyzedType.FieldName:
                    default:
                        break;
                }
            }

            return result.ToArray();
        }
    }
}
