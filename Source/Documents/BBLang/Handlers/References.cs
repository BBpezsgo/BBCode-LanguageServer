using LanguageCore;
using LanguageCore.Compiler;
using LanguageCore.Parser;
using OmniSharpLocation = OmniSharp.Extensions.LanguageServer.Protocol.Models.Location;

namespace LanguageServer.DocumentManagers;

sealed partial class DocumentBBLang
{
    public override async Task<IEnumerable<OmniSharpLocation>?> References(ReferenceParams e, CancellationToken cancellationToken)
    {
        await AwaitForCompilation(Version, cancellationToken).ConfigureAwait(false);

        SinglePosition p = e.Position.ToCool();
        List<OmniSharpLocation> result = new();

        void AddReferences(IReferenceable definition)
        {
            switch (definition)
            {
                case CompiledVariableDefinition v: result.Add(new LanguageCore.Location(v.Definition.Identifier.Position, v.Definition.File).ToOmniSharp()); break;
                case CompiledFunctionDefinition v: result.Add(new LanguageCore.Location(v.Definition.Identifier.Position, v.Definition.File).ToOmniSharp()); break;
                case CompiledGeneralFunctionDefinition v: result.Add(new LanguageCore.Location(v.Definition.Identifier.Position, v.Definition.File).ToOmniSharp()); break;
                case CompiledOperatorDefinition v: result.Add(new LanguageCore.Location(v.Definition.Identifier.Position, v.Definition.File).ToOmniSharp()); break;
                case CompiledConstructorDefinition v: result.Add(new LanguageCore.Location(v.Definition.Identifier.Position, v.Definition.File).ToOmniSharp()); break;
                case CompiledAlias v: result.Add(new LanguageCore.Location(v.Definition.Identifier.Position, v.Definition.File).ToOmniSharp()); break;
                case CompiledEnum v: result.Add(new LanguageCore.Location(v.Definition.Identifier.Position, v.Definition.File).ToOmniSharp()); break;
                case CompiledEnumMember v: result.Add(new LanguageCore.Location(v.Definition.Identifier.Position, v.Definition.File).ToOmniSharp()); break;
                case CompiledField v: result.Add(new LanguageCore.Location(v.Definition.Identifier.Position, v.Definition.File).ToOmniSharp()); break;
                case CompiledVariableConstant v: result.Add(new LanguageCore.Location(v.Definition.Identifier.Position, v.Definition.File).ToOmniSharp()); break;
                default:
                    Logger.Warn($"Reference definition not implemented: {definition.GetType().Name}");
                    break;
            }
            foreach (Reference reference in definition.GetReferences().DistinctBy(v => v.SourceLocation))
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
            Logger.Info($"{statement.GetType().Name} {statement}");
            if (statement is IReferenceableTo referenceableTo)
            {
                if (referenceableTo.Reference is IReferenceable referenceable)
                {
                    AddReferences(referenceable);
                }
                else if (referenceableTo.Reference is StatementCompiler.FunctionQueryResult<CompiledFunctionDefinition> v)
                {
                    AddReferences(v.Function);
                }
            }
            else if (statement is IReferenceable referenceable)
            {
                AddReferences(referenceable);
            }
        }

        return result;
    }
}
