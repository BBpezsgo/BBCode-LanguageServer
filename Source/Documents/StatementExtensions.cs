using LanguageCore;
using LanguageCore.Compiler;
using LanguageCore.Parser;
using LanguageCore.Parser.Statements;
using LanguageCore.Tokenizing;

namespace LanguageServer;

static class StatementExtensions
{
    public static IEnumerable<Statement> EnumerateStatements(this ParserResult parserResult)
    {
        foreach (Statement v in parserResult.TopLevelStatements.IsDefault ? Enumerable.Empty<Statement>() : parserResult.TopLevelStatements.SelectMany(StatementWalker.Visit))
        { yield return v; }

        foreach (Statement statement in parserResult.Functions.IsDefault ? Enumerable.Empty<Statement>() : parserResult.Functions.SelectMany(v => StatementWalker.Visit(v.Block)))
        { yield return statement; }

        foreach (Statement statement in parserResult.Operators.IsDefault ? Enumerable.Empty<Statement>() : parserResult.Operators.SelectMany(v => StatementWalker.Visit(v.Block)))
        { yield return statement; }

        foreach (StructDefinition structs in parserResult.Structs.IsDefault ? Enumerable.Empty<StructDefinition>() : parserResult.Structs)
        {
            foreach (Statement statement in structs.GeneralFunctions.SelectMany(v => StatementWalker.Visit(v.Block)))
            { yield return statement; }

            foreach (Statement statement in structs.Functions.SelectMany(v => StatementWalker.Visit(v.Block)))
            { yield return statement; }

            foreach (Statement statement in structs.Operators.SelectMany(v => StatementWalker.Visit(v.Block)))
            { yield return statement; }

            foreach (Statement statement in structs.Constructors.SelectMany(v => StatementWalker.Visit(v.Block)))
            { yield return statement; }
        }
    }

    public static bool GetStatementAt(this ParserResult parserResult, SinglePosition position, [NotNullWhen(true)] out Statement? statement)
        => (statement = parserResult.EnumerateStatements().LastOrDefault(statement => statement.Position.Range.Contains(position))) is not null;

    public static bool GetThingAt<TThing, TIdentifier>(IEnumerable<TThing> things, Uri file, SinglePosition position, [NotNullWhen(true)] out TThing? result)
        where TThing : IInFile, IIdentifiable<TIdentifier>
        where TIdentifier : IPositioned
    {
        foreach (TThing? thing in things)
        {
            if (thing.File != file)
            { continue; }

            if (!thing.Identifier.Position.Range.Contains(position))
            { continue; }

            result = thing;
            return true;
        }

        result = default;
        return false;
    }

    public static bool GetFunctionAt(this CompilerResult compilerResult, Uri file, SinglePosition position, [NotNullWhen(true)] out CompiledFunctionDefinition? result)
        => GetThingAt<CompiledFunctionDefinition, Token>(compilerResult.FunctionDefinitions, file, position, out result);

    public static bool GetGeneralFunctionAt(this CompilerResult compilerResult, Uri file, SinglePosition position, [NotNullWhen(true)] out CompiledGeneralFunctionDefinition? result)
        => GetThingAt<CompiledGeneralFunctionDefinition, Token>(compilerResult.GeneralFunctionDefinitions, file, position, out result);

    public static bool GetOperatorAt(this CompilerResult compilerResult, Uri file, SinglePosition position, [NotNullWhen(true)] out CompiledOperatorDefinition? result)
        => GetThingAt<CompiledOperatorDefinition, Token>(compilerResult.OperatorDefinitions, file, position, out result);

    public static bool GetStructAt(this CompilerResult compilerResult, Uri file, SinglePosition position, [NotNullWhen(true)] out CompiledStruct? result)
        => GetThingAt<CompiledStruct, Token>(compilerResult.Structs, file, position, out result);

    public static bool GetFieldAt(this CompilerResult compilerResult, Uri file, SinglePosition position, [NotNullWhen(true)] out CompiledField? result)
    {
        foreach (CompiledStruct @struct in compilerResult.Structs)
        {
            if (@struct.File != file) continue;

            foreach (CompiledField field in @struct.Fields)
            {
                if (field.Identifier.Position.Range.Contains(position))
                {
                    result = field;
                    return true;
                }
            }
        }

        result = null;
        return false;
    }
}
