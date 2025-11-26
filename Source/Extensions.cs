using System.IO;
using LanguageCore;
using OmniSharpPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;
using OmniSharpRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using Position = LanguageCore.Position;
using OmniSharpDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using System.Diagnostics;

namespace LanguageServer;

static class Extensions
{
    public static DiagnosticSeverity ToOmniSharp(this DiagnosticsLevel level) => level switch
    {
        DiagnosticsLevel.Error => DiagnosticSeverity.Error,
        DiagnosticsLevel.Warning => DiagnosticSeverity.Warning,
        DiagnosticsLevel.Information => DiagnosticSeverity.Information,
        DiagnosticsLevel.Hint => DiagnosticSeverity.Hint,
        DiagnosticsLevel.OptimizationNotice => DiagnosticSeverity.Information,
        DiagnosticsLevel.FailedOptimization => DiagnosticSeverity.Information,
        _ => throw new UnreachableException(),
    };

    [return: NotNullIfNotNull(nameof(diagnostic))]
    static string? GetFullMessage(LanguageCore.Diagnostic? diagnostic, int indent)
    {
        if (diagnostic is null)return null;
        string result = $"{diagnostic.Message}";
        foreach (LanguageCore.Diagnostic item in diagnostic.SubErrors)
        {
            result += $"\n{new string(' ', indent)} -> {GetFullMessage(item, indent + 2)}";
        }
        return result;
    }

    [return: NotNullIfNotNull(nameof(diagnostic))]
    public static OmniSharpDiagnostic? ToOmniSharp(this LanguageCore.Diagnostic? diagnostic, string? source = null) => diagnostic is null ? null : new OmniSharpDiagnostic()
    {
        Severity = diagnostic.Level.ToOmniSharp(),
        Range = diagnostic.Position.ToOmniSharp(),
        Message = GetFullMessage(diagnostic, 0),
        Source = source,
    };

    public static string Extension(this DocumentUri uri)
        => Path.GetExtension(uri.ToUri().AbsolutePath).TrimStart('.').ToLowerInvariant();

    public static string Extension(this TextDocumentIdentifier uri)
        => Path.GetExtension(uri.Uri.ToUri().AbsolutePath).TrimStart('.').ToLowerInvariant();

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
