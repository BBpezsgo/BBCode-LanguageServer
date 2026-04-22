using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using LanguageCore;
using LanguageCore.Compiler;
using LanguageCore.Parser;
using LanguageCore.Parser.Statements;
using LanguageCore.Tokenizing;
using Position = LanguageCore.Position;

namespace LanguageServer.DocumentManagers;

sealed partial class DocumentBBLang
{
    static string? GetTypeHover(GeneralType type) => type switch
    {
        AliasType v => GetAliasHover(v.Definition),
        EnumType v => GetEnumHover(v.Definition),
        BuiltinType v => $"{v}",
        GenericType v => $"(generic) {v.Identifier}",
        StructType v => GetStructHover(v.Struct),
        _ => type.ToString()
    };
    static string GetFunctionHover<TFunction>(TFunction function, ImmutableDictionary<string, GeneralType>? typeArguments)
        where TFunction : ICompiledFunctionDefinition, ICompiledDefinition<FunctionThingDefinition>
    {
        StringBuilder builder = new();

        IEnumerable<Token> modifiers = Utils.GetVisibleModifiers(function.Definition.Modifiers);
        if (modifiers.Any())
        {
            builder.AppendJoin(' ', modifiers);
            builder.Append(' ');
        }

        builder.Append(GeneralType.TryInsertTypeParameters(function.Type, typeArguments) ?? function.Type);
        builder.Append(' ');
        builder.Append(function.Definition.Identifier.ToString());
        if (function.Definition.Template is not null)
        {
            builder.Append('<');
            builder.AppendJoin(", ", typeArguments is not null ? function.Definition.Template.Parameters.Select(v => typeArguments[v.Content].ToString()) : function.Definition.Template.Parameters.Select(v => v.Content));
            builder.Append('>');
        }
        builder.Append('(');
        for (int i = 0; i < function.Definition.Parameters.Length; i++)
        {
            if (i > 0) builder.Append(", ");
            builder.AppendJoin(' ', function.Definition.Parameters[i].Modifiers);
            if (function.Definition.Parameters[i].Modifiers.Length > 0)
            { builder.Append(' '); }

            builder.Append(GeneralType.TryInsertTypeParameters(function.Parameters[i].Type, typeArguments).ToString());

            builder.Append(' ');
            builder.Append(function.Parameters[i].Identifier.ToString());
        }
        builder.Append(')');
        return builder.ToString();
    }
    static string GetFunctionHover(CompiledConstructorDefinition function, ImmutableDictionary<string, GeneralType>? typeArguments)
    {
        StringBuilder builder = new();

        IEnumerable<Token> modifiers = Utils.GetVisibleModifiers(function.Definition.Modifiers);
        if (modifiers.Any())
        {
            builder.AppendJoin(' ', modifiers);
            builder.Append(' ');
        }

        builder.Append(GeneralType.TryInsertTypeParameters(function.Type, typeArguments) ?? function.Type);
        builder.Append('(');
        for (int i = 0; i < function.Definition.Parameters.Length; i++)
        {
            if (i > 0) builder.Append(", ");
            builder.AppendJoin(' ', function.Definition.Parameters[i].Modifiers);
            if (function.Definition.Parameters[i].Modifiers.Length > 0)
            { builder.Append(' '); }

            builder.Append(GeneralType.TryInsertTypeParameters(function.Parameters[i].Type, typeArguments).ToString());

            builder.Append(' ');
            builder.Append(function.Parameters[i].Identifier);
        }
        builder.Append(')');
        return builder.ToString();
    }
    static string GetStructHover(CompiledStruct @struct) => $"{string.Join(null, Utils.GetVisibleModifiers(@struct.Definition.Modifiers).Select(v => $"{v} "))}{DeclarationKeywords.Struct} {@struct.Identifier}";
    static string GetStructHover(StructDefinition @struct) => $"{string.Join(null, Utils.GetVisibleModifiers(@struct.Modifiers).Select(v => $"{v} "))}{DeclarationKeywords.Struct} {@struct.Identifier}";
    static string GetAliasHover(CompiledAlias alias) => $"{string.Join(null, Utils.GetVisibleModifiers(alias.Definition.Modifiers).Select(v => $"{v} "))}{DeclarationKeywords.Alias} {alias.Identifier} = {alias.Value}";
    static string GetEnumHover(CompiledEnum @enum) => $"{string.Join(null, Utils.GetVisibleModifiers(@enum.Definition.Modifiers).Select(v => $"{v} "))}{DeclarationKeywords.Enum} {@enum.Identifier} : {@enum.Type}";
    static string GetEnumHover(EnumDefinition @enum) => $"{string.Join(null, Utils.GetVisibleModifiers(@enum.Modifiers).Select(v => $"{v} "))}{DeclarationKeywords.Enum} {@enum.Identifier}{(@enum.Type is null ? null : $" : {@enum.Type}")}";
    static string GetEnumMemberHover(CompiledEnumMember enumMember) => $"{enumMember.Enum.Type} {enumMember.Identifier} = {enumMember.Value}";
    static string GetEnumMemberHover(EnumMemberDefinition enumMember) => $"{enumMember.Identifier} = {enumMember.Value}";
    static string GetVariableHover(CompiledVariableDefinition variable) => $"(variable) {variable.Type} {variable.Identifier}";
    static string GetConstantHover(CompiledVariableConstant variable) => $"(constant) {variable.Type} {variable.Identifier}{(variable.Value.IsNull ? "" : $" = {variable.Value.ToStringValue()}")}";
    static string GetParameterHover(CompiledParameter parameter) => $"(parameter) {string.Join(null, parameter.Definition.Modifiers.Select(v => $"{v} "))}{parameter.Type} {parameter.Identifier}";
    static string GetParameterHover(ParameterDefinition parameter) => $"(parameter) {string.Join(null, parameter.Modifiers.Select(v => $"{v} "))}{parameter.Type} {parameter.Identifier}";
    static string GetFieldHover(CompiledField field) => $"(field) {field.Type} {field.Identifier}";
    static string GetFieldHover(FieldDefinition field) => $"(field) {field.Type} {field.Identifier}";

    static string? GetDefinitionHover(object? definition)
    {
        switch (definition)
        {
            case null: return null;
            case CompiledOperatorDefinition v: return GetFunctionHover(v, null);
            case CompiledFunctionDefinition v: return GetFunctionHover(v, null);
            case CompiledGeneralFunctionDefinition v: return GetFunctionHover(v, null);
            case CompiledConstructorDefinition v: return GetFunctionHover(v, null);
            case CompiledVariableConstant v: return GetConstantHover(v);
            case CompiledVariableDefinition v: return GetVariableHover(v);
            case CompiledParameter v: return GetParameterHover(v);
            case CompiledField v: return GetFieldHover(v);
            case CompiledStruct v: return GetStructHover(v);
            case CompiledEnum v: return GetEnumHover(v);
            case CompiledEnumMember v: return GetEnumMemberHover(v);
            case StatementCompiler.FunctionQueryResult<CompiledFunctionDefinition> v: return GetFunctionHover(v.Function, v.TypeArguments);
            case ParameterDefinition v: return GetParameterHover(v);
            case FieldDefinition v: return GetFieldHover(v);
            case StructDefinition v: return GetStructHover(v);
            default:
                Logger.Warn($"Invalid definition {definition.GetType().Name}");
                return null;
        }
    }

    public override async Task<Hover?> Hover(HoverParams e, CancellationToken cancellationToken)
    {
        SinglePosition position = e.Position.ToCool();

        Token? token = Tokens.GetTokenAt(position);

        if (token == null)
        {
            Logger.Debug($"No token at {e.Position.ToStringMin()} ({Tokens.Length})");
            return null;
        }

        Range<SinglePosition> range = token.Position.Range;

        {
            foreach (IHaveAttributes function1 in
                AST.Functions.Append(AST.Operators)
                .Append(AST.Structs.SelectMany(v => v.Functions.CastArray<IHaveAttributes>().Append(v.GeneralFunctions).Append(v.Operators).Append(v.Constructors)))
                .Append(AST.Structs)
                .Append(AST.AliasDefinitions)
                .Append(AST.EnumDefinitions))
            {
                foreach (AttributeUsage attribute in function1.Attributes)
                {
                    if (attribute.Identifier.Position.Range.Contains(position))
                    {
                        string? attributeHover = attribute.Identifier.Content switch
                        {
                            AttributeConstants.MSILIncompatibleIdentifier => "Marks the function not compatible with MSIL, therefore it won't be optimized using the IL generator",
                            AttributeConstants.BuiltinIdentifier => "Marks the function as built-in, so it will be used by the compiler to generate code for syntax sugars",
                            AttributeConstants.ExposeIdentifier => "Marks the function as exposable, so it can be called from outside the interpreter",
                            AttributeConstants.ExternalIdentifier => "Marks the function as external, as it's implementation is defined outside the interpreter",
                            AttributeConstants.InternalType => "Marks the type as the default one for the specified kind of values",
                            AttributeConstants.InternalIdentifier => "Marks this constant for use by the compiler for some internal stuff",
                            _ => null,
                        };

                        if (attributeHover is not null)
                        {
                            return new Hover()
                            {
                                Contents = new MarkedStringsOrMarkupContent(new MarkupContent()
                                {
                                    Kind = MarkupKind.Markdown,
                                    Value = attributeHover,
                                }),
                                Range = attribute.Identifier.Position.Range.ToOmniSharp(),
                            };
                        }
                    }
                }
            }
        }

        string? typeHover = null;
        string? definitionHover = null;
        string? docsHover = null;

        if (CompilerResult.GetFunctionAt(Uri, position, out CompiledFunctionDefinition? function))
        {
            definitionHover = GetFunctionHover(function, null);
            docsHover = GetCommentDocumentation(function.Definition);
        }
        else if (CompilerResult.GetGeneralFunctionAt(Uri, position, out CompiledGeneralFunctionDefinition? generalFunction))
        {
            definitionHover = GetFunctionHover(generalFunction, null);
            docsHover = GetCommentDocumentation(generalFunction.Definition);
        }
        else if (CompilerResult.GetOperatorAt(Uri, position, out CompiledOperatorDefinition? @operator))
        {
            definitionHover = GetFunctionHover(@operator, null);
            docsHover = GetCommentDocumentation(@operator.Definition);
        }
        else if (CompilerResult.GetStructAt(Uri, position, out CompiledStruct? @struct))
        {
            definitionHover = GetStructHover(@struct);
            docsHover = GetCommentDocumentation(@struct.Definition);
        }
        else if (CompilerResult.GetEnumAt(Uri, position, out var @enum))
        {
            definitionHover = GetEnumHover(@enum);
            docsHover = GetCommentDocumentation(@enum.Definition);
        }
        else if (CompilerResult.GetEnumMemberAt(Uri, position, out var enumMember))
        {
            definitionHover = GetEnumMemberHover(enumMember);
            docsHover = GetCommentDocumentation(enumMember.Definition);
        }
        else if (CompilerResult.GetFieldAt(Uri, position, out CompiledField? field))
        {
            definitionHover = GetFieldHover(field);
            docsHover = GetCommentDocumentation(field.Definition);
        }
        else if (CompilerResult.GetParameterDefinitionAt(Uri, position, out ParameterDefinition? parameter, out _) &&
                 parameter.Identifier.Position.Range.Contains(position))
        {
            definitionHover = GetParameterHover(parameter);
            docsHover = GetCommentDocumentation(parameter);
        }

        else if (AST.GetStructAt(position, out StructDefinition? @struct2))
        {
            definitionHover = GetStructHover(@struct2);
            docsHover = GetCommentDocumentation(@struct2);
        }
        else if (AST.GetEnumAt(position, out var @enum2))
        {
            definitionHover = GetEnumHover(@enum2);
            docsHover = GetCommentDocumentation(@enum2);
        }
        else if (AST.GetEnumMemberAt(position, out var enumMember1))
        {
            definitionHover = GetEnumMemberHover(enumMember1);
            docsHover = GetCommentDocumentation(@enumMember1);
        }
        else if (AST.GetFieldAt(position, out FieldDefinition? field2))
        {
            definitionHover = GetFieldHover(field2);
            docsHover = GetCommentDocumentation(field2);
        }
        else if (AST.GetStatementAt(position, out Statement? statement))
        {
            foreach (Statement item in StatementWalker.Visit(statement))
            {
                if (!item.Position.Range.Contains(e.Position.ToCool())) continue;

                Position checkPosition = Utils.GetInteractivePosition(item);

                if (!checkPosition.Range.Contains(e.Position.ToCool())) continue;

                range = checkPosition.Range;

                if (item is IntLiteralExpression intLiteralExpression)
                {
                    StringBuilder numbers = new();
                    string base2 = Convert.ToString(intLiteralExpression.Value, 2);
                    string base10 = Convert.ToString(intLiteralExpression.Value, 10);
                    string base16 = Convert.ToString(intLiteralExpression.Value, 16);
                    string? _char = intLiteralExpression.Value is >= char.MinValue and <= char.MaxValue ? ((char)intLiteralExpression.Value).Escape() : null;

                    if (base2.Length > 4)
                    {
                        if (base2.Length % 8 > 0)
                        {
                            base2 = new string('0', 8 - (base2.Length % 8)) + base2;
                        }

                        base2 = "_" + string.Join('_', base2.Chunk(8).Select(v => new string(v)));
                    }

                    string? type =
                        intLiteralExpression.CompiledType is not null
                        ? $"({intLiteralExpression.CompiledType})"
                        : null;

                    numbers.Append($"{type}0b{base2}\n");
                    numbers.Append($"{type}{base10}\n");
                    numbers.Append($"{type}0x{base16}\n");
                    if (_char is not null) numbers.Append($"{type}'{_char}'");
                    definitionHover = numbers.ToString();
                }
                else if (item is FloatLiteralExpression floatLiteralExpression)
                {
                    string? type =
                        floatLiteralExpression.CompiledType is not null
                        ? $"({floatLiteralExpression.CompiledType})"
                        : null;

                    definitionHover = $"{type}{Convert.ToString(floatLiteralExpression.Value)}";
                }
                else if (item is CharLiteralExpression charLiteralExpression)
                {
                    StringBuilder numbers = new();
                    string base2 = Convert.ToString(charLiteralExpression.Value, 2);
                    string base10 = Convert.ToString(charLiteralExpression.Value, 10);
                    string base16 = Convert.ToString(charLiteralExpression.Value, 16);
                    string _char = charLiteralExpression.Value.Escape();

                    if (base2.Length > 4)
                    {
                        if (base2.Length % 8 > 0)
                        {
                            base2 = new string('0', 8 - (base2.Length % 8)) + base2;
                        }

                        base2 = "_" + string.Join('_', base2.Chunk(8).Select(v => new string(v)));
                    }

                    string? type =
                        charLiteralExpression.CompiledType is not null
                        ? $"({charLiteralExpression.CompiledType})"
                        : null;

                    numbers.Append($"{type}0b{base2}\n");
                    numbers.Append($"{type}{base10}\n");
                    numbers.Append($"{type}0x{base16}\n");
                    numbers.Append($"{type}'{_char}'");
                    definitionHover = numbers.ToString();
                }
                else if (item is BinaryOperatorCallExpression binaryOperatorCallExpression
                    && binaryOperatorCallExpression.Reference is null
                    && binaryOperatorCallExpression.CompiledType is not null
                    && binaryOperatorCallExpression.Left.CompiledType is not null
                    && binaryOperatorCallExpression.Right.CompiledType is not null)
                {
                    definitionHover = $"{binaryOperatorCallExpression.CompiledType} {binaryOperatorCallExpression.Operator}({binaryOperatorCallExpression.Left.CompiledType} left, {binaryOperatorCallExpression.Right.CompiledType} right)";
                }
                else if (item is UnaryOperatorCallExpression unaryOperatorCallExpression
                    && unaryOperatorCallExpression.Reference is null
                    && unaryOperatorCallExpression.CompiledType is not null
                    && unaryOperatorCallExpression.Expression.CompiledType is not null)
                {
                    definitionHover = $"{unaryOperatorCallExpression.CompiledType} {unaryOperatorCallExpression.Operator}({unaryOperatorCallExpression.Expression.CompiledType} value)";
                }
                else if (item is Expression statementWithValue && statementWithValue.CompiledType is not null)
                {
                    typeHover = GetTypeHover(statementWithValue.CompiledType);
                }

                if (item is IReferenceableTo referenceableTo)
                {
                    Logger.Trace($"{referenceableTo.Reference?.GetType().Name ?? "null"} {referenceableTo.Reference}");
                    definitionHover = GetDefinitionHover(referenceableTo.Reference);
                    if (referenceableTo.Reference is ILocated locatedReference)
                    {
                        docsHover = GetCommentDocumentation(locatedReference);
                    }
                }
                else
                {
                    Logger.Trace($"{item.GetType().Name} {item}");
                }
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

        if (typeHover is null
            && GetTypeInstanceAt(e.Position.ToCool(), true, out TypeInstance? typeInstance))
        {
            if (typeInstance is TypeInstanceSimple typeInstanceSimple)
            {
                if (typeInstanceSimple.CompiledType is not null)
                {
                    range = typeInstanceSimple.Position.Range;
                    typeHover = GetTypeHover(typeInstanceSimple.CompiledType);
                }
            }
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
}
