using LanguageCore.Tokenizing;

namespace LanguageServer.DocumentManagers;

sealed partial class DocumentBBLang
{
    public override async Task GetSemanticTokens(SemanticTokensBuilder builder, ITextDocumentIdentifierParams e, CancellationToken cancellationToken)
    {
        await AwaitForCompilation(Version, cancellationToken).ConfigureAwait(false);

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
                case TokenAnalyzedType.TypeAlias:
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
                    builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Operator, Array.Empty<SemanticTokenModifier>());
                    break;
                case TokenAnalyzedType.TypeModifier:
                    builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Operator, Array.Empty<SemanticTokenModifier>());
                    break;
                case TokenAnalyzedType.InstructionLabel:
                    break;
                case TokenAnalyzedType.Enum:
                    builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.Enum, Array.Empty<SemanticTokenModifier>());
                    break;
                case TokenAnalyzedType.EnumMember:
                    builder.Push(token.Position.Range.ToOmniSharp(), SemanticTokenType.EnumMember, Array.Empty<SemanticTokenModifier>());
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

        builder.Commit();
    }
}
