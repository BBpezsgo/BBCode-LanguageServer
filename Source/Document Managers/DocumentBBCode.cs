using System.Collections.Immutable;
using System.Text;
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

    ImmutableDictionary<Uri, ImmutableArray<Token>> Tokens = ImmutableDictionary.Create<Uri, ImmutableArray<Token>>();
    ParserResult AST;
    CompilerResult CompilerResult;

    public DocumentBBCode(DocumentUri uri, string content, string languageId, Documents app) : base(uri, content, languageId, app)
    {
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

    bool GetCommentDocumentation(IPositioned position, Uri? file, [NotNullWhen(true)] out string? result)
        => GetCommentDocumentation(position.Position.Range.Start, file, out result);

    bool GetCommentDocumentation<TDefinition>(TDefinition definition, [NotNullWhen(true)] out string? result)
        where TDefinition : IPositioned, IInFile
        => GetCommentDocumentation(definition.Position.Range.Start, definition.FilePath, out result);

    bool GetCommentDocumentation(SinglePosition position, Uri? file, [NotNullWhen(true)] out string? result)
    {
        result = null;

        if (file is null)
        { return false; }

        if (!Tokens.TryGetValue(file, out ImmutableArray<Token> tokens))
        { return false; }

        for (int i = tokens.Length - 1; i >= 0; i--)
        {
            Token token = tokens[i];
            if (token.Position.Range.Start >= position) continue;

            if (token.TokenType == TokenType.CommentMultiline)
            {
                StringBuilder parsedResult = new();
                string[] lines = token.Content.Split('\n');
                for (int i1 = 0; i1 < lines.Length; i1++)
                {
                    string line = lines[i1];
                    line = line.Trim();
                    if (line.StartsWith('*')) line = line[1..];
                    line = line.TrimStart();
                    parsedResult.AppendLine(line);
                }

                result = parsedResult.ToString();
                return true;
            }

            break;
        }

        return false;
    }

    void Validate()
    {
        Logger.Log($"Validating \"{Uri}\" ...");

        AnalysisResult analysisResult = Analysis.Analyze(Uri);

        Tokens = analysisResult.Tokens;
        AST = analysisResult.AST ?? ParserResult.Empty;
        CompilerResult = analysisResult.CompilerResult ?? CompilerResult;

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
        Logger.Log($"Completion({e})");

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

    #region Hover()

    static string GetFunctionHover<TFunction>(TFunction function)
        where TFunction : FunctionThingDefinition, ICompiledFunction, IReadable
    {
        StringBuilder builder = new();
        IEnumerable<Token> modifiers = Utils.GetVisibleModifiers(function.Modifiers);
        if (modifiers.Any())
        {
            builder.AppendJoin(' ', modifiers);
            builder.Append(' ');
        }

        builder.Append(function.Type);
        builder.Append(' ');
        builder.Append(function.ToReadable(ToReadableFlags.ParameterIdentifiers | ToReadableFlags.Modifiers));
        return builder.ToString();
    }

    static string GetStructHover(CompiledStruct @struct)
    {
        StringBuilder builder = new();
        IEnumerable<Token> modifiers = Utils.GetVisibleModifiers(@struct.Modifiers);
        if (modifiers.Any())
        {
            builder.AppendJoin(' ', modifiers);
            builder.Append(' ');
        }

        builder.Append(DeclarationKeywords.Struct);
        builder.Append(' ');
        builder.Append(@struct.Identifier);
        return builder.ToString();
    }

    static string GetEnumHover(CompiledEnum @enum)
    {
        StringBuilder builder = new();
        builder.Append(DeclarationKeywords.Enum);
        builder.Append(' ');
        builder.Append(@enum.Identifier);
        return builder.ToString();
    }

    static string GetTypeHover(GeneralType type) => type switch
    {
        StructType structType => $"{DeclarationKeywords.Struct} {structType.Struct.Identifier.Content}",
        EnumType enumType => $"{DeclarationKeywords.Enum} {enumType.Enum.Identifier.Content}",
        _ => type.ToString()
    };

    static string GetValueHover(DataItem value) => value.Type switch
    {
        RuntimeType.Null => $"{null}",
        RuntimeType.Byte => $"{value}",
        RuntimeType.Integer => $"{value}",
        RuntimeType.Single => $"{value}",
        RuntimeType.Char => $"\'{value}\'",
        _ => value.ToString(),
    };

    static void HandleTypeHovering(Statement statement, ref string? typeHover)
    {
        if (statement is StatementWithValue statementWithValue &&
            statementWithValue.CompiledType is not null)
        { typeHover = GetTypeHover(statementWithValue.CompiledType); }
    }

    static void HandleValueHovering(Statement statement, ref string? valueHover)
    {
        if (statement is StatementWithValue statementWithValue &&
            statementWithValue.PredictedValue.HasValue)
        { valueHover = GetValueHover(statementWithValue.PredictedValue.Value); }
    }

    bool HandleDefinitionHover(object? definition, ref string? definitionHover, ref string? docsHover) => definition switch
    {
        CompiledOperator v => HandleDefinitionHover(v, ref definitionHover, ref docsHover),
        CompiledFunction v => HandleDefinitionHover(v, ref definitionHover, ref docsHover),
        CompiledGeneralFunction v => HandleDefinitionHover(v, ref definitionHover, ref docsHover),
        CompiledVariable v => HandleDefinitionHover(v, ref definitionHover, ref docsHover),
        VariableDeclaration v => HandleDefinitionHover(v, ref definitionHover, ref docsHover),
        CompiledParameter v => HandleDefinitionHover(v, ref definitionHover, ref docsHover),
        ParameterDefinition v => HandleDefinitionHover(v, ref definitionHover, ref docsHover),
        CompiledField v => HandleDefinitionHover(v, ref definitionHover, ref docsHover),
        FieldDefinition v => HandleDefinitionHover(v, ref definitionHover, ref docsHover),
        CompiledEnum v => HandleDefinitionHover(v, ref definitionHover, ref docsHover),
        CompiledStruct v => HandleDefinitionHover(v, ref definitionHover, ref docsHover),
        _ => false,
    };

    bool HandleDefinitionHover<TFunction>(TFunction function, ref string? definitionHover, ref string? docsHover)
        where TFunction : FunctionThingDefinition, ICompiledFunction, IReadable
    {
        if (function.FilePath is null)
        { return false; }

        definitionHover = GetFunctionHover(function);
        GetCommentDocumentation(function, out docsHover);
        return true;
    }

    bool HandleDefinitionHover(CompiledEnum @enum, ref string? definitionHover, ref string? docsHover)
    {
        if (@enum.FilePath is null)
        { return false; }

        definitionHover = GetEnumHover(@enum);
        GetCommentDocumentation(@enum, out docsHover);
        return true;
    }

    bool HandleDefinitionHover(CompiledStruct @struct, ref string? definitionHover, ref string? docsHover)
    {
        if (@struct.FilePath is null)
        { return false; }

        definitionHover = GetStructHover(@struct);
        GetCommentDocumentation(@struct, out docsHover);
        return true;
    }

    bool HandleDefinitionHover(CompiledVariable variable, ref string? definitionHover, ref string? docsHover)
    {
        if (variable.FilePath is null)
        { return false; }

        StringBuilder builder = new();

        if (variable.Modifiers.Contains(ModifierKeywords.Const))
        { builder.Append("(constant) "); }
        else
        { builder.Append("(variable) "); }

        if (variable.Modifiers.Length > 0)
        {
            builder.AppendJoin(' ', variable.Modifiers);
            builder.Append(' ');
        }
        builder.Append(variable.Type);
        builder.Append(' ');
        builder.Append(variable.Identifier);
        definitionHover = builder.ToString();

        GetCommentDocumentation(variable, out docsHover);
        return true;
    }

    bool HandleDefinitionHover(VariableDeclaration variable, ref string? definitionHover, ref string? docsHover)
    {
        if (variable.FilePath is null)
        { return false; }

        StringBuilder builder = new();

        if (variable.Modifiers.Contains(ModifierKeywords.Const))
        { builder.Append("(constant) "); }
        else
        { builder.Append("(variable) "); }

        if (variable.Modifiers.Length > 0)
        {
            builder.AppendJoin(' ', variable.Modifiers);
            builder.Append(' ');
        }
        builder.Append(variable.Type);
        builder.Append(' ');
        builder.Append(variable.Identifier);
        definitionHover = builder.ToString();

        GetCommentDocumentation(variable, out docsHover);
        return true;
    }

    bool HandleDefinitionHover(CompiledParameter parameter, ref string? definitionHover, ref string? docsHover)
    {
        if (parameter.IsAnonymous)
        { return false; }

        StringBuilder builder = new();
        builder.Append("(parameter) ");
        if (parameter.Modifiers.Length > 0)
        {
            builder.AppendJoin(' ', parameter.Modifiers);
            builder.Append(' ');
        }
        builder.Append(parameter.Type);
        builder.Append(' ');
        builder.Append(parameter.Identifier);
        definitionHover = builder.ToString();

        GetCommentDocumentation(parameter, parameter.Context.FilePath, out docsHover);
        return true;
    }

    bool HandleDefinitionHover(ParameterDefinition parameter, ref string? definitionHover, ref string? docsHover)
    {
        StringBuilder builder = new();
        builder.Append("(parameter) ");
        if (parameter.Modifiers.Length > 0)
        {
            builder.AppendJoin(' ', parameter.Modifiers);
            builder.Append(' ');
        }
        builder.Append(parameter.Type);
        builder.Append(' ');
        builder.Append(parameter.Identifier);
        definitionHover = builder.ToString();

        GetCommentDocumentation(parameter, parameter.Context.FilePath, out docsHover);
        return true;
    }

    bool HandleDefinitionHover(CompiledField field, ref string? definitionHover, ref string? docsHover)
    {
        if (field.Context is null)
        { return false; }
        if (field.Context.FilePath is null)
        { return false; }

        definitionHover = $"(field) {field.Type} {field.Identifier}";
        GetCommentDocumentation(field, field.Context.FilePath, out docsHover);
        return true;
    }

    bool HandleDefinitionHover(FieldDefinition field, ref string? definitionHover, ref string? docsHover)
    {
        if (field.Context is null)
        { return false; }
        if (field.Context.FilePath is null)
        { return false; }

        definitionHover = $"(field) {field.Type} {field.Identifier}";
        GetCommentDocumentation(field, field.Context.FilePath, out docsHover);
        return true;
    }

    bool HandleReferenceHovering(Statement statement, ref string? definitionHover, ref string? docsHover)
    {
        if (statement is IReferenceableTo _ref1 &&
            HandleDefinitionHover(_ref1.Reference, ref definitionHover, ref docsHover))
        { return true; }

        if (statement is VariableDeclaration variableDeclaration)
        { return HandleDefinitionHover(variableDeclaration, ref definitionHover, ref docsHover); }

        return false;
    }

    public override Hover? Hover(HoverParams e)
    {
        Logger.Log($"Hover({e.Position.ToCool().ToStringMin()})");

        SinglePosition position = e.Position.ToCool();

        if (!Tokens.TryGetValue(e.TextDocument.Uri.ToUri(), out ImmutableArray<Token> tokens))
        { return null; }

        Token? token = tokens.GetTokenAt(position);
        StringBuilder contents = new();

        if (token == null)
        { return null; }

        Range<SinglePosition> range = token.Position.Range;

        string? typeHover = null;
        string? valueHover = null;
        string? definitionHover = null;
        string? docsHover = null;

        if (CompilerResult.GetFunctionAt(Uri, position, out CompiledFunction? function))
        {
            HandleDefinitionHover(function, ref definitionHover, ref docsHover);
        }
        else if (CompilerResult.GetGeneralFunctionAt(Uri, position, out CompiledGeneralFunction? generalFunction))
        {
            HandleDefinitionHover(generalFunction, ref definitionHover, ref docsHover);
        }
        else if (CompilerResult.GetOperatorAt(Uri, position, out CompiledOperator? @operator))
        {
            HandleDefinitionHover(@operator, ref definitionHover, ref docsHover);
        }
        else if (CompilerResult.GetStructAt(Uri, position, out CompiledStruct? @struct))
        {
            HandleDefinitionHover(@struct, ref definitionHover, ref docsHover);
        }
        else if (CompilerResult.GetEnumAt(Uri, position, out CompiledEnum? @enum))
        {
            HandleDefinitionHover(@enum, ref definitionHover, ref docsHover);
        }
        else if (CompilerResult.GetFieldAt(Uri, position, out CompiledField? field))
        {
            HandleDefinitionHover(field, ref definitionHover, ref docsHover);
        }
        else if (CompilerResult.GetParameterDefinitionAt(Uri, position, out ParameterDefinition? parameter, out _) &&
                 parameter.Identifier.Position.Range.Contains(position))
        {
            HandleDefinitionHover(parameter, ref definitionHover, ref docsHover);
        }
        else if (AST.GetStatementAt(position, out Statement? statement))
        {
            foreach (Statement item in statement.GetStatementsRecursively(true))
            {
                if (!item.Position.Range.Contains(e.Position.ToCool()))
                { continue; }

                Position checkPosition = Utils.GetInteractivePosition(item);

                if (item is BinaryOperatorCall)
                { checkPosition = item.Position; }

                if (!checkPosition.Range.Contains(e.Position.ToCool()))
                { continue; }

                range = checkPosition.Range;

                HandleTypeHovering(item, ref typeHover);
                HandleReferenceHovering(item, ref definitionHover, ref docsHover);
                HandleValueHovering(item, ref valueHover);
            }
        }

        if (typeHover is null &&
            (AST, CompilerResult).GetTypeInstanceAt(Uri, e.Position.ToCool(), out TypeInstance? typeInstance, out GeneralType? generalType))
        {
            range = typeInstance.Position.Range;
            typeHover = GetTypeHover(generalType);
        }

        if (definitionHover is not null)
        {
            contents.AppendLine($"```{LanguageIdentifier}");
            contents.AppendLine(definitionHover);
            contents.AppendLine("```");
            contents.AppendLine("---");
        }
        else if (typeHover is not null)
        {
            contents.AppendLine($"```{LanguageIdentifier}");
            contents.AppendLine(typeHover);
            contents.AppendLine("```");
            contents.AppendLine("---");
        }

        if (valueHover is not null)
        {
            contents.AppendLine($"```{LanguageIdentifier}");
            contents.AppendLine(valueHover);
            contents.AppendLine("```");
            contents.AppendLine("---");
        }

        if (docsHover is not null)
        {
            contents.AppendLine(docsHover);
        }

        return new Hover()
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent()
            {
                Kind = MarkupKind.Markdown,
                Value = contents.ToString(),
            }),
            Range = range.ToOmniSharp(),
        };
    }

    #endregion

    public override CodeLens[] CodeLens(CodeLensParams e)
    {
        List<CodeLens> result = new();

        foreach (CompiledFunction function in CompilerResult.Functions)
        {
            if (function.FilePath != Uri) continue;

            result.Add(new CodeLens()
            {
                Range = function.Identifier.Position.Range.ToOmniSharp(),
                Command = new Command()
                {
                    Title = $"{function.References.Count} reference",
                }
            });
        }

        foreach (CompiledGeneralFunction function in CompilerResult.GeneralFunctions)
        {
            if (function.FilePath != Uri) continue;

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
            if (function.FilePath != Uri) continue;

            result.Add(new CodeLens()
            {
                Range = function.Identifier.Position.Range.ToOmniSharp(),
                Command = new Command()
                {
                    Title = $"{function.References.Count} reference",
                },
            });
        }

        foreach (CompiledConstructor function in CompilerResult.Constructors)
        {
            if (function.FilePath != Uri) continue;

            result.Add(new CodeLens()
            {
                Range = (function as ConstructorDefinition).Type.Position.Range.ToOmniSharp(),
                Command = new Command()
                {
                    Title = $"{function.References.Count} reference",
                },
            });
        }

        foreach (CompiledStruct function in CompilerResult.Structs)
        {
            if (function.FilePath != Uri) continue;

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

    static void GetDeepestTypeInstance(ref TypeInstance? type1, ref GeneralType? type2, SinglePosition position)
    {
        if (type1 is null || type2 is null) return;

        switch (type1)
        {
            case TypeInstanceSimple typeInstanceSimple:
            {
                if (typeInstanceSimple.Identifier.Position.Range.Contains(position))
                {
                    return;
                }

                // if (typeInstanceSimple.GenericTypes.HasValue)
                // {
                //     for (int i = 0; i < typeInstanceSimple.GenericTypes.Value.Length; i++)
                //     {
                //         TypeInstance item = typeInstanceSimple.GenericTypes.Value[i];
                //         GeneralType item2 = ((StructType)type2).TypeParameters[i];
                //         if (item.Position.Range.Contains(position))
                //         {
                //             return GetDeepestTypeInstance(item, item2, position, out result);
                //         }
                //     }
                // }

                break;
            }

            case TypeInstancePointer typeInstancePointer:
            {
                if (type2 is not PointerType pointerType)
                { return; }

                if (typeInstancePointer.To.Position.Range.Contains(position))
                {
                    type1 = typeInstancePointer.To;
                    type2 = pointerType.To;
                    GetDeepestTypeInstance(ref type1, ref type2, position);
                    return;
                }

                break;
            }
        }

        type1 = null;
        type2 = null;
    }

    bool GetGotoDefinition(object? reference, [NotNullWhen(true)] out LocationLink? result)
    {
        result = null;
        if (reference is null)
        { return false; }

        Uri file = Uri;

        if (reference is IInFile inFile)
        {
            if (inFile.FilePath is null)
            { return false; }
            file = inFile.FilePath;
        }

        if (reference is IIdentifiable<Token> identifiable1)
        {
            result = new LocationLink()
            {
                TargetRange = identifiable1.Identifier.Position.ToOmniSharp(),
                TargetSelectionRange = identifiable1.Identifier.Position.ToOmniSharp(),
                TargetUri = DocumentUri.From(file),
            };
            return true;
        }

        return false;
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

        if (AST.GetStatementAt(e.Position.ToCool(), out Statement? statement))
        {
            foreach (Statement item in statement.GetStatementsRecursively(true))
            {
                Position from = Utils.GetInteractivePosition(item);

                if (!from.Range.Contains(e.Position.ToCool()))
                { continue; }

                if (item is IReferenceableTo _ref1 &&
                    GetGotoDefinition(_ref1.Reference, out LocationLink? link))
                {
                    links.Add(new LocationLink()
                    {
                        OriginSelectionRange = from.ToOmniSharp(),
                        TargetRange = link.TargetRange,
                        TargetSelectionRange = link.TargetSelectionRange,
                        TargetUri = link.TargetUri,
                    });
                }
            }
        }

        {
            if ((AST, CompilerResult).GetTypeInstanceAt(Uri, e.Position.ToCool(), out TypeInstance? origin, out GeneralType? type))
            {
                GetDeepestTypeInstance(ref origin, ref type, e.Position.ToCool());

                if (origin is not null &&
                    type is not null)
                {
                    if (type is StructType structType &&
                        GetGotoDefinition(structType.Struct, out LocationLink? link))
                    {
                        links.Add(new LocationLink()
                        {
                            OriginSelectionRange = origin.Position.ToOmniSharp(),
                            TargetRange = link.TargetRange,
                            TargetSelectionRange = link.TargetSelectionRange,
                            TargetUri = link.TargetUri,
                        });
                    }
                    else if (type is EnumType enumType &&
                        GetGotoDefinition(enumType.Enum, out link))
                    {
                        links.Add(new LocationLink()
                        {
                            OriginSelectionRange = origin.Position.ToOmniSharp(),
                            TargetRange = link.TargetRange,
                            TargetSelectionRange = link.TargetSelectionRange,
                            TargetUri = link.TargetUri,
                        });
                    }
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

        if (CompilerResult.GetFunctionAt(Uri, e.Position.ToCool(), out CompiledFunction? function))
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

        if (CompilerResult.GetGeneralFunctionAt(Uri, e.Position.ToCool(), out CompiledGeneralFunction? generalFunction))
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

        if (CompilerResult.GetOperatorAt(Uri, e.Position.ToCool(), out CompiledOperator? @operator))
        {
            for (int i = 0; i < @operator.References.Count; i++)
            {
                Reference<StatementWithValue> reference = @operator.References[i];
                if (reference.SourceFile == null) continue;
                result.Add(new Location()
                {
                    Range = reference.Source.Position.ToOmniSharp(),
                    Uri = reference.SourceFile,
                });
            }
        }

        if (CompilerResult.GetStructAt(Uri, e.Position.ToCool(), out CompiledStruct? @struct))
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

        return result.ToArray();
    }

    public override SignatureHelp? SignatureHelp(SignatureHelpParams e)
    {
        return null;
    }

    public override void GetSemanticTokens(SemanticTokensBuilder builder, ITextDocumentIdentifierParams e)
    {
        if (!Tokens.TryGetValue(e.TextDocument.Uri.ToUri(), out ImmutableArray<Token> tokens))
        { return; }

        foreach (Token token in tokens)
        {
            switch (token.AnalyzedType)
            {
                case TokenAnalyzedType.Attribute:
                case TokenAnalyzedType.Type:
                    builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Type, SemanticTokenModifier.Defaults);
                    break;
                case TokenAnalyzedType.Struct:
                    builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Struct, SemanticTokenModifier.Defaults);
                    break;
                case TokenAnalyzedType.FunctionName:
                    builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Function, SemanticTokenModifier.Defaults);
                    break;
                case TokenAnalyzedType.VariableName:
                    builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Variable, SemanticTokenModifier.Defaults);
                    break;
                case TokenAnalyzedType.ConstantName:
                    builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Variable, SemanticTokenModifier.Readonly);
                    break;
                case TokenAnalyzedType.ParameterName:
                    builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Parameter, SemanticTokenModifier.Defaults);
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

                default:
                    switch (token.TokenType)
                    {
                        // case TokenType.LiteralString:
                        // case TokenType.LiteralCharacter:
                        //     builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.String, SemanticTokenModifier.Defaults);
                        //     break;
                        case TokenType.LiteralNumber:
                        case TokenType.LiteralHex:
                        case TokenType.LiteralBinary:
                        case TokenType.LiteralFloat:
                            builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Number, SemanticTokenModifier.Defaults);
                            break;
                        case TokenType.PreprocessSkipped:
                            builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Comment, SemanticTokenModifier.Defaults);
                            break;
                    }
                    break;
            }
        }
    }
}
