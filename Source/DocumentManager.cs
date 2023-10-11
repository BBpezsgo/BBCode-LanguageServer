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
            { if (token.Position.Contains(position)) return token; }

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

            try
            {
                EasyCompiler.Result result = EasyCompiler.Compile(
                    file,
                    new Dictionary<string, ExternalFunctionBase>(),
                    TokenizerSettings.Default,
                    ParserSettings.Default,
                    Compiler.CompilerSettings.Default,
                    null,
                    null);

                (Error[] errors, Warning[] warnings, Information[] informations, Hint[] hints) = CollectErrors(result);

                diagnostics.AddRange(GetDiagnosticInfos(errors, warnings, informations, hints, file.FullName));

                Functions = result.CodeGeneratorResult.Functions ?? result.CompilerResult.Functions;
                GeneralFunctions = result.CodeGeneratorResult.GeneralFunctions ?? result.CompilerResult.GeneralFunctions;

                Enums = result.CompilerResult.Enums;

                Classes = result.CodeGeneratorResult.Classes ?? result.CompilerResult.Classes;
                Structs = result.CodeGeneratorResult.Structs ?? result.CompilerResult.Structs;

                Tokens = result.TokenizerResult;

                Logger.Log($"Succesfully compiled ({file.Name})");
            }
            catch (Exception exception)
            {
                Position range = exception.Position;

                Logger.Log($"Exception: {exception}\n  at {range.ToMinString()}");

                diagnostics.Add(new DiagnosticInfo
                {
                    severity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Error,
                    range = range,
                    message = exception.Message,
                });

                if (exception.InnerException is Exception innerException)
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

            for (int i = diagnostics.Count - 1; i >= 0; i--) if (diagnostics[i].range.ToMinString() == "0:0") diagnostics.RemoveAt(i);

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
                    Detail = function.ExternalFunctionName ?? null,
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
                if (function.Block.GetPosition().Range.Contains(position))
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
            Logger.Log($"Hover({e.Position.ToMinString()})");

            List<HoverContent> result = new();
            Range<SinglePosition> range = new(e.Position, e.Position);

            return new HoverInfo()
            {
                Contents = result.ToArray(),
                Range = range,
            };
        }

        CodeLensInfo[] IDocument.CodeLens(DocumentEventArgs e)
        {
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
            Logger.Log($"GotoDefinition({e.Position.ToMinString()})");

            return null;
        }

        SymbolInformationInfo[] IDocument.Symbols(DocumentEventArgs e)
        {
            Logger.Log($"Symbols()");

            return Array.Empty<SymbolInformationInfo>();
        }

        FilePosition[] IDocument.References(FindReferencesEventArgs e)
        {
            Logger.Log($"References({e.Position.ToMinString()})");

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
                    case TokenAnalysedType.Attribute:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Type));
                        break;
                    case TokenAnalysedType.Type:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Type));
                        break;
                    case TokenAnalysedType.Struct:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Struct));
                        break;
                    case TokenAnalysedType.Keyword:
                        break;
                    case TokenAnalysedType.FunctionName:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Function));
                        break;
                    case TokenAnalysedType.VariableName:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Variable));
                        break;
                    case TokenAnalysedType.ParameterName:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Parameter));
                        break;
                    case TokenAnalysedType.Namespace:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Namespace));
                        break;
                    case TokenAnalysedType.Library:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Namespace));
                        break;
                    case TokenAnalysedType.Class:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Class));
                        break;
                    case TokenAnalysedType.Statement:
                        break;
                    case TokenAnalysedType.BuiltinType:
                        break;
                    case TokenAnalysedType.Enum:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Enum));
                        break;
                    case TokenAnalysedType.EnumMember:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.EnumMember));
                        break;
                    case TokenAnalysedType.TypeParameter:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.TypeParameter));
                        break;
                    case TokenAnalysedType.Hash:
                    case TokenAnalysedType.HashParameter:

                    case TokenAnalysedType.None:
                    case TokenAnalysedType.FieldName:
                    default:
                        break;
                }
            }

            return result.ToArray();
        }
    }
}
