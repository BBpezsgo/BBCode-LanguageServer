using LanguageCore;
using LanguageCore.Compiler;
using LanguageCore.Parser;
using LanguageCore.Parser.Statement;
using LanguageCore.Tokenizing;
using Position = LanguageCore.Position;

#pragma warning disable IDE0052 // Remove unread private members

namespace LanguageServer.DocumentManagers
{
    internal class DocumentBBCode : SingleDocumentHandler
    {
        public const string LanguageIdentifier = "bbc";

        Token[] Tokens;

        CompiledClass[] Classes;
        CompiledStruct[] Structs;
        CompiledEnum[] Enums;
        CompiledFunction[] Functions;
        CompiledGeneralFunction[] GeneralFunctions;
        ParserResult AST;

        public DocumentBBCode(DocumentUri uri, string content, string languageId, Documents app) : base(uri, content, languageId, app)
        {
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
            base.OnSaved(e);

            Validate();
        }

        public override void OnOpened(DidOpenTextDocumentParams e)
        {
            base.OnOpened(e);

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

            SinglePosition position = e.Position.ToCool();

            Token? token = Tokens.GetTokenAt(position);

            Range<SinglePosition> range = new(position);

            List<MarkedString> contents = new();

            if (token == null)
            {
                return new Hover()
                {
                    Contents = new MarkedStringsOrMarkupContent(),
                    Range = range.ToOmniSharp(),
                };
            }

            range = token.Position.Range;

            CompiledFunction? function = GetFunctionAt(position);

            if (function != null)
            {
                contents.Add(new MarkedString("csharp", $"{function.Type} {function.ToReadable()}"));
            }

            Statement? statement = AST.GetStatementAt(position);
            if (statement is not null)
            {
                MarkedString? typeHover = null;
                MarkedString? referenceHover = null;

                static void HandleTypeHovering(Statement statement, ref MarkedString? typeHover, ref Range<SinglePosition> range)
                {
                    if (statement is not StatementWithValue statementWithValue ||
                        statementWithValue.CompiledType is null)
                    { return; }

                    typeHover = new MarkedString("csharp", $"{statementWithValue.CompiledType}");
                    range = statement.Position.Range;
                }

                static void HandleReferenceHovering(Statement statement, ref MarkedString? referenceHover, ref Range<SinglePosition> range)
                {
                    if (statement is IReferenceableTo _ref1)
                    {
                        if (_ref1.Reference is CompiledFunction compiledFunction &&
                            compiledFunction.FilePath is not null)
                        {
                            referenceHover = new MarkedString("csharp", $"{compiledFunction.Type} {compiledFunction.ToReadable()}");
                        }
                        else if (_ref1.Reference is MacroDefinition macroDefinition &&
                            macroDefinition.FilePath is not null)
                        {
                            referenceHover = new MarkedString("csharp", $"{macroDefinition.ToReadable()}");
                        }
                        else if (_ref1.Reference is CompiledGeneralFunction generalFunction &&
                            generalFunction.FilePath is not null)
                        {
                            referenceHover = new MarkedString("csharp", $"{generalFunction.Type} {generalFunction.ToReadable()}");
                        }
                        else if (_ref1.Reference is CompiledVariable compiledVariable &&
                                 compiledVariable.FilePath is not null)
                        {
                            referenceHover = new MarkedString("csharp", $"(variable) {compiledVariable.Type} {compiledVariable.VariableName}");
                        }
                        else if (_ref1.Reference is LanguageCore.BBCode.Generator.CompiledParameter compiledParameter &&
                                 !compiledParameter.IsAnonymous)
                        {
                            referenceHover = new MarkedString("csharp", $"(parameter) {compiledParameter.Type} {compiledParameter.Identifier}");
                        }
                        else if (_ref1.Reference is CompiledField compiledField &&
                                 compiledField.Class is not null &&
                                 compiledField.Class.FilePath is not null)
                        {
                            referenceHover = new MarkedString("csharp", $"(field) {compiledField.Type} {compiledField.Identifier}");
                        }
                    }
                }

                HandleTypeHovering(statement, ref typeHover, ref range);
                HandleReferenceHovering(statement, ref referenceHover, ref range);

                foreach (Statement item in statement)
                {
                    if (!item.Position.Range.Contains(e.Position.ToCool()))
                    { continue; }

                    HandleTypeHovering(item, ref typeHover, ref range);
                    HandleReferenceHovering(item, ref referenceHover, ref range);
                }

                if (typeHover is not null) contents.Add(typeHover);
                if (referenceHover is not null) contents.Add(referenceHover);
            }

            return new Hover()
            {
                Contents = new MarkedStringsOrMarkupContent(contents),
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

            List<LocationOrLocationLink> links = new();

            foreach (UsingDefinition @using in AST.Usings)
            {
                if (!@using.Position.Range.Contains(e.Position.ToCool()))
                { continue; }
                if (@using.CompiledUri is null)
                { break; }

                links.Add(new LocationOrLocationLink(new LocationLink()
                {
                    TargetUri = DocumentUri.From(@using.CompiledUri),
                    OriginSelectionRange = new Position(@using.Path).ToOmniSharp(),
                    TargetRange = Position.Zero.ToOmniSharp(),
                    TargetSelectionRange = Position.Zero.ToOmniSharp(),
                }));
                break;
            }

            Statement? statement = AST.GetStatementAt(e.Position.ToCool());
            if (statement is not null)
            {
                foreach (Statement item in statement)
                {
                    if (!item.Position.Range.Contains(e.Position.ToCool()))
                    { continue; }

                    Position from = item.Position;

                    if (item is AnyCall anyCall &&
                        anyCall.PrevStatement is Field field)
                    {
                        from = field.FieldName.Position;
                    }

                    if (item is OperatorCall operatorCall)
                    {
                        from = operatorCall.Operator.Position;
                    }

                    if (item is IReferenceableTo _ref1)
                    {
                        if (_ref1.Reference is CompiledFunction compiledFunction &&
                            compiledFunction.FilePath is not null)
                        {
                            links.Add(new LocationOrLocationLink(new LocationLink()
                            {
                                OriginSelectionRange = from.ToOmniSharp(),
                                TargetRange = compiledFunction.Identifier.Position.ToOmniSharp(),
                                TargetSelectionRange = compiledFunction.Identifier.Position.ToOmniSharp(),
                                TargetUri = DocumentUri.From(compiledFunction.FilePath),
                            }));
                        }
                        else if (_ref1.Reference is MacroDefinition macroDefinition &&
                            macroDefinition.FilePath is not null)
                        {
                            links.Add(new LocationOrLocationLink(new LocationLink()
                            {
                                OriginSelectionRange = from.ToOmniSharp(),
                                TargetRange = macroDefinition.Identifier.Position.ToOmniSharp(),
                                TargetSelectionRange = macroDefinition.Identifier.Position.ToOmniSharp(),
                                TargetUri = DocumentUri.From(macroDefinition.FilePath),
                            }));
                        }
                        else if (_ref1.Reference is CompiledGeneralFunction generalFunction &&
                            generalFunction.FilePath is not null)
                        {
                            links.Add(new LocationOrLocationLink(new LocationLink()
                            {
                                OriginSelectionRange = from.ToOmniSharp(),
                                TargetRange = generalFunction.Identifier.Position.ToOmniSharp(),
                                TargetSelectionRange = generalFunction.Identifier.Position.ToOmniSharp(),
                                TargetUri = DocumentUri.From(generalFunction.FilePath),
                            }));
                        }
                        else if (_ref1.Reference is CompiledVariable compiledVariable &&
                                 compiledVariable.FilePath is not null)
                        {
                            links.Add(new LocationOrLocationLink(new LocationLink()
                            {
                                OriginSelectionRange = from.ToOmniSharp(),
                                TargetRange = compiledVariable.VariableName.Position.ToOmniSharp(),
                                TargetSelectionRange = compiledVariable.VariableName.Position.ToOmniSharp(),
                                TargetUri = DocumentUri.From(compiledVariable.FilePath),
                            }));
                        }
                        else if (_ref1.Reference is LanguageCore.BBCode.Generator.CompiledParameter compiledParameter &&
                                 !compiledParameter.IsAnonymous)
                        {
                            links.Add(new LocationOrLocationLink(new LocationLink()
                            {
                                OriginSelectionRange = from.ToOmniSharp(),
                                TargetRange = compiledParameter.Identifier.Position.ToOmniSharp(),
                                TargetSelectionRange = compiledParameter.Identifier.Position.ToOmniSharp(),
                                TargetUri = e.TextDocument.Uri,
                            }));
                        }
                        else if (_ref1.Reference is CompiledField compiledField &&
                                 compiledField.Class is not null &&
                                 compiledField.Class.FilePath is not null)
                        {
                            links.Add(new LocationOrLocationLink(new LocationLink()
                            {
                                OriginSelectionRange = from.ToOmniSharp(),
                                TargetRange = compiledField.Identifier.Position.ToOmniSharp(),
                                TargetSelectionRange = compiledField.Identifier.Position.ToOmniSharp(),
                                TargetUri = DocumentUri.From(compiledField.Class.FilePath),
                            }));
                        }
                    }

                    if (item is IReferenceableTo<CompiledFunction> _ref2 &&
                        _ref2.Reference is not null &&
                        _ref2.Reference.FilePath is not null)
                    {
                        links.Add(new LocationOrLocationLink(new LocationLink()
                        {
                            OriginSelectionRange = from.ToOmniSharp(),
                            TargetRange = _ref2.Reference.Identifier.Position.ToOmniSharp(),
                            TargetSelectionRange = _ref2.Reference.Identifier.Position.ToOmniSharp(),
                            TargetUri = DocumentUri.From(_ref2.Reference.FilePath),
                        }));
                    }

                    if (item is IReferenceableTo<CompiledOperator> _ref3 &&
                        _ref3.Reference is not null &&
                        _ref3.Reference.FilePath is not null)
                    {
                        links.Add(new LocationOrLocationLink(new LocationLink()
                        {
                            OriginSelectionRange = from.ToOmniSharp(),
                            TargetRange = _ref3.Reference.Identifier.Position.ToOmniSharp(),
                            TargetSelectionRange = _ref3.Reference.Identifier.Position.ToOmniSharp(),
                            TargetUri = DocumentUri.From(_ref3.Reference.FilePath),
                        }));
                    }

                    if (item is IReferenceableTo<CompiledGeneralFunction> _ref4 &&
                        _ref4.Reference is not null &&
                        _ref4.Reference.FilePath is not null)
                    {
                        links.Add(new LocationOrLocationLink(new LocationLink()
                        {
                            OriginSelectionRange = from.ToOmniSharp(),
                            TargetRange = _ref4.Reference.Identifier.Position.ToOmniSharp(),
                            TargetSelectionRange = _ref4.Reference.Identifier.Position.ToOmniSharp(),
                            TargetUri = DocumentUri.From(_ref4.Reference.FilePath),
                        }));
                    }
                }
            }

            return new LocationOrLocationLinks(links);
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
                    case TokenAnalyzedType.FieldName:
                        break;
                    case TokenAnalyzedType.None:
                    default:
                        switch (token.TokenType)
                        {
                            case TokenType.Identifier:
                                break;
                            case TokenType.LiteralString:
                            case TokenType.LiteralCharacter:
                                builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.String, SemanticTokenModifier.Defaults);
                                break;
                            case TokenType.LiteralNumber:
                            case TokenType.LiteralHex:
                            case TokenType.LiteralBinary:
                            case TokenType.LiteralFloat:
                                builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Number, SemanticTokenModifier.Defaults);
                                break;
                            case TokenType.Operator:
                                break;
                            case TokenType.Comment:
                            case TokenType.CommentMultiline:
                                builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Comment, SemanticTokenModifier.Defaults);
                                break;
                            default:
                                break;
                        }
                        break;
                }
            }
        }
    }
}
