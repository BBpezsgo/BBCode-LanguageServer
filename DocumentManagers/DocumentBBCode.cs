using BBCodeLanguageServer.Interface;
using BBCodeLanguageServer.Interface.SystemExtensions;

using IngameCoding.BBCode;
using IngameCoding.BBCode.Compiler;
using IngameCoding.BBCode.Parser;
using IngameCoding.BBCode.Parser.Statements;
using IngameCoding.Core;

using System;
using System.Collections.Generic;
using System.Linq;

namespace BBCodeLanguageServer.DocumentManagers
{
    internal class DocumentBBCode : IDocument
    {
        readonly DocumentInterface App;
        string Text;
        string Url;
        string LanguageId;

        static readonly bool DEBUG = false;

        protected AnalysisResult AnalysisResult;
        AnalysisResult LastSuccessAnalysisResult;
        bool HaveSuccesAlanysisResult;

        AnalysisResult BestAnalysisResult => HaveSuccesAlanysisResult ? LastSuccessAnalysisResult : AnalysisResult;

        public DocumentBBCode(DocumentItem document, DocumentInterface app)
        {
            App = app;
            AnalysisResult = AnalysisResult.Empty();
            OnChanged(document);
        }

        public void OnChanged(DocumentItem e)
        {
            Text = e.Content;
            Url = e.Uri.ToString();
            LanguageId = e.LanguageID;

            Validate(new Document(e));
        }

        Token GetTokenAt(SinglePosition position)
        {
            if (AnalysisResult.Tokens == null) return null;
            if (AnalysisResult.Tokens.Length == 0) return null;

            Logger.Log($"Search token at {position}");

            foreach (var token in AnalysisResult.Tokens)
            {
                var contains = token.Position.Contains(position);
                // Logger.Log($"{position.ToMinString()} in {token.Position.ToMinString()} ? {contains}");
                if (contains)
                {
                    return token;
                }
            }

            return null;
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
            static DiagnosticInfo DiagnostizeHint(IngameCoding.Errors.Hint hint, string source)
            {
                var range = hint.Position;

                Logger.Log($"{source}: {hint.MessageAll}\n  at {range.ToMinString()}");

                return new DiagnosticInfo
                {
                    severity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Hint,
                    range = range,
                    message = hint.Message,
                    source = source,
                };
            }
            static DiagnosticInfo DiagnostizeInformation(IngameCoding.Errors.Information information, string source)
            {
                var range = information.Position;

                Logger.Log(
                    $"{source}: {information.MessageAll}\n" +
                    $"  at {range.ToMinString()}\n" +
                    $"  in {information.File}"
                    );

                return new DiagnosticInfo
                {
                    severity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Information,
                    range = range,
                    message = information.Message,
                    source = source,
                };
            }

            AnalysisResult = Analysis.Analyze(Text, file, path);
            Logger.Log($"Check file paths...");
            AnalysisResult.CheckFilePaths(notSetMessage => Logger.Log($"{notSetMessage}"));

            for (int i = 0; i < AnalysisResult.TokenizerInicodeChars.Length; i++)
            {
                var unicode = AnalysisResult.TokenizerInicodeChars[i];
                Logger.Log($"Unicode token {unicode.Position.ToMinString()}");
            }

            Logger.Log($"File references: {AnalysisResult.FileReferences.Length}");

            if (!AnalysisResult.TokenizingSuccess)
            {
                diagnostics.Add(DiagnostizeException(AnalysisResult.TokenizerFatalError, "Tokenizer"));
            }
            else if (!AnalysisResult.ParsingSuccess)
            {
                if (AnalysisResult.ParserFatalError != null)
                {
                    if (AnalysisResult.ParserFatalError.File == path || AnalysisResult.ParserFatalError.File == null)
                    {
                        diagnostics.Add(DiagnostizeException(AnalysisResult.ParserFatalError, "Parser"));
                    }
                    else
                    {
                        Logger.Log($"Parser Error: {AnalysisResult.ParserFatalError.MessageAll}\n in {AnalysisResult.ParserFatalError.File}");
                    }
                }

                for (int i = 0; i < AnalysisResult.ParserErrors.Length; i++)
                {
                    var error = AnalysisResult.ParserErrors[i];
                    if (error.File != path && error.File != null) continue;
                    diagnostics.Add(DiagnostizeError(error, "Parser"));
                }
            }
            else if (!AnalysisResult.CompilingSuccess)
            {
                if (AnalysisResult.CompilerFatalError != null)
                {
                    if (AnalysisResult.CompilerFatalError.File == path || AnalysisResult.CompilerFatalError.File == null)
                    {
                        diagnostics.Add(DiagnostizeException(AnalysisResult.CompilerFatalError, "Compiler"));
                    }
                }

                for (int i = 0; i < AnalysisResult.CompilerErrors.Length; i++)
                {
                    var error = AnalysisResult.CompilerErrors[i];
                    if (error.File != path && error.File != null) continue;

                    if (error.Message.StartsWith("Builtin function '") && error.Message.EndsWith("' not found"))
                    {
                        Logger.Log($"Compiler Warning: {AnalysisResult.CompilerErrors[i]}");
                        diagnostics.Add(new DiagnosticInfo
                        {
                            severity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Warning,
                            range = error.Position,
                            message = error.Message,
                            source = "Compiler",
                        });
                        continue;
                    }

                    diagnostics.Add(DiagnostizeError(error, "Compiler"));
                }
            }
            else
            {
                LastSuccessAnalysisResult = AnalysisResult;
                HaveSuccesAlanysisResult = true;
            }

            for (int i = 0; i < AnalysisResult.Warnings.Length; i++)
            {
                var warning = AnalysisResult.Warnings[i];

                if (warning.File != path && warning.File != null) continue;

                diagnostics.Add(new DiagnosticInfo
                {
                    severity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Warning,
                    range = warning.Position,
                    message = warning.Message,
                });
            }

            for (int i = 0; i < AnalysisResult.TokenizerWarnings.Length; i++)
            {
                var warning = AnalysisResult.TokenizerWarnings[i];
                Logger.Log($"Tokenizer warning: {warning.MessageAll}");
                if (warning.File != path) continue;

                diagnostics.Add(new DiagnosticInfo
                {
                    severity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Warning,
                    range = warning.Position,
                    message = warning.Message,
                    source = "Tokenizer",
                });
            }

            for (int i = 0; i < AnalysisResult.Hints.Length; i++)
            {
                var hint = AnalysisResult.Hints[i];

                if (hint.File != path && hint.File != null) continue;

                diagnostics.Add(DiagnostizeHint(hint, "Compiler"));
            }

            for (int i = 0; i < AnalysisResult.Informations.Length; i++)
            {
                var information = AnalysisResult.Informations[i];

                if (information.File != path && information.File != null) continue;

                diagnostics.Add(DiagnostizeInformation(information, "Compiler"));
            }

            for (int i = diagnostics.Count - 1; i >= 0; i--) if (diagnostics[i].range.ToMinString() == "0:0") diagnostics.RemoveAt(i);

            App.Interface.PublishDiagnostics(e.Uri, diagnostics.ToArray());
            // AnalysisResult.FileReferences = Analysis.FileReferences(file, path);
        }

        CompletionInfo[] IDocument.Completion(DocumentPositionContextEventArgs e)
        {
            Logger.Log($"Completion()");

            List<CompletionInfo> result = new();

            if (BestAnalysisResult.ParsingSuccess && BestAnalysisResult.Parsed)
            {
                foreach (var function in BestAnalysisResult.ParserResult.Functions)
                {
                    result.Add(new CompletionInfo()
                    {
                        Label = function.Name.text,
                        Detail = function.ReadableID(),
                        Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Function,
                    });
                }

                foreach (var @struct in BestAnalysisResult.ParserResult.Structs)
                {
                    result.Add(new CompletionInfo()
                    {
                        Label = @struct.Value.Name.text,
                        Detail = @struct.Value.FullName,
                        Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Struct,
                    });
                }

                foreach (var @class in BestAnalysisResult.ParserResult.Classes)
                {
                    result.Add(new CompletionInfo()
                    {
                        Label = @class.Value.Name.text,
                        Detail = @class.Value.FullName,
                        Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Class,
                    });
                }

                foreach (var variable in BestAnalysisResult.ParserResult.GlobalVariables)
                {
                    result.Add(new CompletionInfo()
                    {
                        Label = variable.VariableName.text,
                        Detail = $"(global var) {variable.Type.text} {variable.VariableName.text}",
                        Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Variable,
                    });
                }
            }

            result.Add(new CompletionInfo()
            {
                Label = "int",
                Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Keyword,
            });
            result.Add(new CompletionInfo()
            {
                Label = "float",
                Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Keyword,
            });
            result.Add(new CompletionInfo()
            {
                Label = "bool",
                Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Keyword,
            });
            result.Add(new CompletionInfo()
            {
                Label = "string",
                Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Keyword,
            });
            result.Add(new CompletionInfo()
            {
                Label = "new",
                Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Keyword,
            });
            result.Add(new CompletionInfo()
            {
                Label = "void",
                Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Keyword,
            });
            result.Add(new CompletionInfo()
            {
                Label = "struct",
                Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Keyword,
            });
            result.Add(new CompletionInfo()
            {
                Label = "namespace",
                Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Keyword,
            });
            result.Add(new CompletionInfo()
            {
                Label = "var",
                Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Keyword,
            });

            result.Add(new CompletionInfo()
            {
                Label = "return",
                Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Keyword,
            });
            result.Add(new CompletionInfo()
            {
                Label = "if",
                Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Keyword,
            });
            result.Add(new CompletionInfo()
            {
                Label = "elseif",
                Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Keyword,
            });
            result.Add(new CompletionInfo()
            {
                Label = "else",
                Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Keyword,
            });
            result.Add(new CompletionInfo()
            {
                Label = "for",
                Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Keyword,
            });
            result.Add(new CompletionInfo()
            {
                Label = "while",
                Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Keyword,
            });

            result.Add(new CompletionInfo()
            {
                Label = "true",
                Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Keyword,
            });
            result.Add(new CompletionInfo()
            {
                Label = "false",
                Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind.Keyword,
            });

            return result.ToArray();
        }

        HoverInfo IDocument.Hover(DocumentPositionEventArgs e)
        {
            static HoverContent InfoFunctionDefinition(FunctionDefinition funcDef, bool IsDefinition)
            {
                var text = $"{funcDef.Type} {funcDef.FullName}(";

                bool addComma = false;

                for (int i = 0; i < funcDef.Parameters.Count; i++)
                {
                    if (addComma) text += ", ";

                    if (funcDef.Parameters[i].withThisKeyword)
                    { text = $"{funcDef.Type} {funcDef.Parameters[i].type}.{funcDef.FullName}("; continue; }

                    text += $"{funcDef.Parameters[i].type} {funcDef.Parameters[i].name}";
                    addComma = true;
                }

                text += ")";

                return new HoverContent()
                {
                    Lang = "csharp",
                    Text = text,
                };
            }
            static HoverContent InfoBuiltinFunction(TokenAnalysis.RefBuiltinFunction funcDef, bool IsDefinition)
            {
                var text = $"{funcDef.ReturnType} {funcDef.Name}(";

                bool addComma = false;

                for (int i = 0; i < funcDef.ParameterTypes.Length; i++)
                {
                    if (addComma) text += ", ";

                    text += $"{funcDef.ParameterTypes[i]} {funcDef.ParameterNames[i]}";
                    addComma = true;
                }

                text += ")";

                return new HoverContent()
                {
                    Lang = "csharp",
                    Text = text,
                };
            }
            static HoverContent InfoBuiltinMethod(TokenAnalysis.RefBuiltinMethod funcDef, bool IsDefinition)
            {
                var text = $"{funcDef.ReturnType} {funcDef.PrevType}.{funcDef.Name}(";

                bool addComma = false;

                for (int i = 0; i < funcDef.ParameterTypes.Length; i++)
                {
                    if (addComma) text += ", ";

                    text += $"{funcDef.ParameterTypes[i]} {funcDef.ParameterNames[i]}";
                    addComma = true;
                }

                text += ")";

                return new HoverContent()
                {
                    Lang = "csharp",
                    Text = text,
                };
            }
            static HoverContent InfoStructDefinition(StructDefinition structDef)
            {
                return new HoverContent()
                {
                    Lang = "csharp",
                    Text = $"struct {structDef.FullName}",
                };
            }
            static bool InfoReachedUnit(Token token, out HoverContent result)
            {
                if (!DEBUG) { result = null; return false; }
                if (!token.Analysis.CompilerReached)
                {
                    result = new HoverContent()
                    {
                        Lang = "text",
                        Text = token.Analysis.ParserReached ? "Not Compiled" : "Not Parsed",
                    };
                    return true;
                }
                result = null;
                return false;
            }

            SinglePosition pos = e.Position;

            Logger.Log($"Hover({pos.ToMinString()})");

            List<HoverContent> result = new();

            for (int i = 0; i < AnalysisResult.TokenizerInicodeChars.Length; i++)
            {
                var unicode = AnalysisResult.TokenizerInicodeChars[i];
                if (!unicode.Position.Contains(pos)) continue;
                Logger.Log($"Hover: Unicode token found");

                return new HoverInfo()
                {
                    Range = unicode.Position,
                    Contents = new HoverContent[] { new HoverContent()
                    {
                        Lang = "text",
                        Text = $"Unicode character '{unicode.Text}'"
                    }},
                };
            }

            if (AnalysisResult.ParserFatalError == null && AnalysisResult.Parsed)
            {
                {
                    Range<SinglePosition> range = new();

                    StatementFinder.GetAllStatement(AnalysisResult.ParserResult, statement =>
                    {
                        if (statement is Statement_FunctionCall functionCall)
                        {
                            if (!functionCall.Identifier.Position.Contains(pos)) return false;
                            if (functionCall.Identifier.text == "return") return false;

                            Logger.Log($"Hover: Func. call found");

                            range = functionCall.Identifier.Position;

                            if (InfoReachedUnit(functionCall.Identifier, out var reachedUnit))
                            { result.Add(reachedUnit); }

                            Logger.Log($"{functionCall.Identifier.Analysis}");

                            if (functionCall.Identifier.Analysis.Reference is TokenAnalysis.RefFunction refFunction)
                            {
                                result.Add(InfoFunctionDefinition(refFunction.Definition, false));
                                return true;
                            }
                            else if (functionCall.Identifier.Analysis.Reference is TokenAnalysis.RefBuiltinFunction refBuiltinFunction)
                            {
                                result.Add(InfoBuiltinFunction(refBuiltinFunction, false));
                                return true;
                            }
                            else if (functionCall.Identifier.Analysis.Reference is TokenAnalysis.RefBuiltinMethod refBuiltinMethod)
                            {
                                result.Add(InfoBuiltinMethod(refBuiltinMethod, false));
                                return true;
                            }
                            else
                            {
                                string newContentText = "";

                                newContentText += $"? {functionCall.TargetNamespacePathPrefix}{functionCall.Identifier}(";

                                bool addComma = false;
                                int paramIndex = 0;
                                foreach (var param in functionCall.Parameters)
                                {
                                    paramIndex++;
                                    if (addComma) newContentText += $", ";
                                    if (param is Statement_Literal literalParam)
                                    {
                                        switch (literalParam.Type.typeName)
                                        {
                                            case BuiltinType.AUTO:
                                                newContentText += $"var p{paramIndex}";
                                                break;
                                            case BuiltinType.INT:
                                                newContentText += $"int p{paramIndex}";
                                                break;
                                            case BuiltinType.FLOAT:
                                                newContentText += $"float p{paramIndex}";
                                                break;
                                            case BuiltinType.VOID:
                                                newContentText += $"void p{paramIndex}";
                                                break;
                                            case BuiltinType.STRING:
                                                newContentText += $"string p{paramIndex}";
                                                break;
                                            case BuiltinType.BOOLEAN:
                                                newContentText += $"bool p{paramIndex}";
                                                break;
                                            case BuiltinType.STRUCT:
                                                newContentText += $"struct p{paramIndex}";
                                                break;
                                            case BuiltinType.ANY:
                                                newContentText += $"any p{paramIndex}";
                                                break;
                                        }
                                    }
                                    else
                                    {
                                        newContentText += $"? p{paramIndex}";
                                    }
                                    addComma = true;
                                }

                                newContentText += $") // Function Call";

                                result.Add(new HoverContent()
                                {
                                    Lang = "csharp",
                                    Text = newContentText,
                                });
                            }

                            return true;
                        }
                        else if (statement is Statement_Literal literal)
                        {
                            if (literal.ValueToken == null) return false;
                            if (!literal.ValueToken.Position.Contains(pos)) return false;

                            Logger.Log($"Hover: Literal found");

                            range = literal.ValueToken.Position;

                            if (InfoReachedUnit(literal.ValueToken, out var reachedUnit))
                            { result.Add(reachedUnit); }

                            var info = Hover(literal.ValueToken);

                            if (info != null)
                            {
                                if (literal.Type.typeName == BuiltinType.STRING)
                                { range.End.Character++; }
                                result.Add(info);
                            }

                            return true;
                        }
                        else if (statement is Statement_Variable variable)
                        {
                            if (!variable.VariableName.Position.Contains(pos)) return false;

                            Logger.Log($"Hover: Variable found {variable.VariableName.Analysis}");

                            range = variable.VariableName.Position;

                            if (InfoReachedUnit(variable.VariableName, out var reachedUnit))
                            { result.Add(reachedUnit); }

                            if (variable.VariableName.Analysis.Reference is TokenAnalysis.RefVariable refVariable)
                            {
                                var def = refVariable.Declaration;
                                result.Add(new()
                                {
                                    Lang = "csharp",
                                    Text = $"{def.Type.ToString()} {def.VariableName.text}; // {(refVariable.IsGlobal ? "Global Variable" : "Local Variable")}",
                                });
                            }
                            else if (variable.VariableName.Analysis.Reference is TokenAnalysis.RefParameter refParameter)
                            {
                                result.Add(new()
                                {
                                    Lang = "csharp",
                                    Text = $"{refParameter.Type.ToString()} {variable.VariableName.text}; // Parameter",
                                });
                            }
                            else
                            {
                                result.Add(new()
                                {
                                    Lang = "csharp",
                                    Text = $"var {variable.VariableName.text};",
                                });
                            }

                            return true;
                        }
                        else if (statement is Statement_NewVariable newVariable)
                        {
                            if (!newVariable.VariableName.Position.Contains(pos)) return false;

                            Logger.Log($"Hover: NewVariable found {newVariable.VariableName.Analysis}");

                            range = newVariable.VariableName.Position;

                            if (InfoReachedUnit(newVariable.VariableName, out var reachedUnit))
                            { result.Add(reachedUnit); }

                            if (newVariable.VariableName.Analysis.Reference is TokenAnalysis.RefVariable refVariable)
                            {
                                var def = refVariable.Declaration;
                                result.Add(new()
                                {
                                    Lang = "csharp",
                                    Text = $"{def.Type.ToString()} {def.VariableName.text}; // {(refVariable.IsGlobal ? "Global Variable" : "Local Variable")}",
                                });
                            }
                            else
                            {
                                result.Add(new()
                                {
                                    Lang = "csharp",
                                    Text = $"{newVariable.Type.ToString()} {newVariable.VariableName.text}; // Variable",
                                });
                            }

                            return true;
                        }
                        else if (statement is Statement_Field field)
                        {
                            if (!field.FieldName.Position.Contains(pos)) return false;

                            Logger.Log($"Hover: Field found {field.FieldName.Analysis}");

                            range = field.FieldName.Position;

                            if (InfoReachedUnit(field.FieldName, out var reachedUnit))
                            { result.Add(reachedUnit); }

                            if (field.FieldName.Analysis.Reference is TokenAnalysis.RefField refField)
                            {
                                if (refField.StructName != null)
                                {
                                    result.Add(new()
                                    {
                                        Lang = "csharp",
                                        Text = $"struct {refField.StructName}\n{{\n  {refField.Type} {refField.Name}; // Field\n}}",
                                    });
                                }
                                else
                                {
                                    result.Add(new()
                                    {
                                        Lang = "csharp",
                                        Text = $"{refField.Type} {field.FieldName.text}; // Field",
                                    });
                                }
                            }
                            else
                            {
                                result.Add(new()
                                {
                                    Lang = "csharp",
                                    Text = $"var {field.FieldName.text}; // Field",
                                });
                            }

                            return true;
                        }
                        else if (statement is Statement_NewInstance newStruct)
                        {
                            /*
                            if (!newStruct.structName.Position.Contains(pos)) return false;

                            result.Add(new MarkedString()
                            {
                                language = "text",
                                value = "Statement: New Struct",
                            });

                            range = GetRange(newStruct.structName);
                            range.start.character--;
                            range.end.character--;

                            result.Add(new()
                            {
                                language = "csharp",
                                value = $"new {newStruct.TargetNamespacePathPrefix}{newStruct.structName}",
                            });

                            return true;
                            */
                        }

                        return false;
                    });

                    if (result.Count > 0 && !range.IsUnset())
                    {
                        // if (range.IsUnset())
                        // { throw new ServiceException($"Hover range is null"); }
                        return new HoverInfo()
                        {
                            Range = range,
                            Contents = result.ToArray(),
                        };
                    }
                }

                foreach (var funcDef in AnalysisResult.ParserResult.Functions)
                {
                    if (!funcDef.Name.Position.Contains(pos))
                    {
                        foreach (var paramDef in funcDef.Parameters)
                        {
                            if (!paramDef.name.Position.Contains(pos)) continue;
                            Logger.Log($"Hover: Param. def. found");

                            if (InfoReachedUnit(funcDef.Name, out var reachedUnit_))
                            { result.Add(reachedUnit_); }

                            result.Add(new()
                            {
                                Lang = "csharp",
                                Text = $"{paramDef.type.text} {paramDef.name.text}; // Parameter",
                            });

                            return new HoverInfo()
                            {
                                Range = paramDef.name.Position,
                                Contents = result.ToArray(),
                            };
                        }
                        continue;
                    }

                    Logger.Log($"Hover: Func. def. found");

                    if (InfoReachedUnit(funcDef.Name, out var reachedUnit))
                    { result.Add(reachedUnit); }

                    result.Add(InfoFunctionDefinition(funcDef, true));

                    return new HoverInfo()
                    {
                        Range = funcDef.Name.Position,
                        Contents = result.ToArray(),
                    };
                }

                foreach (var pair in AnalysisResult.ParserResult.Structs)
                {
                    var structDef = pair.Value;
                    if (!structDef.Name.Position.Contains(pos))
                    {
                        foreach (var field in structDef.Fields)
                        {
                            if (!field.name.Position.Contains(pos)) continue;

                            Logger.Log($"Hover: Struct field dec. found, {field.name.Position}");

                            if (InfoReachedUnit(field.name, out var reachedUnit3))
                            { result.Add(reachedUnit3); }

                            result.Add(new()
                            {
                                Lang = "csharp",
                                Text = $"struct {structDef.Name.text}\n{{\n  {field.type} {field.name.text}; // Field\n}}",
                            });

                            return new HoverInfo()
                            {
                                Range = field.name.Position,
                                Contents = result.ToArray(),
                            };
                        }
                        continue;
                    }

                    Logger.Log($"Hover: Struct def. found, {structDef.Name.Position}");

                    if (InfoReachedUnit(structDef.Name, out var reachedUnit))
                    { result.Add(reachedUnit); }

                    result.Add(InfoStructDefinition(structDef));

                    return new HoverInfo()
                    {
                        Range = structDef.Name.Position,
                        Contents = result.ToArray(),
                    };
                }

                foreach (var pair in AnalysisResult.ParserResult.Classes)
                {
                    var classDef = pair.Value;
                    if (!classDef.Name.Position.Contains(pos))
                    {
                        foreach (var field in classDef.Fields)
                        {
                            if (!field.name.Position.Contains(pos)) continue;

                            Logger.Log($"Hover: Class field dec. found, {field.name.Position}");

                            if (InfoReachedUnit(field.name, out var reachedUnit3))
                            { result.Add(reachedUnit3); }

                            result.Add(new()
                            {
                                Lang = "csharp",
                                Text = $"class {classDef.Name.text}\n{{\n  {field.type} {field.name.text}; // Field\n}}",
                            });

                            return new HoverInfo()
                            {
                                Range = field.name.Position,
                                Contents = result.ToArray(),
                            };
                        }
                        continue;
                    }

                    Logger.Log($"Hover: Class def. found, {classDef.Name.Position}");

                    if (InfoReachedUnit(classDef.Name, out var reachedUnit))
                    { result.Add(reachedUnit); }

                    result.Add(new HoverContent()
                    {
                        Lang = "csharp",
                        Text = $"class {classDef.FullName}",
                    });

                    return new HoverInfo()
                    {
                        Range = classDef.Name.Position,
                        Contents = result.ToArray(),
                    };
                }

                foreach (var namespaceDef in AnalysisResult.ParserResult.Namespaces)
                {
                    if (!namespaceDef.Name.Position.Contains(pos)) continue;

                    Logger.Log($"Hover: Namespace def. found, {namespaceDef.Name.Position}");

                    if (InfoReachedUnit(namespaceDef.Name, out var reachedUnit))
                    { result.Add(reachedUnit); }

                    result.Add(new HoverContent()
                    {
                        Lang = "csharp",
                        Text = $"namespace {namespaceDef.Name.text}",
                    });

                    return new HoverInfo()
                    {
                        Range = namespaceDef.Name.Position,
                        Contents = result.ToArray(),
                    };
                }

                for (int i = 0; i < AnalysisResult.ParserResult.Usings.Count; i++)
                {
                    UsingDefinition usingItem = AnalysisResult.ParserResult.Usings[i];

                    foreach (var pathToken in usingItem.Path)
                    {
                        if (pathToken.Position.Contains(pos))
                        {
                            Logger.Log($"Hover: Using def. found, {pathToken.Position}");

                            if (InfoReachedUnit(pathToken, out var reachedUnit))
                            { result.Add(reachedUnit); }

                            if (usingItem.Path.Length == 1 && pathToken.type == TokenType.LITERAL_STRING)
                            {
                                result.Add(new HoverContent()
                                {
                                    Lang = "csharp",
                                    Text = $"using \"{pathToken.text}\";",
                                });
                            }
                            else
                            {
                                result.Add(new HoverContent()
                                {
                                    Lang = "csharp",
                                    Text = $"using {usingItem.PathString};",
                                });
                            }

                            if (!string.IsNullOrEmpty(usingItem.CompiledUri))
                            {
                                result.Add(new HoverContent()
                                {
                                    Lang = "text",
                                    Text = usingItem.CompiledUri,
                                });
                            }

                            return new HoverInfo()
                            {
                                Range = Range<SinglePosition>.Create(usingItem.Path),
                                Contents = result.ToArray(),
                            };
                        }
                    }

                    if (usingItem.Keyword.Position.Contains(pos)) break;
                }
            }

            Logger.Log($"Hover: Fallback to token");

            Token token = GetTokenAt(pos);

            if (token == null)
            {
                Logger.Log($"Hover: No token at {pos.ToMinString()}");
                return null;
            }

            if (InfoReachedUnit(token, out var reachedUnit2))
            { result.Add(reachedUnit2); }

            if (false && token is TypeToken typeToken)
            {
                Logger.Log($"Hover: TypeToken found");

                try
                {

                    var info = Hover(typeToken);
                    if (info != null)
                    {
                        result.Add(info);

                        return new HoverInfo()
                        {
                            Range = token.Position,
                            Contents = result.ToArray(),
                        };
                    }
                }
                catch (Exception error)
                {
                    Logger.Warn($"{error}");
                }

                result.Add(new HoverContent()
                {
                    Lang = "text",
                    Text = $"TypeToken: {typeToken.typeName} {typeToken.text}{(typeToken.IsList ? "[]" : "")}",
                });
            }
            else
            {
                Logger.Log($"Hover: Token found");

                try
                {

                    var info = Hover(token);
                    if (info != null)
                    {
                        result.Add(info);

                        return new HoverInfo()
                        {
                            Range = token.Position,
                            Contents = result.ToArray(),
                        };
                    }
                }
                catch (Exception error)
                {
                    Logger.Warn($"{error}");
                }

                result.Add(new HoverContent()
                {
                    Lang = "text",
                    Text = $"Token: {token.type} {token.text}",
                });
            }

            return null;
        }

        static HoverContent Hover(TypeToken token)
        {
            HoverContent info = token.typeName switch
            {
                BuiltinType.AUTO => null,
                BuiltinType.ANY => null,

                BuiltinType.STRUCT => new HoverContent()
                {
                    Lang = "csharp",
                    Text = $"struct {token.text} // Type",
                },
                BuiltinType.VOID => new HoverContent()
                {
                    Lang = "csharp",
                    Text = $"{token.text} // Type",
                },

                _ => new HoverContent()
                {
                    Lang = "csharp",
                    Text = $"{token.text} // Type",
                },
            };

            if (info == null) return null;
            return info;
        }
        static HoverContent Hover(Token token)
        {
            if (token.Analysis.Reference is not null)
            {
                if (token.Analysis.Reference is TokenAnalysis.RefVariable refVariable) return new HoverContent()
                {
                    Lang = "csharp",
                    Text = $"{refVariable.Declaration.Type} {token.text}; // {(refVariable.IsGlobal ? "Global" : "Local")} variable",
                };
                if (token.Analysis.Reference is TokenAnalysis.RefParameter refParameter) return new HoverContent()
                {
                    Lang = "csharp",
                    Text = $"{refParameter.Type} {token.text}; // Parameter",
                };
            }

            {
                var info = InfoTokenSubSubtype(token);
                if (info != null) return info;
            }

            {
                var info = InfoTokenSubtype(token);
                if (info != null) return info;
            }

            {
                var info = InfoToken(token);
                if (info != null) return info;
            }

            return null;
        }

        static HoverContent InfoToken(Token t)
        {
            if ((new string[]
            {
                    "{", "}",
                    "[", "]",
                    "=", ",",
                    ";",
            }).Contains(t.text)) return null;
            return t.type switch
            {
                TokenType.LITERAL_FLOAT => null,
                TokenType.LITERAL_NUMBER => null,
                TokenType.LITERAL_HEX => null,
                TokenType.LITERAL_BIN => null,
                TokenType.LITERAL_STRING => null,
                TokenType.OPERATOR => null,
                _ => null,
            };
        }
        static HoverContent InfoTokenSubtype(Token t) => t.Analysis.Subtype switch
        {
            TokenSubtype.VariableName => new HoverContent()
            {
                Lang = "csharp",
                Text = $"var {t.text}; // Variable or parameter",
            },
            TokenSubtype.MethodName => new HoverContent()
            {
                Lang = "csharp",
                Text = $"? {t.text}()",
            },
            TokenSubtype.Type => new HoverContent()
            {
                Lang = "csharp",
                Text = $"{t.text} // Type",
            },
            TokenSubtype.Struct => new HoverContent()
            {
                Lang = "csharp",
                Text = $"struct {t.text}",
            },
            TokenSubtype.None => null,
            TokenSubtype.Keyword => null,
            TokenSubtype.Statement => null,
            TokenSubtype.Library => new HoverContent()
            {
                Lang = "csharp",
                Text = $"namespace {t.text}",
            },
            TokenSubtype.BuiltinType => null,
            TokenSubtype.Hash => null,
            TokenSubtype.HashParameter => null,
            _ => null,
        };
        static HoverContent InfoTokenSubSubtype(Token t) => t.Analysis.SubSubtype switch
        {
            TokenSubSubtype.Attribute => new HoverContent()
            {
                Lang = "csharp",
                Text = $"{t.text} // Attribute",
            },
            TokenSubSubtype.Type => new HoverContent()
            {
                Lang = "csharp",
                Text = $"{t.text} // Type",
            },
            TokenSubSubtype.Struct => new HoverContent()
            {
                Lang = "csharp",
                Text = $"struct {t.text}",
            },
            TokenSubSubtype.FunctionName => new HoverContent()
            {
                Lang = "csharp",
                Text = $"? {t.text}()",
            },
            TokenSubSubtype.VariableName => new HoverContent()
            {
                Lang = "csharp",
                Text = $"var {t.text}; // Variable",
            },
            TokenSubSubtype.ParameterName => new HoverContent()
            {
                Lang = "csharp",
                Text = $"var {t.text}; // Parameter",
            },
            TokenSubSubtype.FieldName => new HoverContent()
            {
                Lang = "csharp",
                Text = $"?.{t.text}; // Field",
            },
            TokenSubSubtype.None => null,
            TokenSubSubtype.Keyword => null,
            TokenSubSubtype.Namespace => new HoverContent()
            {
                Lang = "csharp",
                Text = $"namespace {t.text}",
            },
            _ => null,
        };

        CodeLensInfo[] IDocument.CodeLens(DocumentEventArgs e)
        {
            string path = System.Net.WebUtility.UrlDecode(e.Document.Uri.AbsolutePath);
            List<CodeLensInfo> result = new();

            if (!AnalysisResult.Compiled) return Array.Empty<CodeLensInfo>();

            if (AnalysisResult.ParserFatalError == null && AnalysisResult.TokenizingSuccess)
            {
                if (AnalysisResult.CompilerFatalError == null)
                {
                    if (AnalysisResult.CompilerResult.compiledFunctions != null)
                    {
                        List<string> FunctionsBruh = new();

                        foreach (var _func in AnalysisResult.CompilerResult.compiledFunctions)
                        {
                            var func = _func.Value;
                            if (func.FilePath != path) continue;

                            FunctionsBruh.Add(func.ID());

                            int referenceCount = func.TimesUsedTotal;

                            result.Add(new CodeLensInfo($"{referenceCount} reference", func.Name));

                            if (func.CompiledAttributes.ContainsKey("CodeEntry"))
                            {
                                result.Add(new CodeLensInfo($"This is the code entry", func.Name));
                            }
                        }

                        if (AnalysisResult.ParserResult.Functions != null)
                        {
                            foreach (var func in AnalysisResult.ParserResult.Functions)
                            {
                                if (FunctionsBruh.Contains(func.ID())) continue;

                                FunctionsBruh.Add(func.ID());
                                result.Add(new CodeLensInfo($"0 reference", func.Name));
                            }
                        }
                    }
                }
                else
                {
                    if (AnalysisResult.ParserResult.Functions != null)
                    {
                        foreach (var func in AnalysisResult.ParserResult.Functions)
                        {
                            result.Add(new CodeLensInfo($"? reference", func.Name));
                        }
                    }
                }

                for (int i = 0; i < AnalysisResult.ParserResult.Usings.Count; i++)
                {
                    var usingDef = AnalysisResult.ParserResult.Usings[i];
                    var usingAnly = AnalysisResult.ParserResult.UsingsAnalytics[i];

                    if (usingDef.DownloadTime.HasValue)
                    {
                        result.Add(new CodeLensInfo($"Downloaded in {Math.Floor(usingDef.DownloadTime.Value + .9d)} ms", usingDef.Keyword));
                    }

                    if (usingAnly.Found)
                    {
                        if (usingAnly.ParseTime == -1d)
                        { result.Add(new CodeLensInfo($"Not parsed", usingDef.Keyword)); }
                        else
                        { result.Add(new CodeLensInfo($"Parsed in {Math.Floor(usingAnly.ParseTime + .9d)} ms", usingDef.Keyword)); }
                    }
                }
            }

            return result.ToArray();
        }

        SingleOrArray<FilePosition>? IDocument.GotoDefinition(DocumentPositionEventArgs e)
        {
            Logger.Log($"GotoDefinition()");

            if (!AnalysisResult.TokenizingSuccess) return null;
            if (AnalysisResult.ParserFatalError != null) return null;
            if (!AnalysisResult.Parsed) return null;

            var pos = e.Position;
            var token = GetTokenAt(pos);

            if (token == null)
            {
                Logger.Log($"No token at {pos.ToMinString()}");
                return null;
            }

            string currentFilePath = System.Net.WebUtility.UrlDecode(e.Document.Uri.AbsolutePath);
            SingleOrArray<FilePosition>? result = null;

            if (e.Document.Uri.Scheme == "file")
            {
                if (System.IO.File.Exists(currentFilePath))
                {
                    var currentFile = new System.IO.FileInfo(currentFilePath);

                    foreach (var usingItem in AnalysisResult.ParserResult.Usings)
                    {
                        bool isContains = false;
                        foreach (var item in usingItem.Path)
                        {
                            if (item.Position.Contains(pos))
                            {
                                isContains = true;
                                break;
                            }
                        }

                        if (isContains == false) break;

                        var usingFilePath = usingItem.CompiledUri ?? currentFile.Directory.FullName + "\\" + usingItem.PathString + "." + FileExtensions.Code;

                        if (System.IO.File.Exists(usingFilePath))
                        {
                            Logger.Log($"Using file found {usingFilePath}");
                            return new SingleOrArray<FilePosition>(new FilePosition(
                                Range<SinglePosition>.Create(usingItem.Path),
                                new Range<SinglePosition>(new SinglePosition(1, 1), new SinglePosition(5, 5)),
                                new Uri($"file:///" + usingFilePath.Replace('\\', '/'))
                                ));
                        }
                        else
                        {
                            Logger.Log($"Using file not found");
                        }
                    }
                }
                else
                {
                    Logger.Log($"{currentFilePath} not found");
                }
            }
            else
            {
                Logger.Log($"Document uri scheme is not file");
            }

            if (AnalysisResult.CompilerFatalError != null || !AnalysisResult.Compiled) return result;

            foreach (var @struct in AnalysisResult.ParserResult.Structs)
            {
                foreach (var field in @struct.Value.Fields)
                {
                    if (!field.type.Position.Contains(pos)) continue;
                    if (field.type.Analysis.Reference == null) continue;
                    if (field.type.Analysis.Reference is TokenAnalysis.RefStruct refStruct)
                    {
                        var uri = new Uri($"file:///" + refStruct.Definition.FilePath.Replace('\\', '/'));
                        return new SingleOrArray<FilePosition>(new FilePosition(refStruct.Definition.Name.Position, uri));
                    }
                    return result;
                }
            }

            StatementFinder.GetAllStatement(AnalysisResult.ParserResult, statement =>
            {
                if (statement is Statement_FunctionCall functionCall)
                {
                    if (!functionCall.Identifier.Position.Contains(pos)) return false;
                    if (functionCall.Identifier.text == "return") return false;
                    if (functionCall.Identifier.Analysis == null) return true;
                    if (functionCall.Identifier.Analysis.Reference == null) return true;

                    if (functionCall.Identifier.Analysis.Reference is TokenAnalysis.RefFunction refFunction)
                    {
                        Logger.Log($"GotoDef: Func. call found {refFunction}");
                        result = new SingleOrArray<FilePosition>(new FilePosition(refFunction.Definition.Name.Position, refFunction.Definition.FilePath));
                    }

                    return true;
                }
                else if (statement is Statement_Variable variable)
                {
                    if (!variable.VariableName.Position.Contains(pos)) return false;
                    if (variable.VariableName.Analysis == null) return true;
                    if (variable.VariableName.Analysis.Reference == null) return true;

                    if (variable.VariableName.Analysis.Reference is TokenAnalysis.RefVariable refVariable)
                    {
                        Logger.Log($"GotoDef: Variable found {refVariable}");
                        result = new SingleOrArray<FilePosition>(new FilePosition(refVariable.Declaration.VariableName.Position, refVariable.Declaration.FilePath));
                    }

                    return true;
                }
                else if (statement is Statement_Field field)
                {
                    if (!field.FieldName.Position.Contains(pos)) return false;
                    if (field.FieldName.Analysis == null) return true;
                    if (field.FieldName.Analysis.Reference == null) return true;

                    if (field.FieldName.Analysis.Reference is TokenAnalysis.RefField refField)
                    {
                        Logger.Log($"GotoDef: Field found {refField}");
                        result = new SingleOrArray<FilePosition>(new FilePosition(refField.NameToken.Position, refField.FilePath));
                    }

                    return true;
                }

                return false;
            });

            /*
            StatementFinder.GetAllStatement(AnalysisResult.ParserResult, statement =>
            {
                if (statement is Statement_FunctionCall functionCall)
                {
                    if (functionCall.Identifier.Position.Contains(pos))
                    {
                        if (functionCall.Identifier.Analysis.Reference is TokenAnalysis.RefFunction refFunction)
                        {
                            if (refFunction.Definition.FilePath == null)
                            {
                                Logger.Warn($"Function.Definition.FilePath is null");
                                return true;
                            }
                            var uri = new Uri($"file:///" + refFunction.Definition.FilePath.Replace('\\', '/'));
                            result = new SingleOrArray<FilePosition>(new FilePosition(refFunction.Definition.Name.Position, uri));

                            Logger.Log($"FuncCall Ref found {uri}");
                        }
                        else
                        {
                            Logger.Log($"FuncCall Ref is null");
                        }
                        return true;
                    }
                }
                else if (statement is Statement_NewInstance newStruct)
                {
                    if (newStruct.TypeName.Position.Contains(pos))
                    {
                        if (newStruct.TypeName.Analysis.Reference is TokenAnalysis.RefStruct refStruct)
                        {
                            var uri = new Uri($"file:///" + refStruct.Definition.FilePath.Replace('\\', '/'));
                            result = new SingleOrArray<FilePosition>(new FilePosition(refStruct.Definition.Name.Position, uri));

                            Logger.Log($"Struct Ref found {refStruct.Definition.Name.Position} {uri}");
                        }
                        else
                        {
                            Logger.Log($"Struct Ref is null");
                        }
                        return true;
                    }
                }
                else if (statement is Statement_Field field)
                {
                    if (!field.FieldName.Position.Contains(pos)) return false;

                    if (field.FieldName.Analysis.Reference is TokenAnalysis.RefField refField)
                    {
                        var uri = new Uri($"file:///" + refField.FilePath.Replace('\\', '/'));
                        result = new SingleOrArray<FilePosition>(new FilePosition(refField.NameToken.Position, uri));

                        Logger.Log($"Field Ref found {refField.NameToken.Position} {uri}");
                    }
                    else
                    {
                        Logger.Log($"Field Ref is null");
                    }

                    return true;
                }
                else if (statement is Statement_Variable variable)
                {
                    if (!variable.VariableName.Position.Contains(pos)) return false;

                    if (variable.VariableName.Analysis.Reference is TokenAnalysis.RefVariable refVariable)
                    {
                        var uri = new Uri($"file:///" + refVariable.Declaration.FilePath.Replace('\\', '/'));
                        result = new SingleOrArray<FilePosition>(new FilePosition(refVariable.Declaration.VariableName.Position, uri));

                        Logger.Log($"Variable Ref found {refVariable} {uri}");
                    }
                    else
                    {
                        Logger.Log($"Variable Ref is null");
                    }

                    return true;
                }
                return false;
            });
            */

            return result;
        }

        SymbolInformationInfo[] IDocument.Symbols(DocumentEventArgs e)
        {
            List<SymbolInformationInfo> symbols = new();

            if (AnalysisResult.ParserFatalError == null && AnalysisResult.TokenizingSuccess && AnalysisResult.Parsed)
            {
                var parserResult = AnalysisResult.ParserResult;

                List<string> Namespaces = new();

                foreach (var item in parserResult.Namespaces)
                {
                    symbols.Add(new SymbolInformationInfo()
                    {
                        Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Namespace,
                        Location = new DocumentLocation()
                        {
                            Range = new Position(item.Keyword, item.Name, item.BracketStart, item.BracketEnd),
                            Uri = e.Document.Uri,
                        },
                        Name = item.Name.text,
                    });
                }

                foreach (var item in parserResult.Functions)
                {
                    foreach (var attr in item.Attributes)
                    {
                        if (attr.Name.text == "Catch")
                        {
                            if (attr.Parameters.Length != 1) continue;
                            if (attr.Parameters[0] is not string eventName) continue;
                            symbols.Add(new SymbolInformationInfo()
                            {
                                Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Event,
                                Location = new DocumentLocation()
                                {
                                    Range = new Position(item.Type, item.Name, item.BracketStart, item.BracketEnd, attr.Name),
                                    Uri = e.Document.Uri,
                                },
                                Name = "On" + eventName.FirstCharToUpper(),
                            });
                        }
                    }
                    SymbolInformationInfo funcSymbol = new()
                    {
                        Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Function,
                        Location = new DocumentLocation()
                        {
                            Range = new Position(item.Type, item.Name, item.BracketStart, item.BracketEnd),
                            Uri = e.Document.Uri,
                        },
                        Name = item.Name.text,
                    };

                    if (item.Parameters.Count > 0) funcSymbol.Name += "(";

                    for (int i = 0; i < item.Parameters.Count; i++)
                    {
                        if (i > 0) { funcSymbol.Name += ", "; }
                        ParameterDefinition prm = item.Parameters[i];
                        funcSymbol.Name += prm.type.text;
                    }

                    if (item.Parameters.Count > 0) funcSymbol.Name += ")";

                    if (item.NamespacePath.Length > 0 && false)
                    {
                        foreach (var item2 in item.NamespacePath)
                        {
                            if (Namespaces.Contains(item2)) continue;
                            Namespaces.Add(item2);
                            symbols.Add(new SymbolInformationInfo()
                            {
                                Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Namespace,
                                Location = new DocumentLocation()
                                {
                                    Range = new Position(item.Name),
                                    Uri = e.Document.Uri,
                                },
                                Name = item2,
                            });
                        }
                    }

                    symbols.Add(funcSymbol);
                }

                foreach (var item in parserResult.Structs)
                {
                    symbols.Add(new SymbolInformationInfo()
                    {
                        Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Struct,
                        Location = new DocumentLocation()
                        {
                            Range = new Position(item.Value.Name, item.Value.BracketStart, item.Value.BracketEnd),
                            Uri = e.Document.Uri,
                        },
                        Name = item.Value.Name.text,
                    });

                    foreach (var field in item.Value.Fields)
                    {
                        symbols.Add(new SymbolInformationInfo()
                        {
                            Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Field,
                            Location = new DocumentLocation()
                            {
                                Range = new Position(field.type, field.name),
                                Uri = e.Document.Uri,
                            },
                            Name = field.name.text,
                        });
                    }
                }
            }

            return symbols.ToArray();
        }

        FilePosition[] IDocument.References(FindReferencesEventArgs e)
        {
            List<FilePosition> result = new();

            Logger.Log($"References({e.Position.ToMinString()})");

            if (AnalysisResult.ParserFatalError == null && AnalysisResult.Parsed)
            {
                foreach (var funcDef in AnalysisResult.ParserResult.Functions)
                {
                    if (!funcDef.Name.Position.Contains(e.Position)) continue;
                }

                foreach (var pair in AnalysisResult.ParserResult.Structs)
                {
                    var structDef = pair.Value;
                    if (!structDef.Name.Position.Contains(e.Position)) continue;
                }
            }

            return result.ToArray();
        }

        SignatureHelpInfo IDocument.SignatureHelp(SignatureHelpEventArgs e)
        {
            if (!HaveSuccesAlanysisResult) return null;
            if (!BestAnalysisResult.Compiled) return null;
            return null;
            /*
            List<SignatureInfo> signatures = new();
            foreach (var func in BestAnalysisResult.CompilerResult.compiledFunctions)
            {
                ParameterInfo[] parameters = new ParameterInfo[func.Value.ParameterCount];
                for (int i = 0; i < func.Value.Parameters.Count; i++)
                {
                    parameters[i] = new ParameterInfo(func.Value.Parameters[i].name.text, null);
                }
                signatures.Add(new SignatureInfo(0, func.Key, null, parameters));
            }
            return new SignatureHelpInfo(0, 0, signatures.ToArray());
            */
        }

        SemanticToken[] IDocument.GetSemanticTokens(DocumentEventArgs e)
        {
            if (!AnalysisResult.TokenizingSuccess || !AnalysisResult.Tokenized) return Array.Empty<SemanticToken>();

            var tokens = AnalysisResult.Tokens;
            List<SemanticToken> result = new();

            foreach (var token in tokens)
            {
                switch (token.type)
                {
                    case TokenType.IDENTIFIER:
                        {
                            switch (token.Analysis.SubSubtype)
                            {
                                case TokenSubSubtype.Attribute:
                                    break;
                                case TokenSubSubtype.Type:
                                    break;
                                case TokenSubSubtype.Struct:
                                    result.Add(new SemanticToken(token,
                                        OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Struct));
                                    break;
                                case TokenSubSubtype.Keyword:
                                    break;
                                case TokenSubSubtype.FunctionName:
                                    break;
                                case TokenSubSubtype.VariableName:
                                    result.Add(new SemanticToken(token,
                                        OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Variable));
                                    break;
                                case TokenSubSubtype.FieldName:
                                    break;
                                case TokenSubSubtype.ParameterName:
                                    result.Add(new SemanticToken(token,
                                        OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Parameter));
                                    break;
                                case TokenSubSubtype.Namespace:
                                    result.Add(new SemanticToken(token,
                                        OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Namespace));
                                    break;

                                case TokenSubSubtype.None:
                                default:
                                    switch (token.Analysis.Subtype)
                                    {
                                        case TokenSubtype.MethodName:
                                            break;
                                        case TokenSubtype.Keyword:
                                            break;
                                        case TokenSubtype.Type:
                                            break;
                                        case TokenSubtype.VariableName:
                                            result.Add(new SemanticToken(token,
                                                OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Variable));
                                            break;
                                        case TokenSubtype.Statement:
                                            break;
                                        case TokenSubtype.Library:
                                            result.Add(new SemanticToken(token,
                                                OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Namespace));
                                            break;
                                        case TokenSubtype.Struct:
                                            result.Add(new SemanticToken(token,
                                                OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Struct));
                                            break;
                                        case TokenSubtype.BuiltinType:
                                            break;
                                        case TokenSubtype.Hash:
                                            break;
                                        case TokenSubtype.HashParameter:
                                            break;
                                        case TokenSubtype.Class:
                                            result.Add(new SemanticToken(token,
                                                OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Class));
                                            break;
                                        case TokenSubtype.None:
                                        default:
                                            break;
                                    }
                                    break;
                            }
                        }
                        break;

                    case TokenType.LITERAL_NUMBER:
                    case TokenType.LITERAL_HEX:
                    case TokenType.LITERAL_BIN:
                    case TokenType.LITERAL_FLOAT:
                        break;

                    case TokenType.LITERAL_STRING:
                        break;

                    case TokenType.OPERATOR:
                        break;

                    case TokenType.STRING_ESCAPE_SEQUENCE:
                    case TokenType.POTENTIAL_FLOAT:
                    case TokenType.POTENTIAL_COMMENT:
                    case TokenType.POTENTIAL_END_MULTILINE_COMMENT:
                    case TokenType.COMMENT:
                    case TokenType.COMMENT_MULTILINE:
                    case TokenType.WHITESPACE:
                    case TokenType.LINEBREAK:
                    default:
                        break;
                }
            }

            return result.ToArray();
        }
    }
}
