using System.Collections.Immutable;
using LanguageCore;
using LanguageCore.Compiler;
using LanguageCore.Parser;
using LanguageCore.Parser.Statements;

namespace LanguageServer.DocumentManagers;

sealed partial class DocumentBBLang
{
    public override async Task<IEnumerable<CommandOrCodeAction>?> CodeAction(CodeActionParams request, CancellationToken cancellationToken)
    {
        await AwaitForCompilation(Version, cancellationToken).ConfigureAwait(false);

        List<CommandOrCodeAction> result = new();

        Range<SinglePosition> range = request.Range.ToCool();

        if (AST.GetStatementAt(range.Start, out var statement))
        {
            if (statement is VariableDefinition variableDefinition)
            {
                if (variableDefinition.Type.Position.Range.Contains(range.Start))
                {
                    if (variableDefinition.InitialValue?.CompiledType is not null)
                    {
                        if (variableDefinition.Type is TypeInstanceSimple simpleType && simpleType.Identifier.Content == StatementKeywords.Var)
                        {
                            result.Add(new CodeAction()
                            {
                                Kind = CodeActionKind.RefactorRewrite,
                                Title = "Use explicit type",
                                Edit = new WorkspaceEdit()
                                {
                                    Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>()
                                    {
                                        {
                                            Uri,
                                            new TextEdit[]
                                            {
                                                new()
                                                {
                                                    Range = variableDefinition.Type.Position.Range.ToOmniSharp(),
                                                    NewText = variableDefinition.InitialValue.CompiledType.ToString(),
                                                }
                                            }
                                        }
                                    }
                                }
                            });
                        }
                        else if (variableDefinition.InitialValue?.CompiledType.ToString() == variableDefinition.Type.ToString())
                        {
                            result.Add(new CodeAction()
                            {
                                Kind = CodeActionKind.RefactorRewrite,
                                Title = "Use implicit type",
                                Edit = new WorkspaceEdit()
                                {
                                    Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>()
                                    {
                                        {
                                            Uri,
                                            new TextEdit[]
                                            {
                                                new()
                                                {
                                                    Range = variableDefinition.Type.Position.Range.ToOmniSharp(),
                                                    NewText = StatementKeywords.Var,
                                                }
                                            }
                                        }
                                    }
                                }
                            });
                        }
                    }
                }
            }
        }

        HashSet<CompiledStatement> visitedStatements = new();
        CompilerResult.EnumerateStatements(statement =>
        {
            if (statement.Location.File != Uri) return true;
            if (!statement.Location.Position.Range.Contains(range.Start)) return true;
            if (statement is not CompiledFunctionCall compiledFunctionCall) return true;

            CompiledFunction? f = CompilerResult.Functions.FirstOrDefault(v => LanguageCore.Utils.ReferenceEquals(v.Function, compiledFunctionCall.Function.Template) && StatementCompiler.TypeArgumentsEquals(v.TypeArguments, compiledFunctionCall.Function.TypeArguments));

            if (f is null) return false;

            Logger.Trace($"compiledFunctionCall: {compiledFunctionCall}");
            Logger.Trace($"f: {f}");

            if (f.Function.Parameters.Length != compiledFunctionCall.Arguments.Length) return true;
            if (!visitedStatements.Add(statement)) return true;

            StatementCompiler.InlineContext inlineContext = new()
            {
                Arguments = f.Function.Parameters
                    .Select((value, i) => (value.Identifier, compiledFunctionCall.Arguments[i]))
                    .ToImmutableDictionary(v => v.Identifier, v => v.Item2),
            };

            if (StatementCompiler.InlineFunction(f.Body, inlineContext, out CompiledStatement? inlined1, out DiagnosticAt? inlineError))
            {
                Stringifier.Builder builder = new()
                {
                    IndentLevel = compiledFunctionCall.Location.Position.Range.Start.Character / 4,
                };
                Stringifier.Stringify(inlined1, builder);
                result.Add(new CodeAction()
                {
                    Kind = CodeActionKind.RefactorInline,
                    Title = $"Inline {f.Function.ToReadable()}",
                    Edit = new WorkspaceEdit()
                    {
                        Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>()
                        {
                            {
                                Uri,
                                new TextEdit[]
                                {
                                    new()
                                    {
                                        Range = compiledFunctionCall.Location.Position.Range.ToOmniSharp(),
                                        NewText = builder.ToString(),
                                    }
                                }
                            }
                        }
                    }
                });
            }
            else
            {
                Logger.Trace($"Failed to inline {f.ToReadable()}: {inlineError}");
            }
            return true;
        });

        return result;
    }
}
