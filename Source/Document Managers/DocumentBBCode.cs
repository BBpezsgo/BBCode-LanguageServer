using LanguageCore;
using LanguageCore.Compiler;
using LanguageCore.Parser;
using LanguageCore.Parser.Statement;
using LanguageCore.Runtime;
using LanguageCore.Tokenizing;
using Position = LanguageCore.Position;

namespace LanguageServer.DocumentManagers;

internal class DocumentBBCode : SingleDocumentHandler
{
    public const string LanguageIdentifier = "bbc";

    Token[] Tokens;
    ParserResult AST;
    CompilerResult CompilerResult;

    public DocumentBBCode(DocumentUri uri, string content, string languageId, Documents app) : base(uri, content, languageId, app)
    {
        Tokens = Array.Empty<Token>();
        AST = ParserResult.Empty;
        CompilerResult = CompilerResult.Empty;
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

    (TypeInstance, GeneralType)? GetTypeInstanceAt(SinglePosition position)
    {
        bool Handle1(GeneralType? type, out (TypeInstance, GeneralType) result)
        {
            result = default;
            if (type is null) return false;
            return false;
            // if (type.Origin is null) return false;
            // if (!type.Origin.Position.Range.Contains(position)) return false;
            // 
            // result = (type.Origin, type);
            // return true;
        }

        bool Handle2(Statement? statement, out (TypeInstance, GeneralType) result)
        {
            result = default;
            if (statement is null) return false;

            if (statement is TypeCast typeCast)
            { return Handle3(typeCast.Type, typeCast.CompiledType, out result); }

            if (statement is VariableDeclaration variableDeclaration)
            { return Handle3(variableDeclaration.Type, variableDeclaration.CompiledType, out result); }

            return false;
        }

        bool Handle3(TypeInstance? type1, GeneralType? type2, out (TypeInstance, GeneralType) result)
        {
            result = default;
            if (type1 is null || type2 is null) return false;
            if (!type1.Position.Range.Contains(position)) return false;

            result = (type1, type2);
            return true;
        }

        foreach (CompiledFunction function in CompilerResult.Functions)
        {
            if (function.FilePath != Uri)
            { continue; }

            if (Handle1(function.Type, out (TypeInstance, GeneralType) result1))
            { return result1; }

            foreach (GeneralType parameter in function.ParameterTypes)
            {
                if (Handle1(parameter, out (TypeInstance, GeneralType) result2))
                { return result2; }
            }
        }

        foreach (CompiledGeneralFunction function in CompilerResult.GeneralFunctions)
        {
            if (function.FilePath != Uri)
            { continue; }

            if (Handle1(function.Type, out (TypeInstance, GeneralType) result1))
            { return result1; }

            foreach (GeneralType parameter in function.ParameterTypes)
            {
                if (Handle1(parameter, out (TypeInstance, GeneralType) result2))
                { return result2; }
            }
        }

        Statement? statement = AST.GetStatementAt(position);
        if (statement is not null)
        {
            foreach (Statement item in statement.GetStatementsRecursively(true))
            {
                if (Handle2(item, out (TypeInstance, GeneralType) result2))
                { return result2; }
            }
        }

        return null;
    }

    void Validate()
    {
        Logger.Log($"Validating \"{Uri}\" ...");

        AnalysisResult analysisResult = Analysis.Analyze(Uri);

        Tokens = analysisResult.Tokens;
        AST = analysisResult.AST;
        CompilerResult = analysisResult.CompilerResult ?? CompilerResult.Empty;

        foreach (KeyValuePair<Uri, List<Diagnostic>> diagnostics in analysisResult.Diagnostics)
        {
            IEnumerable<Diagnostic> filteredDiagnostics = diagnostics.Value.Where(item => !item.Range.IsEmpty());

            OmniSharpService.Instance?.Server?.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams()
            {
                Uri = diagnostics.Key,
                Diagnostics = new Container<Diagnostic>(filteredDiagnostics),
            });
        }
    }

    public override CompletionItem[] Completion(CompletionParams e)
    {
        Logger.Log($"Completion()");

        List<CompletionItem> result = new();

        foreach (CompiledFunction function in CompilerResult.Functions)
        {
            result.Add(new CompletionItem()
            {
                Deprecated = false,
                Detail = function.ToReadable(),
                Kind = CompletionItemKind.Function,
                Label = function.Identifier.Content,
                Preselect = false,
            });
        }

        foreach (CompiledEnum @enum in CompilerResult.Enums)
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

        foreach (CompiledStruct @struct in CompilerResult.Structs)
        {
            result.Add(new CompletionItem()
            {
                Deprecated = false,
                Detail = null,
                Kind = CompletionItemKind.Struct,
                Label = @struct.Identifier.Content,
                Preselect = false,
            });
        }

        SinglePosition position = e.Position.ToCool();
        foreach (CompiledFunction function in CompilerResult.Functions)
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

    public override Hover? Hover(HoverParams e)
    {
        Logger.Log($"Hover({e.Position.ToCool().ToStringMin()})");

        SinglePosition position = e.Position.ToCool();

        Token? token = Tokens.GetTokenAt(position);
        List<MarkedString> contents = new();

        if (token == null)
        { return null; }

        Range<SinglePosition> range = token.Position.Range;

        {
            CompiledFunction? function = CompilerResult.GetFunctionAt(Uri, position);
            if (function != null)
            { contents.Add(new MarkedString("bbcode", $"{function.Type} {function.ToReadable(ToReadableFlags.ParameterIdentifiers | ToReadableFlags.Modifiers)}")); }
        }

        {
            CompiledGeneralFunction? function = CompilerResult.GetGeneralFunctionAt(Uri, position);
            if (function != null)
            { contents.Add(new MarkedString("bbcode", $"{function.Type} {function.ToReadable(ToReadableFlags.ParameterIdentifiers | ToReadableFlags.Modifiers)}")); }
        }

        {
            CompiledOperator? @operator = CompilerResult.GetOperatorAt(Uri, position);
            if (@operator != null)
            { contents.Add(new MarkedString("bbcode", $"{@operator.Type} {@operator.ToReadable(ToReadableFlags.ParameterIdentifiers | ToReadableFlags.Modifiers)}")); }
        }

        {
            CompiledStruct? @struct = CompilerResult.GetStructAt(Uri, position);
            if (@struct != null)
            { contents.Add(new MarkedString("bbcode", @struct.ToString())); }
        }

        {
            CompiledEnum? @enum = CompilerResult.GetEnumAt(Uri, position);
            if (@enum != null)
            { contents.Add(new MarkedString("bbcode", @enum.ToString())); }
        }

        static MarkedString GetTypeHover(GeneralType type)
        {
            if (type is StructType structType)
            { return new MarkedString("bbcode", $"struct {structType.Struct.Identifier.Content}"); }

            if (type is EnumType enumType)
            { return new MarkedString("bbcode", $"enum {enumType.Enum.Identifier.Content}"); }

            return new MarkedString("bbcode", type.ToString());
        }

        static MarkedString GetValueHover(DataItem value)
        {
            return value.Type switch
            {
                RuntimeType.Null => new MarkedString("bbcode", $"{null}"),
                RuntimeType.UInt8 => new MarkedString("bbcode", $"{value}"),
                RuntimeType.SInt32 => new MarkedString("bbcode", $"{value}"),
                RuntimeType.Single => new MarkedString("bbcode", $"{value}"),
                RuntimeType.UInt16 => new MarkedString("bbcode", $"\'{value}\'"),
                _ => new MarkedString(value.ToString()),
            };
        }

        Statement? statement = AST.GetStatementAt(position);
        if (statement is not null)
        {
            MarkedString? typeHover = null;
            MarkedString? valueHover = null;
            MarkedString? referenceHover = null;

            static void HandleTypeHovering(Statement statement, ref MarkedString? typeHover)
            {
                if (statement is not StatementWithValue statementWithValue ||
                    statementWithValue.CompiledType is null)
                { return; }

                typeHover = GetTypeHover(statementWithValue.CompiledType);
            }

            static void HandleValueHovering(Statement statement, ref MarkedString? valueHover)
            {
                if (statement is not StatementWithValue statementWithValue ||
                    !statementWithValue.PredictedValue.HasValue)
                { return; }

                valueHover = GetValueHover(statementWithValue.PredictedValue.Value);
            }

            static void HandleReferenceHovering(Statement statement, ref MarkedString? referenceHover)
            {
                if (statement is IReferenceableTo _ref1)
                {
                    if (_ref1.Reference is CompiledOperator compiledOperator &&
                        compiledOperator.FilePath is not null)
                    {
                        referenceHover = new MarkedString("bbcode", $"{compiledOperator.Type} {compiledOperator.ToReadable(ToReadableFlags.ParameterIdentifiers | ToReadableFlags.Modifiers)}");
                    }
                    else if (_ref1.Reference is CompiledFunction compiledFunction &&
                        compiledFunction.FilePath is not null)
                    {
                        referenceHover = new MarkedString("bbcode", $"{compiledFunction.Type} {compiledFunction.ToReadable(ToReadableFlags.ParameterIdentifiers | ToReadableFlags.Modifiers)}");
                    }
                    else if (_ref1.Reference is MacroDefinition macroDefinition &&
                        macroDefinition.FilePath is not null)
                    {
                        referenceHover = new MarkedString("bbcode", $"{macroDefinition.ToReadable()}");
                    }
                    else if (_ref1.Reference is CompiledGeneralFunction generalFunction &&
                        generalFunction.FilePath is not null)
                    {
                        referenceHover = new MarkedString("bbcode", $"{generalFunction.Type} {generalFunction.ToReadable(ToReadableFlags.ParameterIdentifiers | ToReadableFlags.Modifiers)}");
                    }
                    else if (_ref1.Reference is CompiledVariable compiledVariable &&
                             compiledVariable.FilePath is not null)
                    {
                        referenceHover = new MarkedString("bbcode", $"(variable) {compiledVariable.Type} {compiledVariable.VariableName}");
                    }
                    else if (_ref1.Reference is LanguageCore.BBCode.Generator.CompiledParameter compiledParameter &&
                             !compiledParameter.IsAnonymous)
                    {
                        referenceHover = new MarkedString("bbcode", $"(parameter) {compiledParameter.Type} {compiledParameter.Identifier}");
                    }
                    else if (_ref1.Reference is CompiledField compiledField &&
                             compiledField.Context is not null &&
                             compiledField.Context.FilePath is not null)
                    {
                        referenceHover = new MarkedString("bbcode", $"(field) {compiledField.Type} {compiledField.Identifier}");
                    }
                }
            }

            foreach (Statement item in statement.GetStatementsRecursively(true))
            {
                if (!item.Position.Range.Contains(e.Position.ToCool()))
                { continue; }

                Position checkPosition = item switch
                {
                    AnyCall functionCall => functionCall.PrevStatement.Position,
                    OperatorCall operatorCall => operatorCall.Operator.Position,
                    _ => item.Position,
                };

                if (!checkPosition.Range.Contains(e.Position.ToCool()))
                { continue; }

                range = item.Position.Range;

                HandleTypeHovering(item, ref typeHover);
                HandleReferenceHovering(item, ref referenceHover);
                HandleValueHovering(item, ref valueHover);
            }

            if (typeHover is not null) contents.Add(typeHover);
            if (referenceHover is not null) contents.Add(referenceHover);
            if (valueHover is not null) contents.Add(valueHover);
        }

        {
            (TypeInstance, GeneralType)? _type = GetTypeInstanceAt(e.Position.ToCool());

            if (_type.HasValue)
            {
                GeneralType type = _type.Value.Item2;
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

        foreach (CompiledFunction function in CompilerResult.Functions)
        {
            if (function.FilePath != Uri)
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

        foreach (CompiledGeneralFunction function in CompilerResult.GeneralFunctions)
        {
            if (function.FilePath != Uri)
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

        foreach (CompiledOperator function in CompilerResult.Operators)
        {
            if (function.FilePath != Uri)
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

        foreach (CompiledStruct function in CompilerResult.Structs)
        {
            if (function.FilePath != Uri)
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
            foreach (Statement item in statement.GetStatementsRecursively(true))
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
                             compiledField.Context is not null &&
                             compiledField.Context.FilePath is not null)
                    {
                        links.Add(new LocationOrLocationLink(new LocationLink()
                        {
                            OriginSelectionRange = from.ToOmniSharp(),
                            TargetRange = compiledField.Identifier.Position.ToOmniSharp(),
                            TargetSelectionRange = compiledField.Identifier.Position.ToOmniSharp(),
                            TargetUri = DocumentUri.From(compiledField.Context.FilePath),
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
            (TypeInstance, GeneralType)? _type = GetTypeInstanceAt(e.Position.ToCool());

            if (_type.HasValue)
            {
                GeneralType type = _type.Value.Item2;
                TypeInstance origin = _type.Value.Item1;

                if (type is StructType structType && structType.Struct.FilePath != null)
                {
                    links.Add(new LocationOrLocationLink(new LocationLink()
                    {
                        OriginSelectionRange = origin.Position.ToOmniSharp(),
                        TargetRange = structType.Struct.Identifier.Position.ToOmniSharp(),
                        TargetSelectionRange = structType.Struct.Identifier.Position.ToOmniSharp(),
                        TargetUri = DocumentUri.From(structType.Struct.FilePath),
                    }));
                }

                if (type is EnumType enumType && enumType.Enum.FilePath != null)
                {
                    links.Add(new LocationOrLocationLink(new LocationLink()
                    {
                        OriginSelectionRange = origin.Position.ToOmniSharp(),
                        TargetRange = enumType.Enum.Identifier.Position.ToOmniSharp(),
                        TargetSelectionRange = enumType.Enum.Identifier.Position.ToOmniSharp(),
                        TargetUri = DocumentUri.From(enumType.Enum.FilePath),
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

        foreach (CompiledFunction function in CompilerResult.Functions)
        {
            DocumentUri? uri = function.FilePath is null ? null : (DocumentUri)function.FilePath;
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

        foreach (CompiledGeneralFunction function in CompilerResult.GeneralFunctions)
        {
            DocumentUri? uri = function.FilePath is null ? null : (DocumentUri)function.FilePath;
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

        foreach (CompiledStruct @struct in CompilerResult.Structs)
        {
            DocumentUri? uri = @struct.FilePath is null ? null : (DocumentUri)@struct.FilePath;
            if (uri is not null && !uri.Equals(e.TextDocument.Uri)) continue;

            result.Add(new SymbolInformation()
            {
                Kind = SymbolKind.Struct,
                Name = @struct.Identifier.Content,
                Location = new Location()
                {
                    Range = @struct.Position.ToOmniSharp(),
                    Uri = uri ?? e.TextDocument.Uri,
                },
            });
        }

        foreach (CompiledEnum @enum in CompilerResult.Enums)
        {
            DocumentUri? uri = @enum.FilePath is null ? null : (DocumentUri)@enum.FilePath;
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

        {
            CompiledFunction? function = CompilerResult.GetFunctionAt(Uri, e.Position.ToCool());
            if (function is not null)
            {
                for (int i = 0; i < function.References.Count; i++)
                {
                    Reference<StatementWithValue> reference = function.References[i];
                    if (reference.SourceFile == null) continue;
                    result.Add(new Location()
                    {
                        Range = reference.Source.Position.ToOmniSharp(),
                        Uri = reference.SourceFile,
                    });
                }
            }
        }

        {
            CompiledGeneralFunction? generalFunction = CompilerResult.GetGeneralFunctionAt(Uri, e.Position.ToCool());
            if (generalFunction is not null)
            {
                for (int i = 0; i < generalFunction.References.Count; i++)
                {
                    Reference<Statement> reference = generalFunction.References[i];
                    if (reference.SourceFile == null) continue;
                    result.Add(new Location()
                    {
                        Range = reference.Source.Position.ToOmniSharp(),
                        Uri = reference.SourceFile,
                    });
                }
            }
        }

        {
            CompiledOperator? @operator = CompilerResult.GetOperatorAt(Uri, e.Position.ToCool());
            if (@operator is not null)
            {
                for (int i = 0; i < @operator.References.Count; i++)
                {
                    Reference<OperatorCall> reference = @operator.References[i];
                    if (reference.SourceFile == null) continue;
                    result.Add(new Location()
                    {
                        Range = reference.Source.Position.ToOmniSharp(),
                        Uri = reference.SourceFile,
                    });
                }
            }
        }

        {
            CompiledStruct? @struct = CompilerResult.GetStructAt(Uri, e.Position.ToCool());
            if (@struct is not null)
            {
                for (int i = 0; i < @struct.References.Count; i++)
                {
                    Reference<TypeInstance> reference = @struct.References[i];
                    if (reference.SourceFile == null) continue;
                    result.Add(new Location()
                    {
                        Range = reference.Source.Position.ToOmniSharp(),
                        Uri = reference.SourceFile,
                    });
                }
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
