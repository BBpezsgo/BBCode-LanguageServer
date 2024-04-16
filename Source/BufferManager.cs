using System.Collections;
using System.Collections.Concurrent;

namespace LanguageServer.Handlers;

public class Buffers : IReadOnlyDictionary<DocumentUri, string>
{
    readonly ConcurrentDictionary<DocumentUri, string> _buffers = new();

    public string this[DocumentUri key] => _buffers[key];

    public string? Update(DidChangeTextDocumentParams e)
    {
        string? text = e.ContentChanges.FirstOrDefault()?.Text;
        Logger.Log($"Document buffer updated ({text?.Length.ToString() ?? "null"}): \"{e.TextDocument}\"");

        if (text == null)
        { return _buffers.TryGetValue(e.TextDocument.Uri, out string? buffer) ? buffer : null; }

        _buffers[e.TextDocument.Uri] = text;
        return text;
    }

    public string Update(DidOpenTextDocumentParams e)
    {
        Logger.Log($"Document buffer updated ({e.TextDocument.Text.Length}): \"{e.TextDocument}\"");
        _buffers[e.TextDocument.Uri] = e.TextDocument.Text;
        return e.TextDocument.Text;
    }

    public string? Update(DidSaveTextDocumentParams e)
    {
        if (e.Text == null)
        { return _buffers.TryGetValue(e.TextDocument.Uri, out string? buffer) ? buffer : null; }

        Logger.Log($"Document buffer updated ({e.Text.Length}): \"{e.TextDocument}\"");
        _buffers[e.TextDocument.Uri] = e.Text;
        return e.Text;
    }

    public string? GetValue(DocumentUri uri)
        => _buffers.TryGetValue(uri, out string? buffer) ? buffer : null;

    #region Interface Implementations

    IEnumerable<DocumentUri> IReadOnlyDictionary<DocumentUri, string>.Keys => _buffers.Keys;
    IEnumerable<string> IReadOnlyDictionary<DocumentUri, string>.Values => _buffers.Values;
    int IReadOnlyCollection<KeyValuePair<DocumentUri, string>>.Count => _buffers.Count;

    bool IReadOnlyDictionary<DocumentUri, string>.ContainsKey(DocumentUri key) => _buffers.ContainsKey(key);
    bool IReadOnlyDictionary<DocumentUri, string>.TryGetValue(DocumentUri key, [MaybeNullWhen(false)] out string value) => _buffers.TryGetValue(key, out value);

    IEnumerator<KeyValuePair<DocumentUri, string>> IEnumerable<KeyValuePair<DocumentUri, string>>.GetEnumerator() => _buffers.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _buffers.GetEnumerator();

    #endregion
}
