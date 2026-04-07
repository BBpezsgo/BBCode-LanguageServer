using LanguageCore.Compiler;
using LanguageCore.Parser;

namespace LanguageServer.DocumentManagers;

sealed partial class DocumentBBLang
{
    public override async Task<IEnumerable<CodeLens>?> CodeLens(CodeLensParams e, CancellationToken cancellationToken)
    {
        await AwaitForCompilation(Version, cancellationToken).ConfigureAwait(false);

        List<CodeLens> result = new();

        foreach (CompiledFunctionDefinition function in CompilerResult.FunctionDefinitions)
        {
            if (function.File != Uri) continue;

            result.Add(new CodeLens()
            {
                Range = function.Definition.Identifier.Position.Range.ToOmniSharp(),
                Command = new Command()
                {
                    Title = $"{function.References.DistinctBy(v => v.Source).Count(v => !v.SourceLocation.IsDefault)} reference",
                },
            });
        }

        foreach (CompiledGeneralFunctionDefinition function in CompilerResult.GeneralFunctionDefinitions)
        {
            if (function.File != Uri) continue;

            result.Add(new CodeLens()
            {
                Range = function.Definition.Identifier.Position.Range.ToOmniSharp(),
                Command = new Command()
                {
                    Title = $"{function.References.DistinctBy(v => v.Source).Count(v => !v.SourceLocation.IsDefault)} reference",
                },
            });
        }

        foreach (CompiledOperatorDefinition function in CompilerResult.OperatorDefinitions)
        {
            if (function.File != Uri) continue;

            result.Add(new CodeLens()
            {
                Range = function.Definition.Identifier.Position.Range.ToOmniSharp(),
                Command = new Command()
                {
                    Title = $"{function.References.DistinctBy(v => v.Source).Count(v => !v.SourceLocation.IsDefault)} reference",
                },
            });
        }

        foreach (CompiledConstructorDefinition function in CompilerResult.ConstructorDefinitions)
        {
            if (function.File != Uri) continue;

            result.Add(new CodeLens()
            {
                Range = function.Definition.Type.Position.Range.ToOmniSharp(),
                Command = new Command()
                {
                    Title = $"{function.References.DistinctBy(v => v.Source).Count(v => !v.SourceLocation.IsDefault)} reference",
                },
            });
        }

        foreach (CompiledStruct @struct in CompilerResult.Structs)
        {
            if (@struct.Definition.File != Uri) continue;

            result.Add(new CodeLens()
            {
                Range = @struct.Definition.Identifier.Position.Range.ToOmniSharp(),
                Command = new Command()
                {
                    Title = $"{@struct.References.DistinctBy(v => v.Source).Count(v => !v.SourceLocation.IsDefault)} reference",
                },
            });
        }

        return result;
    }
}
