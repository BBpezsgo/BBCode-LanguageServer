using System.Collections.Immutable;
using System.Text;
using LanguageCore;
using LanguageCore.Compiler;
using LanguageCore.Parser;
using LanguageCore.Parser.Statements;
using Position = LanguageCore.Position;

namespace LanguageServer.DocumentManagers;

sealed partial class DocumentBBLang
{
    public override async Task<SignatureHelp?> SignatureHelp(SignatureHelpParams e, CancellationToken cancellationToken)
    {
        await AwaitForCompilation(Version, cancellationToken).ConfigureAwait(false);

        SinglePosition position = e.Position.ToCool();

        AnyCallExpression? call = null;

        foreach (Statement item in CompilerResult.StatementsIn(e.TextDocument.Uri.ToUri()).SelectMany(StatementWalker.Visit))
        {
            if (item is AnyCallExpression anyCall)
            {
                if (!new Position(anyCall.Arguments.Brackets).Range.Contains(position)) continue;
                call = anyCall;
                Logger.Info($"Call found");
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
                        ArgumentListExpression.CreateAnonymous(functionType.Brackets, functionType.File),
                        functionType.File
                    );
                    Logger.Warn($"Converting {functionType} to {call}");
                }
            }
        }

        if (call is null)
        {
            Logger.Warn($"No call");
            return null;
        }

        if (!call.ToFunctionCall(out FunctionCallExpression? functionCall))
        {
            Logger.Warn($"Invalid call {call}");
            return null;
        }

        int activeArgument = 0;
        for (int i = 0; i < call.Arguments.Commas.Length; i++)
        {
            if (position >= call.Arguments.Commas[i].Position.Range.End)
            {
                activeArgument = i + 1;
            }
        }

        ImmutableArray<GeneralType?> methodArgumentTypes = functionCall.MethodArguments.Select(v => v.CompiledType).ToImmutableArray();

        IEnumerable<CompiledFunctionDefinition> candidatesBuilder = CompilerResult.FunctionDefinitions
            .Where(v =>
            {
                if (v.Identifier != functionCall.Identifier.Content) return false;
                if (!v.Definition.CanUse(Uri)) return false;
                if (methodArgumentTypes.Length > v.Parameters.Length) return false;
                return true;
            });

        if (functionCall.IsMethodCall)
        {
            Logger.Info($"Filtering extension functions ...");

            candidatesBuilder = candidatesBuilder.Where(v => v.Definition.IsExtension);

            if (functionCall.Object is null)
            {
                Logger.Error($"Invalid method call (object is null)");
            }

            if (functionCall.Object?.Value.CompiledType is not null)
            {
                GeneralType passed = functionCall.Object.Value.CompiledType;
                Logger.Info($"Filtering functions with `this` parameter and type `{passed}`");
                candidatesBuilder = candidatesBuilder
                    .Where(v =>
                    {
                        GeneralType defined = v.Parameters[0].Type;

                        if (defined.SameAs(passed)) return true;
                        if (defined.SameAs(new PointerType(passed))) return true;
                        if (defined.SameAs(new ReferenceType(passed))) return true;

                        return false;
                    });
            }
            else
            {
                Logger.Warn($"Method call object type is null ({functionCall.Object?.Value})");
            }
        }

        ImmutableArray<CompiledFunctionDefinition> candidates = candidatesBuilder.ToImmutableArray();

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

        foreach (var candidate in candidates)
        {
            Logger.Info(candidate.ToString());
        }

        return new SignatureHelp()
        {
            ActiveSignature = activeSignature,
            ActiveParameter = activeArgument,
            Signatures = new Container<SignatureInformation>(
                candidates.Select(v =>
                {
                    StringBuilder label = new();
                    List<(int, int)> parameters = new();
                    label.Append(v.Type.ToString());
                    label.Append(' ');

                    if (v.Definition.IsExtension)
                    {
                        label.Append(v.Parameters[0].Type.ToString());
                        label.Append('.');
                    }

                    label.Append(v.Identifier);

                    if (v.Definition.Template is not null)
                    {
                        label.Append('<');
                        label.AppendJoin(", ", v.Definition.Template.Parameters.Select(v => v.Content));
                        label.Append('>');
                    }

                    label.Append('(');
                    bool addComma = false;
                    for (int i = 0; i < v.Parameters.Length; i++)
                    {
                        if (v.Parameters[i].Definition.IsThis) continue;
                        if (addComma) label.Append(", ");
                        addComma = true;

                        if (v.Parameters[i].Definition.IsRef) label.Append("ref ");

                        CompiledParameter p = v.Parameters[i];
                        label.Append(p.Type);
                        label.Append(' ');
                        parameters.Add((label.Length, label.Length + p.Identifier.Length));
                        label.Append(p.Identifier);
                    }
                    label.Append(')');
                    return new SignatureInformation()
                    {
                        Label = label.ToString(),
                        ActiveParameter = activeArgument < v.Parameters.Length ? activeArgument : null,
                        Parameters = new Container<ParameterInformation>(parameters.Select(p => new ParameterInformation() { Label = new ParameterInformationLabel(p), })),
                        Documentation =
                            GetCommentDocumentation(v.Definition, out string? docs)
                            ? new StringOrMarkupContent(new MarkupContent()
                            {
                                Kind = MarkupKind.Markdown,
                                Value = docs,
                            })
                            : null,
                    };
                })
            ),
        };
    }
}
