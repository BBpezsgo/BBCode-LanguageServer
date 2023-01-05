using BBCodeLanguageServer.Interface;
using BBCodeLanguageServer.Interface.SystemExtensions;

using IngameCoding.BBCode;
using IngameCoding.BBCode.Compiler;
using IngameCoding.BBCode.Parser;
using IngameCoding.BBCode.Parser.Statements;
using IngameCoding.Core;

using LanguageServer.Parameters;
using LanguageServer.Parameters.TextDocument;

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;

using static BBCodeLanguageServer.Interface.Extensions;

namespace BBCodeLanguageServer
{
    internal class Doc
    {
        readonly DocumentInterface App;
        string Text;
        string Url;
        string LanguageId;

        protected AnalysisResult AnalysisResult;
        AnalysisResult LastSuccessAnalysisResult;
        bool HaveSuccesAlanysisResult;

        AnalysisResult BestAnalysisResult => HaveSuccesAlanysisResult ? LastSuccessAnalysisResult : AnalysisResult;

        public Doc(DocumentItem document, DocumentInterface app)
        {
            App = app;
            AnalysisResult = AnalysisResult.Empty();
            OnChanged(document);
        }

        internal void OnChanged(DocumentItem e)
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

        internal void Validate(Document e)
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
                var range = exception.Position.Convert1();

                Logger.Log($"{source} Error: {exception.MessageAll}\n  at {range.start.line}:{range.start.character} - {range.end.line}:{range.end.character}");

                return new DiagnosticInfo
                {
                    severity = DiagnosticSeverity.Error,
                    range = range,
                    message = exception.Message,
                    source = source,
                };
            }
            static DiagnosticInfo DiagnostizeError(IngameCoding.Errors.Error error, string source)
            {
                var range = error.Position.Convert1();

                Logger.Log($"{source} Error: {error.MessageAll}\n  at {range.start.line}:{range.start.character} - {range.end.line}:{range.end.character}");

                return new DiagnosticInfo
                {
                    severity = DiagnosticSeverity.Error,
                    range = range,
                    message = error.Message,
                    source = source,
                };
            }
            static DiagnosticInfo DiagnostizeHint(IngameCoding.Errors.Hint hint, string source)
            {
                var range = hint.Position.Convert1();

                Logger.Log($"{source}: {hint.MessageAll}\n  at {range.start.line}:{range.start.character} - {range.end.line}:{range.end.character}");

                return new DiagnosticInfo
                {
                    severity = DiagnosticSeverity.Hint,
                    range = range,
                    message = hint.Message,
                    source = source,
                };
            }
            static DiagnosticInfo DiagnostizeInformation(IngameCoding.Errors.Information information, string source)
            {
                var range = information.Position.Convert1();

                Logger.Log(
                    $"{source}: {information.MessageAll}\n" +
                    $"  at {range.start.line}:{range.start.character} - {range.end.line}:{range.end.character}\n" +
                    $"  in {information.File}"
                    );

                return new DiagnosticInfo
                {
                    severity = DiagnosticSeverity.Information,
                    range = range,
                    message = information.Message,
                    source = source,
                };
            }

            AnalysisResult = Analysis.Analyze(Text, file, path);
            Logger.Log($"Check file paths...");
            AnalysisResult.CheckFilePaths(notSetMessage => Logger.Log($"{notSetMessage}"));

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
                            severity = DiagnosticSeverity.Warning,
                            range = error.Position.Convert1(),
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

                for (int i = 0; i < AnalysisResult.ParserResult.Usings.Count; i++)
                {
                    if (i >= AnalysisResult.ParserResult.UsingsAnalytics.Count) break;
                    var usingDef = AnalysisResult.ParserResult.Usings[i];
                    var usingAnly = AnalysisResult.ParserResult.UsingsAnalytics[i];

                    diagnostics.Add(new DiagnosticInfo
                    {
                        severity = DiagnosticSeverity.Warning,
                        range = Convert1(usingDef.Path),
                        message = "File not found",
                        source = "Compiler",
                    });
                }
            }

            for (int i = 0; i < AnalysisResult.Warnings.Length; i++)
            {
                var warning = AnalysisResult.Warnings[i];

                if (warning.File != path && warning.File != null) continue;

                diagnostics.Add(new DiagnosticInfo
                {
                    severity = DiagnosticSeverity.Warning,
                    range = warning.Position.Convert1(),
                    message = warning.Message,
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

            App.Interface.PublishDiagnostics(e.Uri, diagnostics.ToArray());
            // AnalysisResult.FileReferences = Analysis.FileReferences(file, path);
        }

        internal CompletionInfo[] Completion(DocumentPositionContextEventArgs e)
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
                        Kind = CompletionItemKind.Function,
                    });
                }

                foreach (var @struct in BestAnalysisResult.ParserResult.Structs)
                {
                    result.Add(new CompletionInfo()
                    {
                        Label = @struct.Value.Name.text,
                        Kind = CompletionItemKind.Struct,
                    });
                }

                foreach (var variable in BestAnalysisResult.ParserResult.GlobalVariables)
                {
                    result.Add(new CompletionInfo()
                    {
                        Label = variable.variableName.text,
                        Kind = CompletionItemKind.Variable,
                    });
                }
            }

            result.Add(new CompletionInfo()
            {
                Label = "int",
                Kind = CompletionItemKind.Keyword,
            });
            result.Add(new CompletionInfo()
            {
                Label = "float",
                Kind = CompletionItemKind.Keyword,
            });
            result.Add(new CompletionInfo()
            {
                Label = "bool",
                Kind = CompletionItemKind.Keyword,
            });
            result.Add(new CompletionInfo()
            {
                Label = "string",
                Kind = CompletionItemKind.Keyword,
            });
            result.Add(new CompletionInfo()
            {
                Label = "new",
                Kind = CompletionItemKind.Keyword,
            });
            result.Add(new CompletionInfo()
            {
                Label = "void",
                Kind = CompletionItemKind.Keyword,
            });
            result.Add(new CompletionInfo()
            {
                Label = "struct",
                Kind = CompletionItemKind.Keyword,
            });
            result.Add(new CompletionInfo()
            {
                Label = "namespace",
                Kind = CompletionItemKind.Keyword,
            });
            result.Add(new CompletionInfo()
            {
                Label = "var",
                Kind = CompletionItemKind.Keyword,
            });

            result.Add(new CompletionInfo()
            {
                Label = "return",
                Kind = CompletionItemKind.Keyword,
            });
            result.Add(new CompletionInfo()
            {
                Label = "if",
                Kind = CompletionItemKind.Keyword,
            });
            result.Add(new CompletionInfo()
            {
                Label = "elseif",
                Kind = CompletionItemKind.Keyword,
            });
            result.Add(new CompletionInfo()
            {
                Label = "else",
                Kind = CompletionItemKind.Keyword,
            });
            result.Add(new CompletionInfo()
            {
                Label = "for",
                Kind = CompletionItemKind.Keyword,
            });
            result.Add(new CompletionInfo()
            {
                Label = "while",
                Kind = CompletionItemKind.Keyword,
            });

            return result.ToArray();
        }

        internal HoverInfo Hover(DocumentPositionEventArgs e)
        {
            static HoverContent InfoFunctionDefinition(FunctionDefinition funcDef, bool IsDefinition)
            {
                var text = $"{funcDef.Type.text} {funcDef.FullName}(";

                bool addComma = false;

                for (int i = 0; i < funcDef.Parameters.Count; i++)
                {
                    if (addComma) text += ", ";

                    if (funcDef.Parameters[i].withThisKeyword)
                    { text = $"{funcDef.Type.text} {funcDef.Parameters[i].type.text}.{funcDef.FullName}("; continue; }

                    text += $"{funcDef.Parameters[i].type} {funcDef.Parameters[i].name}";
                    addComma = true;
                }

                text += ")";

                return new HoverContent()
                {
                    Lang = "csharp",
                    Text = $"{text} {(IsDefinition ? "// Function Definition" : "// Function Call")}",
                };
            }
            static HoverContent InfoStructDefinition(StructDefinition structDef)
            {
                return new HoverContent()
                {
                    Lang = "csharp",
                    Text = $"struct {structDef.FullName} // Struct Definition",
                };
            }
            static bool InfoReachedUnit(Token token, out HoverContent result)
            {
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

            if (AnalysisResult.ParserFatalError == null && AnalysisResult.Parsed)
            {
                {
                    Range<SinglePosition> range = new();

                    StatementFinder.GetAllStatement(AnalysisResult.ParserResult, statement =>
                    {
                        if (statement is Statement_FunctionCall functionCall)
                        {
                            if (!functionCall.functionNameT.Position.Contains(pos)) return false;
                            if (functionCall.functionNameT.text == "return") return false;

                            Logger.Log($"Hover: Func. call found");

                            range = functionCall.functionNameT.Position;

                            if (InfoReachedUnit(functionCall.functionNameT, out var reachedUnit))
                            { result.Add(reachedUnit); }

                            Logger.Log($"{functionCall.functionNameT.Analysis}");

                            if (functionCall.functionNameT.Analysis.Reference is TokenAnalysis.RefFunction refFunction)
                            {
                                result.Add(InfoFunctionDefinition(refFunction.Definition, false));
                                return true;
                            }
                            else
                            {
                                string newContentText = "";

                                newContentText += $"? {functionCall.TargetNamespacePathPrefix}{functionCall.functionNameT}(";

                                bool addComma = false;
                                int paramIndex = 0;
                                foreach (var param in functionCall.parameters)
                                {
                                    paramIndex++;
                                    if (addComma) newContentText += $", ";
                                    if (param is Statement_Literal literalParam)
                                    {
                                        switch (literalParam.type.typeName)
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
                                            case BuiltinType.RUNTIME:
                                                newContentText += $"? p{paramIndex}";
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
                                if (literal.type.typeName == BuiltinType.STRING)
                                { range.End.Character++; }
                                result.Add(info);
                            }

                            return true;
                        }
                        else if (statement is Statement_Variable variable)
                        {
                            if (!variable.variableName.Position.Contains(pos)) return false;

                            Logger.Log($"Hover: Variable found {variable.variableName.Analysis}");

                            range = variable.variableName.Position;

                            if (InfoReachedUnit(variable.variableName, out var reachedUnit))
                            { result.Add(reachedUnit); }

                            if (variable.variableName.Analysis.Reference is TokenAnalysis.RefVariable refVariable)
                            {
                                var def = refVariable.Declaration;
                                result.Add(new()
                                {
                                    Lang = "csharp",
                                    Text = $"{def.type.text} {def.variableName.text}; // {(refVariable.IsGlobal ? "Global Variable" : "Local Variable")}",
                                });
                            }
                            else if (variable.variableName.Analysis.Reference is TokenAnalysis.RefParameter refParameter)
                            {
                                result.Add(new()
                                {
                                    Lang = "csharp",
                                    Text = $"{refParameter.Type} {variable.variableName.text}; // Parameter",
                                });
                            }
                            else
                            {
                                result.Add(new()
                                {
                                    Lang = "csharp",
                                    Text = $"var {variable.variableName.text};",
                                });
                            }

                            return true;
                        }
                        else if (statement is Statement_NewVariable newVariable)
                        {
                            if (!newVariable.variableName.Position.Contains(pos)) return false;

                            Logger.Log($"Hover: NewVariable found {newVariable.variableName.Analysis}");

                            range = newVariable.variableName.Position;

                            if (InfoReachedUnit(newVariable.variableName, out var reachedUnit))
                            { result.Add(reachedUnit); }

                            if (newVariable.variableName.Analysis.Reference is TokenAnalysis.RefVariable refVariable)
                            {
                                var def = refVariable.Declaration;
                                result.Add(new()
                                {
                                    Lang = "csharp",
                                    Text = $"{def.type.text} {def.variableName.text}; // {(refVariable.IsGlobal ? "Global Variable" : "Local Variable")}",
                                });
                            }
                            else
                            {
                                result.Add(new()
                                {
                                    Lang = "csharp",
                                    Text = $"{newVariable.type} {newVariable.variableName.text}; // Variable",
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
                                result.Add(new()
                                {
                                    Lang = "csharp",
                                    Text = $"struct {refField.StructName}\n{{\n  {refField.Type} {refField.Name.text}; // Field\n  // ...\n}}",
                                });
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
                        else if (statement is Statement_NewStruct newStruct)
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

                    if (result.Count > 0)
                    {
                        if (range.IsUnset())
                        { throw new ServiceException($"Hover range is null"); }
                        return new HoverInfo()
                        {
                            Range = range,
                            Contents = result.ToArray(),
                        };
                    }
                }

                foreach (var funcDef in AnalysisResult.ParserResult.Functions)
                {
                    if (!funcDef.Name.Position.Contains(pos)) continue;

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
                                Text = $"struct {structDef.Name.text}\n{{\n  {field.type} {field.name.text}; // Field\n  // ...\n}}",
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

                foreach (var namespaceDef in AnalysisResult.ParserResult.Namespaces)
                {
                    if (!namespaceDef.Name.Position.Contains(pos)) continue;

                    Logger.Log($"Hover: Namespace def. found, {namespaceDef.Name.Position}");

                    if (InfoReachedUnit(namespaceDef.Name, out var reachedUnit))
                    { result.Add(reachedUnit); }

                    result.Add(new()
                    {
                        Lang = "text",
                        Text = $"Struct Definition",
                    });

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
                        if (!pathToken.Position.Contains(pos)) continue;

                        Logger.Log($"Hover: Using def. found, {pathToken.Position}");

                        if (InfoReachedUnit(pathToken, out var reachedUnit))
                        { result.Add(reachedUnit); }

                        result.Add(new HoverContent()
                        {
                            Lang = "csharp",
                            Text = $"using {usingItem.PathString};",
                        });

                        return new HoverInfo()
                        {
                            Range = Range<SinglePosition>.Create(usingItem.Path),
                            Contents = result.ToArray(),
                        };
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
                    Text = $"TypeToken: {typeToken.typeName} {typeToken.text}{(typeToken.isList ? "[]" : "")}",
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

            Logger.Log($"Hover: Unknown error {pos.ToMinString()}");

            return null;
        }

        internal static HoverContent Hover(TypeToken token)
        {
            HoverContent info = token.typeName switch
            {
                BuiltinType.AUTO => null,
                BuiltinType.RUNTIME => null,
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
        internal static HoverContent Hover(Token token)
        {
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
                TokenType.LITERAL_FLOAT => new HoverContent()
                {
                    Lang = "csharp",
                    Text = $"{t.text} // Literal Float",
                },
                TokenType.LITERAL_NUMBER => new HoverContent()
                {
                    Lang = "csharp",
                    Text = $"{t.text} // Literal Integer",
                },
                TokenType.LITERAL_STRING => new HoverContent()
                {
                    Lang = "csharp",
                    Text = $"\"{t.text}\" // Literal String",
                },
                TokenType.OPERATOR => new HoverContent()
                {
                    Lang = "csharp",
                    Text = $"{t.text} // Operator",
                },
                _ => null,
            };
        }
        static HoverContent InfoTokenSubtype(Token t) => t.Analysis.Subtype switch
        {
            TokenSubtype.VariableName => new HoverContent()
            {
                Lang = "csharp",
                Text = $"{t.text}",
            },
            TokenSubtype.MethodName => new HoverContent()
            {
                Lang = "csharp",
                Text = $"? {t.text}() // Function Call",
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
            TokenSubtype.Statement => new HoverContent()
            {
                Lang = "text",
                Text = $"Statement {t.text}",
            },
            TokenSubtype.Library => new HoverContent()
            {
                Lang = "text",
                Text = $"Library {t.text}",
            },
            TokenSubtype.BuiltinType => new HoverContent()
            {
                Lang = "csharp",
                Text = $"{t.text} // Built-in Type",
            },
            TokenSubtype.Hash => new HoverContent()
            {
                Lang = "text",
                Text = $"Hash {t.text}",
            },
            TokenSubtype.HashParameter => new HoverContent()
            {
                Lang = "csharp",
                Text = $"\"{t.text}\" // Hash Parameter",
            },
            _ => null,
        };
        static HoverContent InfoTokenSubSubtype(Token t) => t.Analysis.SubSubtype switch
        {
            TokenSubSubtype.Attribute => new HoverContent()
            {
                Lang = "csharp",
                Text = $"[{t.text}]",
            },
            TokenSubSubtype.Type => new HoverContent()
            {
                Lang = "csharp",
                Text = $"{t.text} // Type",
            },
            TokenSubSubtype.Struct => new HoverContent()
            {
                Lang = "csharp",
                Text = $"struct {t.text} // Type",
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
                Text = $"{t.text} // Parameter",
            },
            TokenSubSubtype.FieldName => new HoverContent()
            {
                Lang = "csharp",
                Text = $"?.{t.text} // Field",
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

        internal CodeLensInfo[] CodeLens(DocumentEventArgs e)
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

                            int referenceCount = func.TimesUsed;

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

                    if (usingAnly.Found)
                    {
                        result.Add(new CodeLensInfo($"Parsed in {Math.Floor(usingAnly.ParseTime + .9d)} ms", usingDef.Keyword));
                    }
                }
            }

            return result.ToArray();
        }

        internal SingleOrArray<FilePosition>? GotoDefinition(DocumentPositionEventArgs e)
        {
            Logger.Log($"GotoDefinition()");

            if (!this.AnalysisResult.TokenizingSuccess) return null;
            if (this.AnalysisResult.ParserFatalError != null) return null;
            if (!this.AnalysisResult.Parsed) return null;

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

                        var usingFilePath = currentFile.Directory.FullName + "\\" + usingItem.PathString + "." + IngameCoding.Core.FileExtensions.Code;

                        if (System.IO.File.Exists(usingFilePath))
                        {
                            result = new SingleOrArray<FilePosition>(new FilePosition(
                                Range < SinglePosition >.Create(usingItem.Path),
                                new Range<SinglePosition>(new SinglePosition(1, 1), new SinglePosition(1, 1)),
                                new Uri($"file:///" + System.Net.WebUtility.UrlEncode(usingFilePath.Replace('\\', '/')))
                                ));

                            Logger.Log($"Using file found");
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

            if (this.AnalysisResult.CompilerFatalError != null || !this.AnalysisResult.Compiled) return result;

            StatementFinder.GetAllStatement(AnalysisResult.ParserResult, statement =>
            {
                if (statement is Statement_FunctionCall functionCall)
                {
                    if (functionCall.functionNameT.Position.Contains(pos))
                    {
                        if (functionCall.functionNameT.Analysis.Reference is TokenAnalysis.RefFunction refFunction)
                        {
                            if (refFunction.Definition.FilePath == null)
                            {
                                Logger.Warn($"Function.Definition.FilePath is null");
                                return true;
                            }
                            var uri = new Uri($"file:///" + System.Net.WebUtility.UrlEncode(refFunction.Definition.FilePath.Replace('\\', '/')));
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
                else if (statement is Statement_NewStruct newStruct)
                {
                    if (newStruct.structName.Position.Contains(pos))
                    {
                        if (newStruct.structName.Analysis.Reference is TokenAnalysis.RefStruct refStruct)
                        {
                            var uri = new Uri($"file:///" + System.Net.WebUtility.UrlEncode(refStruct.Definition.FilePath.Replace('\\', '/')));
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
                        var uri = new Uri($"file:///" + System.Net.WebUtility.UrlEncode(refField.FilePath.Replace('\\', '/')));
                        result = new SingleOrArray<FilePosition>(new FilePosition(refField.Name.Position, uri));

                        Logger.Log($"Field Ref found {refField.Name.Position} {uri}");
                    }
                    else
                    {
                        Logger.Log($"Field Ref is null");
                    }

                    return true;
                }
                else if (statement is Statement_Variable variable)
                {
                    if (!variable.variableName.Position.Contains(pos)) return false;

                    if (variable.variableName.Analysis.Reference is TokenAnalysis.RefVariable refVariable)
                    {
                        var uri = new Uri($"file:///" + System.Net.WebUtility.UrlEncode(refVariable.Declaration.FilePath.Replace('\\', '/')));
                        result = new SingleOrArray<FilePosition>(new FilePosition(refVariable.Declaration.variableName.Position, uri));

                        Logger.Log($"Variable Ref found {refVariable.Declaration.variableName.Position} {uri}");
                    }
                    else
                    {
                        Logger.Log($"Variable Ref is null");
                    }

                    return true;
                }
                return false;
            });

            return result;
        }

        internal SymbolInformationInfo[] Symbols(DocumentEventArgs e)
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
                        Kind = SymbolKind.Namespace,
                        Location = new Location()
                        {
                            range = Convert1(item.Keyword, item.Name, item.BracketStart, item.BracketEnd),
                            uri = e.Document.Uri,
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
                                Kind = SymbolKind.Event,
                                Location = new Location()
                                {
                                    range = Convert1(item.Type, item.Name, item.BracketStart, item.BracketEnd, attr.Name),
                                    uri = e.Document.Uri,
                                },
                                Name = "On" + eventName.FirstCharToUpper(),
                            });
                        }
                    }
                    SymbolInformationInfo funcSymbol = new()
                    {
                        Kind = SymbolKind.Function,
                        Location = new Location()
                        {
                            range = Convert1(item.Type, item.Name, item.BracketStart, item.BracketEnd),
                            uri = e.Document.Uri,
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
                                Kind = SymbolKind.Namespace,
                                Location = new Location()
                                {
                                    range = item.Name.Position.Convert1(),
                                    uri = e.Document.Uri,
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
                        Kind = SymbolKind.Struct,
                        Location = new Location()
                        {
                            range = Convert1(item.Value.Name, item.Value.BracketStart, item.Value.BracketEnd),
                            uri = e.Document.Uri,
                        },
                        Name = item.Value.Name.text,
                    });

                    foreach (var field in item.Value.Fields)
                    {
                        symbols.Add(new SymbolInformationInfo()
                        {
                            Kind = SymbolKind.Field,
                            Location = new Location()
                            {
                                range = Convert1(field.type, field.name),
                                uri = e.Document.Uri,
                            },
                            Name = field.name.text,
                        });
                    }
                }
            }

            return symbols.ToArray();
        }

        internal FilePosition[] References(DocumentEventArgs e) => Array.Empty<FilePosition>();
    }
}
