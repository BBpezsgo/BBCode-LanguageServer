using System.IO;
using LanguageCore;
using LanguageCore.Parser;
using OmniSharpPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;
using OmniSharpRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using Position = LanguageCore.Position;
using OmniSharpDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using System.Diagnostics;

namespace LanguageServer;

public class ServiceException : Exception
{
    public ServiceException() { }
    public ServiceException(string message) : base(message) { }
    public ServiceException(string message, Exception inner) : base(message, inner) { }
}

public static class Extensions
{
    public static IEnumerable<OmniSharpDiagnostic> ToOmniSharp(this IEnumerable<LanguageCore.Diagnostic> hints, string? source = null)
    {
        foreach (LanguageCore.Diagnostic diagnostics in hints)
        { yield return diagnostics.ToOmniSharp(source); }
    }

    public static DiagnosticSeverity ToOmniSharp(this DiagnosticsLevel level) => level switch
    {
        DiagnosticsLevel.Error => DiagnosticSeverity.Error,
        DiagnosticsLevel.Warning => DiagnosticSeverity.Warning,
        DiagnosticsLevel.Information => DiagnosticSeverity.Information,
        DiagnosticsLevel.Hint => DiagnosticSeverity.Hint,
        _ => throw new UnreachableException(),
    };

    [return: NotNullIfNotNull(nameof(diagnostic))]
    public static OmniSharpDiagnostic? ToOmniSharp(this LanguageCore.Diagnostic? diagnostic, string? source = null) => diagnostic is null ? null : new OmniSharpDiagnostic()
    {
        Severity = diagnostic.Level.ToOmniSharp(),
        Range = diagnostic.Position.ToOmniSharp(),
        Message = diagnostic.Message,
        Source = source,
    };

    [return: NotNullIfNotNull(nameof(error))]
    public static OmniSharpDiagnostic? ToOmniSharp(this LanguageException? error, string? source = null) => error is null ? null : new OmniSharpDiagnostic()
    {
        Severity = DiagnosticSeverity.Error,
        Range = error.Position.ToOmniSharp(),
        Message = error.Message,
        Source = source,
    };

    public static DocumentUri? Uri(this FunctionThingDefinition function)
        => function.File is null ? null : (DocumentUri)function.File;

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
