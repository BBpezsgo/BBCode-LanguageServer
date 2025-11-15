using System.Collections;
using LanguageCore;
using LanguageServer.DocumentManagers;

namespace LanguageServer;

class Documents : ISourceProviderSync, ISourceQueryProvider, IEnumerable<DocumentHandler>
{
    readonly List<DocumentHandler> _documents;

    public Documents()
    {
        _documents = new List<DocumentHandler>();
    }

    /// <exception cref="ServiceException"/>
    public static DocumentHandler GenerateDocument(DocumentUri uri, string content, string languageId, Documents documentInterface) => languageId switch
    {
        LanguageConstants.LanguageId => new DocumentBBLang(uri, content, languageId, documentInterface),
        _ => throw new ServiceException($"Unknown language \"{languageId}\"")
    };

    public bool TryGet(DocumentUri uri, [NotNullWhen(true)] out DocumentHandler? document)
    {
        for (int i = 0; i < _documents.Count; i++)
        {
            if (_documents[i].Uri == uri)
            {
                document = _documents[i];
                return true;
            }
        }
        document = null;
        return false;
    }

    public void Remove(TextDocumentIdentifier documentId)
    {
        Logger.Log($"Unregister document: \"{documentId.Uri}\"");
        for (int i = _documents.Count - 1; i >= 0; i--)
        {
            if (_documents[i].Uri == documentId.Uri)
            {
                Logger.Log($"Document unregistered: \"{documentId.Uri}\"");
                _documents.RemoveAt(i);
            }
        }
    }

    public void RemoveDuplicates()
    {
        for (int i = _documents.Count - 1; i >= 0; i--)
        {
            for (int j = _documents.Count - 1; j >= i + 1; j--)
            {
                if (_documents[i].Uri == _documents[j].Uri)
                {
                    Logger.Log($"Unregister duplicated document: \"{_documents[i].Uri}\"");
                    _documents.RemoveAt(i);
                }
            }
        }
    }

    public DocumentHandler? Get(TextDocumentIdentifier documentId)
        => TryGet(documentId.Uri, out DocumentHandler? document) ? document : null;

    /// <exception cref="ServiceException"/>
    public DocumentHandler GetOrCreate(TextDocumentIdentifier documentId, string? content = null)
    {
        RemoveDuplicates();

        if (TryGet(documentId.Uri, out DocumentHandler? document))
        { return document; }

        Logger.Log($"Register document: \"{documentId.Uri}\"");

        if (documentId.Uri.Scheme == "file")
        {
            if (content is null)
            {
                string path = System.Net.WebUtility.UrlDecode(documentId.Uri.ToUri().AbsolutePath);
                if (!System.IO.File.Exists(path))
                { throw new ServiceException($"File not found: \"{path}\""); }
                content = System.IO.File.ReadAllText(path);
            }

            string extension = documentId.Extension();

            Logger.Log($"Document registered: \"{documentId.Uri}\"");
            document = GenerateDocument(documentId.Uri, content, extension, this);
            _documents.Add(document);

            return document;
        }

        throw new ServiceException($"Unknown document uri scheme: \"{documentId.Uri.Scheme}\"");
    }

    public IEnumerable<Uri> GetQuery(string requestedFile, Uri? currentFile)
    {
        if (!requestedFile.EndsWith($".{LanguageConstants.LanguageExtension}", StringComparison.Ordinal))
        {
            requestedFile += $".{LanguageConstants.LanguageExtension}";
        }

        if (Uri.TryCreate(currentFile, requestedFile, out Uri? uri))
        {
            yield return uri;
        }
    }

    public SourceProviderResultSync TryLoad(string requestedFile, Uri? currentFile)
    {
        Uri? lastUri = null;

        foreach (Uri query in GetQuery(requestedFile, currentFile))
        {
            lastUri = query;

            foreach (DocumentHandler document in _documents)
            {
                if (document.Uri != query) continue;
                Logger.Log($"[BBLang Compiler] Using document provided by client (size: {document.Content.Length})");
                return SourceProviderResultSync.Success(query, document.Content);
            }
        }

        if (lastUri is not null)
        {
            return SourceProviderResultSync.NotFound(lastUri!);
        }
        else
        {
            return SourceProviderResultSync.NextHandler();
        }
    }

    public IEnumerator<DocumentHandler> GetEnumerator() => _documents.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _documents.GetEnumerator();
}
