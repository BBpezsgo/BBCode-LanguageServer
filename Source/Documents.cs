using System.Collections;
using LanguageCore;
using LanguageServer.DocumentManagers;

namespace LanguageServer;

sealed class Documents : ISourceProviderSync, ISourceQueryProvider, IVersionProvider, IEnumerable<DocumentBase>
{
    readonly List<DocumentBase> _documents;

    public Documents()
    {
        _documents = new List<DocumentBase>();
    }

    /// <exception cref="ServiceException"/>
    public static DocumentBase GenerateDocument(DocumentUri uri, string? content, string languageId, Documents documentInterface) => languageId switch
    {
        LanguageConstants.LanguageId => new DocumentBBLang(uri, content, languageId, documentInterface),
        _ => throw new ServiceException($"Unknown language \"{languageId}\"")
    };

    public bool TryGet(DocumentUri uri, [NotNullWhen(true)] out DocumentBase? document)
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

    public DocumentBase? Get(TextDocumentIdentifier documentId)
        => TryGet(documentId.Uri, out DocumentBase? document) ? document : null;

    /// <exception cref="ServiceException"/>
    public DocumentBase GetOrCreate(TextDocumentIdentifier documentId, string? content = null)
    {
        RemoveDuplicates();

        if (TryGet(documentId.Uri, out DocumentBase? document))
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
        else
        {
            string extension = documentId.Extension();

            Logger.Log($"Document registered: \"{documentId.Uri}\"");
            document = GenerateDocument(documentId.Uri, content, extension, this);
            _documents.Add(document);

            return document;
        }
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

            foreach (DocumentBase document in _documents)
            {
                if (document.Uri != query) continue;
                if (document.Content is null)
                {
                    Logger.Log($"[BBLang Compiler] Document provided by client (no content) ({document.DocumentUri})");
                    return SourceProviderResultSync.Error(query, "Document not loaded");
                }
                else
                {
                    Logger.Log($"[BBLang Compiler] Document provided by client (size: {document.Content.Length} bytes) ({document.DocumentUri})");
                    return SourceProviderResultSync.Success(query, document.Content);
                }
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

    public bool TryGetVersion(Uri uri, out ulong version)
    {
        version = default;

        foreach (DocumentBase document in _documents)
        {
            if (document.Uri != uri) continue;
            if (document.Content is null || !document.Version.HasValue) return false;
            version = (ulong)document.Version.Value;
        }

        return false;
    }

    public IEnumerator<DocumentBase> GetEnumerator() => _documents.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _documents.GetEnumerator();
}
