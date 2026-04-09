using LanguageCore;
using LanguageCore.Parser;
using LanguageCore.Parser.Statements;

namespace LanguageServer.DocumentManagers;

sealed partial class DocumentBBLang
{
    public override async Task<IEnumerable<SymbolInformationOrDocumentSymbol>?> Symbols(DocumentSymbolParams e, CancellationToken cancellationToken)
    {
        await AwaitForCompilation(Version, cancellationToken).ConfigureAwait(false);

        List<SymbolInformationOrDocumentSymbol> result = new();

        foreach (FunctionDefinition function in AST.Functions)
        {
            if (function.File != e.TextDocument.Uri) continue;

            result.Add(new DocumentSymbol()
            {
                Kind = SymbolKind.Function,
                Name = function.Identifier.Content,
                Range = function.Position.Range.ToOmniSharp(),
                SelectionRange = function.Identifier.Position.Range.ToOmniSharp(),
                Detail = function.ToReadable(),
            });
        }

        foreach (FunctionDefinition function in AST.Operators)
        {
            if (function.File != e.TextDocument.Uri) continue;

            result.Add(new DocumentSymbol()
            {
                Kind = SymbolKind.Function,
                Name = function.Identifier.Content,
                Range = function.Position.Range.ToOmniSharp(),
                SelectionRange = function.Identifier.Position.Range.ToOmniSharp(),
                Detail = function.ToReadable(),
            });
        }

        foreach (StructDefinition @struct in AST.Structs)
        {
            if (@struct.File != e.TextDocument.Uri) continue;

            List<DocumentSymbol> children = new();

            foreach (FunctionDefinition function in @struct.Functions)
            {
                children.Add(new DocumentSymbol()
                {
                    Kind = SymbolKind.Method,
                    Name = function.Identifier.Content,
                    Range = function.Position.Range.ToOmniSharp(),
                    SelectionRange = function.Identifier.Position.Range.ToOmniSharp(),
                    Detail = function.ToReadable(),
                });
            }

            foreach (FunctionDefinition function in @struct.Operators)
            {
                children.Add(new DocumentSymbol()
                {
                    Kind = SymbolKind.Operator,
                    Name = function.Identifier.Content,
                    Range = function.Position.Range.ToOmniSharp(),
                    SelectionRange = function.Identifier.Position.Range.ToOmniSharp(),
                    Detail = function.ToReadable(),
                });
            }

            foreach (GeneralFunctionDefinition function in @struct.GeneralFunctions)
            {
                children.Add(new DocumentSymbol()
                {
                    Kind = SymbolKind.Function,
                    Name = function.Identifier.Content,
                    Range = function.Position.Range.ToOmniSharp(),
                    SelectionRange = function.Identifier.Position.Range.ToOmniSharp(),
                    Detail = function.ToReadable(),
                });
            }

            foreach (ConstructorDefinition function in @struct.Constructors)
            {
                children.Add(new DocumentSymbol()
                {
                    Kind = SymbolKind.Constructor,
                    Name = function.Identifier.Content,
                    Range = function.Position.Range.ToOmniSharp(),
                    SelectionRange = function.Identifier.Position.Range.ToOmniSharp(),
                    Detail = function.ToReadable(),
                });
            }

            foreach (FieldDefinition field in @struct.Fields)
            {
                children.Add(new DocumentSymbol()
                {
                    Kind = SymbolKind.Field,
                    Name = field.Identifier.Content,
                    Range = field.Position.Range.ToOmniSharp(),
                    SelectionRange = field.Identifier.Position.Range.ToOmniSharp(),
                });
            }

            result.Add(new DocumentSymbol()
            {
                Kind = SymbolKind.Struct,
                Name = @struct.Identifier.Content,
                Range = @struct.Position.Range.ToOmniSharp(),
                SelectionRange = @struct.Identifier.Position.Range.ToOmniSharp(),
                Children = children,
            });
        }

        foreach (AliasDefinition alias in AST.AliasDefinitions)
        {
            if (alias.File != e.TextDocument.Uri) continue;

            result.Add(new DocumentSymbol()
            {
                Kind = SymbolKind.Class,
                Name = alias.Identifier.Content,
                Range = alias.Position.Range.ToOmniSharp(),
                SelectionRange = alias.Identifier.Position.Range.ToOmniSharp(),
            });
        }

        foreach (EnumDefinition @enum in AST.EnumDefinitions)
        {
            if (@enum.File != e.TextDocument.Uri) continue;

            List<DocumentSymbol> children = new();

            foreach (EnumMemberDefinition member in @enum.Members)
            {
                children.Add(new DocumentSymbol()
                {
                    Kind = SymbolKind.EnumMember,
                    Name = member.Identifier.Content,
                    Range = member.Position.Range.ToOmniSharp(),
                    SelectionRange = member.Identifier.Position.Range.ToOmniSharp(),
                });
            }

            result.Add(new DocumentSymbol()
            {
                Kind = SymbolKind.Enum,
                Name = @enum.Identifier.Content,
                Range = @enum.Position.Range.ToOmniSharp(),
                SelectionRange = @enum.Identifier.Position.Range.ToOmniSharp(),
            });
        }

        foreach (VariableDefinition variable in AST.TopLevelStatements.OfType<VariableDefinition>())
        {
            if (variable.File != e.TextDocument.Uri) continue;

            result.Add(new DocumentSymbol()
            {
                Kind = variable.Modifiers.Contains(ModifierKeywords.Const) ? SymbolKind.Constant : SymbolKind.Variable,
                Name = variable.Identifier.Content,
                Range = variable.Position.ToOmniSharp(),
                SelectionRange = variable.Identifier.Position.Range.ToOmniSharp(),
            });
        }

        return result;
    }
}
