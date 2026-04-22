using LanguageCore;

namespace LanguageServer.DocumentManagers;

sealed partial class DocumentBBLang
{
    public override async Task<IEnumerable<TextEdit>?> DocumentFormatting(DocumentFormattingParams request, CancellationToken cancellationToken)
    {
        await AwaitForCompilation(Version, cancellationToken).ConfigureAwait(false);

        List<TextEdit> result = new();

        Stringifier.Builder builder = new();

        foreach (var item in AST.Usings)
        {
            Stringifier.Stringify(item, builder);
            builder.Append(';');
            builder.NewLine();
        }

        foreach (var item in AST.Structs)
        {
            Stringifier.Stringify(item, builder);
            builder.NewLine();
        }

        foreach (var item in AST.EnumDefinitions)
        {
            Stringifier.Stringify(item, builder);
            builder.NewLine();
        }

        foreach (var item in AST.AliasDefinitions)
        {
            Stringifier.Stringify(item, builder);
            builder.NewLine();
        }

        foreach (var item in AST.Functions)
        {
            Stringifier.Stringify(item, builder);
            builder.NewLine();
        }

        foreach (var item in AST.Operators)
        {
            Stringifier.Stringify(item, builder);
            builder.NewLine();
        }

        foreach (var statement in AST.TopLevelStatements)
        {
            Stringifier.Stringify(statement, builder);
            builder.Append(';');
            builder.NewLine();
        }

        result.Add(new TextEdit()
        {
            NewText = builder.ToString(),
            Range = new Range<SinglePosition>(AST.Tokens[0].Position.Range.Start, AST.Tokens[^1].Position.Range.End).ToOmniSharp(),
        });

        return result;
    }
}
