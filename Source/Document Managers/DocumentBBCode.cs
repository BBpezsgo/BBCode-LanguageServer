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

        (TypeInstance, CompiledType)? GetTypeInstanceAt(SinglePosition position)
        {
            bool Handle1(CompiledType? type, out (TypeInstance, CompiledType) result)
            {
                result = default;
                if (type is null) return false;
                if (type.Origin is null) return false;
                if (!type.Origin.Position.Range.Contains(position)) return false;

                result = (type.Origin, type);
                return true;
            }

            bool Handle2(Statement? statement, out (TypeInstance, CompiledType) result)
            {
                result = default;
                if (statement is null) return false;

                if (statement is TypeCast typeCast)
                { return Handle3(typeCast.Type, typeCast.CompiledType, out result); }

                if (statement is VariableDeclaration variableDeclaration)
                { return Handle3(variableDeclaration.Type, variableDeclaration.CompiledType, out result); }

                return false;
            }

            bool Handle3(TypeInstance? type1, CompiledType? type2, out (TypeInstance, CompiledType) result)
            {
                result = default;
                if (type1 is null || type2 is null) return false;
                if (!type1.Position.Range.Contains(position)) return false;

                result = (type1, type2);
                return true;
            }

            foreach (CompiledFunction function in Functions)
            {
                if (function.FilePath != Path)
                { continue; }

                if (Handle1(function.Type, out (TypeInstance, CompiledType) result1))
                { return result1; }

                foreach (CompiledType parameter in function.ParameterTypes)
                {
                    if (Handle1(parameter, out (TypeInstance, CompiledType) result2))
                    { return result2; }
                }
            }

            foreach (CompiledGeneralFunction function in GeneralFunctions)
            {
                if (function.FilePath != Path)
                { continue; }

                if (Handle1(function.Type, out (TypeInstance, CompiledType) result1))
                { return result1; }

                foreach (CompiledType parameter in function.ParameterTypes)
                {
                    if (Handle1(parameter, out (TypeInstance, CompiledType) result2))
                    { return result2; }
                }
            }

            Statement? statement = AST.GetStatementAt(position);
            if (statement is not null)
            {
                if (Handle2(statement, out (TypeInstance, CompiledType) result1))
                { return result1; }

                foreach (Statement item in statement)
                {
                    if (Handle2(item, out (TypeInstance, CompiledType) result2))
                    { return result2; }
                }
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

            {
                CompiledFunction? function = GetFunctionAt(position);

                if (function != null)
                {
                    contents.Add(new MarkedString("csharp", $"{function.Type} {function.ToReadable()}"));
                }
            }

            static MarkedString GetTypeHover(CompiledType type)
            {
                if (type.IsClass)
                { return new MarkedString("csharp", $"class {type.Name}"); }

                if (type.IsStruct)
                { return new MarkedString("csharp", $"struct {type.Name}"); }

                if (type.IsEnum)
                { return new MarkedString("csharp", $"enum {type.Name}"); }

                return new MarkedString("csharp", type.ToString());
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

                    typeHover = GetTypeHover(statementWithValue.CompiledType);
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

            {
                (TypeInstance, CompiledType)? _type = GetTypeInstanceAt(e.Position.ToCool());

                if (_type.HasValue)
                {
                    CompiledType type = _type.Value.Item2;
                    TypeInstance origin = _type.Value.Item1;

                    range = origin.Position.Range;
                    contents.Add(GetTypeHover(type));
                }
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
                    Position from = item.Position;

                    if (item is AnyCall anyCall &&
                        anyCall.PrevStatement is Field field1)
                    { from = field1.FieldName.Position; }

                    if (item is OperatorCall operatorCall)
                    { from = operatorCall.Operator.Position; }

                    if (item is Field field2)
                    { from = field2.FieldName.Position; }

                    if (item is ConstructorCall constructorCall)
                    { from = new Position(constructorCall.Keyword, constructorCall.TypeName); }

                    if (item is NewInstance newInstance)
                    { from = new Position(newInstance.Keyword, newInstance.TypeName); }

                    if (!from.Range.Contains(e.Position.ToCool()))
                    { continue; }

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

            {
                (TypeInstance, CompiledType)? _type = GetTypeInstanceAt(e.Position.ToCool());

                if (_type.HasValue)
                {
                    CompiledType type = _type.Value.Item2;
                    TypeInstance origin = _type.Value.Item1;

                    if (type.IsClass && type.Class.FilePath != null)
                    {
                        links.Add(new LocationOrLocationLink(new LocationLink()
                        {
                            OriginSelectionRange = origin.Position.ToOmniSharp(),
                            TargetRange = type.Class.Name.Position.ToOmniSharp(),
                            TargetSelectionRange = type.Class.Name.Position.ToOmniSharp(),
                            TargetUri = DocumentUri.From(type.Class.FilePath),
                        }));
                    }

                    if (type.IsStruct && type.Struct.FilePath != null)
                    {
                        links.Add(new LocationOrLocationLink(new LocationLink()
                        {
                            OriginSelectionRange = origin.Position.ToOmniSharp(),
                            TargetRange = type.Struct.Name.Position.ToOmniSharp(),
                            TargetSelectionRange = type.Struct.Name.Position.ToOmniSharp(),
                            TargetUri = DocumentUri.From(type.Struct.FilePath),
                        }));
                    }

                    if (type.IsEnum && type.Enum.FilePath != null)
                    {
                        links.Add(new LocationOrLocationLink(new LocationLink()
                        {
                            OriginSelectionRange = origin.Position.ToOmniSharp(),
                            TargetRange = type.Enum.Identifier.Position.ToOmniSharp(),
                            TargetSelectionRange = type.Enum.Identifier.Position.ToOmniSharp(),
                            TargetUri = DocumentUri.From(type.Enum.FilePath),
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
                DocumentUri? uri = function.FilePath is null ? null : DocumentUri.File(function.FilePath);
                if (uri is not null && !uri.Equals(e.TextDocument.Uri)) continue;

                result.Add(new SymbolInformation()
                {
                    Kind = SymbolKind.Function,
                    Name = function.Identifier.Content,
                    Location = new Location()
                    {
                        Range = function.Position.Range.ToOmniSharp(),
                        Uri = uri ?? e.TextDocument.Uri,
                    },
                });
            }

            foreach (CompiledGeneralFunction function in GeneralFunctions)
            {
                DocumentUri? uri = function.FilePath is null ? null : DocumentUri.File(function.FilePath);
                if (uri is not null && !uri.Equals(e.TextDocument.Uri)) continue;

                result.Add(new SymbolInformation()
                {
                    Kind = SymbolKind.Function,
                    Name = function.Identifier.Content,
                    Location = new Location()
                    {
                        Range = function.Position.Range.ToOmniSharp(),
                        Uri = uri ?? e.TextDocument.Uri,
                    },
                });
            }

            foreach (CompiledClass @class in Classes)
            {
                DocumentUri? uri = @class.FilePath is null ? null : DocumentUri.File(@class.FilePath);
                if (uri is not null && !uri.Equals(e.TextDocument.Uri)) continue;

                result.Add(new SymbolInformation()
                {
                    Kind = SymbolKind.Class,
                    Name = @class.Name.Content,
                    Location = new Location()
                    {
                        Range = @class.Position.ToOmniSharp(),
                        Uri = uri ?? e.TextDocument.Uri,
                    },
                });
            }

            foreach (CompiledStruct @struct in Structs)
            {
                DocumentUri? uri = @struct.FilePath is null ? null : DocumentUri.File(@struct.FilePath);
                if (uri is not null && !uri.Equals(e.TextDocument.Uri)) continue;

                result.Add(new SymbolInformation()
                {
                    Kind = SymbolKind.Struct,
                    Name = @struct.Name.Content,
                    Location = new Location()
                    {
                        Range = @struct.Position.ToOmniSharp(),
                        Uri = uri ?? e.TextDocument.Uri,
                    },
                });
            }

            foreach (CompiledEnum @enum in Enums)
            {
                DocumentUri? uri = @enum.FilePath is null ? null : DocumentUri.File(@enum.FilePath);
                if (uri is not null && !uri.Equals(e.TextDocument.Uri)) continue;

                result.Add(new SymbolInformation()
                {
                    Kind = SymbolKind.Enum,
                    Name = @enum.Identifier.Content,
                    Location = new Location()
                    {
                        Range = @enum.Position.ToOmniSharp(),
                        Uri = uri ?? e.TextDocument.Uri,
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
