using System.Collections.Immutable;
using LanguageCore;
using LanguageCore.Compiler;
using LanguageCore.Parser;
using LanguageCore.Parser.Statements;
using LanguageCore.Tokenizing;

namespace LanguageServer;

static class StatementExtensions
{
    public static bool GetStatementAt(this ParserResult parserResult, SinglePosition position, [NotNullWhen(true)] out Statement? statement)
        => (statement = parserResult.EnumerateStatements().LastOrDefault(statement => statement.Position.Range.Contains(position))) is not null;

    public static bool GetFieldAt(this ParserResult parserResult, SinglePosition position, [NotNullWhen(true)] out FieldDefinition? result)
    {
        foreach (FieldDefinition field in (parserResult.Structs.IsDefault ? ImmutableArray<StructDefinition>.Empty : parserResult.Structs).SelectMany(v => v.Fields))
        {
            if (!field.Identifier.Position.Range.Contains(position)) continue;

            result = field;
            return true;
        }

        result = null;
        return false;
    }

    public static bool GetStructAt(this ParserResult parserResult, SinglePosition position, [NotNullWhen(true)] out StructDefinition? result)
    {
        foreach (StructDefinition @struct in parserResult.Structs.IsDefault ? ImmutableArray<StructDefinition>.Empty : parserResult.Structs)
        {
            if (!@struct.Identifier.Position.Range.Contains(position)) continue;

            result = @struct;
            return true;
        }

        result = null;
        return false;
    }

    public static bool GetFunctionAt(this CompilerResult compilerResult, Uri file, SinglePosition position, [NotNullWhen(true)] out CompiledFunctionDefinition? result)
    {
        foreach (CompiledFunctionDefinition thing in compilerResult.FunctionDefinitions)
        {
            if (thing.File != file) continue;
            if (!thing.Definition.Identifier.Position.Range.Contains(position)) continue;

            result = thing;
            return true;
        }

        result = default;
        return false;
    }

    public static bool GetGeneralFunctionAt(this CompilerResult compilerResult, Uri file, SinglePosition position, [NotNullWhen(true)] out CompiledGeneralFunctionDefinition? result)
    {
        foreach (CompiledGeneralFunctionDefinition thing in compilerResult.GeneralFunctionDefinitions)
        {
            if (thing.File != file) continue;
            if (!thing.Definition.Identifier.Position.Range.Contains(position)) continue;

            result = thing;
            return true;
        }

        result = default;
        return false;
    }

    public static bool GetOperatorAt(this CompilerResult compilerResult, Uri file, SinglePosition position, [NotNullWhen(true)] out CompiledOperatorDefinition? result)
    {
        foreach (CompiledOperatorDefinition thing in compilerResult.OperatorDefinitions)
        {
            if (thing.File != file) continue;
            if (!thing.Definition.Identifier.Position.Range.Contains(position)) continue;

            result = thing;
            return true;
        }

        result = default;
        return false;
    }

    public static bool GetStructAt(this CompilerResult compilerResult, Uri file, SinglePosition position, [NotNullWhen(true)] out CompiledStruct? result)
    {
        foreach (var thing in compilerResult.Structs)
        {
            if (thing.File != file) continue;
            if (!thing.Definition.Identifier.Position.Range.Contains(position)) continue;

            result = thing;
            return true;
        }

        result = default;
        return false;
    }

    public static bool GetFieldAt(this CompilerResult compilerResult, Uri file, SinglePosition position, [NotNullWhen(true)] out CompiledField? result)
    {
        foreach (CompiledStruct @struct in compilerResult.Structs)
        {
            if (@struct.Definition.File != file) continue;

            foreach (CompiledField field in @struct.Fields)
            {
                if (field.Definition.Identifier.Position.Range.Contains(position))
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
