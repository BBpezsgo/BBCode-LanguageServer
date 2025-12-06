using System.Collections.Immutable;
using System.Text;
using LanguageCore;
using LanguageCore.BBLang.Generator;
using LanguageCore.Compiler;
using LanguageCore.Parser;
using LanguageCore.Parser.Statements;
using LanguageCore.Tokenizing;
using OmniSharpLocation = OmniSharp.Extensions.LanguageServer.Protocol.Models.Location;
using Position = LanguageCore.Position;

namespace LanguageServer.DocumentManagers;

sealed class DocumentBBLang : DocumentBase
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

        if (!Documents.TryGet(file, out DocumentBase? document) ||
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
                for (int j = 0; j < lines.Length; j++)
                {
                    string line = lines[j];
                    line = line.Trim();
                    if (line.StartsWith('*')) line = line[1..];
                    line = line.TrimStart();
                    parsedResult.AppendLine(line);
                }

                result = parsedResult.ToString();
                return true;
            }
            else if (token.TokenType == TokenType.Comment)
            {
                result = token.Content.Trim();
                return true;
            }

            break;
        }

        return false;
    }

    void Validate()
    {
        Logger.Log($"Validate()\n {string.Join("\n ", Documents.Select(v => v.Uri))}");

        Configuration config = ConfigurationManager.Parse([
            ..Documents.SelectMany(v => ConfigurationManager.Search(v.Uri, Documents)).DistinctBy(v => v.Uri)
        ]);

        DiagnosticsCollection diagnostics = new();

        CompilerResult compilerResult = CompilerResult.MakeEmpty(Uri);
        try
        {
            compilerResult = StatementCompiler.CompileFiles(Documents.Select(v => v.Uri.ToString()).ToArray(), new CompilerSettings(CodeGeneratorForMain.DefaultCompilerSettings)
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
                AdditionalImports = config.AdditionalImports.ToImmutableArray(),
                ExternalFunctions = [
                    ..config.ExternalFunctions
                ],
                ExternalConstants = [
                    ..config.ExternalConstants
                ],
                TokenizerSettings = new TokenizerSettings(TokenizerSettings.Default)
                {
                    TokenizeComments = true,
                },
            }, diagnostics);
            if (!diagnostics.HasErrors)
            {
                Logger.Info($"Validation successful");
            }
        }
        catch (LanguageException languageException)
        {
            diagnostics.Add(languageException.ToDiagnostic());
        }

        ParsedFile raw = compilerResult.RawTokens.FirstOrDefault(v => v.File == Uri);
        Tokens = !raw.AST.OriginalTokens.IsDefault ? raw.AST.OriginalTokens : !raw.Tokens.Tokens.IsDefault ? Tokens : ImmutableArray<Token>.Empty;
        AST = raw.AST.IsNotEmpty ? raw.AST : AST;
        CompilerResult = compilerResult;

        foreach (DiagnosticWithoutContext item in diagnostics.DiagnosticsWithoutContext)
        {
            Logger.Error(item.ToString());
        }

        Dictionary<Uri, List<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>> diagnosticsPerFile = new();

        foreach (LanguageCore.Diagnostic diagnostic in diagnostics.Diagnostics)
        {
            if (diagnostic.File is null) continue;
            if (!diagnosticsPerFile.TryGetValue(diagnostic.File, out List<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>? container))
            {
                container = diagnosticsPerFile[diagnostic.File] = new();
            }
            container.Add(diagnostic.ToOmniSharp());
        }

        foreach (ParsedFile item in compilerResult.RawTokens)
        {
            if (!diagnosticsPerFile.TryGetValue(item.File, out List<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>? fileDiagnostics))
            {
                fileDiagnostics = new();
            }

            int? version = null;
            if (Documents.TryGet(item.File, out DocumentBase? document)) version = document.Version;
            OmniSharpService.Instance?.Server?.PublishDiagnostics(new PublishDiagnosticsParams()
            {
                Uri = item.File,
                Diagnostics = fileDiagnostics,
                Version = version,
            });
        }
    }

    public override CompletionItem[] Completion(CompletionParams e)
    {
        List<CompletionItem> result = new();

        Dictionary<string, int> functionOverloads = new();

        foreach (CompiledFunctionDefinition function in CompilerResult.FunctionDefinitions)
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

        foreach ((ImmutableArray<Statement> statements, _) in CompilerResult.RawStatements)
        {
            foreach (VariableDefinition statement in statements.OfType<VariableDefinition>())
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
        foreach (CompiledFunctionDefinition function in CompilerResult.FunctionDefinitions)
        {
            if (function.File != e.TextDocument.Uri.ToUri())
            { continue; }

            if (function.Block == null) continue;
            if (function.Block.Position.Range.Contains(position))
            {
                foreach (CompiledParameter parameter in function.Parameters)
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
        where TFunction : FunctionThingDefinition, ICompiledFunctionDefinition, IReadable
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
        builder.Append(function.Identifier.ToString());
        if (function.Template != null)
        {
            builder.Append('<');
            builder.AppendJoin(", ", function.Template.Parameters);
            builder.Append('>');
            builder.Append(Environment.NewLine);
        }
        builder.Append('(');
        for (int i = 0; i < function.Parameters.Count; i++)
        {
            if (i > 0) builder.Append(", ");
            builder.AppendJoin(' ', function.Parameters[i].Modifiers);
            if (function.Parameters[i].Modifiers.Length > 0)
            { builder.Append(' '); }

            builder.Append(function.Parameters[i].Type.ToString());

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
        GenericType genericType => $"(generic) {genericType.Identifier}",
        AliasType aliasType => $"{DeclarationKeywords.Alias} {aliasType.Identifier} {aliasType.Value}",
        _ => type.ToString()
    };

    static string GetValueHover(CompiledValue value) => value.Type switch
    {
        RuntimeType.Null => $"{null}",
        RuntimeType.U8 => $"{value}",
        RuntimeType.I8 => $"{value}",
        RuntimeType.U16 => $"{value}",
        RuntimeType.I16 => $"{value}",
        RuntimeType.U32 => $"{value}",
        RuntimeType.I32 => $"{value}",
        RuntimeType.F32 => $"{value}",
        _ => value.ToString(),
    };

    static void HandleTypeHovering(Statement statement, ref string? typeHover)
    {
        if (statement is Expression statementWithValue &&
            statementWithValue.CompiledType is not null)
        { typeHover = GetTypeHover(statementWithValue.CompiledType); }
    }

    static void HandleValueHovering(Statement statement, ref string? valueHover)
    {
        if (statement is Expression statementWithValue &&
            statementWithValue.PredictedValue.HasValue)
        { valueHover = GetValueHover(statementWithValue.PredictedValue.Value); }
    }

    bool HandleDefinitionHover(object? definition, ref string? definitionHover, ref string? docsHover) => definition switch
    {
        CompiledOperatorDefinition v => HandleDefinitionHover(v, ref definitionHover, ref docsHover),
        CompiledFunctionDefinition v => HandleDefinitionHover(v, ref definitionHover, ref docsHover),
        CompiledGeneralFunctionDefinition v => HandleDefinitionHover(v, ref definitionHover, ref docsHover),
        // CompiledVariable v => HandleDefinitionHover(v, ref definitionHover, ref docsHover),
        CompiledVariableConstant v => HandleDefinitionHover(v, ref definitionHover, ref docsHover),
        VariableDefinition v => HandleDefinitionHover(v, ref definitionHover, ref docsHover),
        CompiledVariableDefinition v => HandleDefinitionHover(v, ref definitionHover, ref docsHover),
        CompiledParameter v => HandleDefinitionHover(v, ref definitionHover, ref docsHover),
        ParameterDefinition v => HandleDefinitionHover(v, ref definitionHover, ref docsHover),
        CompiledField v => HandleDefinitionHover(v, ref definitionHover, ref docsHover),
        FieldDefinition v => HandleDefinitionHover(v, ref definitionHover, ref docsHover),
        CompiledStruct v => HandleDefinitionHover(v, ref definitionHover, ref docsHover),

        StructDefinition v => HandleDefinitionHover(v, ref definitionHover, ref docsHover),

        _ => false,
    };

    bool HandleDefinitionHover<TFunction>(TFunction function, ref string? definitionHover, ref string? docsHover)
        where TFunction : FunctionThingDefinition, ICompiledFunctionDefinition, IReadable
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

    /*
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
    */

    bool HandleDefinitionHover(VariableDefinition variable, ref string? definitionHover, ref string? docsHover)
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

    bool HandleDefinitionHover(CompiledVariableDefinition variable, ref string? definitionHover, ref string? docsHover)
    {
        StringBuilder builder = new();

        builder.Append("(variable) ");

        builder.Append(variable.Type.ToString());
        builder.Append(' ');
        builder.Append(variable.Identifier);
        definitionHover = builder.ToString();

        GetCommentDocumentation(variable, out docsHover);
        return true;
    }

    bool HandleDefinitionHover(CompiledVariableConstant variable, ref string? definitionHover, ref string? docsHover)
    {
        StringBuilder builder = new();

        builder.Append("(constant) ");

        builder.Append(variable.Type.ToString());
        builder.Append(' ');
        builder.Append(variable.Identifier);

        builder.Append(" = ");
        builder.Append(variable.Value.ToString());

        definitionHover = builder.ToString();

        GetCommentDocumentation(variable, out docsHover);
        return true;
    }

    bool HandleDefinitionHover(CompiledParameter parameter, ref string? definitionHover, ref string? docsHover)
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

        GetCommentDocumentation(parameter, parameter.File, out docsHover);
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

        GetCommentDocumentation(parameter, parameter.File, out docsHover);
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

        if (statement is VariableDefinition variableDeclaration)
        { return HandleDefinitionHover(variableDeclaration, ref definitionHover, ref docsHover); }

        return false;
    }

    public override Hover? Hover(HoverParams e)
    {
        SinglePosition position = e.Position.ToCool();

        Token? token = Tokens.GetTokenAt(position);

        if (token == null)
        {
            Logger.Warn($"No token at {e.Position.ToCool().ToStringMin()} ({Tokens.Length})");
            return null;
        }

        Range<SinglePosition> range = token.Position.Range;

        string? typeHover = null;
        string? valueHover = null;
        string? definitionHover = null;
        string? docsHover = null;
        Statement? statement = null;

        if (CompilerResult.GetFunctionAt(Uri, position, out CompiledFunctionDefinition? function))
        {
            HandleDefinitionHover(function, ref definitionHover, ref docsHover);
        }
        else if (CompilerResult.GetGeneralFunctionAt(Uri, position, out CompiledGeneralFunctionDefinition? generalFunction))
        {
            HandleDefinitionHover(generalFunction, ref definitionHover, ref docsHover);
        }
        else if (CompilerResult.GetOperatorAt(Uri, position, out CompiledOperatorDefinition? @operator))
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
        else if (AST.GetStatementAt(position, out statement))
        {
            foreach (Statement item in statement.GetStatementsRecursively(StatementWalkFlags.IncludeThis))
            {
                if (!item.Position.Range.Contains(e.Position.ToCool()))
                { continue; }

                Position checkPosition = Utils.GetInteractivePosition(item);

                if (item is BinaryOperatorCallExpression)
                { checkPosition = item.Position; }

                if (!checkPosition.Range.Contains(e.Position.ToCool()))
                { continue; }

                range = checkPosition.Range;

                HandleDefinitionHover(item, ref definitionHover, ref docsHover);
                HandleTypeHovering(item, ref typeHover);
                HandleReferenceHovering(item, ref definitionHover, ref docsHover);
                HandleValueHovering(item, ref valueHover);
            }
        }
        else
        {
            foreach (UsingDefinition @using in AST.Usings)
            {
                if (new Position(@using.Path.DefaultIfEmpty(@using.Keyword)).Range.Contains(e.Position.ToCool()))
                {
                    if (@using.CompiledUri != null)
                    { definitionHover = $"{@using.Keyword} \"{@using.CompiledUri}\""; }
                    break;
                }
            }
        }

        if (typeHover is null &&
            (AST, CompilerResult).GetTypeInstanceAt(Uri, e.Position.ToCool(), out TypeInstance? typeInstance, out GeneralType? generalType))
        {
            GetDeepestTypeInstance(ref typeInstance, ref generalType, e.Position.ToCool());

            range = typeInstance.Position.Range;
            typeHover = GetTypeHover(generalType);
        }

        StringBuilder contents = new();

        if (statement is not null)
        {
            //if (contents.Length > 0) contents.AppendLine("---");
            //contents.AppendLine("**Statement:**");
            //contents.AppendLine($"```");
            //contents.AppendLine(statement.GetType().Name);
            //contents.AppendLine("```");

            //if (statement is IReferenceableTo referenceableTo)
            //{
            //    if (contents.Length > 0) contents.AppendLine("---");
            //    contents.AppendLine("**Ref:**");
            //    contents.AppendLine($"```");
            //    contents.AppendLine(referenceableTo.Reference?.GetType().Name ?? "null");
            //    contents.AppendLine("```");
            //}
        }

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
            contents.AppendLine("**Type:**");
            contents.AppendLine($"```{LanguageConstants.LanguageId}");
            contents.AppendLine(typeHover);
            contents.AppendLine("```");
        }

        if (valueHover is not null)
        {
            if (contents.Length > 0) contents.AppendLine("---");
            contents.AppendLine("**Value:**");
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

        foreach (CompiledFunctionDefinition function in CompilerResult.FunctionDefinitions)
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

        foreach (CompiledGeneralFunctionDefinition function in CompilerResult.GeneralFunctionDefinitions)
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

        foreach (CompiledOperatorDefinition function in CompilerResult.OperatorDefinitions)
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

        foreach (CompiledConstructorDefinition function in CompilerResult.ConstructorDefinitions)
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
        }

        return result.ToArray();
    }

    static void GetDeepestTypeInstance(ref TypeInstance type1, ref GeneralType type2, SinglePosition position)
    {
        switch (type1)
        {
            case TypeInstanceSimple typeInstanceSimple:
                {
                    if (typeInstanceSimple.TypeArguments.HasValue)
                    {
                        if (type2 is StructType structType &&
                            structType.Struct.Template is not null)
                        {
                            for (int i = 0; i < typeInstanceSimple.TypeArguments.Value.Length; i++)
                            {
                                TypeInstance? item = typeInstanceSimple.TypeArguments.Value[i];
                                GeneralType? item2 = structType.TypeArguments[structType.Struct.Template.Parameters[i].Content];
                                if (item.Position.Range.Contains(position))
                                {
                                    type1 = item;
                                    type2 = item2;
                                    GetDeepestTypeInstance(ref type1, ref type2, position);
                                    return;
                                }
                            }
                        }
                    }

                    if (typeInstanceSimple.Identifier.Position.Range.Contains(position))
                    {
                        return;
                    }

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

            default:
                break;
        }
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

        if (reference is ILocated located)
        {
            result = new LocationLink()
            {
                TargetRange = located.Location.Position.ToOmniSharp(),
                TargetSelectionRange = located.Location.Position.ToOmniSharp(),
                TargetUri = DocumentUri.From(located.Location.File),
            };
            return true;
        }

        return false;
    }

    public override LocationOrLocationLinks? GotoDefinition(DefinitionParams e)
    {
        List<LocationOrLocationLink> links = new();

        foreach (UsingDefinition @using in AST.Usings.IsDefault ? ImmutableArray<UsingDefinition>.Empty : AST.Usings)
        {
            if (!@using.Position.Range.Contains(e.Position.ToCool()))
            { continue; }
            if (@using.CompiledUri is null)
            { break; }

            links.Add(new LocationOrLocationLink(new LocationLink()
            {
                TargetUri = DocumentUri.From(@using.CompiledUri),
                OriginSelectionRange = new Position(@using.Path.DefaultIfEmpty(@using.Keyword)).ToOmniSharp(),
                TargetRange = Position.Zero.ToOmniSharp(),
                TargetSelectionRange = Position.Zero.ToOmniSharp(),
            }));
            break;
        }

        if (AST.GetStatementAt(e.Position.ToCool(), out Statement? statement))
        {
            foreach (Statement item in statement.GetStatementsRecursively(StatementWalkFlags.IncludeThis))
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
                    Position position = origin switch
                    {
                        TypeInstanceSimple v => v.Identifier.Position,
                        _ => origin.Position,
                    };

                    if (type.Is(out StructType? structType) &&
                        GetGotoDefinition(structType.Struct, out LocationLink? link))
                    {
                        links.Add(new LocationLink()
                        {
                            OriginSelectionRange = position.ToOmniSharp(),
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
                            OriginSelectionRange = position.ToOmniSharp(),
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
                            OriginSelectionRange = position.ToOmniSharp(),
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
        List<SymbolInformationOrDocumentSymbol> result = new();

        foreach (CompiledFunctionDefinition function in CompilerResult.FunctionDefinitions)
        {
            DocumentUri? uri = function.File is null ? null : (DocumentUri)function.File;
            if (uri is not null && !uri.Equals(e.TextDocument.Uri)) continue;

            result.Add(new SymbolInformation()
            {
                Kind = SymbolKind.Function,
                Name = function.Identifier.Content,
                Location = new OmniSharpLocation()
                {
                    Range = function.Position.Range.ToOmniSharp(),
                    Uri = uri ?? e.TextDocument.Uri,
                },
            });
        }

        foreach (CompiledOperatorDefinition function in CompilerResult.OperatorDefinitions)
        {
            DocumentUri? uri = function.File is null ? null : (DocumentUri)function.File;
            if (uri is not null && !uri.Equals(e.TextDocument.Uri)) continue;

            result.Add(new SymbolInformation()
            {
                Kind = SymbolKind.Function,
                Name = function.Identifier.Content,
                Location = new OmniSharpLocation()
                {
                    Range = function.Position.Range.ToOmniSharp(),
                    Uri = uri ?? e.TextDocument.Uri,
                },
            });
        }

        foreach (CompiledGeneralFunctionDefinition function in CompilerResult.GeneralFunctionDefinitions)
        {
            DocumentUri? uri = function.File is null ? null : (DocumentUri)function.File;
            if (uri is not null && !uri.Equals(e.TextDocument.Uri)) continue;

            result.Add(new SymbolInformation()
            {
                Kind = SymbolKind.Function,
                Name = function.Identifier.Content,
                Location = new OmniSharpLocation()
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
                Location = new OmniSharpLocation()
                {
                    Range = @struct.Position.ToOmniSharp(),
                    Uri = uri ?? e.TextDocument.Uri,
                },
            });
        }

        return result.ToArray();
    }

    public override OmniSharpLocation[] References(ReferenceParams e)
    {
        List<OmniSharpLocation> result = new();

        if (CompilerResult.GetFunctionAt(Uri, e.Position.ToCool(), out CompiledFunctionDefinition? function))
        {
            foreach (Reference<Expression?> reference in function.References)
            {
                if (reference.SourceFile == null) continue;
                if (reference.Source == null) continue;
                result.Add(new OmniSharpLocation()
                {
                    Range = reference.Source.Position.ToOmniSharp(),
                    Uri = reference.SourceFile,
                });
            }
        }

        if (CompilerResult.GetGeneralFunctionAt(Uri, e.Position.ToCool(), out CompiledGeneralFunctionDefinition? generalFunction))
        {
            foreach (Reference<Expression?> reference in generalFunction.References)
            {
                if (reference.SourceFile == null) continue;
                if (reference.Source == null) continue;
                result.Add(new OmniSharpLocation()
                {
                    Range = reference.Source.Position.ToOmniSharp(),
                    Uri = reference.SourceFile,
                });
            }
        }

        if (CompilerResult.GetOperatorAt(Uri, e.Position.ToCool(), out CompiledOperatorDefinition? @operator))
        {
            foreach (Reference<Expression> reference in @operator.References)
            {
                if (reference.SourceFile == null) continue;
                result.Add(new OmniSharpLocation()
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
                result.Add(new OmniSharpLocation()
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

        AnyCallExpression? call = null;

        foreach (IEnumerable<Statement>? items in CompilerResult.StatementsIn(e.TextDocument.Uri.ToUri()).Select(statement => statement.GetStatementsRecursively(StatementWalkFlags.IncludeThis)))
        {
            foreach (Statement item in items)
            {
                if (item is AnyCallExpression anyCall)
                {
                    if (!new Position(anyCall.Brackets).Range.Contains(position)) continue;
                    call = anyCall;
                    Logger.Warn($"Call found");
                }
                else if (item is VariableDefinition variableDeclaration)
                {
                    if (variableDeclaration.Type is TypeInstanceFunction functionType
                        && functionType.FunctionReturnType is TypeInstanceSimple typeInstanceSimple
                        && !typeInstanceSimple.TypeArguments.HasValue)
                    {
                        if (!new Position(functionType.Brackets).Range.Contains(position)) continue;
                        call = new AnyCallExpression(
                            new IdentifierExpression(typeInstanceSimple.Identifier, typeInstanceSimple.File),
                            ImmutableArray<ArgumentExpression>.Empty,
                            ImmutableArray<Token>.Empty,
                            functionType.Brackets,
                            functionType.File
                        );
                        Logger.Warn($"Converting {functionType} to {call}");
                    }
                }
            }
        }

        if (call is not null && call.ToFunctionCall(out FunctionCallExpression? functionCall))
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

            ImmutableArray<CompiledFunctionDefinition> candidates = CompilerResult.FunctionDefinitions.Where(v => v.Identifier.Content == functionCall.Identifier.Content).ToImmutableArray();

            int? activeSignature = null;
            if (call.Reference is not null)
            {
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (candidates[i] == call.Reference)
                    {
                        activeSignature = i;
                        break;
                    }
                }
            }

            return new SignatureHelp()
            {
                ActiveSignature = activeSignature,
                ActiveParameter = activeParameter,
                Signatures = new Container<SignatureInformation>(
                    candidates.Select(v => new SignatureInformation()
                    {
                        Label = v.Identifier.Content,
                        ActiveParameter = activeParameter.HasValue && activeParameter.Value < v.Parameters.Length ? activeParameter.Value : null,
                        Parameters = new Container<ParameterInformation>(
                            v.Parameters.Select(p => new ParameterInformation()
                            {
                                Label = p.Identifier.Content,
                            })
                        ),
                        Documentation =
                            GetCommentDocumentation(v, v.File, out string? docs)
                            ? new StringOrMarkupContent(new MarkupContent()
                            {
                                Kind = MarkupKind.Markdown,
                                Value = docs,
                            })
                            : null,
                    })
                ),
            };
        }

        return null;
    }

    public override void GetSemanticTokens(SemanticTokensBuilder builder, ITextDocumentIdentifierParams e)
    {
        foreach (Token token in Tokens)
        {
            switch (token.AnalyzedType)
            {
                case TokenAnalyzedType.Attribute:
                    builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Type, Array.Empty<SemanticTokenModifier>());
                    break;
                case TokenAnalyzedType.Type:
                    builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Type, Array.Empty<SemanticTokenModifier>());
                    break;
                case TokenAnalyzedType.Struct:
                    builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Struct, Array.Empty<SemanticTokenModifier>());
                    break;
                case TokenAnalyzedType.FunctionName:
                    builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Function, Array.Empty<SemanticTokenModifier>());
                    break;
                case TokenAnalyzedType.VariableName:
                    builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Variable, Array.Empty<SemanticTokenModifier>());
                    break;
                case TokenAnalyzedType.ConstantName:
                    builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Variable, SemanticTokenModifier.Readonly);
                    break;
                case TokenAnalyzedType.ParameterName:
                    builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Parameter, Array.Empty<SemanticTokenModifier>());
                    break;
                case TokenAnalyzedType.TypeParameter:
                    builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.TypeParameter, Array.Empty<SemanticTokenModifier>());
                    break;
                case TokenAnalyzedType.Keyword:
                    builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Keyword, Array.Empty<SemanticTokenModifier>());
                    break;
                case TokenAnalyzedType.FieldName:
                    builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Property, Array.Empty<SemanticTokenModifier>());
                    break;
                case TokenAnalyzedType.CompileTag:
                    break;
                case TokenAnalyzedType.CompileTagParameter:
                    break;
                case TokenAnalyzedType.Statement:
                    break;
                case TokenAnalyzedType.BuiltinType:
                    builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Keyword, Array.Empty<SemanticTokenModifier>());
                    break;
                case TokenAnalyzedType.MathOperator:
                    builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Operator, Array.Empty<SemanticTokenModifier>());
                    break;
                case TokenAnalyzedType.OtherOperator:
                    break;
                case TokenAnalyzedType.TypeModifier:
                    break;
                case TokenAnalyzedType.InstructionLabel:
                    break;
                case TokenAnalyzedType.None:
                default:
                    switch (token.TokenType)
                    {
                        case TokenType.LiteralNumber:
                        case TokenType.LiteralHex:
                        case TokenType.LiteralBinary:
                        case TokenType.LiteralFloat:
                            builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Number, Array.Empty<SemanticTokenModifier>());
                            break;
                        case TokenType.PreprocessSkipped:
                            builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Comment, Array.Empty<SemanticTokenModifier>());
                            break;
                    }
                    break;
            }
        }
    }
}
