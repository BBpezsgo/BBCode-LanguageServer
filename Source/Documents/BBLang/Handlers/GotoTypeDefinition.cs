using LanguageCore.Compiler;
using LanguageCore.Parser;
using Position = LanguageCore.Position;

namespace LanguageServer.DocumentManagers;

sealed partial class DocumentBBLang
{
    public override async Task<LocationOrLocationLinks?> GotoTypeDefinition(TypeDefinitionParams e, CancellationToken cancellationToken)
    {
        await AwaitForCompilation(Version, cancellationToken).ConfigureAwait(false);

        List<LocationOrLocationLink> links = new();

        if ((AST, CompilerResult).GetTypeInstanceAt(Uri, e.Position.ToCool(), out TypeInstance? origin, out GeneralType? type))
        {
            GetDeepestTypeInstance(ref origin, ref type, e.Position.ToCool());

            if (origin is not null &&
                type is not null)
            {
                Position position = origin switch
                {
                    TypeInstanceSimple v => v.Identifier.Position,
                    _ => origin.Position,
                };

                if (type.Is(out StructType? structType) &&
                    GetGotoDefinition(structType.Struct, out LocationLink? link))
                {
                    links.Add(new LocationLink()
                    {
                        OriginSelectionRange = position.ToOmniSharp(),
                        TargetRange = link.TargetRange,
                        TargetSelectionRange = link.TargetSelectionRange,
                        TargetUri = link.TargetUri,
                    });
                }
                else if (type.Is(out GenericType? genericType) &&
                            genericType.Definition != null)
                {
                    links.Add(new LocationLink()
                    {
                        OriginSelectionRange = position.ToOmniSharp(),
                        TargetRange = genericType.Definition.Position.Range.ToOmniSharp(),
                        TargetSelectionRange = genericType.Definition.Position.Range.ToOmniSharp(),
                        TargetUri = DocumentUri,
                    });
                }
                else if (type is AliasType aliasType &&
                            aliasType.Definition != null)
                {
                    links.Add(new LocationLink()
                    {
                        OriginSelectionRange = position.ToOmniSharp(),
                        TargetRange = aliasType.Definition.Definition.Position.Range.ToOmniSharp(),
                        TargetSelectionRange = aliasType.Definition.Definition.Position.Range.ToOmniSharp(),
                        TargetUri = aliasType.Definition.Definition.File,
                    });
                }
            }
        }

        return new LocationOrLocationLinks(links);
    }
}
