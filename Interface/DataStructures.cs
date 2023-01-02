using LanguageServer.Parameters;
using LanguageServer.Parameters.TextDocument;
using LanguageServer.Parameters.Workspace;

namespace BBCodeLanguageServer.Interface
{
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
        internal static Range Convert1(this IngameCoding.Core.Position position) => new IngameCoding.Core.Range<IngameCoding.Core.SinglePosition>(position.Start, position.End).Convert1();
        internal static Range Convert1(params IngameCoding.Tokenizer.BaseToken[] tokens)
        {
            if (tokens.Length == 0) throw new System.ArgumentException("Argument 'tokens's length can not be 0");

            Range result = null;

            for (int i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];

                if (token == null) continue;
                if (result == null) { result = token.Position.Convert1(); continue; }

                var range = token.Position.Convert1();

                if (result.start.line > range.start.line)
                {
                    result.start.line = range.start.line;
                    result.start.character = range.start.character;
                }
                else if (result.start.character > range.start.character)
                {
                    result.start.character = range.start.character;
                }

                if (result.end.line < range.end.line)
                {
                    result.end.line = range.end.line;
                    result.end.character = range.end.character;
                }
                else if (result.end.character < range.end.character)
                {
                    result.end.character = range.end.character;
                }
            }

            if (result == null) throw new System.Exception("All tokens are null");

            return result;
        }

        internal static Range Convert1(this IngameCoding.Core.Range<IngameCoding.Core.SinglePosition> self) => new()
        {
            start = self.Start.Convert1(),
            end = self.End.Convert1(),
        };
        internal static OmniSharp.Extensions.LanguageServer.Protocol.Models.Range Convert2(this IngameCoding.Core.Range<IngameCoding.Core.SinglePosition> self) => new()
        {
            Start = self.Start.Convert2(),
            End = self.End.Convert2(),
        };

        internal static Position Convert1(this IngameCoding.Core.SinglePosition self) => new()
        {
            line = self.Line - 1,
            character = System.Math.Max(self.Character - 2, 0),
        };
        internal static OmniSharp.Extensions.LanguageServer.Protocol.Models.Position Convert2(this IngameCoding.Core.SinglePosition self) => new()
        {
            Line = self.Line - 1,
            Character = System.Math.Max(self.Character - 2, 0),
        };

        internal static IngameCoding.Core.Range<IngameCoding.Core.SinglePosition> Convert1(this Range self) => new()
        {
            Start = self.start.Convert1(),
            End = self.end.Convert1(),
        };

        internal static IngameCoding.Core.SinglePosition Convert1(this Position self) => new()
        {
            Line = (int)self.line + 1,
            Character = (int)self.character + 1,
        };

        internal static LocationSingleOrArray Convert(this SingleOrArray<FilePosition> self)
        {
            if (self.v.Length == 1)
            {
                return new LocationSingleOrArray(self.v[0].Convert1());
            }
            return new LocationSingleOrArray(self.v.Convert1());
        }

        internal static OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind Convert(this CompletionItemKind self) => self switch
        {
            CompletionItemKind.Text => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Text,
            CompletionItemKind.Method => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Method,
            CompletionItemKind.Function => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Function,
            CompletionItemKind.Constructor => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Constructor,
            CompletionItemKind.Field => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Field,
            CompletionItemKind.Variable => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Variable,
            CompletionItemKind.Class => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Class,
            CompletionItemKind.Interface => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Interface,
            CompletionItemKind.Module => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Module,
            CompletionItemKind.Property => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Property,
            CompletionItemKind.Unit => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Unit,
            CompletionItemKind.Value => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Value,
            CompletionItemKind.Enum => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Enum,
            CompletionItemKind.Keyword => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Keyword,
            CompletionItemKind.Snippet => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Snippet,
            CompletionItemKind.Color => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Color,
            CompletionItemKind.File => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.File,
            CompletionItemKind.Reference => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Reference,
            CompletionItemKind.Folder => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Folder,
            CompletionItemKind.EnumMember => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.EnumMember,
            CompletionItemKind.Constant => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Constant,
            CompletionItemKind.Struct => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Struct,
            CompletionItemKind.Event => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Event,
            CompletionItemKind.Operator => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Operator,
            CompletionItemKind.TypeParameter => OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.TypeParameter,
            _ => throw new System.NotImplementedException(),
        };

        internal static OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind Convert(this SymbolKind self) => self switch
        {
            SymbolKind.Array => OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Array,
            SymbolKind.File => OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.File,
            SymbolKind.Module => OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Module,
            SymbolKind.Namespace => OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Namespace,
            SymbolKind.Package => OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Package,
            SymbolKind.Class => OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Class,
            SymbolKind.Method => OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Method,
            SymbolKind.Property => OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Property,
            SymbolKind.Field => OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Field,
            SymbolKind.Constructor => OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Constructor,
            SymbolKind.Enum => OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Enum,
            SymbolKind.Interface => OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Interface,
            SymbolKind.Function => OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Function,
            SymbolKind.Variable => OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Variable,
            SymbolKind.Constant => OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Constant,
            SymbolKind.String => OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.String,
            SymbolKind.Number => OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Number,
            SymbolKind.Boolean => OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Boolean,
            SymbolKind.Object => OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Object,
            SymbolKind.Key => OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Key,
            SymbolKind.Null => OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Null,
            SymbolKind.EnumMember => OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.EnumMember,
            SymbolKind.Struct => OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Struct,
            SymbolKind.Event => OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Event,
            SymbolKind.Operator => OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Operator,
            SymbolKind.TypeParameter => OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.TypeParameter,
            _ => throw new System.NotImplementedException(),
        };

        internal static T[] Convert1<T>(this IConvertable1<T>[] self)
        {
            T[] result = new T[self.Length];
            for (int i = 0; i < self.Length; i++)
            {
                result[i] = self[i].Convert1();
            }
            return result;
        }
        internal static T[] Convert2<T>(this IConvertable2<T>[] self)
        {
            T[] result = new T[self.Length];
            for (int i = 0; i < self.Length; i++)
            {
                result[i] = self[i].Convert2();
            }
            return result;
        }

        internal static T Convert1<T>(this IConvertable1<T> self) => self.Convert1();
        internal static T Convert2<T>(this IConvertable2<T> self) => self.Convert2();
    }

    internal interface IConvertable1<T>
    {
        internal T Convert1();
    }

    internal interface IConvertable2<T>
    {
        internal T Convert2();
    }

    internal class CodeLensInfo : IConvertable1<CodeLens>, IConvertable2<OmniSharp.Extensions.LanguageServer.Protocol.Models.CodeLens>
    {
        internal IngameCoding.Core.Range<IngameCoding.Core.SinglePosition> Range;
        internal string Title;

        internal CodeLensInfo(string title, IngameCoding.Tokenizer.BaseToken range)
        {
            Title = title;
            Range = range.Position;
        }

        CodeLens IConvertable1<CodeLens>.Convert1() => new()
        {
            range = Range.Convert1(),
            command = new Command()
            {
                title = Title,
            },
        };

        OmniSharp.Extensions.LanguageServer.Protocol.Models.CodeLens IConvertable2<OmniSharp.Extensions.LanguageServer.Protocol.Models.CodeLens>.Convert2() => new()
        {
            Command = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Command()
            {
                Title = Title,
            },
            Range = Range.Convert2(),
        };
    }

    internal class CompletionInfo : IConvertable1<CompletionItem>, IConvertable2<OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItem>
    {
        internal CompletionItemKind Kind;
        internal string Label;

        public CompletionItem Convert1() => new()
        {
            kind = Kind,
            label = Label,
        };

        OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItem IConvertable2<OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItem>.Convert2() => new()
        {
            Kind = Kind.Convert(),
            Label = Label,
        };
    }

    internal class SymbolInformationInfo : IConvertable1<SymbolInformation>, IConvertable2<OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolInformationOrDocumentSymbol>
    {
        internal SymbolKind Kind;
        internal string Name;
        internal Location Location;

        public SymbolInformation Convert1() => new()
        {
            kind = Kind,
            name = Name,
            location = Location,
        };

        OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolInformationOrDocumentSymbol IConvertable2<OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolInformationOrDocumentSymbol>.Convert2() => new(new OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolInformation()
        {
            Kind = Kind.Convert(),
            Name = Name,
            Location = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Location()
            {
                Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range()
                {
                    Start = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Position((int)Location.range.start.line, (int)Location.range.start.character),
                    End = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Position((int)Location.range.end.line, (int)Location.range.end.character),
                },
                Uri = Location.uri,
            },
        });
    }

    internal class HoverContent : IConvertable1<MarkedString>, IConvertable2<OmniSharp.Extensions.LanguageServer.Protocol.Models.MarkedString>
    {
        internal string Lang;
        internal string Text;

        MarkedString IConvertable1<MarkedString>.Convert1() => new()
        {
            language = Lang,
            value = Text,
        };

        OmniSharp.Extensions.LanguageServer.Protocol.Models.MarkedString IConvertable2<OmniSharp.Extensions.LanguageServer.Protocol.Models.MarkedString>.Convert2() => new(Lang, Text);
    }

    internal class HoverInfo : IConvertable1<Hover>, IConvertable2<OmniSharp.Extensions.LanguageServer.Protocol.Models.Hover>
    {
        internal HoverContent Content
        {
            set => Contents = new HoverContent[1] { value };
        }
        internal HoverContent[] Contents;
        internal IngameCoding.Core.Range<IngameCoding.Core.SinglePosition> Range;

        public Hover Convert1() => new()
        {
            range = Range.Convert1(),
            contents = Contents.Convert1(),
        };

        OmniSharp.Extensions.LanguageServer.Protocol.Models.Hover IConvertable2<OmniSharp.Extensions.LanguageServer.Protocol.Models.Hover>.Convert2() => new()
        {
            Range = Range.Convert2(),
            Contents = new OmniSharp.Extensions.LanguageServer.Protocol.Models.MarkedStringsOrMarkupContent(Contents.Convert2()),
        };
    }

    internal class FilePosition : IConvertable1<Location>, IConvertable2<OmniSharp.Extensions.LanguageServer.Protocol.Models.LocationOrLocationLink>
    {
        internal IngameCoding.Core.Range<IngameCoding.Core.SinglePosition> Range;
        internal System.Uri Uri;

        public FilePosition(IngameCoding.Core.Range<IngameCoding.Core.SinglePosition> range, System.Uri uri)
        {
            Range = range;
            Uri = uri;
        }

        public Location Convert1() => new()
        {
            range = Range.Convert1(),
            uri = Uri,
        };

        OmniSharp.Extensions.LanguageServer.Protocol.Models.LocationOrLocationLink IConvertable2<OmniSharp.Extensions.LanguageServer.Protocol.Models.LocationOrLocationLink>.Convert2() => new(new OmniSharp.Extensions.LanguageServer.Protocol.Models.Location()
        {
            Range = Range.Convert2(),
            Uri = Uri,
        });
    }

    internal class DiagnosticInfo : IConvertable1<Diagnostic>, IConvertable2<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>
    {
        internal DiagnosticSeverity severity;
        internal Range range;
        internal string message;
        internal string source;

        public Diagnostic Convert1() => new()
        {
            message = message,
            range = range,
            severity = severity,
            source = source,
        };

        OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic IConvertable2<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>.Convert2() => new()
        {
            Message = message,
            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range()
            {
                Start = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Position((int)range.start.line, (int)range.start.character),
                End = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Position((int)range.end.line, (int)range.end.character),
            },
            Severity = (OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity)(int)severity,
            Source = source,
        };
    }

    internal struct SingleOrArray<T>
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

    internal struct DocumentItemEventArgs
    {
        internal readonly DocumentItem Document;

        public DocumentItemEventArgs(DocumentItem document)
        {
            Document = document;
        }
    }

    internal struct DocumentItem
    {
        internal readonly System.Uri Uri;
        internal readonly string LanguageID;
        internal readonly string Content;

        public DocumentItem(System.Uri uri, string code, string languageID) : this()
        {
            Uri = uri;
            Content = code;
            LanguageID = languageID;
        }

        internal DocumentItem(TextDocumentItem v)
        {
            this.Uri = v.uri;
            this.LanguageID = v.languageId;
            this.Content = v.text;
        }
    }

    internal struct Document
    {
        internal readonly System.Uri Uri;

        public Document(TextDocumentItem v)
        {
            this.Uri = v.uri;
        }

        public Document(DocumentItem v)
        {
            this.Uri = v.Uri;
        }

        public Document(OmniSharp.Extensions.LanguageServer.Protocol.Models.TextDocumentIdentifier textDocument)
        {
            this.Uri = textDocument.Uri.ToUri();
        }

        internal Document(TextDocumentIdentifier v)
        {
            this.Uri = v.uri;
        }
    }

    internal struct DocumentEventArgs
    {
        internal readonly Document Document;

        internal DocumentEventArgs(TextDocumentIdentifier v)
        {
            this.Document = new Document(v);
        }

        internal DocumentEventArgs(Document v)
        {
            this.Document = v;
        }
    }

    internal struct DocumentPositionEventArgs
    {
        internal readonly IngameCoding.Core.SinglePosition Position;
        internal readonly Document Document;

        public DocumentPositionEventArgs(OmniSharp.Extensions.LanguageServer.Protocol.Models.DefinitionParams v) : this()
        {
            this.Position = new IngameCoding.Core.SinglePosition(v.Position.Line + 1, v.Position.Character + 1);
            this.Position.Character++;
            this.Document = new Document(v.TextDocument);
        }

        public DocumentPositionEventArgs(OmniSharp.Extensions.LanguageServer.Protocol.Models.HoverParams v) : this()
        {
            this.Position = new IngameCoding.Core.SinglePosition(v.Position.Line + 1, v.Position.Character + 1);
            this.Position.Character++;
            this.Document = new Document(v.TextDocument);
        }

        internal DocumentPositionEventArgs(TextDocumentPositionParams v)
        {
            this.Position = v.position.Convert1();
            this.Position.Character++;
            this.Document = new Document(v.textDocument);
        }
    }

    internal struct FindReferencesEventArgs
    {
        internal readonly bool IncludeDeclaration;
        internal readonly IngameCoding.Core.SinglePosition Position;
        internal readonly Document Document;

        public FindReferencesEventArgs(OmniSharp.Extensions.LanguageServer.Protocol.Models.ReferenceParams e) : this()
        {
            this.IncludeDeclaration = e.Context.IncludeDeclaration;
            this.Position = new IngameCoding.Core.SinglePosition(e.Position.Line + 1, e.Position.Character + 1);
            this.Position.Character++;
            this.Document = new Document(e.TextDocument);
        }
    }

    internal struct DocumentPositionContextEventArgs
    {
        internal readonly IngameCoding.Core.SinglePosition Position;
        internal readonly Document Document;
        internal readonly CompletionContext Context;

        public DocumentPositionContextEventArgs(OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionParams e, string content) : this()
        {
            this.Position = new IngameCoding.Core.SinglePosition(e.Position.Line + 1, e.Position.Character + 1);
            this.Position.Character++;
            this.Document = new Document(new DocumentItem(e.TextDocument.Uri.ToUri(), content, "bbc"));
        }

        internal DocumentPositionContextEventArgs(CompletionParams v)
        {
            this.Position = v.position.Convert1();
            this.Position.Character++;
            this.Document = new Document(v.textDocument);
            this.Context = v.context;
        }
    }

    internal struct ConfigEventArgs
    {
        internal readonly dynamic Config;

        public ConfigEventArgs(OmniSharp.Extensions.LanguageServer.Protocol.Models.DidChangeConfigurationParams v)
        {
            this.Config = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(v.Settings.ToString());
        }

        internal ConfigEventArgs(DidChangeConfigurationParams v)
        {
            this.Config = new Newtonsoft.Json.Linq.JObject(v.settings);
        }
    }
}
