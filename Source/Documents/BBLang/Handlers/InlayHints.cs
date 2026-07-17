using System.Diagnostics;
using LanguageCore;
using LanguageCore.Compiler;
using LanguageCore.Parser;
using LanguageCore.Parser.Statements;
using LanguageCore.Tokenizing;

namespace LanguageServer.DocumentManagers;

sealed partial class DocumentBBLang
{
    public override async Task<IEnumerable<InlayHint>?> InlayHints(InlayHintParams request, CancellationToken cancellationToken)
    {
        await AwaitForCompilation(Version, cancellationToken).ConfigureAwait(false);

        MutableRange<SinglePosition> range = request.Range.ToCool();
        List<InlayHint> result = new();

        static IEnumerable<TypeInstance> EnumerateNestedTypeInstances(TypeInstance v)
        {
            yield return v;
            switch (v)
            {
                case TypeInstanceFunction w:
                    foreach (TypeInstance x in EnumerateNestedTypeInstances(w.FunctionReturnType)) yield return x;
                    foreach (TypeInstance x in w.FunctionParameterTypes.SelectMany(v => EnumerateNestedTypeInstances(v))) yield return x;
                    break;
                case TypeInstancePointer w:
                    foreach (TypeInstance x in EnumerateNestedTypeInstances(w.To)) yield return x;
                    break;
                case TypeInstanceReference w:
                    foreach (TypeInstance x in EnumerateNestedTypeInstances(w.To)) yield return x;
                    break;
                case TypeInstanceSimple w:
                    if (w.TypeArguments.HasValue) foreach (TypeInstance x in w.TypeArguments.Value.SelectMany(v => EnumerateNestedTypeInstances(v))) yield return x;
                    break;
                case TypeInstanceStackArray w:
                    foreach (TypeInstance x in EnumerateNestedTypeInstances(w.StackArrayOf)) yield return x;
                    break;
                case MissingTypeInstance: break;
                default: throw new UnreachableException();
            }
        }

        foreach (TypeInstance item in AST.EnumerateTypeInstances())
        {
            if (!RangeUtils.Overlaps(range, item.Position.Range)) continue;

            foreach (TypeInstance type in EnumerateNestedTypeInstances(item))
            {
                if (type is TypeInstanceStackArray arrayType
                    && arrayType.StackArraySize is null
                    && arrayType.CompiledType is not null
                    && arrayType.CompiledType.Length.HasValue)
                {
                    result.Add(new InlayHint()
                    {
                        Kind = InlayHintKind.Parameter,
                        Label = new StringOrInlayHintLabelParts(arrayType.CompiledType.Length.Value.ToString()),
                        Position = arrayType.SquareBrackets.Start.Position.Range.End.ToOmniSharp(),
                        TextEdits = new Container<TextEdit>(new TextEdit()
                        {
                            NewText = arrayType.CompiledType.Length.Value.ToString(),
                            Range = new Range<SinglePosition>(arrayType.SquareBrackets.Start.Position.Range.End, arrayType.SquareBrackets.Start.Position.Range.End).ToOmniSharp(),
                        })
                    });
                }
            }
        }

        foreach (Statement item in AST.EnumerateStatements())
        {
            if (!RangeUtils.Overlaps(range, item.Position.Range)) continue;

            switch (item)
            {
                case AnyCallExpression v:
                    {
                        if (v.Reference is null) continue;
                        if (v.Reference.TypeArguments is not null && v.Reference.Function.Definition.Template is not null)
                        {
                            List<InlayHintLabelPart> parts = [];
                            foreach (Token i in v.Reference.Function.Definition.Template.Parameters)
                            {
                                if (parts.Count > 0) parts.Add(new InlayHintLabelPart() { Value = ", " });
                                GeneralType? w = v.Reference.TypeArguments.TryGetValue(i.Content, out GeneralType? w2) ? w2 : null;
                                parts.Add(new()
                                {
                                    Value = w is null ? "?" : w.ToString(),
                                    Location = new LanguageCore.Location(i.Position, v.Reference.Function.Definition.File).ToOmniSharp(),
                                });
                            }
                            parts.Insert(0, new() { Value = "<" });
                            parts.Add(new() { Value = ">" });
                            result.Add(new()
                            {
                                Kind = InlayHintKind.Type,
                                Label = new StringOrInlayHintLabelParts(parts),
                                Position = v.Expression.Position.Range.End.ToOmniSharp(),
                            });
                        }

                        int j = v.Expression is FieldExpression ? 1 : 0;
                        for (int i = 0; i < v.Arguments.Arguments.Length; i++)
                        {
                            ArgumentExpression a = v.Arguments.Arguments[i];
                            ParameterDefinition b = v.Reference.Function.Definition.Parameters[i + j];
                            result.Add(new()
                            {
                                Kind = InlayHintKind.Parameter,
                                Label = new StringOrInlayHintLabelParts([
                                    new() { Value = b.Identifier.Content, Location = new LanguageCore.Location(b.Identifier.Position, b.File).ToOmniSharp() },
                                    new() { Value = ":" },
                                ]),
                                Position = a.Position.Range.Start.ToOmniSharp(),
                                PaddingRight = true,
                            });
                        }
                        break;
                    }
                case IndexCallExpression v:
                    {
                        if (v.Reference is null) continue;
                        ArgumentExpression a = v.Index;
                        ParameterDefinition b = v.Reference.Definition.Parameters[1];
                        result.Add(new()
                        {
                            Kind = InlayHintKind.Parameter,
                            Label = new StringOrInlayHintLabelParts([
                                new() { Value = b.Identifier.Content, Location = new LanguageCore.Location(b.Identifier.Position, b.File).ToOmniSharp() },
                                new() { Value = ":" },
                            ]),
                            Position = a.Position.Range.Start.ToOmniSharp(),
                            PaddingRight = true,
                        });
                        break;
                    }
                case ConstructorCallExpression v:
                    {
                        if (v.Reference is null) continue;
                        for (int i = 0; i < v.Arguments.Arguments.Length; i++)
                        {
                            ArgumentExpression a = v.Arguments.Arguments[i];
                            ParameterDefinition b = v.Reference.Definition.Parameters[i];
                            result.Add(new()
                            {
                                Kind = InlayHintKind.Parameter,
                                Label = new StringOrInlayHintLabelParts([
                                    new() { Value = b.Identifier.Content, Location = new LanguageCore.Location(b.Identifier.Position, b.File).ToOmniSharp() },
                                    new() { Value = ":" },
                                ]),
                                Position = a.Position.Range.Start.ToOmniSharp(),
                                PaddingRight = true,
                            });
                        }
                        break;
                    }
            }
        }

        return result;
    }
}
