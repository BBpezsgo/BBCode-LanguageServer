using LanguageCore;
using LanguageCore.Compiler;
using LanguageCore.Parser;
using LanguageCore.Parser.Statements;
using OmniSharpLocation = OmniSharp.Extensions.LanguageServer.Protocol.Models.Location;

namespace LanguageServer.DocumentManagers;

sealed partial class DocumentBBLang
{
    public override async Task<IEnumerable<OmniSharpLocation>?> References(ReferenceParams e, CancellationToken cancellationToken)
    {
        await AwaitForCompilation(Version, cancellationToken).ConfigureAwait(false);

        SinglePosition p = e.Position.ToCool();
        List<OmniSharpLocation> result = new();

        void AddReferences<T>(IReferenceable<T?> definition)
            where T : IPositioned
        {
            foreach (Reference<T?> reference in definition.References.DistinctBy(v => v.SourceLocation))
            {
                result.Add(reference.SourceLocation.ToOmniSharp());
            }
        }

        if (CompilerResult.GetFunctionAt(Uri, p, out CompiledFunctionDefinition? function))
        {
            AddReferences(function);
        }
        else if (CompilerResult.GetGeneralFunctionAt(Uri, p, out CompiledGeneralFunctionDefinition? generalFunction))
        {
            AddReferences(generalFunction);
        }
        else if (CompilerResult.GetOperatorAt(Uri, p, out CompiledOperatorDefinition? @operator))
        {
            AddReferences(@operator!);
        }
        else if (CompilerResult.GetStructAt(Uri, p, out CompiledStruct? @struct))
        {
            AddReferences(@struct!);
        }
        else if (CompilerResult.GetAliasAt(Uri, p, out var alias))
        {
            AddReferences(alias!);
        }
        else if (CompilerResult.GetEnumAt(Uri, p, out var @enum))
        {
            AddReferences(@enum!);
        }
        else if (CompilerResult.GetEnumMemberAt(Uri, p, out var enumMember))
        {
            AddReferences(enumMember!);
        }
        else if (CompilerResult.GetFieldAt(Uri, p, out var field))
        {
            AddReferences(field!);
        }
        else if (AST.GetTypeInstanceAt(p, out var typeInstance, out var compiledType))
        {
            GetDeepestTypeInstance(ref typeInstance, ref compiledType, p);
            Logger.Info(typeInstance);
            Logger.Info(compiledType);
            if (compiledType is AliasType aliasType)
            {
                AddReferences(aliasType.Definition!);
            }
            else if (compiledType is EnumType enumType)
            {
                AddReferences(enumType.Definition!);
            }
            else if (compiledType is StructType structType)
            {
                AddReferences(structType.Struct!);
            }
        }
        else if (AST.GetStatementAt(p, out var statement))
        {
            Logger.Info(statement);
            if (statement is IReferenceableTo referenceableTo)
            {
                Logger.Info(statement);
                Logger.Info(referenceableTo.Reference);
                switch (referenceableTo.Reference)
                {
                    case CompiledConstructorDefinition referenceable:
                        AddReferences(referenceable!);
                        break;
                    case StatementCompiler.FunctionQueryResult<CompiledFunctionDefinition> referenceable:
                        AddReferences(referenceable.Function);
                        break;
                    case null:
                        break;
                    default:
                        Logger.Warn($"Not implemented: `{referenceableTo.Reference?.GetType()}`");
                        break;
                }
            }
        }

        return result;
    }
}
