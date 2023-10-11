namespace LanguageServer.Interface
{
    public interface IInterface
    {
        internal event ServiceAppEvent OnInitialize;
        internal event ServiceAppEvent<DocumentItemEventArgs> OnDocumentChanged;
        internal event ServiceAppEvent<DocumentItemEventArgs> OnDocumentOpened;
        internal event ServiceAppEvent<DocumentEventArgs> OnDocumentClosed;
        internal event ServiceAppEvent<DocumentEventArgs> OnDocumentSaved;
        internal event ServiceAppEvent<ConfigEventArgs> OnConfigChanged;
        internal event ServiceAppEvent<DocumentEventArgs, CodeLensInfo[]> OnCodeLens;
        internal event ServiceAppEvent<DocumentPositionContextEventArgs, CompletionInfo[]> OnCompletion;
        internal event ServiceAppEvent<DocumentEventArgs, SymbolInformationInfo[]> OnDocumentSymbols;
        internal event ServiceAppEvent<DocumentPositionEventArgs, SingleOrArray<FilePosition>?> OnGotoDefinition;
        internal event ServiceAppEvent<DocumentPositionEventArgs, HoverInfo> OnHover;
        internal event ServiceAppEvent<FindReferencesEventArgs, FilePosition[]> OnReferences;
        internal event ServiceAppEvent<SignatureHelpEventArgs, SignatureHelpInfo> OnSignatureHelp;
        internal event ServiceAppEvent<DocumentEventArgs, SemanticToken[]> OnSemanticTokensNeed;

        internal void PublishDiagnostics(System.Uri uri, DiagnosticInfo[] diagnostics);
    }
}
