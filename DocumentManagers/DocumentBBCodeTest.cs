using BBCodeLanguageServer.Interface;

using IngameCoding.BBCode;
using IngameCoding.BBCode.Compiler;
using IngameCoding.Core;

using System;
using System.Collections.Generic;
using System.Linq;

namespace BBCodeLanguageServer.DocumentManagers
{
    internal class DocumentBBCodeTest : IDocument
    {
        readonly DocumentInterface App;
        string Text;
        string Url;
        string LanguageId;

        Token[] Tokens;
        IngameCoding.Tester.Parser.ParserResult? ParserResult;

        public DocumentBBCodeTest(DocumentItem document, DocumentInterface app)
        {
            App = app;
            OnChanged(document);
        }

        public void OnChanged(DocumentItem e)
        {
            Text = e.Content;
            Url = e.Uri.ToString();
            LanguageId = e.LanguageID;

            Validate(new Document(e));
        }

        void Validate(Document e)
        {
            var diagnostics = new List<DiagnosticInfo>();
            System.IO.FileInfo file = null;
            string path = System.Net.WebUtility.UrlDecode(e.Uri.AbsolutePath);

            Logger.Log($"Validate({path})");

            if (e.Uri.Scheme == "file")
            {
                if (System.IO.File.Exists(path))
                {
                    file = new System.IO.FileInfo(path);
                }
                else
                {
                    Logger.Log($"{path} not found");
                }
            }
            else
            {
                return;
            }

            static DiagnosticInfo DiagnostizeException(IngameCoding.Errors.Exception exception, string source)
            {
                var range = exception.Position;

                Logger.Log($"{source} Error: {exception.MessageAll}\n  at {range.ToMinString()}");

                return new DiagnosticInfo
                {
                    severity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Error,
                    range = range,
                    message = exception.Message,
                    source = source,
                };
            }
            static DiagnosticInfo DiagnostizeError(IngameCoding.Errors.Error error, string source)
            {
                var range = error.Position;

                Logger.Log($"{source} Error: {error.MessageAll}\n  at {range.ToMinString()}");

                return new DiagnosticInfo
                {
                    severity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Error,
                    range = range,
                    message = error.Message,
                    source = source,
                };
            }

            ParserResult = null;
            Tokens = null;

            Tokenizer tokenizer = new(TokenizerSettings.Default);
            try
            {
                (Tokens, _) = tokenizer.Parse(Text);
            }
            catch (IngameCoding.Errors.Exception error)
            {
                diagnostics.Add(DiagnostizeException(error, "Tokenizer"));
                return;
            }

            List<IngameCoding.Errors.Warning> warnings = new();

            IngameCoding.Tester.Parser.Parser parser = new();

            try
            {
                ParserResult = parser.Parse(Tokens, warnings);
            }
            catch (IngameCoding.Errors.Exception error)
            {
                diagnostics.Add(DiagnostizeException(error, "Parser"));
                return;
            }

            if (parser.Errors.Count > 0)
            {
                for (int i = 0; i < parser.Errors.Count; i++)
                {
                    diagnostics.Add(DiagnostizeError(parser.Errors[i], "Parser"));
                }
                return;
            }

            App.Interface.PublishDiagnostics(e.Uri, diagnostics.ToArray());
        }

        CompletionInfo[] IDocument.Completion(DocumentPositionContextEventArgs e)
        {
            Logger.Log($"Completion()");

            List<CompletionInfo> result = new();

            result.Add(new CompletionInfo()
            {
                Label = "test",
                Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Keyword,
            });

            return result.ToArray();
        }

        CodeLensInfo[] IDocument.CodeLens(DocumentEventArgs e)
        {
            string path = System.Net.WebUtility.UrlDecode(e.Document.Uri.AbsolutePath);
            List<CodeLensInfo> result = new();

            if (!ParserResult.HasValue) return Array.Empty<CodeLensInfo>();

            for (int i = 0; i < ParserResult.Value.TestDefinitions.Length; i++)
            {
                var testDefinition = ParserResult.Value.TestDefinitions[i];
                result.Add(new CodeLensInfo($"$(run) Run Test", testDefinition.Keyword, $"bbc.runBbcTestFileSpecificTest", $"\"{path}\"", $"\"{testDefinition.Name.ToString()}\""));
            }

            return result.ToArray();
        }

        SymbolInformationInfo[] IDocument.Symbols(DocumentEventArgs e)
        {
            if (!ParserResult.HasValue) return Array.Empty<SymbolInformationInfo>();

            List<SymbolInformationInfo> result = new();

            for (int i = 0; i < ParserResult.Value.TestDefinitions.Length; i++)
            {
                var testDefinition = ParserResult.Value.TestDefinitions[i];
                result.Add(new SymbolInformationInfo()
                {
                    Name = $"Test {testDefinition.Name.text}",
                    Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.File,
                    Location = new DocumentLocation()
                    {
                        Range = new Position(testDefinition.Keyword, testDefinition.Name, testDefinition.LeftBracket, testDefinition.RightBracket),
                        Uri = e.Document.Uri,
                    },
                });
            }

            return result.ToArray();
        }

        HoverInfo IDocument.Hover(DocumentPositionEventArgs e) => null;
        SingleOrArray<FilePosition>? IDocument.GotoDefinition(DocumentPositionEventArgs e) => null;
        FilePosition[] IDocument.References(DocumentEventArgs e) => Array.Empty<FilePosition>();
        SignatureHelpInfo IDocument.SignatureHelp(SignatureHelpEventArgs e) => null;
        SemanticToken[] IDocument.GetSemanticTokens(DocumentEventArgs e) => Array.Empty<SemanticToken>();
    }
}
