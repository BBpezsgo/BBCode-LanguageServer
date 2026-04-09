using System.Collections.Immutable;
using System.Text;
using LanguageCore;
using LanguageCore.Compiler;
using LanguageCore.Parser;
using LanguageCore.Parser.Statements;
using LanguageCore.Tokenizing;

namespace LanguageServer.DocumentManagers;

sealed partial class DocumentBBLang
{
    string? GetCommentDocumentation(ILocated definition)
        => GetCommentDocumentation(definition, out string? result) ? result : null;

    bool GetCommentDocumentation(ILocated definition, [NotNullWhen(true)] out string? result)
        => GetCommentDocumentation(definition.Location.Position.Range.Start, definition.Location.File, out result);

    bool GetCommentDocumentation(SinglePosition position, Uri? file, [NotNullWhen(true)] out string? result)
    {
        result = null;

        if (file is null) return false;

        ImmutableArray<Token> tokens;

        if (Documents.TryGet(file, out DocumentBase? document) &&
            document is DocumentBBLang documentBBLang)
        {
            tokens = documentBBLang.Tokens;
        }
        else
        {
            ParsedFile f = CompilerResult.RawTokens.FirstOrDefault(v => v.File == file);
            if (f.File is null)
            {
                Logger.Warn($"Couldn't get comment documentation for {file}:{position.ToStringMin()} (file not found)");
                return false;
            }
            tokens = f.Tokens.Tokens;
        }

        if (tokens.IsDefault)
        {
            Logger.Warn($"Couldn't get comment documentation for {file}:{position.ToStringMin()} (file not tokenized)");
            return false;
        }

        for (int i = tokens.Length - 1; i >= 0; i--)
        {
            Token token = tokens[i];
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

        //Logger.Warn($"Couldn't get comment documentation for {file}:{position.ToStringMin()} (no comments found)");
        return false;
    }

    static void GetDeepestTypeInstance(ref TypeInstance type1, ref GeneralType type2, SinglePosition position)
    {
        switch (type1)
        {
            case TypeInstanceSimple simpleType:
                {
                    if (simpleType.TypeArguments.HasValue)
                    {
                        if (type2.Is(out StructType? structType) &&
                            structType.Struct.Definition.Template is not null)
                        {
                            for (int i = 0; i < simpleType.TypeArguments.Value.Length; i++)
                            {
                                TypeInstance? item = simpleType.TypeArguments.Value[i];
                                GeneralType? item2 = structType.TypeArguments[structType.Struct.Definition.Template.Parameters[i].Content];
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

                    break;
                }
            case TypeInstancePointer pointerType:
                {
                    if (!type2.Is(out PointerType? pointerType2)) return;

                    if (pointerType.To.Position.Range.Contains(position))
                    {
                        type1 = pointerType.To;
                        type2 = pointerType2.To;
                        GetDeepestTypeInstance(ref type1, ref type2, position);
                        return;
                    }

                    break;
                }
            case TypeInstanceReference referenceType:
                {
                    if (!type2.Is(out ReferenceType? referenceType2)) return;

                    if (referenceType.To.Position.Range.Contains(position))
                    {
                        type1 = referenceType.To;
                        type2 = referenceType2.To;
                        GetDeepestTypeInstance(ref type1, ref type2, position);
                        return;
                    }

                    break;
                }
            case TypeInstanceStackArray arrayType:
                {
                    if (!type2.Is(out ArrayType? arrayType2)) return;

                    if (arrayType.StackArrayOf.Position.Range.Contains(position))
                    {
                        type1 = arrayType.StackArrayOf;
                        type2 = arrayType2.Of;
                        GetDeepestTypeInstance(ref type1, ref type2, position);
                        return;
                    }

                    break;
                }
            case TypeInstanceFunction functionType:
                {
                    if (!type2.Is(out FunctionType? functionType2)) return;
                    if (functionType2.Parameters.Length != functionType.FunctionParameterTypes.Length) return;

                    if (functionType.FunctionReturnType.Position.Range.Contains(position))
                    {
                        type1 = functionType.FunctionReturnType;
                        type2 = functionType2.ReturnType;
                        GetDeepestTypeInstance(ref type1, ref type2, position);
                        return;
                    }

                    for (int i = 0; i < functionType.FunctionParameterTypes.Length; i++)
                    {
                        if (functionType.FunctionParameterTypes[i].Position.Range.Contains(position))
                        {
                            type1 = functionType.FunctionParameterTypes[i];
                            type2 = functionType2.Parameters[i];
                            GetDeepestTypeInstance(ref type1, ref type2, position);
                            return;
                        }
                    }

                    break;
                }
            default:
                break;
        }
    }

    static TypeInstance GetDeepestTypeInstance(TypeInstance type, SinglePosition position)
    {
        GetDeepestTypeInstance(ref type, position);
        return type;
    }

    static void GetDeepestTypeInstance(ref TypeInstance type, SinglePosition position)
    {
        switch (type)
        {
            case TypeInstanceSimple simpleType:
                {
                    if (simpleType.TypeArguments.HasValue)
                    {
                        for (int i = 0; i < simpleType.TypeArguments.Value.Length; i++)
                        {
                            TypeInstance? item = simpleType.TypeArguments.Value[i];
                            if (item.Position.Range.Contains(position))
                            {
                                type = item;
                                GetDeepestTypeInstance(ref type, position);
                                return;
                            }
                        }
                    }

                    break;
                }
            case TypeInstancePointer pointerType:
                {
                    if (pointerType.To.Position.Range.Contains(position))
                    {
                        type = pointerType.To;
                        GetDeepestTypeInstance(ref type, position);
                        return;
                    }

                    break;
                }
            case TypeInstanceReference referenceType:
                {
                    if (referenceType.To.Position.Range.Contains(position))
                    {
                        type = referenceType.To;
                        GetDeepestTypeInstance(ref type, position);
                        return;
                    }

                    break;
                }
            case TypeInstanceStackArray arrayType:
                {
                    if (arrayType.StackArrayOf.Position.Range.Contains(position))
                    {
                        type = arrayType.StackArrayOf;
                        GetDeepestTypeInstance(ref type, position);
                        return;
                    }

                    break;
                }
            case TypeInstanceFunction functionType:
                {
                    if (functionType.FunctionReturnType.Position.Range.Contains(position))
                    {
                        type = functionType.FunctionReturnType;
                        GetDeepestTypeInstance(ref type, position);
                        return;
                    }

                    for (int i = 0; i < functionType.FunctionParameterTypes.Length; i++)
                    {
                        if (functionType.FunctionParameterTypes[i].Position.Range.Contains(position))
                        {
                            type = functionType.FunctionParameterTypes[i];
                            GetDeepestTypeInstance(ref type, position);
                            return;
                        }
                    }

                    break;
                }
            default:
                break;
        }
    }

    bool GetTypeInstanceAt(SinglePosition position, bool includeStatements, [NotNullWhen(true)] out TypeInstance? result)
    {
        foreach (TypeInstance type in
            // Functions
            AST.Functions
                .SelectMany(v => v.Parameters.Parameters.Select(v => v.Type).Append(v.Type))

            // Operators
            .Append(
                AST.Operators
                .SelectMany(v => v.Parameters.Parameters.Select(v => v.Type).Append(v.Type))
            )

            // Fields
            .Append(
                AST.Structs.SelectMany(v => v.Fields).Select(v => v.Type)
            )

            // Methods
            .Append(
                AST.Structs.SelectMany(v => v.Functions.Append(v.Operators))
                .SelectMany(v => v.Parameters.Parameters.Select(v => v.Type).Append(v.Type))
            )

            // Constructors
            .Append(
                AST.Structs.SelectMany(v => v.Constructors)
                .SelectMany(v => v.Parameters.Parameters.Select(v => v.Type).Append(v.Type))
            )

            // General functions
            .Append(
                AST.Structs.SelectMany(v => v.GeneralFunctions)
                .SelectMany(v => v.Parameters.Parameters.Select(v => v.Type))
            )

            // Aliases
            .Append(AST.AliasDefinitions.Select(v => v.Value))

            // Enums
            .Append(AST.EnumDefinitions.Where(v => v.Type is not null).Select(v => v.Type!)))
        {
            if (!type.Position.Range.Contains(position)) continue;
            result = GetDeepestTypeInstance(type, position);
            return true;
        }

        if (includeStatements)
        {
            if (AST.GetStatementAt(position, out Statement? statement))
            {
                foreach (Statement item in StatementWalker.Visit(statement))
                {
                    if (!item.Position.Range.Contains(position)) continue;
                    if (item is not IHaveType haveType) break;

                    result = GetDeepestTypeInstance(haveType.Type, position);
                    if (!result.Position.Range.Contains(position)) break;

                    return true;
                }
            }
        }

        result = null;
        return false;
    }
}
