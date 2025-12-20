using System.IO;
using LanguageCore;
using LanguageCore.Workspaces;

namespace LanguageServer;

static class ConfigurationManager
{
    public static IReadOnlyList<(Uri Uri, string Content)> Search(Uri currentDocument, Documents documents)
    {
        Uri currentUri = currentDocument;
        List<(Uri Uri, string Content)> result = new();
        EndlessCheck endlessCheck = new(50);
        while (currentUri.LocalPath != "/")
        {
            if (endlessCheck.Step()) break;
            Uri uri = new(currentUri, $"./{Configuration.FileName}");
            if (documents.TryGet(uri, out DocumentBase? document))
            {
                if (document.Content is null) continue;
                result.Add((uri, document.Content));
            }
            else if (uri.Scheme == "file" && File.Exists(uri.LocalPath))
            {
                result.Add((uri, File.ReadAllText(uri.LocalPath)));
            }
            currentUri = new Uri(currentUri, "..");
        }
        return result;
    }
}
