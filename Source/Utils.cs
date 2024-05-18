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
    public static IEnumerable<Diagnostic> ToOmniSharp(this IEnumerable<Hint> hints, string? source = null)
    {
        foreach (Hint hint in hints)
        { yield return hint.ToOmniSharp(source); }
    }

    public static IEnumerable<Diagnostic> ToOmniSharp(this IEnumerable<Information> informations, string? source = null)
    {
        foreach (Information information in informations)
        { yield return information.ToOmniSharp(source); }
    }

    public static IEnumerable<Diagnostic> ToOmniSharp(this IEnumerable<Warning> warnings, string? source = null)
    {
        foreach (Warning warning in warnings)
        { yield return warning.ToOmniSharp(source); }
    }

    public static IEnumerable<Diagnostic> ToOmniSharp(this IEnumerable<LanguageError> errors, string? source = null)
    {
        foreach (LanguageError error in errors)
        { yield return error.ToOmniSharp(source); }
    }

    [return: NotNullIfNotNull(nameof(warning))]
    public static Diagnostic? ToOmniSharp(this Warning? warning, string? source = null) => warning is null ? null : new Diagnostic()
    {
        Severity = DiagnosticSeverity.Warning,
        Range = warning.Position.ToOmniSharp(),
        Message = warning.Message,
        Source = source,
    };

    [return: NotNullIfNotNull(nameof(information))]
    public static Diagnostic? ToOmniSharp(this Information? information, string? source = null) => information is null ? null : new Diagnostic()
    {
        Severity = DiagnosticSeverity.Information,
        Range = information.Position.ToOmniSharp(),
        Message = information.Message,
        Source = source,
    };

    [return: NotNullIfNotNull(nameof(hint))]
    public static Diagnostic? ToOmniSharp(this Hint? hint, string? source = null) => hint is null ? null : new Diagnostic()
    {
        Severity = DiagnosticSeverity.Hint,
        Range = hint.Position.ToOmniSharp(),
        Message = hint.Message,
        Source = source,
    };

    [return: NotNullIfNotNull(nameof(error))]
    public static Diagnostic? ToOmniSharp(this LanguageError? error, string? source = null) => error is null ? null : new Diagnostic()
    {
        Severity = DiagnosticSeverity.Error,
        Range = error.Position.ToOmniSharp(),
        Message = error.Message,
        Source = source,
    };

    [return: NotNullIfNotNull(nameof(error))]
    public static Diagnostic? ToOmniSharp(this LanguageException? error, string? source = null) => error is null ? null : new Diagnostic()
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
