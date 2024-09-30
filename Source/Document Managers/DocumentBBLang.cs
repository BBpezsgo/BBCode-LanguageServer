using System.Collections.Immutable;
using System.Text;
using LanguageCore;
using LanguageCore.Compiler;
using LanguageCore.Parser;
using LanguageCore.Parser.Statement;
using LanguageCore.Tokenizing;
using Position = LanguageCore.Position;

namespace LanguageServer.DocumentManagers;

class DocumentBBLang : DocumentHandler
{
    public ImmutableArray<Token> Tokens { get; set; }
    public ParserResult AST { get; set; }
    public CompilerResult CompilerResult { get; set; }

    public DocumentBBLang(DocumentUri uri, string content, string languageId, Documents app) : base(uri, content, languageId, app)
    {
        Tokens = ImmutableArray<Token>.Empty;
        AST = ParserResult.Empty;
        CompilerResult = CompilerResult.MakeEmpty(uri.ToUri());
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
        => GetCommentDocumentation(definition.Position.Range.Start, definition.File, out result);

    bool GetCommentDocumentation(SinglePosition position, Uri? file, [NotNullWhen(true)] out string? result)
    {
        result = null;

        if (file is null)
        { return false; }

        if (!Documents.TryGet(file, out DocumentHandler? document) ||
            document is not DocumentBBLang documentBBLang)
        { return false; }

        for (int i = documentBBLang.Tokens.Length - 1; i >= 0; i--)
        {
            Token token = documentBBLang.Tokens[i];
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
        Logger.Log($"Validating: \"{Uri}\"");

        AnalysisResult analysisResult = Analysis.Analyze(Uri);

        Tokens = analysisResult.Tokens;
        AST = analysisResult.AST ?? ParserResult.Empty;
        CompilerResult = analysisResult.CompilerResult ?? CompilerResult;

        foreach (KeyValuePair<Uri, List<Diagnostic>> diagnostics in analysisResult.Diagnostics)
        {
            OmniSharpService.Instance?.Server?.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams()
            {
                Uri = diagnostics.Key,
                Diagnostics = diagnostics.Value,
            });
        }
    }

    public override CompletionItem[] Completion(CompletionParams e)
    {
        // Logger.Log($"Completion({e})");

        List<CompletionItem> result = new();

        Dictionary<string, int> functionOverloads = new();

        foreach (CompiledFunction function in CompilerResult.Functions)
        {
            if (!function.CanUse(e.TextDocument.Uri.ToUri()))
            { continue; }

            if (functionOverloads.TryGetValue(function.Identifier.Content, out int value))
            { functionOverloads[function.Identifier.Content] = value + 1; }
            else
            { functionOverloads[function.Identifier.Content] = 1; }
        }

        foreach ((string function, int overloads) in functionOverloads)
        {
            result.Add(new CompletionItem()
            {
                Kind = CompletionItemKind.Function,
                Label = function,
                LabelDetails = new CompletionItemLabelDetails()
                {
                    Description = overloads <= 1 ? null : $"{overloads} overloads",
                },
                InsertText = $"{function}($1)",
                InsertTextFormat = InsertTextFormat.Snippet,
            });
        }

        foreach (CompiledStruct @struct in CompilerResult.Structs)
        {
            if (!@struct.CanUse(e.TextDocument.Uri.ToUri()))
            { continue; }

            result.Add(new CompletionItem()
            {
                Kind = CompletionItemKind.Struct,
                Label = @struct.Identifier.Content,
            });
        }

        foreach ((ImmutableArray<Statement> statements, _) in CompilerResult.TopLevelStatements)
        {
            foreach (VariableDeclaration statement in statements.OfType<VariableDeclaration>())
            {
                if (!statement.CanUse(e.TextDocument.Uri.ToUri()))
                { continue; }

                if (statement.Modifiers.Contains(ModifierKeywords.Const))
                {
                    result.Add(new CompletionItem()
                    {
                        Kind = CompletionItemKind.Constant,
                        Label = statement.Identifier.Content,
                    });
                }
                else
                {
                    result.Add(new CompletionItem()
                    {
                        Kind = CompletionItemKind.Variable,
                        Label = statement.Identifier.Content,
                    });
                }
            }
        }

        SinglePosition position = e.Position.ToCool();
        foreach (CompiledFunction function in CompilerResult.Functions)
        {
            if (function.File != e.TextDocument.Uri.ToUri())
            { continue; }

            if (function.Block == null) continue;
            if (function.Block.Position.Range.Contains(position))
            {
                foreach (ParameterDefinition parameter in function.Parameters)
                {
                    result.Add(new CompletionItem()
                    {
                        Kind = CompletionItemKind.Variable,
                        Label = parameter.Identifier.Content,
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

        if (function.Template != null)
        {
            builder.Append(function.Template.Keyword);
            builder.Append('<');
            builder.AppendJoin(", ", function.Template.Parameters);
            builder.Append('>');
            builder.Append(Environment.NewLine);
        }

        IEnumerable<Token> modifiers = Utils.GetVisibleModifiers(function.Modifiers);
        if (modifiers.Any())
        {
            builder.AppendJoin(' ', modifiers);
            builder.Append(' ');
        }

        builder.Append(function.Type);
        builder.Append(' ');
        builder.Append(function.Identifier.ToString());
        builder.Append('(');
        for (int i = 0; i < function.Parameters.Count; i++)
        {
            if (i > 0) builder.Append(", ");
            builder.AppendJoin(' ', function.Parameters[i].Modifiers);
            if (function.Parameters[i].Modifiers.Length > 0)
            { builder.Append(' '); }

            builder.Append(function.ParameterTypes[i].ToString());

            builder.Append(' ');
            builder.Append(function.Parameters[i].Identifier.ToString());
        }
        builder.Append(')');
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

    static string GetStructHover(StructDefinition @struct)
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

    static string GetTypeHover(GeneralType type) => type switch
    {
        StructType structType => $"{DeclarationKeywords.Struct} {structType.Struct.Identifier.Content}",
        GenericType genericType => $"(generic) {genericType}",
        AliasType aliasType => $"(alias) {aliasType}",
        _ => type.ToString()
    };

    static string GetValueHover(CompiledValue value) => value.Type switch
    {
        RuntimeType.Null => $"{null}",
        RuntimeType.U8 => $"{value}",
        RuntimeType.I8 => $"{value}",
        RuntimeType.Char => $"\'{value}\'",
        RuntimeType.I16 => $"{value}",
        RuntimeType.U32 => $"{value}",
        RuntimeType.I32 => $"{value}",
        RuntimeType.F32 => $"{value}",
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
        CompiledStruct v => HandleDefinitionHover(v, ref definitionHover, ref docsHover),

        StructDefinition v => HandleDefinitionHover(v, ref definitionHover, ref docsHover),

        _ => false,
    };

    bool HandleDefinitionHover<TFunction>(TFunction function, ref string? definitionHover, ref string? docsHover)
        where TFunction : FunctionThingDefinition, ICompiledFunction, IReadable
    {
        if (function.File is null)
        { return false; }

        definitionHover = GetFunctionHover(function);
        GetCommentDocumentation(function, out docsHover);
        return true;
    }

    bool HandleDefinitionHover(CompiledStruct @struct, ref string? definitionHover, ref string? docsHover)
    {
        if (@struct.File is null)
        { return false; }

        definitionHover = GetStructHover(@struct);
        GetCommentDocumentation(@struct, out docsHover);
        return true;
    }

    bool HandleDefinitionHover(StructDefinition @struct, ref string? definitionHover, ref string? docsHover)
    {
        if (@struct.File is null)
        { return false; }

        definitionHover = GetStructHover(@struct);
        GetCommentDocumentation(@struct, out docsHover);
        return true;
    }

    bool HandleDefinitionHover(CompiledVariable variable, ref string? definitionHover, ref string? docsHover)
    {
        if (variable.File is null)
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
        if (variable.File is null)
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
        builder.Append(variable.CompiledType?.ToString() ?? variable.Type.ToString());
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

        GetCommentDocumentation(parameter, parameter.Context.File, out docsHover);
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

        GetCommentDocumentation(parameter, parameter.Context.File, out docsHover);
        return true;
    }

    bool HandleDefinitionHover(CompiledField field, ref string? definitionHover, ref string? docsHover)
    {
        if (field.Context is null)
        { return false; }
        if (field.Context.File is null)
        { return false; }

        definitionHover = $"(field) {field.Type} {field.Identifier}";
        GetCommentDocumentation(field, field.Context.File, out docsHover);
        return true;
    }

    bool HandleDefinitionHover(FieldDefinition field, ref string? definitionHover, ref string? docsHover)
    {
        if (field.Context is null)
        { return false; }
        if (field.Context.File is null)
        { return false; }

        definitionHover = $"(field) {field.Type} {field.Identifier}";
        GetCommentDocumentation(field, field.Context.File, out docsHover);
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
        // Logger.Log($"Hover({e.Position.ToCool().ToStringMin()})");

        SinglePosition position = e.Position.ToCool();

        Token? token = Tokens.GetTokenAt(position);

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
        else if (CompilerResult.GetThingAt<StructDefinition, Token>(AST.Structs, Uri, position, out StructDefinition? @struct2))
        {
            HandleDefinitionHover(@struct2, ref definitionHover, ref docsHover);
        }
        else if (CompilerResult.GetFieldAt(Uri, position, out CompiledField? field))
        {
            HandleDefinitionHover(field, ref definitionHover, ref docsHover);
        }
        else if (AST.GetFieldAt(Uri, position, out FieldDefinition? field2))
        {
            HandleDefinitionHover(field2, ref definitionHover, ref docsHover);
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
        else
        {
            foreach (UsingDefinition @using in AST.Usings)
            {
                if (new Position(@using.Path.Or(@using.Keyword)).Range.Contains(e.Position.ToCool()))
                {
                    if (@using.CompiledUri != null)
                    { definitionHover = $"{@using.Keyword} \"{@using.CompiledUri.Replace('\\', '/')}\""; }
                    break;
                }
            }
        }

        if (typeHover is null &&
            (AST, CompilerResult).GetTypeInstanceAt(Uri, e.Position.ToCool(), out TypeInstance? typeInstance, out GeneralType? generalType))
        {
            range = typeInstance.Position.Range;
            typeHover = GetTypeHover(generalType);
        }

        StringBuilder contents = new();

        if (definitionHover is not null)
        {
            if (contents.Length > 0) contents.AppendLine("---");
            contents.AppendLine($"```{LanguageConstants.LanguageId}");
            contents.AppendLine(definitionHover);
            contents.AppendLine("```");
        }
        else if (typeHover is not null)
        {
            if (contents.Length > 0) contents.AppendLine("---");
            contents.AppendLine($"```{LanguageConstants.LanguageId}");
            contents.AppendLine(typeHover);
            contents.AppendLine("```");
        }

        if (valueHover is not null)
        {
            if (contents.Length > 0) contents.AppendLine("---");
            contents.AppendLine($"```{LanguageConstants.LanguageId}");
            contents.AppendLine(valueHover);
            contents.AppendLine("```");
        }

        if (docsHover is not null)
        {
            if (contents.Length > 0) contents.AppendLine("---");
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
            if (function.File != Uri) continue;

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
            if (function.File != Uri) continue;

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
            if (function.File != Uri) continue;

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
            if (function.File != Uri) continue;

            result.Add(new CodeLens()
            {
                Range = (function as ConstructorDefinition).Type.Position.Range.ToOmniSharp(),
                Command = new Command()
                {
                    Title = $"{function.References.Count} reference",
                },
            });
        }

        foreach (CompiledStruct @struct in CompilerResult.Structs)
        {
            if (@struct.File != Uri) continue;

            result.Add(new CodeLens()
            {
                Range = @struct.Identifier.Position.Range.ToOmniSharp(),
                Command = new Command()
                {
                    Title = $"{@struct.References.Count} reference",
                },
            });

            foreach (CompiledField field in @struct.Fields)
            {
                result.Add(new CodeLens()
                {
                    Range = field.Identifier.Position.Range.ToOmniSharp(),
                    Command = new Command()
                    {
                        Title = $"{field.References.Count} reference",
                    },
                });
            }
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
                    if (!type2.Is(out PointerType? pointerType))
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
            if (inFile.File is null)
            { return false; }
            file = inFile.File;
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
        // Logger.Log($"GotoDefinition({e.Position.ToCool().ToStringMin()})");

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
                OriginSelectionRange = new Position(@using.Path.Or(@using.Keyword)).ToOmniSharp(),
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
                        OriginSelectionRange = from.Range.ToOmniSharp(),
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
                    if (type.Is(out StructType? structType) &&
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
                    else if (type.Is(out GenericType? genericType) &&
                             genericType.Definition != null)
                    {
                        links.Add(new LocationLink()
                        {
                            OriginSelectionRange = origin.Position.ToOmniSharp(),
                            TargetRange = genericType.Definition.Position.Range.ToOmniSharp(),
                            TargetSelectionRange = genericType.Definition.Position.Range.ToOmniSharp(),
                            TargetUri = DocumentUri,
                        });
                    }
                    else if (type is AliasType aliasType &&
                             aliasType.Definition != null)
                    {
                        links.Add(new LocationLink()
                        {
                            OriginSelectionRange = origin.Position.ToOmniSharp(),
                            TargetRange = aliasType.Definition.Position.Range.ToOmniSharp(),
                            TargetSelectionRange = aliasType.Definition.Position.Range.ToOmniSharp(),
                            TargetUri = aliasType.Definition.File,
                        });
                    }
                }
            }
        }

        return new LocationOrLocationLinks(links);
    }

    public override SymbolInformationOrDocumentSymbol[] Symbols(DocumentSymbolParams e)
    {
        // Logger.Log($"Symbols()");

        List<SymbolInformationOrDocumentSymbol> result = new();

        foreach (CompiledFunction function in CompilerResult.Functions)
        {
            DocumentUri? uri = function.File is null ? null : (DocumentUri)function.File;
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

        foreach (CompiledOperator function in CompilerResult.Operators)
        {
            DocumentUri? uri = function.File is null ? null : (DocumentUri)function.File;
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
            DocumentUri? uri = function.File is null ? null : (DocumentUri)function.File;
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
            DocumentUri? uri = @struct.File is null ? null : (DocumentUri)@struct.File;
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

        return result.ToArray();
    }

    public override Location[] References(ReferenceParams e)
    {
        // Logger.Log($"References({e.Position.ToCool().ToStringMin()})");

        List<Location> result = new();

        if (CompilerResult.GetFunctionAt(Uri, e.Position.ToCool(), out CompiledFunction? function))
        {
            foreach (Reference<StatementWithValue?> reference in function.References)
            {
                if (reference.SourceFile == null) continue;
                if (reference.Source == null) continue;
                result.Add(new Location()
                {
                    Range = reference.Source.Position.ToOmniSharp(),
                    Uri = reference.SourceFile,
                });
            }
        }

        if (CompilerResult.GetGeneralFunctionAt(Uri, e.Position.ToCool(), out CompiledGeneralFunction? generalFunction))
        {
            foreach (Reference<Statement?> reference in generalFunction.References)
            {
                if (reference.SourceFile == null) continue;
                if (reference.Source == null) continue;
                result.Add(new Location()
                {
                    Range = reference.Source.Position.ToOmniSharp(),
                    Uri = reference.SourceFile,
                });
            }
        }

        if (CompilerResult.GetOperatorAt(Uri, e.Position.ToCool(), out CompiledOperator? @operator))
        {
            foreach (Reference<StatementWithValue> reference in @operator.References)
            {
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
            foreach (Reference<TypeInstance> reference in @struct.References)
            {
                if (reference.SourceFile == null) continue;
                result.Add(new Location()
                {
                    Range = reference.Source.Position.ToOmniSharp(),
                    Uri = reference.SourceFile,
                });
            }
        }

        if (CompilerResult.GetFieldAt(Uri, e.Position.ToCool(), out CompiledField? compiledField))
        {
            foreach (Reference<Statement> reference in compiledField.References)
            {
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
        SinglePosition position = e.Position.ToCool();

        AnyCall? call = null;

        foreach (IEnumerable<Statement>? items in CompilerResult.StatementsIn(e.TextDocument.Uri.ToUri()).Select(statement => statement.GetStatementsRecursively(true)))
        {
            foreach (Statement? item in items)
            {
                if (item is not AnyCall anyCall) continue;
                if (!new Position(anyCall.Brackets).Range.Contains(position)) continue;
                call = anyCall;
            }
        }

        if (call is not null &&
            call.Reference is CompiledFunction compiledFunction)
        {
            int? activeParameter = null;
            for (int i = 0; i < call.Commas.Length; i++)
            {
                if (position >= call.Commas[i].Position.Range.Start)
                {
                    activeParameter = i;
                    break;
                }
            }

            return new SignatureHelp()
            {
                ActiveSignature = 0,
                ActiveParameter = activeParameter,
                Signatures = new Container<SignatureInformation>(
                    new SignatureInformation()
                    {
                        Label = compiledFunction.Identifier.Content,
                        ActiveParameter = activeParameter,
                        Parameters = new Container<ParameterInformation>(
                            Enumerable.Range(0, compiledFunction.Parameters.Count)
                            .Select(i =>
                            {
                                string identifier = compiledFunction.Parameters[i].Identifier.Content;
                                GeneralType type = compiledFunction.ParameterTypes[i];
                                return new ParameterInformation()
                                {
                                    Label = identifier,
                                };
                            })
                        ),
                        Documentation =
                            GetCommentDocumentation(compiledFunction, compiledFunction.File, out string? docs)
                            ? new StringOrMarkupContent(new MarkupContent()
                            {
                                Kind = MarkupKind.Markdown,
                                Value = docs,
                            })
                            : null,
                    }
                ),
            };
        }

        return null;
    }

    public override void GetSemanticTokens(SemanticTokensBuilder builder, ITextDocumentIdentifierParams e)
    {
        Tokens = Analysis.Analyze(Uri).Tokens;

        foreach (Token token in Tokens)
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
