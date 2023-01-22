using LanguageServer;
using LanguageServer.Parameters;
using LanguageServer.Parameters.General;
using LanguageServer.Parameters.TextDocument;
using LanguageServer.Parameters.Workspace;

namespace BBCodeLanguageServer.Interface
{
    internal class ServiceAppInterface1 : ServiceConnection, IInterface
    {
        System.Uri WorkerSpaceRoot;
        readonly TextDocumentManager Documents;
        int MaxNumberOfProblems = 1000;

        public Connection Connection => this;

        event ServiceAppEvent OnInitialize;
        event ServiceAppEvent<DocumentItemEventArgs> OnDocumentChanged;
        event ServiceAppEvent<DocumentItemEventArgs> OnDocumentOpened;
        event ServiceAppEvent<DocumentEventArgs> OnDocumentClosed;
        event ServiceAppEvent<ConfigEventArgs> OnConfigChanged;
        event ServiceAppEvent<DocumentEventArgs, CodeLensInfo[]> OnCodeLens;
        event ServiceAppEvent<DocumentPositionContextEventArgs, CompletionInfo[]> OnCompletion;
        event ServiceAppEvent<DocumentEventArgs, SymbolInformationInfo[]> OnDocumentSymbols;
        event ServiceAppEvent<DocumentPositionEventArgs, SingleOrArray<FilePosition>?> OnGotoDefinition;
        event ServiceAppEvent<DocumentPositionEventArgs, HoverInfo> OnHover;

        internal ServiceAppInterface1(System.IO.Stream input, System.IO.Stream output) : base(input, output)
        {
            Documents = new TextDocumentManager();
            Documents.Changed += (sender, e) => OnDocumentChanged?.Invoke(new DocumentItemEventArgs(new DocumentItem(e.Document)));
        }

        event ServiceAppEvent IInterface.OnInitialize
        {
            add => OnInitialize += value;
            remove => OnInitialize -= value;
        }
        event ServiceAppEvent<DocumentItemEventArgs> IInterface.OnDocumentChanged
        {
            add => OnDocumentChanged += value;
            remove => OnDocumentChanged -= value;
        }
        event ServiceAppEvent<DocumentItemEventArgs> IInterface.OnDocumentOpened
        {
            add => OnDocumentOpened += value;
            remove => OnDocumentOpened -= value;
        }
        event ServiceAppEvent<DocumentEventArgs> IInterface.OnDocumentClosed
        {
            add => OnDocumentClosed += value;
            remove => OnDocumentClosed -= value;
        }
        event ServiceAppEvent<ConfigEventArgs> IInterface.OnConfigChanged
        {
            add => OnConfigChanged += value;
            remove => OnConfigChanged -= value;
        }
        event ServiceAppEvent<DocumentEventArgs, CodeLensInfo[]> IInterface.OnCodeLens
        {
            add => OnCodeLens += value;
            remove => OnCodeLens -= value;
        }
        event ServiceAppEvent<DocumentPositionContextEventArgs, CompletionInfo[]> IInterface.OnCompletion
        {
            add => OnCompletion += value;
            remove => OnCompletion -= value;
        }
        event ServiceAppEvent<DocumentEventArgs, SymbolInformationInfo[]> IInterface.OnDocumentSymbols
        {
            add => OnDocumentSymbols += value;
            remove => OnDocumentSymbols -= value;
        }
        event ServiceAppEvent<DocumentPositionEventArgs, SingleOrArray<FilePosition>?> IInterface.OnGotoDefinition
        {
            add => OnGotoDefinition += value;
            remove => OnGotoDefinition -= value;
        }
        event ServiceAppEvent<DocumentPositionEventArgs, HoverInfo> IInterface.OnHover
        {
            add => OnHover += value;
            remove => OnHover -= value;
        }
        event ServiceAppEvent<FindReferencesEventArgs, FilePosition[]> IInterface.OnReferences
        {
            add { }
            remove { }
        }

        event ServiceAppEvent<SignatureHelpEventArgs, SignatureHelpInfo> IInterface.OnSignatureHelp
        {
            add
            {
                throw new System.NotImplementedException();
            }

            remove
            {
                throw new System.NotImplementedException();
            }
        }

        protected override VoidResult<ResponseError> Shutdown()
        {
            Logger.Log("Shutdown()");
            System.Threading.Timer timer = null;
            timer = new System.Threading.Timer((_) =>
            {
                timer.Dispose();
                System.Environment.Exit(0);
            }, null, 1000, System.Threading.Timeout.Infinite);
            return VoidResult<ResponseError>.Success();
        }

        protected override void DidOpenTextDocument(DidOpenTextDocumentParams e)
        {
            Logger.Log($"DidOpenTextDocument({e.textDocument.uri})");
            Documents.Add(e.textDocument);

            OnDocumentOpened?.Invoke(new DocumentItemEventArgs(new DocumentItem(e.textDocument)));
        }

        protected override void DidChangeTextDocument(DidChangeTextDocumentParams e)
        {
            Logger.Log($"DidChangeTextDocument({e.textDocument.uri})");
            Documents.Change(e.textDocument.uri, e.textDocument.version, e.contentChanges);
        }

        protected override void DidCloseTextDocument(DidCloseTextDocumentParams e)
        {
            Logger.Log($"DidCloseTextDocument({e.textDocument.uri})");
            Documents.Remove(e.textDocument.uri);

            OnDocumentClosed?.Invoke(new DocumentEventArgs(e.textDocument));
        }

        protected override void DidChangeConfiguration(DidChangeConfigurationParams e)
        {
            Logger.Log("DidChangeConfiguration()");
            MaxNumberOfProblems = e?.settings?.languageServerExample?.maxNumberOfProblems ?? MaxNumberOfProblems;
            Logger.Log($"maxNumberOfProblems is set to {MaxNumberOfProblems}.");

            foreach (var document in Documents.All)
            {
                //TODO: this
                /*
                if (Docs.ContainsKey(document.uri.ToString()))
                {
                    Docs[document.uri.ToString()].Validate(new Document(document));
                }
                */
            }

            OnConfigChanged?.Invoke(new ConfigEventArgs(e));
        }

        protected override void DidChangeWatchedFiles(DidChangeWatchedFilesParams e)
        {
            Logger.Log("DidChangeWatchedFiles()");
        }

        protected override Result<CodeLens[], ResponseError> CodeLens(CodeLensParams e)
        {
            Logger.Log($"CodeLens()");

            CodeLensInfo[] result = null;
            try
            {
                result = OnCodeLens?.Invoke(new DocumentEventArgs(e.textDocument));
                if (result == null)
                { return Result<CodeLens[], ResponseError>.Success(System.Array.Empty<CodeLens>()); }
                return Result<CodeLens[], ResponseError>.Success(result.Convert1<CodeLens>());
            }
            catch (ServiceException error)
            {
                return Result<CodeLens[], ResponseError>.Error(new ResponseError()
                { message = error.Message, });
            }
        }

        protected override Result<CompletionResult, ResponseError> Completion(CompletionParams e)
        {
            Logger.Log($"Completion()");

            CompletionInfo[] result = null;
            try
            {
                result = OnCompletion?.Invoke(new DocumentPositionContextEventArgs(e));
                if (result == null)
                { return Result<CompletionResult, ResponseError>.Success(new CompletionResult(System.Array.Empty<CompletionItem>())); }
                return Result<CompletionResult, ResponseError>.Success(result.Convert1());
            }
            catch (ServiceException error)
            {
                return Result<CompletionResult, ResponseError>.Error(new ResponseError()
                { message = error.Message, });
            }
        }

        protected override Result<DocumentSymbolResult, ResponseError> DocumentSymbols(DocumentSymbolParams e)
        {
            Logger.Log($"DocumentSymbols()");

            try
            {
                SymbolInformationInfo[] result = OnDocumentSymbols?.Invoke(new DocumentEventArgs(e.textDocument));
                if (result == null)
                { return Result<DocumentSymbolResult, ResponseError>.Success(new DocumentSymbolResult(System.Array.Empty<DocumentSymbol>())); }
                return Result<DocumentSymbolResult, ResponseError>.Success(new DocumentSymbolResult(result.Convert1()));
            }
            catch (ServiceException error)
            {
                return Result<DocumentSymbolResult, ResponseError>.Error(new ResponseError()
                { message = error.Message, });
            }
        }

        protected override Result<LocationSingleOrArray, ResponseError> GotoDefinition(TextDocumentPositionParams e)
        {
            Logger.Log($"GotoDefinition()");

            try
            {
                SingleOrArray<FilePosition>? result = OnGotoDefinition?.Invoke(new DocumentPositionEventArgs(e));
                if (!result.HasValue)
                { return Result<LocationSingleOrArray, ResponseError>.Success(new LocationSingleOrArray(System.Array.Empty<Location>())); }
                return Result<LocationSingleOrArray, ResponseError>.Success(result.Value.Convert());
            }
            catch (ServiceException error)
            {
                return Result<LocationSingleOrArray, ResponseError>.Error(new ResponseError()
                { message = error.Message, });
            }
        }

        protected override Result<Hover, ResponseError> Hover(TextDocumentPositionParams e)
        {
            Logger.Log($"Hover({e.position.line}:{e.position.character})");

            try
            {
                HoverInfo result = OnHover?.Invoke(new DocumentPositionEventArgs(e));
                if (result == null)
                { return Result<Hover, ResponseError>.Success(new Hover()); }
                return Result<Hover, ResponseError>.Success(result.Convert1());
            }
            catch (ServiceException error)
            {
                return Result<Hover, ResponseError>.Error(new ResponseError()
                { message = error.Message, });
            }
        }

        protected override Result<ColorInformation[], ResponseError> DocumentColor(DocumentColorParams @params)
        {
            return Result<ColorInformation[], ResponseError>.Success(new ColorInformation[1]
            {
                new ColorInformation()
                {
                    range = new Range()
                    {
                        start = new Position()
                        {
                            line = 0,
                            character = 0,
                        },
                        end = new Position()
                        {
                            line = 0,
                            character = 5,
                        },
                    },
                    color = new Color(255, 0, 0, 255),
                },
            });
        }

        protected override Result<InitializeResult, ResponseError<InitializeErrorData>> Initialize(InitializeParams e)
        {
            Logger.Log($"Initialize()");

            WorkerSpaceRoot = e.rootUri;
            var result = new InitializeResult
            {
                capabilities = new ServerCapabilities
                {
                    textDocumentSync = TextDocumentSyncKind.Full,
                    completionProvider = new CompletionOptions
                    {
                        resolveProvider = false,
                    },
                    hoverProvider = true,
                    codeLensProvider = new CodeLensOptions()
                    {
                        resolveProvider = false,
                    },
                    definitionProvider = true,
                    documentSymbolProvider = true,
                    colorProvider = new ColorProviderOptionsOrBoolean(true),
                },
            };

            OnInitialize?.Invoke();

            return Result<InitializeResult, ResponseError<InitializeErrorData>>.Success(result);
        }

        void IInterface.PublishDiagnostics(System.Uri uri, DiagnosticInfo[] diagnostics)
        {
            Proxy.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
            {
                uri = uri,
                diagnostics = diagnostics.Convert1(),
            });
        }
    }
}
