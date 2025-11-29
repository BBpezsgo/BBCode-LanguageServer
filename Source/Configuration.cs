using System.Collections.Immutable;
using System.IO;
using LanguageCore;
using LanguageCore.Runtime;

namespace LanguageServer;

sealed class Configuration
{
    public required IReadOnlyList<string> ExtraDirectories { get; init; }
    public required IReadOnlyList<string> AdditionalImports { get; init; }
    public required IReadOnlyList<ExternalFunctionStub> ExternalFunctions { get; init; }
    public required IReadOnlyList<ExternalConstant> ExternalConstants { get; init; }
}

static class ConfigurationManager
{
    const string FileName = "bbl.conf";

    public static IReadOnlyList<(Uri Uri, string Content)> Search(Uri currentDocument, Documents documents)
    {
        Logger.Log("Searching for configuration file ...");

        Uri currentUri = currentDocument;
        List<(Uri Uri, string Content)> result = new();
        EndlessCheck endlessCheck = new(50);
        while (currentUri.LocalPath != "/")
        {
            if (endlessCheck.Step()) break;
            Uri uri = new(currentUri, $"./{FileName}");
            Logger.Log($"  Try {uri}");
            if (documents.TryGet(uri, out DocumentBase? document))
            {
                result.Add((uri, document.Content));
                Logger.Log($"  document");
            }
            else if (File.Exists(uri.LocalPath))
            {
                result.Add((uri, File.ReadAllText(uri.LocalPath)));
                Logger.Log($"  local file");
            }
            currentUri = new Uri(currentUri, "..");
        }
        return result;
    }

    delegate void DeclarationParser(ReadOnlySpan<char> key, ReadOnlySpan<char> value, LanguageCore.Location location);

    static void Parse(IReadOnlyCollection<(Uri Uri, string Content)> configurations, DeclarationParser parser)
    {
        Logger.Log($"Parsing {configurations.Count} files ...");

        foreach ((Uri uri, string configuration) in configurations)
        {
            string[] values = configuration.Split('\n');
            for (int line = 0; line < values.Length; line++)
            {
                ReadOnlySpan<char> decl = values[line];
                int i = decl.IndexOf('#');
                if (i != -1) decl = decl[..i];
                decl = decl.Trim();
                if (decl.IsEmpty) continue;

                LanguageCore.Location location = new(new LanguageCore.Position((new SinglePosition(line, 0), new SinglePosition(line, decl.Length - 1)), (-1, -1)), uri);

                i = decl.IndexOf('=');
                if (i == -1)
                {
                    Logger.Warn($"[Configuration]: Invalid configuration\n  at {location}");
                    continue;
                }

                ReadOnlySpan<char> key = decl[..i].Trim();
                ReadOnlySpan<char> value = decl[(i + 1)..].Trim();

                parser.Invoke(key, value, location);
            }
        }
    }

    public static Configuration Parse(IReadOnlyCollection<(Uri Uri, string Content)> configurations)
    {
        List<string> extraDirectories = new();
        List<string> additionalImports = new();
        List<ExternalFunctionStub> externalFunctions = new()
        {
            new ExternalFunctionStub(-1, "stdin", 0, 2),
            new ExternalFunctionStub(-2, "stdout", 2, 0),
        };
        List<ExternalConstant> externalConstants = new();

        Parse(configurations, (key, value, location) =>
        {
            if (key.Equals("searchin", StringComparison.InvariantCultureIgnoreCase))
            {
                extraDirectories.Add(value.ToString());
            }
            else if (key.Equals("include", StringComparison.InvariantCultureIgnoreCase))
            {
                additionalImports.Add(value.ToString());
            }
            else if (key.Equals("externalfunc", StringComparison.InvariantCultureIgnoreCase))
            {
                string? name = null;
                int returnValueSize = 0;
                int parametersSize = 0;
                int i = -1;
                foreach (System.Range argR in value.Split(' '))
                {
                    i++;
                    ReadOnlySpan<char> arg = value[argR].Trim();
                    if (arg.IsEmpty) continue;
                    if (i == 0)
                    {
                        name = arg.ToString();
                    }
                    else if (i == 1)
                    {
                        if (int.TryParse(arg, out int v))
                        {
                            returnValueSize = v;
                        }
                        else
                        {
                            Logger.Warn($"[Configuration]: Invalid integer `{arg}`\n  at {location}");
                        }
                    }
                    else
                    {
                        if (int.TryParse(arg, out int v))
                        {
                            parametersSize += v;
                        }
                        else
                        {
                            Logger.Warn($"[Configuration]: Invalid integer `{arg}`\n  at {location}");
                        }
                    }
                }
                if (name is not null)
                {
                    if (!externalFunctions.Any(v => v.Name == name))
                    {
                        externalFunctions.Add(new ExternalFunctionStub(
                            externalFunctions.GenerateId(name),
                            name,
                            parametersSize,
                            returnValueSize
                        ));
                    }
                    else
                    {
                        Logger.Warn($"[Configuration]: External function {name} already exists\n  at {location}");
                    }
                }
                else
                {
                    Logger.Warn($"[Configuration]: Invalid config\n  at {location}");
                }
            }
            else
            {
                Logger.Warn($"[Configuration]: Invalid configuration key `{key}`\n  at {location}");
            }
        });

        Logger.Log($"Extra directories: \n{string.Join('\n', extraDirectories)}");
        Logger.Log($"Additional imports: \n{string.Join('\n', additionalImports)}");
        Logger.Log($"External functions: \n{string.Join('\n', externalFunctions)}");
        Logger.Log($"External constants: \n{string.Join('\n', externalConstants)}");

        return new Configuration()
        {
            AdditionalImports = additionalImports,
            ExtraDirectories = extraDirectories,
            ExternalFunctions = externalFunctions,
            ExternalConstants = externalConstants,
        };
    }
}
