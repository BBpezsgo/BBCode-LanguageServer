using IngameCoding.BBCode;

#pragma warning disable CS0649

namespace BBCodeLanguageServer.Interface
{
    using BBCodeLanguageServer.Interface.SystemExtensions;

    using OmniSharp.Extensions.LanguageServer.Protocol.Models;

    namespace SystemExtensions
    {
        using System;

        internal static class Extensions
        {
            internal static string FirstCharToUpper(this string input) => input switch
            {
                null => throw new ArgumentNullException(nameof(input)),
                "" => throw new ArgumentException($"{nameof(input)} cannot be empty", nameof(input)),
                _ => string.Concat(input[0].ToString().ToUpper(), input.AsSpan(1))
            };

            internal static TValue AddOrGet<TKey, TValue>(this System.Collections.Generic.Dictionary<TKey, TValue> self, TKey key, TValue value) where TValue : class
            {
                if (self.ContainsKey(key))
                { return value; }

                self.Add(key, value);
                return null;
            }
            internal static TValue TryAdd<TKey, TValue>(this System.Collections.Generic.Dictionary<TKey, TValue> self, TKey key, TValue value) where TValue : class
            {
                if (!self.ContainsKey(key))
                { self.Add(key, value); }
                return self[key];
            }

            internal static string Extension(this Uri uri) => uri.AbsolutePath[(uri.AbsolutePath.LastIndexOf(".") + 1)..].ToLower();
        }
    }

    internal delegate TResult ServiceAppEvent<T1, TResult>(T1 e);
    internal delegate void ServiceAppEvent<T>(T e);
    internal delegate void ServiceAppEvent();

    [System.Serializable]
    public class ServiceException : System.Exception
    {
        public ServiceException() { }
        public ServiceException(string message) : base(message) { }
        public ServiceException(string message, System.Exception inner) : base(message, inner) { }
        protected ServiceException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }

    internal static class Extensions
    {
        internal static Range Convert2(this IngameCoding.Core.Range<IngameCoding.Core.SinglePosition> self) => new()
        {
            Start = self.Start.Convert2(),
            End = self.End.Convert2(),
        };

        internal static Position Convert2(this IngameCoding.Core.SinglePosition self) => new()
        {
            Line = System.Math.Max(self.Line - 1, 0),
            Character = System.Math.Max(self.Character - 2, 0),
        };

        internal static Range Convert2(this IngameCoding.Core.Position self) => new()
        {
            Start = self.Start.Convert2(),
            End = self.End.Convert2(),
        };

        internal static T[] Convert2<T>(this IConvertable<T>[] self)
        {
            T[] result = new T[self.Length];
            for (int i = 0; i < self.Length; i++)
            {
                result[i] = self[i].Convert2();
            }
            return result;
        }

        internal static T Convert2<T>(this IConvertable<T> self) => self.Convert2();
    }

    internal interface IConvertable<T>
    {
        internal T Convert2();
    }

    internal class CodeLensInfo : IConvertable<CodeLens>
    {
        internal IngameCoding.Core.Range<IngameCoding.Core.SinglePosition> Range;
        internal string Title;

        internal string CommandName;
        internal string[] CommandArgs;

        internal CodeLensInfo(string title, IngameCoding.Tokenizer.BaseToken range)
        {
            Title = title;
            Range = range.Position;
        }

        internal CodeLensInfo(string title, IngameCoding.Tokenizer.BaseToken range, string Command, params string[] CommandArgs)
        {
            this.Title = title;
            this.Range = range.Position;
            this.CommandName = Command;
            this.CommandArgs = CommandArgs;
        }

        CodeLens IConvertable<CodeLens>.Convert2() => (CommandName == null) ? new()
        {
            Command = new Command()
            {
                Title = Title,
            },
            Range = Range.Convert2(),
        } : new()
        {
            Command = new Command()
            {
                Title = Title,
                Name = CommandName,
                Arguments = new Newtonsoft.Json.Linq.JArray(CommandArgs),
            },
            Range = Range.Convert2(),
        };
    }

    internal class ParameterInfo : IConvertable<ParameterInformation>
    {
        readonly string Label;
        readonly string Documentation;

        public ParameterInfo(string Label, string Documentation)
        {
            this.Label = Label;
            this.Documentation = Documentation;
        }

        ParameterInformation IConvertable<ParameterInformation>.Convert2() => new()
        {
            Label = new ParameterInformationLabel(this.Label),
            Documentation = new StringOrMarkupContent(this.Documentation),
        };
    }

    internal class SignatureInfo : IConvertable<SignatureInformation>
    {
        readonly int ActiveParameter;
        readonly string Label;
        readonly string Documentation;
        readonly ParameterInfo[] Parameters;

        public SignatureInfo(int ActiveParameter, string Label, string Documentation, params ParameterInfo[] Parameters)
        {
            this.ActiveParameter = ActiveParameter;
            this.Label = Label;
            this.Documentation = Documentation;
            this.Parameters = Parameters;
        }

        SignatureInformation IConvertable<SignatureInformation>.Convert2() => new()
        {
            ActiveParameter = this.ActiveParameter,
            Label = this.Label,
            Documentation = new StringOrMarkupContent(this.Documentation),
            Parameters = new Container<ParameterInformation>(this.Parameters.Convert2())
        };
    }

    internal class SignatureHelpInfo : IConvertable<SignatureHelp>
    {
        readonly int ActiveParameter;
        readonly int ActiveSignature;
        readonly SignatureInfo[] Signatures;

        public SignatureHelpInfo(int ActiveParameter, int ActiveSignature, params SignatureInfo[] Signatures)
        {
            this.ActiveParameter = ActiveParameter;
            this.ActiveSignature = ActiveSignature;
            this.Signatures = Signatures;
        }

        SignatureHelp IConvertable<SignatureHelp>.Convert2() => new()
        {
            ActiveParameter = ActiveParameter,
            ActiveSignature = ActiveSignature,
            Signatures = new Container<SignatureInformation>(Signatures.Convert2()),
        };
    }

    internal class CompletionInfo : IConvertable<CompletionItem>
    {
        internal CompletionItemKind Kind;
        internal string Label;
        internal bool Preselect;
        internal string Detail;
        internal bool Deprecated;

        CompletionItem IConvertable<CompletionItem>.Convert2() => new()
        {
            Kind = Kind,
            Label = Label,
            Deprecated = Deprecated,
            Detail = Detail,
            Preselect = Preselect,
        };
    }

    internal class SymbolInformationInfo : IConvertable<SymbolInformationOrDocumentSymbol>
    {
        internal SymbolKind Kind;
        internal string Name;
        internal DocumentLocation Location;

        SymbolInformationOrDocumentSymbol IConvertable<SymbolInformationOrDocumentSymbol>.Convert2() => new(new SymbolInformation()
        {
            Kind = Kind,
            Name = Name,
            Location = new Location()
            {
                Range = Location.Range.Convert2(),
                Uri = Location.Uri,
            },
        });
    }

    internal class HoverContent : IConvertable<MarkedString>
    {
        internal string Lang;
        internal string Text;

        MarkedString IConvertable<MarkedString>.Convert2() => new(Lang, Text);
    }

    internal class HoverInfo : IConvertable<Hover>
    {
        internal HoverContent Content
        {
            set => Contents = new HoverContent[1] { value };
        }
        internal HoverContent[] Contents;
        internal IngameCoding.Core.Range<IngameCoding.Core.SinglePosition> Range;

        Hover IConvertable<Hover>.Convert2() => new()
        {
            Range = Range.Convert2(),
            Contents = new MarkedStringsOrMarkupContent(Contents.Convert2()),
        };
    }

    internal class FilePosition : IConvertable<LocationOrLocationLink>
    {
        /// <summary>
        /// Span of the origin of this link.<br/><br/>
        /// Used as the underlined span for mouse interaction. Defaults to the word range at the mouse position.
        /// </summary>
        internal IngameCoding.Core.Range<IngameCoding.Core.SinglePosition>? OriginRange;
        /// <summary>
        /// The full target range of this link. If the target for example is a symbol
        /// then target range is the range enclosing this symbol not including
        /// leading/trailing whitespace but everything else like comments.
        /// This information is typically used to highlight the range in the editor.
        /// </summary>
        internal IngameCoding.Core.Range<IngameCoding.Core.SinglePosition> TargetRange;
        /// <summary>
        /// The target resource identifier of this link.
        /// </summary>
        internal System.Uri TargetUri;

        public FilePosition(IngameCoding.Core.Range<IngameCoding.Core.SinglePosition> targetRange, System.Uri targetUri)
        {
            TargetRange = targetRange;
            TargetUri = targetUri;
            OriginRange = null;
        }

        public FilePosition(IngameCoding.Core.Range<IngameCoding.Core.SinglePosition> targetRange, string targetUri)
        {
            TargetRange = targetRange;
            TargetUri = new System.Uri(targetUri);
            OriginRange = null;
        }

        public FilePosition(IngameCoding.Core.Range<IngameCoding.Core.SinglePosition> originRange, IngameCoding.Core.Range<IngameCoding.Core.SinglePosition> targetRange, System.Uri targetUri)
        {
            TargetRange = targetRange;
            TargetUri = targetUri;
            OriginRange = originRange;
        }

        LocationOrLocationLink IConvertable<LocationOrLocationLink>.Convert2() => 
            OriginRange.HasValue ?
            new(new LocationLink()
            {
                TargetRange = TargetRange.Convert2(),
                TargetUri = TargetUri,
                OriginSelectionRange = OriginRange.Value.Convert2(),
            })
            :
            new(new Location()
            {
                Range = TargetRange.Convert2(),
                Uri = TargetUri,
            });
    }

    internal class DiagnosticInfo : IConvertable<Diagnostic>
    {
        internal DiagnosticSeverity severity;
        internal IngameCoding.Core.Position range;
        internal string message;
        internal string source;

        Diagnostic IConvertable<Diagnostic>.Convert2() => new()
        {
            Message = message,
            Range = range.Convert2(),
            Severity = (DiagnosticSeverity)(int)severity,
            Source = source,
        };
    }

    internal readonly struct SingleOrArray<T>
    {
        internal readonly T[] v;

        internal SingleOrArray(T Value)
        {
            v = new T[1] { Value };
        }

        internal SingleOrArray(params T[] Values)
        {
            v = new T[Values.Length];
            for (int i = 0; i < Values.Length; i++)
            { v[i] = Values[i]; }
        }
    }

    internal readonly struct DocumentItemEventArgs
    {
        internal readonly DocumentItem Document;

        public DocumentItemEventArgs(DocumentItem document)
        {
            Document = document;
        }
    }

    internal readonly struct DocumentItem
    {
        internal readonly System.Uri Uri;
        internal readonly string LanguageID;
        internal readonly string Content;

        public DocumentItem(System.Uri uri, string code, string languageID)
        {
            Uri = uri;
            Content = code;
            LanguageID = languageID;
        }
    }

    internal readonly struct Document
    {
        internal readonly System.Uri Uri;

        public Document(DocumentItem v)
        {
            this.Uri = v.Uri;
        }

        public Document(TextDocumentIdentifier textDocument)
        {
            this.Uri = textDocument.Uri.ToUri();
        }
    }

    internal readonly struct DocumentEventArgs
    {
        internal readonly Document Document;

        public DocumentEventArgs(ITextDocumentIdentifierParams v)
        {
            this.Document = new Document(v.TextDocument);
        }
        internal DocumentEventArgs(Document v)
        {
            this.Document = v;
        }
    }

    internal readonly struct DocumentPositionEventArgs
    {
        internal readonly IngameCoding.Core.SinglePosition Position;
        internal readonly Document Document;

        public DocumentPositionEventArgs(DefinitionParams v) : this()
        {
            this.Position = new IngameCoding.Core.SinglePosition(v.Position.Line + 1, v.Position.Character + 1);
            this.Position.Character++;
            this.Document = new Document(v.TextDocument);
        }

        public DocumentPositionEventArgs(HoverParams v) : this()
        {
            this.Position = new IngameCoding.Core.SinglePosition(v.Position.Line + 1, v.Position.Character + 1);
            this.Position.Character++;
            this.Document = new Document(v.TextDocument);
        }
    }

    internal readonly struct FindReferencesEventArgs
    {
        internal readonly bool IncludeDeclaration;
        internal readonly IngameCoding.Core.SinglePosition Position;
        internal readonly Document Document;

        public FindReferencesEventArgs(ReferenceParams e) : this()
        {
            this.IncludeDeclaration = e.Context.IncludeDeclaration;
            this.Position = new IngameCoding.Core.SinglePosition(e.Position.Line + 1, e.Position.Character + 1);
            this.Position.Character++;
            this.Document = new Document(e.TextDocument);
        }
    }

    internal readonly struct DocumentPositionContextEventArgs
    {
        internal readonly IngameCoding.Core.SinglePosition Position;
        internal readonly Document Document;
        internal readonly CompletionContext Context;

        public DocumentPositionContextEventArgs(CompletionParams e, string content)
        {
            this.Position = new IngameCoding.Core.SinglePosition(e.Position.Line + 1, e.Position.Character + 1);
            this.Position.Character++;
            this.Document = new Document(new DocumentItem(e.TextDocument.Uri.ToUri(), content, e.TextDocument.Uri.ToUri().Extension()));
            this.Context = e.Context;
        }
    }

    internal readonly struct ConfigEventArgs
    {
        internal readonly dynamic Config;

        public ConfigEventArgs(DidChangeConfigurationParams v)
        {
            this.Config = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(v.Settings.ToString());
        }
    }

    internal readonly struct SignatureHelpEventArgs
    {
        internal readonly IngameCoding.Core.SinglePosition Position;
        internal readonly Document Document;
        internal readonly SignatureHelpContext Context;

        public SignatureHelpEventArgs(SignatureHelpParams v)
        {
            this.Position = new IngameCoding.Core.SinglePosition(v.Position.Line + 1, v.Position.Character + 1);
            this.Document = new Document(v.TextDocument);
            this.Context = v.Context;
        }
    }

    internal readonly struct SemanticToken
    {
        internal readonly int Line;
        internal readonly int Col;
        internal readonly int Length;
        internal readonly SemanticTokenType Type;
        internal readonly SemanticTokenModifier[] Modifier;

        public SemanticToken(Token token, SemanticTokenType type, params SemanticTokenModifier[] modifiers)
        {
            this.Line = token.Position.Start.Line;
            this.Col = token.Position.Start.Character;
            this.Length = token.Content.Length;
            this.Type = type;
            this.Modifier = modifiers;
        }
    }

    internal class DocumentLocation
    {
        public System.Uri Uri;

        public IngameCoding.Core.Position Range;
    }
}
