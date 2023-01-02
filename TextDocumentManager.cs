using LanguageServer.Parameters.TextDocument;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BBCodeLanguageServer
{
    internal class TextDocumentManager
    {
        readonly List<TextDocumentItem> all = new();
        internal IReadOnlyList<TextDocumentItem> All => all;

        internal void Add(TextDocumentItem document)
        {
            if (all.Any(x => x.uri == document.uri))
            {
                return;
            }
            all.Add(document);
            OnChanged(document);
        }

        internal void Change(Uri uri, long version, TextDocumentContentChangeEvent[] changeEvents)
        {
            var index = all.FindIndex(x => x.uri == uri);
            if (index < 0)
            {
                return;
            }
            var document = all[index];
            if (document.version >= version)
            {
                return;
            }
            foreach(var ev in changeEvents)
            {
                Apply(document, ev);
            }
            document.version = version;
            OnChanged(document);
        }

        static void Apply(TextDocumentItem document, TextDocumentContentChangeEvent ev)
        {
            if (ev.range != null)
            {
                var startPos = GetPosition(document.text, (int)ev.range.start.line, (int)ev.range.start.character);
                var endPos = GetPosition(document.text, (int)ev.range.end.line, (int)ev.range.end.character);
                var newText = document.text.Substring(0, startPos) + ev.text + document.text.Substring(endPos);
                document.text = newText;
            }
            else
            {
                document.text = ev.text;
            }
        }

        static int GetPosition(string text, int line, int character)
        {
            var pos = 0;
            for (; 0 <= line; line--)
            {
                var lf = text.IndexOf('\n', pos);
                if (lf < 0)
                {
                    return text.Length;
                }
                pos = lf + 1;
            }
            var linefeed = text.IndexOf('\n', pos);
            int max;
            if (linefeed < 0)
            {
                max = text.Length;
            }
            else if (linefeed > 0 && text[linefeed - 1] == '\r')
            {
                max = linefeed - 1;
            }
            else
            {
                max = linefeed;
            }
            pos += character;
            return (pos < max) ? pos : max;
        }

        internal void Remove(Uri uri)
        {
            var index = all.FindIndex(x => x.uri == uri);
            if (index < 0)
            {
                return;
            }
            all.RemoveAt(index);
        }

        internal event EventHandler<TextDocumentChangedEventArgs> Changed;

        protected virtual void OnChanged(TextDocumentItem document)
        {
            Changed?.Invoke(this, new TextDocumentChangedEventArgs(document));
        }

        public class TextDocumentChangedEventArgs : System.EventArgs
        {
            internal readonly TextDocumentItem Document;
            internal TextDocumentChangedEventArgs(TextDocumentItem document) => this.Document = document;
        }
    }
}
