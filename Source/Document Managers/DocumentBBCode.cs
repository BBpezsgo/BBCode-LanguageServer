#pragma warning disable IDE0052 // Remove unread private members
#pragma warning disable IDE0051 // Remove unused private members

namespace LanguageServer.DocumentManagers
{
    using LanguageCore;
    using LanguageCore.Compiler;
    using LanguageCore.Parser;
    using LanguageCore.Parser.Statement;
    using LanguageCore.Tokenizing;

    internal class DocumentBBCode : SingleDocumentHandler
    {
        Token[] Tokens;

        CompiledClass[] Classes;
        CompiledStruct[] Structs;
        CompiledEnum[] Enums;
        CompiledFunction[] Functions;
        CompiledGeneralFunction[] GeneralFunctions;
        ParserResult AST;

        public DocumentBBCode(DocumentUri uri, string content, string languageId, Documents app) : base(uri, content, languageId, app)
        {
            Validate();

            Tokens = Array.Empty<Token>();

            Classes = Array.Empty<CompiledClass>();
            Structs = Array.Empty<CompiledStruct>();
            Enums = Array.Empty<CompiledEnum>();
            Functions = Array.Empty<CompiledFunction>();
            GeneralFunctions = Array.Empty<CompiledGeneralFunction>();

            AST = ParserResult.Empty;
        }

        public override void OnChanged(DidChangeTextDocumentParams e)
        {
            base.OnChanged(e);

            Validate();
        }

        public override void OnSaved(DidSaveTextDocumentParams e)
        {
            Validate();
        }

        CompiledFunction? GetFunctionAt(SinglePosition position)
        {
            for (int i = 0; i < Functions.Length; i++)
            {
                if (Functions[i].FilePath != Path)
                { continue; }

                if (!Functions[i].Identifier.Position.Range.Contains(position))
                { continue; }

                return Functions[i];
            }
            return null;
        }

        void Validate()
        {
            string path = Path;

            Logger.Log($"Validating \"{path}\" ...");

            if (Uri.Scheme != "file") return;

            if (!System.IO.File.Exists(path))
            {
                Logger.Warn($"File \"{path}\" not found");
                return;
            }

            System.IO.FileInfo file = new(path);

            AnalysisResult analysisResult = Analysis.Analyze(file);

            Tokens = analysisResult.Tokens;

            AST = analysisResult.AST;

            Structs = analysisResult.Structs;
            Classes = analysisResult.Classes;
            Enums = analysisResult.Enums;
            Functions = analysisResult.Functions;
            GeneralFunctions = analysisResult.GeneralFunctions;

            foreach (KeyValuePair<string, List<Diagnostic>> diagnostics in analysisResult.Diagnostics)
            {
                IEnumerable<Diagnostic> filteredDiagnostics = diagnostics.Value.Where(item => !item.Range.IsEmpty());

                OmniSharpService.Instance?.Server?.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams()
                {
                    Uri = DocumentUri.File(diagnostics.Key),
                    Diagnostics = new Container<Diagnostic>(filteredDiagnostics),
                });
            }
        }

        public override CompletionItem[] Completion(CompletionParams e)
        {
            Logger.Log($"Completion()");

            List<CompletionItem> result = new();

            foreach (CompiledFunction function in Functions)
            {
                if (function.Context != null) continue;

                result.Add(new CompletionItem()
                {
                    Deprecated = false,
                    Detail = function.ToReadable(),
                    Kind = CompletionItemKind.Function,
                    Label = function.Identifier.Content,
                    Preselect = false,
                });
            }

            foreach (CompiledEnum @enum in Enums)
            {
                result.Add(new CompletionItem()
                {
                    Deprecated = false,
                    Detail = null,
                    Kind = CompletionItemKind.Enum,
                    Label = @enum.Identifier.Content,
                    Preselect = false,
                });
            }

            foreach (CompiledClass @class in Classes)
            {
                result.Add(new CompletionItem()
                {
                    Deprecated = false,
                    Detail = null,
                    Kind = CompletionItemKind.Class,
                    Label = @class.Name.Content,
                    Preselect = false,
                });
            }

            foreach (CompiledStruct @struct in Structs)
            {
                result.Add(new CompletionItem()
                {
                    Deprecated = false,
                    Detail = null,
                    Kind = CompletionItemKind.Struct,
                    Label = @struct.Name.Content,
                    Preselect = false,
                });
            }

            SinglePosition position = e.Position.ToCool();
            foreach (CompiledFunction function in Functions)
            {
                if (function.Block == null) continue;
                if (function.Block.Position.Range.Contains(position))
                {
                    foreach (ParameterDefinition parameter in function.Parameters)
                    {
                        result.Add(new CompletionItem()
                        {
                            Deprecated = false,
                            Detail = null,
                            Kind = CompletionItemKind.Variable,
                            Label = parameter.Identifier.Content,
                            Preselect = false,
                        });
                    }

                    break;
                }
            }

            return result.ToArray();
        }

        public override Hover Hover(HoverParams e)
        {
            Logger.Log($"Hover({e.Position.ToCool().ToStringMin()})");

            List<MarkedString> result = new();
            Range<SinglePosition> range = new(e.Position.ToCool());

            return new Hover()
            {
                Contents = new MarkedStringsOrMarkupContent(result),
                Range = range.ToOmniSharp(),
            };
        }

        public override CodeLens[] CodeLens(CodeLensParams e)
        {
            List<CodeLens> result = new();

            foreach (CompiledFunction function in Functions)
            {
                if (function.FilePath != Path)
                { continue; }

                result.Add(new CodeLens()
                {
                    Range = function.Identifier.Position.Range.ToOmniSharp(),
                    Command = new Command()
                    {
                        Title = $"{function.References.Count} reference",
                    },
                });
            }

            return result.ToArray();
        }

        public override LocationOrLocationLinks? GotoDefinition(DefinitionParams e)
        {
            Logger.Log($"GotoDefinition({e.Position.ToCool().ToStringMin()})");

            return null;
        }

        public override SymbolInformationOrDocumentSymbol[] Symbols(DocumentSymbolParams e)
        {
            Logger.Log($"Symbols()");

            List<SymbolInformationOrDocumentSymbol> result = new();

            foreach (CompiledFunction function in Functions)
            {
                if (function.FilePath != null && function.FilePath.Replace('\\', '/') != e.TextDocument.Uri.ToString().Replace("file:///", string.Empty).Replace('\\', '/')) continue;

                result.Add(new SymbolInformation()
                {
                    Kind = SymbolKind.Function,
                    Name = function.Identifier.Content,
                    Location = new Location()
                    {
                        Range = function.Position.Range.ToOmniSharp(),
                        Uri = function.FilePath is null ? e.TextDocument.Uri : new Uri($"file:///{function.FilePath.Replace('\\', '/')}", UriKind.Absolute),
                    },
                });
            }

            foreach (CompiledClass @class in Classes)
            {
                if (@class.FilePath != null && @class.FilePath.Replace('\\', '/') != e.TextDocument.Uri.ToString().Replace("file:///", string.Empty).Replace('\\', '/')) continue;

                result.Add(new SymbolInformation()
                {
                    Kind = SymbolKind.Class,
                    Name = @class.Name.Content,
                    Location = new Location()
                    {
                        Range = @class.Position.ToOmniSharp(),
                        Uri = @class.FilePath is null ? e.TextDocument.Uri : new Uri($"file:///{@class.FilePath.Replace('\\', '/')}", UriKind.Absolute),
                    },
                });
            }

            foreach (CompiledStruct @struct in Structs)
            {
                if (@struct.FilePath != null && @struct.FilePath.Replace('\\', '/') != e.TextDocument.Uri.ToString().Replace("file:///", string.Empty).Replace('\\', '/')) continue;

                result.Add(new SymbolInformation()
                {
                    Kind = SymbolKind.Struct,
                    Name = @struct.Name.Content,
                    Location = new Location()
                    {
                        Range = @struct.Position.ToOmniSharp(),
                        Uri = @struct.FilePath is null ? e.TextDocument.Uri : new Uri($"file:///{@struct.FilePath.Replace('\\', '/')}", UriKind.Absolute),
                    },
                });
            }

            foreach (CompiledEnum @enum in Enums)
            {
                if (@enum.FilePath != null && @enum.FilePath.Replace('\\', '/') != e.TextDocument.Uri.ToString().Replace("file:///", string.Empty).Replace('\\', '/')) continue;

                result.Add(new SymbolInformation()
                {
                    Kind = SymbolKind.Enum,
                    Name = @enum.Identifier.Content,
                    Location = new Location()
                    {
                        Range = @enum.Position.ToOmniSharp(),
                        Uri = @enum.FilePath is null ? e.TextDocument.Uri : new Uri($"file:///{@enum.FilePath.Replace('\\', '/')}", UriKind.Absolute),
                    },
                });
            }

            return result.ToArray();
        }

        public override Location[] References(ReferenceParams e)
        {
            Logger.Log($"References({e.Position.ToCool().ToStringMin()})");

            List<Location> result = new();

            CompiledFunction? function = GetFunctionAt(e.Position.ToCool());
            if (function is not null)
            {
                for (int i = 0; i < function.References.Count; i++)
                {
                    Reference<Statement> reference = function.References[i];
                    if (reference.SourceFile == null) continue;
                    result.Add(new Location()
                    {
                        Range = reference.Source.Position.ToOmniSharp(),
                        Uri = reference.SourceFile,
                    });
                }
            }

            return result.ToArray();
        }

        public override SignatureHelp? SignatureHelp(SignatureHelpParams e)
        {
            return null;
        }

        public override void GetSemanticTokens(SemanticTokensBuilder builder, ITextDocumentIdentifierParams e)
        {
            if (Tokens == null) return;

            foreach (Token token in Tokens)
            {
                switch (token.AnalyzedType)
                {
                    case TokenAnalyzedType.Attribute:
                        builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Type, SemanticTokenModifier.Defaults);
                        break;
                    case TokenAnalyzedType.Type:
                        builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Type, SemanticTokenModifier.Defaults);
                        break;
                    case TokenAnalyzedType.Struct:
                        builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Struct, SemanticTokenModifier.Defaults);
                        break;
                    case TokenAnalyzedType.Keyword:
                        break;
                    case TokenAnalyzedType.FunctionName:
                        builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Function, SemanticTokenModifier.Defaults);
                        break;
                    case TokenAnalyzedType.VariableName:
                        builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Variable, SemanticTokenModifier.Defaults);
                        break;
                    case TokenAnalyzedType.ParameterName:
                        builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Parameter, SemanticTokenModifier.Defaults);
                        break;
                    case TokenAnalyzedType.Namespace:
                        builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Namespace, SemanticTokenModifier.Defaults);
                        break;
                    case TokenAnalyzedType.Library:
                        builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Namespace, SemanticTokenModifier.Defaults);
                        break;
                    case TokenAnalyzedType.Class:
                        builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Class, SemanticTokenModifier.Defaults);
                        break;
                    case TokenAnalyzedType.Statement:
                        break;
                    case TokenAnalyzedType.BuiltinType:
                        break;
                    case TokenAnalyzedType.Enum:
                        builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Enum, SemanticTokenModifier.Defaults);
                        break;
                    case TokenAnalyzedType.EnumMember:
                        builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.EnumMember, SemanticTokenModifier.Defaults);
                        break;
                    case TokenAnalyzedType.TypeParameter:
                        builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.TypeParameter, SemanticTokenModifier.Defaults);
                        break;
                    case TokenAnalyzedType.Hash:
                    case TokenAnalyzedType.HashParameter:
                        break;
                    case TokenAnalyzedType.None:
                    case TokenAnalyzedType.FieldName:
                    default:
                        break;
                }
            }
        }
    }
}
