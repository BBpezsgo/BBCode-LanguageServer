using System.IO;
using LanguageCore;
using LanguageCore.Parser;
using OmniSharpPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;
using OmniSharpRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using Position = LanguageCore.Position;

namespace LanguageServer;

public class ServiceException : Exception
{
    public ServiceException() { }
    public ServiceException(string message) : base(message) { }
    public ServiceException(string message, Exception inner) : base(message, inner) { }
}

public static class Extensions
{
    public static DocumentUri? Uri(this FunctionThingDefinition function)
        => function.FilePath is null ? null : (DocumentUri)function.FilePath;

    public static TValue EnsureExistence<TKey, TValue>(this IDictionary<TKey, TValue> self, TKey key, TValue value) where TKey : notnull
    {
        if (self.TryGetValue(key, out TValue? _value))
        {
            return _value;
        }
        self.Add(key, value);
        return value;
    }

    public static string Extension(this DocumentUri uri)
        => Path.GetExtension(uri.ToUri().AbsolutePath).TrimStart('.').ToLowerInvariant();

    public static string Extension(this TextDocumentIdentifier uri)
        => Path.GetExtension(uri.Uri.ToUri().AbsolutePath).TrimStart('.').ToLowerInvariant();

    public static OmniSharpRange ToOmniSharp(this MutableRange<SinglePosition> self) => new()
    {
        Start = self.Start.ToOmniSharp(),
        End = self.End.ToOmniSharp(),
    };

    public static OmniSharpRange ToOmniSharp(this Range<SinglePosition> self) => new()
    {
        Start = self.Start.ToOmniSharp(),
        End = self.End.ToOmniSharp(),
    };

    public static OmniSharpPosition ToOmniSharp(this SinglePosition self) => new()
    {
        Line = self.Line,
        Character = self.Character,
    };

    public static OmniSharpRange ToOmniSharp(this Position self) => new()
    {
        Start = self.Range.Start.ToOmniSharp(),
        End = self.Range.End.ToOmniSharp(),
    };

    public static MutableRange<SinglePosition> ToCool(this OmniSharpRange self) => new()
    {
        Start = self.Start.ToCool(),
        End = self.End.ToCool(),
    };

    public static SinglePosition ToCool(this OmniSharpPosition self) => new()
    {
        Line = self.Line,
        Character = self.Character,
    };
}
