using System.Collections.Immutable;
using System.IO;
using LanguageCore;
using LanguageCore.Compiler;
using LanguageCore.Parser;
using LanguageCore.Parser.Statements;

namespace LanguageServer.DocumentManagers;

sealed partial class DocumentBBLang
{
    public override async Task<IEnumerable<CompletionItem>?> Completion(CompletionParams e, CancellationToken cancellationToken)
    {
        await AwaitForCompilation(Version, cancellationToken, false).ConfigureAwait(false);

        List<CompletionItem> result = new();

        void AddTypeItems()
        {
            foreach (CompiledStruct @struct in CompilerResult.Structs)
            {
                if (!@struct.Definition.CanUse(Uri))
                { continue; }

                result.Add(new CompletionItem()
                {
                    Kind = CompletionItemKind.Struct,
                    Label = @struct.Identifier,
                });
            }

            foreach (CompiledAlias alias in CompilerResult.Aliases)
            {
                if (!alias.Definition.CanUse(Uri)) continue;

                result.Add(new CompletionItem()
                {
                    Kind = CompletionItemKind.Class,
                    Label = alias.Identifier,
                    LabelDetails = new CompletionItemLabelDetails()
                    {
                        Detail = $" = {alias.Value}",
                    },
                });
            }

            foreach (CompiledEnum @enum in CompilerResult.Enums)
            {
                if (!@enum.Definition.CanUse(Uri)) continue;

                result.Add(new CompletionItem()
                {
                    Kind = CompletionItemKind.Enum,
                    Label = @enum.Identifier,
                });
            }

            result.AddRange(TypeKeywords.List.Select(v => new CompletionItem() { Kind = CompletionItemKind.Keyword, Label = v }));
        }

        void AddExpressionItems()
        {
            {
                Dictionary<string, List<CompiledFunctionDefinition>> functionOverloads = new();

                foreach (CompiledFunctionDefinition function in CompilerResult.FunctionDefinitions)
                {
                    if (!function.Definition.CanUse(Uri)) continue;

                    if (!functionOverloads.TryGetValue(function.Identifier, out var overloads))
                    { overloads = functionOverloads[function.Identifier] = new(); }
                    overloads.Add(function);
                }

                foreach ((string function, var overloads) in functionOverloads)
                {
                    result.Add(new CompletionItem()
                    {
                        Kind = CompletionItemKind.Function,
                        Label = function,
                        LabelDetails = new CompletionItemLabelDetails()
                        {
                            Description = overloads.Count switch
                            {
                                0 => null,
                                1 => overloads[0].Type.ToString(),
                                _ => $"{overloads.Count} overloads",
                            },
                        },
                    });
                }
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
                            //LabelDetails = new CompletionItemLabelDetails()
                            //{
                            //    Description = statement.Type.ToString(),
                            //},
                        });
                    }
                    else
                    {
                        result.Add(new CompletionItem()
                        {
                            Kind = CompletionItemKind.Variable,
                            Label = statement.Identifier.Content,
                            //LabelDetails = new CompletionItemLabelDetails()
                            //{
                            //    Description = statement.Type.ToString(),
                            //},
                        });
                    }
                }
            }

            SinglePosition position = e.Position.ToCool();
            foreach (CompiledFunctionDefinition function in CompilerResult.FunctionDefinitions)
            {
                if (function.File != e.TextDocument.Uri.ToUri())
                { continue; }

                if (function.Definition.Block == null) continue;
                if (function.Definition.Block.Position.Range.Contains(position))
                {
                    foreach (CompiledParameter parameter in function.Parameters)
                    {
                        result.Add(new CompletionItem()
                        {
                            Kind = CompletionItemKind.Variable,
                            Label = parameter.Identifier,
                            LabelDetails = new CompletionItemLabelDetails()
                            {
                                Description = parameter.Type.ToString(),
                            },
                        });
                    }

                    break;
                }
            }
        }

        SinglePosition p = e.Position.ToCool();

        foreach (AttributeUsage? attribute in
            AST.AliasDefinitions.Cast<IHaveAttributes>()
            .Append(AST.Structs)
            .Append(AST.Functions)
            .Append(AST.Operators)
            .Append(AST.EnumDefinitions)
            .Append(AST.EnumerateStatements().OfType<IHaveAttributes>())
            .SelectMany(v => v.Attributes)
        )
        {
            if (attribute.Identifier.Position.Range.Contains(p))
            {
                foreach (string item in AttributeConstants.List)
                {
                    result.Add(new CompletionItem()
                    {
                        Label = item,
                        Kind = CompletionItemKind.Class,
                    });
                }
                return result;
            }

            if (attribute.Brackets.Position.Range.Contains(p))
            {
                switch (attribute.Identifier.Content)
                {
                    case AttributeConstants.BuiltinIdentifier:
                        if (attribute.Parameters.Length >= 1
                            && attribute.Parameters[0].Position.Range.Contains(p)
                            && attribute.Parameters[0] is StringLiteralExpression)
                        {
                            result.AddRange(BuiltinFunctions.Prototypes.Keys.Select(v => new CompletionItem()
                            {
                                Label = v,
                                Kind = CompletionItemKind.Constant,
                            }));
                        }
                        break;
                    case AttributeConstants.ExternalIdentifier:
                        if (attribute.Parameters.Length >= 1
                            && attribute.Parameters[0].Position.Range.Contains(p)
                            && attribute.Parameters[0] is StringLiteralExpression)
                        {
                            result.AddRange(CompilerSettings.ExternalFunctions.Select(v => v.Name is null ? null : new CompletionItem()
                            {
                                Label = v.Name,
                                Kind = CompletionItemKind.Constant,
                            }).Where(v => v is not null)!);
                            result.AddRange(CompilerSettings.ExternalConstants.Select(v => new CompletionItem()
                            {
                                Label = v.Name,
                                Kind = CompletionItemKind.Constant,
                            }));
                        }
                        break;
                    case AttributeConstants.InternalIdentifier:
                        if (attribute.Parameters.Length >= 1
                            && attribute.Parameters[0].Position.Range.Contains(p)
                            && attribute.Parameters[0] is StringLiteralExpression)
                        {
                            result.AddRange(new CompletionItem()
                            {
                                Label = "heap_start",
                                Kind = CompletionItemKind.Constant,
                            });
                        }
                        break;
                    case AttributeConstants.InternalType:
                        if (attribute.Parameters.Length >= 1
                            && attribute.Parameters[0].Position.Range.Contains(p)
                            && attribute.Parameters[0] is StringLiteralExpression)
                        {
                            result.AddRange(InternalTypes.List.Select(v => new CompletionItem()
                            {
                                Label = v,
                                Kind = CompletionItemKind.Constant,
                            }));
                        }
                        break;
                    default:
                        break;
                }
                return result;
            }
        }

        foreach (UsingDefinition @using in AST.Usings)
        {
            if (!@using.Position.Range.Contains(p)) continue;
            if (p <= @using.Keyword.Position.Range.End) continue;

            HashSet<Uri> uris = new();
            foreach (ISourceProvider sourceProvider in CompilerSettings.SourceProviders)
            {
                if (sourceProvider is not ISourceQueryProvider queryProvider) continue;
                foreach (Uri query in queryProvider.GetQuery(@using.PathString, Uri))
                {
                    uris.Add(query);
                }
            }

            foreach (Uri uri in uris)
            {
                if (uri.IsFile)
                {
                    string? dir = System.IO.Path.GetDirectoryName(uri.LocalPath);
                    if (dir is null) continue;
                    if (!Directory.Exists(dir)) continue;

                    foreach (string directory in Directory.GetDirectories(dir))
                    {
                        result.Add(new CompletionItem()
                        {
                            Kind = CompletionItemKind.Folder,
                            Label = System.IO.Path.GetFileNameWithoutExtension(directory),
                        });
                    }

                    foreach (string file in Directory.GetFiles(dir))
                    {
                        if (!file.EndsWith($".{LanguageConstants.LanguageExtension}")) continue;

                        result.Add(new CompletionItem()
                        {
                            Kind = CompletionItemKind.File,
                            Label = System.IO.Path.GetFileNameWithoutExtension(file),
                        });
                    }
                }
            }

            return result;
        }

        foreach (Statement statement in AST.EnumerateStatements())
        {
            GeneralType? objectType = null;

            if (statement is FieldExpression fieldExpression
                && fieldExpression.Identifier.Position.Range.Contains(p))
            {
                if (fieldExpression.Object.CompiledType is not null)
                {
                    objectType = fieldExpression.Object.CompiledType;
                }
                else
                {
                    Logger.Warn($"Missing type on {fieldExpression.Object.GetType().Name} {fieldExpression.Object}");
                }
            }

            if (statement is IdentifierExpression identifierExpression
                && new Range<SinglePosition>(identifierExpression.Identifier.Position.Range.Start, identifierExpression.Identifier.Position.Range.End + 1).Contains(p)
                && e.Context is not null
                && e.Context.TriggerKind == CompletionTriggerKind.TriggerCharacter
                && e.Context.TriggerCharacter == ".")
            {
                if (identifierExpression.CompiledType is not null)
                {
                    objectType = identifierExpression.CompiledType;
                }
                else
                {
                    Logger.Warn($"Missing type on {identifierExpression.GetType().Name} {identifierExpression}");
                }
            }

            if (objectType is not null)
            {
                List<GeneralType> checkTypes = new();

                {
                    GeneralType prevType = objectType;
                    checkTypes.Add(new PointerType(prevType));
                    checkTypes.Add(new ReferenceType(prevType));
                    checkTypes.Add(prevType);
                    while (true)
                    {
                        if (prevType.Is(out PointerType? pointerType2))
                        {
                            prevType = pointerType2.To;
                            checkTypes.Add(prevType);
                        }
                        else if (prevType.Is(out ReferenceType? referenceType2))
                        {
                            prevType = referenceType2.To;
                            checkTypes.Add(prevType);
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                Dictionary<string, List<(CompiledFunctionDefinition Function, ImmutableDictionary<string, GeneralType>? TypeArguments)>> functionOverloads = new();

                foreach (GeneralType prevType in checkTypes)
                {
                    if (prevType.Is(out StructType? structType))
                    {
                        foreach (CompiledField field in structType.Struct.Fields)
                        {
                            result.Add(new CompletionItem()
                            {
                                Kind = CompletionItemKind.Field,
                                Label = field.Identifier,
                                LabelDetails = new CompletionItemLabelDetails()
                                {
                                    Description = GeneralType.TryInsertTypeParameters(field.Type, structType.TypeArguments).ToString(),
                                },
                            });
                        }
                    }

                    foreach (CompiledFunctionDefinition function in CompilerResult.FunctionDefinitions)
                    {
                        if (!function.Definition.CanUse(Uri)) continue;
                        if (function.Parameters.Length <= 0) continue;
                        if (!function.Parameters[0].Definition.IsThis) continue;
                        if (!StatementCompiler.CanCastImplicitly(prevType, function.Parameters[0].Type, out _, out _)) continue;

                        if (!functionOverloads.TryGetValue(function.Identifier, out var overloads))
                        { overloads = functionOverloads[function.Identifier] = new(); }
                        overloads.Add((function, (prevType.Is(out StructType? w) && function.Context == w.Struct) ? w.TypeArguments : null));
                    }
                }

                foreach ((string function, var overloads) in functionOverloads)
                {
                    result.Add(new CompletionItem()
                    {
                        Kind = CompletionItemKind.Function,
                        Label = function,
                        LabelDetails = new CompletionItemLabelDetails()
                        {
                            Description = overloads.Count switch
                            {
                                0 => null,
                                1 => GeneralType.TryInsertTypeParameters(overloads[0].Function.Type, overloads[0].TypeArguments).ToString(),
                                _ => $"{overloads.Count} overloads",
                            },
                        },
                    });
                }

                return result;
            }

            if (statement is NewInstanceExpression newInstanceExpression
                && newInstanceExpression.Type.Position.Range.Contains(p))
            {
                AddTypeItems();
                return result;
            }

            if (statement is MissingExpression
                && statement.Position.Range.Contains(p))
            {
                AddExpressionItems();
                return result;
            }
        }

        AddExpressionItems();

        AddTypeItems();

        result.AddRange(DeclarationKeywords.List.Select(v => new CompletionItem() { Kind = CompletionItemKind.Keyword, Label = v }));
        result.AddRange(ProtectionKeywords.List.Select(v => new CompletionItem() { Kind = CompletionItemKind.Keyword, Label = v }));
        result.AddRange(ModifierKeywords.List.Select(v => new CompletionItem() { Kind = CompletionItemKind.Keyword, Label = v }));
        result.AddRange(StatementKeywords.List.Select(v => new CompletionItem() { Kind = CompletionItemKind.Keyword, Label = v }));

        return result;
    }
}
