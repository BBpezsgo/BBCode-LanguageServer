using System.Collections.Concurrent;
using Microsoft.Language.Xml;

namespace LanguageServer.Handlers;

public class Buffers
{
    readonly ConcurrentDictionary<DocumentUri, StringBuffer> _buffers = new();

    public StringBuffer Update(DidChangeTextDocumentParams e)
    {
        string? text = e.ContentChanges.FirstOrDefault()?.Text;
        Logger.Log($"Document \"{e.TextDocument}\" buffer updated ({(text is not null ? text.Length.ToString() : "null")})");
        StringBuffer buffer = new(text);
        return _buffers.AddOrUpdate(e.TextDocument.Uri, buffer, (k, v) => buffer);
    }

    public StringBuffer Update(DidOpenTextDocumentParams e)
    {
        Logger.Log($"Document \"{e.TextDocument}\" buffer updated ({e.TextDocument.Text.Length})");
        StringBuffer buffer = new(e.TextDocument.Text);
        return _buffers.AddOrUpdate(e.TextDocument.Uri, buffer, (k, v) => buffer);
    }

    public StringBuffer? Update(DidSaveTextDocumentParams e)
    {
        if (e.Text == null) return Get(e.TextDocument.Uri);
        Logger.Log($"Document \"{e.TextDocument}\" buffer updated ({e.Text.Length})");
        StringBuffer buffer = new(e.Text);
        return _buffers.AddOrUpdate(e.TextDocument.Uri, buffer, (k, v) => buffer);
    }

    public StringBuffer? Get(DocumentUri uri)
        => _buffers.TryGetValue(uri, out StringBuffer? buffer) ? buffer : null;

    public string? GetText(DocumentUri uri)
    {
        StringBuffer? buffer = Get(uri);
        if (buffer == null) return null;
        return buffer.GetText(0, buffer.Length);
    }
}
