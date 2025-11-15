using System.Collections.Immutable;
using LanguageCore;
using LanguageCore.Compiler;
using LanguageCore.Parser;
using LanguageCore.Parser.Statements;
using LanguageCore.Tokenizing;
using Position = LanguageCore.Position;

namespace LanguageServer;

static class Utils
{
    static bool GetParameterDefinitionAt<TFunction>(
        TFunction function,
        SinglePosition position,
        [NotNullWhen(true)] out ParameterDefinition? parameter,
        [NotNullWhen(true)] out GeneralType? parameterType)
        where TFunction : ICompiledFunctionDefinition
    {
        for (int i = 0; i < function.Parameters.Length; i++)
        {
            parameter = function.Parameters[i];
            parameterType = function.Parameters[i].Type;

            if (parameter.Position.Range.Contains(position))
            { return true; }
        }

        parameter = null;
        parameterType = null;
        return false;
    }

    static bool GetParameterDefinitionAt<TFunction>(
        IEnumerable<TFunction> functions,
        Uri file,
        SinglePosition position,
        [NotNullWhen(true)] out ParameterDefinition? parameter,
        [NotNullWhen(true)] out GeneralType? parameterType)
        where TFunction : ICompiledFunctionDefinition, IInFile
    {
        foreach (TFunction function in functions)
        {
            if (function.File != file)
            { continue; }

            if (GetParameterDefinitionAt(function, position, out parameter, out parameterType))
            { return true; }
        }

        parameter = null;
        parameterType = null;
        return false;
    }

    public static bool GetParameterDefinitionAt(
        this CompilerResult compilerResult,
        Uri file,
        SinglePosition position,
        [NotNullWhen(true)] out ParameterDefinition? parameter,
        [NotNullWhen(true)] out GeneralType? parameterType)
    {
        if (GetParameterDefinitionAt(compilerResult.FunctionDefinitions, file, position, out parameter, out parameterType))
        { return true; }

        if (GetParameterDefinitionAt(compilerResult.OperatorDefinitions, file, position, out parameter, out parameterType))
        { return true; }

        if (GetParameterDefinitionAt(compilerResult.GeneralFunctionDefinitions, file, position, out parameter, out parameterType))
        { return true; }

        if (GetParameterDefinitionAt(compilerResult.ConstructorDefinitions, file, position, out parameter, out parameterType))
        { return true; }

        return false;
    }

    static bool GetReturnTypeAt<TFunction>(
        TFunction function,
        SinglePosition position,
        [NotNullWhen(true)] out TypeInstance? typeInstance,
        [NotNullWhen(true)] out GeneralType? generalType)
        where TFunction : FunctionDefinition, ICompiledFunctionDefinition
    {
        if (function.Type.Position.Range.Contains(position))
        {
            typeInstance = function.Type;
            generalType = ((ICompiledFunctionDefinition)function).Type;
            return true;
        }

        typeInstance = null;
        generalType = null;
        return false;
    }

    static bool GetReturnTypeAt<TFunction>(
        IEnumerable<TFunction> functions,
        Uri file,
        SinglePosition position,
        [NotNullWhen(true)] out TypeInstance? typeInstance,
        [NotNullWhen(true)] out GeneralType? generalType)
        where TFunction : FunctionDefinition, ICompiledFunctionDefinition, IInFile
    {
        foreach (TFunction function in functions)
        {
            if (function.File != file)
            { continue; }

            if (GetReturnTypeAt(function, position, out typeInstance, out generalType))
            { return true; }
        }

        typeInstance = null;
        generalType = null;
        return false;
    }

    static bool GetReturnTypeAt(
        CompilerResult compilerResult,
        Uri file,
        SinglePosition position,
        [NotNullWhen(true)] out TypeInstance? typeInstance,
        [NotNullWhen(true)] out GeneralType? generalType)
    {
        if (GetReturnTypeAt(compilerResult.FunctionDefinitions, file, position, out typeInstance, out generalType))
        { return true; }

        if (GetReturnTypeAt(compilerResult.OperatorDefinitions, file, position, out typeInstance, out generalType))
        { return true; }

        return false;
    }

    public static bool GetTypeInstanceAt(
        this CompilerResult compilerResult,
        Uri file,
        SinglePosition position,
        [NotNullWhen(true)] out TypeInstance? typeInstance,
        [NotNullWhen(true)] out GeneralType? generalType)
    {
        if (GetParameterDefinitionAt(compilerResult, file, position, out ParameterDefinition? parameter, out GeneralType? parameterType) &&
            parameter.Type.Position.Range.Contains(position))
        {
            typeInstance = parameter.Type;
            generalType = parameterType;
            return true;
        }

        if (GetReturnTypeAt(compilerResult, file, position, out TypeInstance? returnType, out GeneralType? returnCompiledType) &&
            returnType.Position.Range.Contains(position))
        {
            typeInstance = returnType;
            generalType = returnCompiledType;
            return true;
        }

        foreach (CompiledStruct @struct in compilerResult.Structs)
        {
            foreach (CompiledField field in @struct.Fields)
            {
                if (((FieldDefinition)field).Type.Position.Range.Contains(position))
                {
                    typeInstance = ((FieldDefinition)field).Type;
                    generalType = field.Type;
                    return true;
                }
            }
        }

        typeInstance = null;
        generalType = null;
        return false;
    }

    public static bool GetTypeInstanceAt(
        this ParserResult ast,
        SinglePosition position,
        [NotNullWhen(true)] out TypeInstance? typeInstance,
        [NotNullWhen(true)] out GeneralType? generalType)
    {
        bool Handle3(TypeInstance? type1, GeneralType? type2, [NotNullWhen(true)] out TypeInstance? typeInstance, [NotNullWhen(true)] out GeneralType? generalType)
        {
            typeInstance = null;
            generalType = null;

            if (type1 is null || type2 is null) return false;
            if (!type1.Position.Range.Contains(position)) return false;

            typeInstance = type1;
            generalType = type2;
            return true;
        }

        bool Handle2(Statement? statement, [NotNullWhen(true)] out TypeInstance? typeInstance, [NotNullWhen(true)] out GeneralType? generalType)
        {
            typeInstance = null;
            generalType = null;

            return statement switch
            {
                TypeStatement v => Handle3(v.Type, v.CompiledType, out typeInstance, out generalType),
                BasicTypeCast v => Handle3(v.Type, v.CompiledType, out typeInstance, out generalType),
                ManagedTypeCast v => Handle3(v.Type, v.CompiledType, out typeInstance, out generalType),
                VariableDeclaration v => Handle3(v.Type, v.CompiledType, out typeInstance, out generalType),
                NewInstance v => Handle3(v.Type, v.CompiledType, out typeInstance, out generalType),
                _ => false
            };
        }

        Statement? statement = ast.GetStatementAt(position);
        if (statement is not null)
        {
            foreach (Statement item in statement.GetStatementsRecursively(true))
            {
                if (Handle2(item, out typeInstance, out generalType))
                { return true; }
            }
        }

        typeInstance = null;
        generalType = null;
        return false;
    }

    public static bool GetTypeInstanceAt(
        this ValueTuple<ParserResult, CompilerResult> self,
        Uri file,
        SinglePosition position,
        [NotNullWhen(true)] out TypeInstance? typeInstance,
        [NotNullWhen(true)] out GeneralType? generalType)
    {
        if (GetTypeInstanceAt(self.Item2, file, position, out typeInstance, out generalType))
        { return true; }

        if (GetTypeInstanceAt(self.Item1, position, out typeInstance, out generalType))
        { return true; }

        return false;
    }

    public static IEnumerable<Token> GetVisibleModifiers(IEnumerable<Token> modifiers)
    {
        return modifiers.Where(v => v.Content != "export");
    }

    public static Position GetInteractivePosition(Statement statement) => statement switch
    {
        AnyCall v => v.PrevStatement switch
        {
            Field v2 => v2.Identifier.Position,
            _ => v.PrevStatement.Position,
        },
        BinaryOperatorCall v => v.Operator.Position,
        UnaryOperatorCall v => v.Operator.Position,
        VariableDeclaration v => v.Identifier.Position,
        Field v => v.Identifier.Position,
        ConstructorCall v => new Position([v.Keyword, v.Type]),
        ManagedTypeCast v => new Position([v.Type, v.Brackets]),
        _ => statement.Position,
    };
}
