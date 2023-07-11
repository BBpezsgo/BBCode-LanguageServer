using ProgrammingLanguage.LanguageServer.Interface;

using ProgrammingLanguage.BBCode;
using ProgrammingLanguage.BBCode.Analysis;
using ProgrammingLanguage.BBCode.Compiler;
using ProgrammingLanguage.BBCode.Parser;
using ProgrammingLanguage.BBCode.Parser.Statements;
using ProgrammingLanguage.Core;
using ProgrammingLanguage.Errors;

using System;
using System.Collections.Generic;
using System.Linq;

namespace ProgrammingLanguage.LanguageServer.DocumentManagers
{
    internal class DocumentBBCode : IDocument
    {
        public Uri Uri { get; private set; }

        readonly DocumentInterface App;
        string Text;
        string LanguageId;

        Token[] Tokens;

        CompiledClass[] Classes;
        CompiledStruct[] Structs;
        CompiledEnum[] Enums;
        CompiledFunction[] Functions;
        CompiledGeneralFunction[] GeneralFunctions;

        public DocumentBBCode(DocumentItem document, DocumentInterface app)
        {
            App = app;
            OnChanged(document);
        }

        public void OnChanged(DocumentItem e)
        {
            Text = e.Content;
            Uri = e.Uri;
            LanguageId = e.LanguageID;

            Validate(new Document(e));
        }

        public void OnSaved(Document e)
        {
            Validate(e);
        }

        Token GetTokenAt(SinglePosition position)
        {
            if (Tokens == null) return null;
            if (Tokens.Length == 0) return null;

            foreach (var token in Tokens)
            { if (token.Position.Contains(position)) return token; }

            return null;
        }

        void Validate(Document e)
        {
            List<DiagnosticInfo> diagnostics = new();
            string path = System.Net.WebUtility.UrlDecode(e.Uri.AbsolutePath);

            Logger.Log($"Validate({path})");

            if (e.Uri.Scheme != "file") return;

            if (!System.IO.File.Exists(path))
            {
                Logger.Log($"{path} not found");
                return;
            }

            System.IO.FileInfo file = new(path);

            try
            {
                EasyCompiler.Result result = EasyCompiler.Compile(
                    file,
                    new Dictionary<string, BuiltinFunction>(),
                    TokenizerSettings.Default,
                    ParserSettings.Default,
                    Compiler.CompilerSettings.Default,
                    null,
                    null);

                List<Error> errors = new();
                List<Warning> warnings = new();

                if (result.CompilerResult.Warnings != null)
                { warnings.AddRange(result.CompilerResult.Warnings); }
                if (result.CompilerResult.Errors != null)
                { errors.AddRange(result.CompilerResult.Errors); }

                if (result.CodeGeneratorResult.Warnings != null)
                { warnings.AddRange(result.CodeGeneratorResult.Warnings); }
                if (result.CodeGeneratorResult.Errors != null)
                { errors.AddRange(result.CodeGeneratorResult.Errors); }

                for (int i = 0; i < errors.Count; i++)
                {
                    Error error = errors[i];

                    if (error.File == null || error.File != path) continue;

                    diagnostics.Add(new DiagnosticInfo
                    {
                        severity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Error,
                        range = error.Position,
                        message = error.Message,
                    });
                }

                for (int i = 0; i < warnings.Count; i++)
                {
                    Warning warning = warnings[i];

                    if (warning.File == null || warning.File != path) continue;

                    diagnostics.Add(new DiagnosticInfo
                    {
                        severity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Warning,
                        range = warning.Position,
                        message = warning.Message,
                    });
                }

                if (result.CodeGeneratorResult.Informations != null) for (int i = 0; i < result.CodeGeneratorResult.Informations.Length; i++)
                    {
                        Information information = result.CodeGeneratorResult.Informations[i];

                        if (information.File == null || information.File != path) continue;

                        diagnostics.Add(new DiagnosticInfo
                        {
                            severity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Information,
                            range = information.Position,
                            message = information.Message,
                        });
                    }

                if (result.CodeGeneratorResult.Hints != null) for (int i = 0; i < result.CodeGeneratorResult.Hints.Length; i++)
                    {
                        Hint hint = result.CodeGeneratorResult.Hints[i];

                        if (hint.File == null || hint.File != path) continue;

                        diagnostics.Add(new DiagnosticInfo
                        {
                            severity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Hint,
                            range = hint.Position,
                            message = hint.Message,
                        });
                    }

                Functions = result.CodeGeneratorResult.Functions ?? result.CompilerResult.Functions;
                GeneralFunctions = result.CodeGeneratorResult.GeneralFunctions ?? result.CompilerResult.GeneralFunctions;
                
                Enums = result.CompilerResult.Enums;

                Classes = result.CodeGeneratorResult.Classes ?? result.CompilerResult.Classes;
                Structs = result.CodeGeneratorResult.Structs ?? result.CompilerResult.Structs;

                Tokens = result.TokenizerResult;

                Logger.Log($"Succesfully compiled ({file.Name})");
            }
            catch (ProgrammingLanguage.Errors.Exception exception)
            {
                var range = exception.Position;

                Logger.Log($"Exception: {exception.MessageAll}\n  at {range.ToMinString()}");

                diagnostics.Add(new DiagnosticInfo
                {
                    severity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Error,
                    range = range,
                    message = exception.Message,
                });

                if (exception.InnerException is ProgrammingLanguage.Errors.Exception innerException)
                {
                    range = innerException.Position;

                    diagnostics.Add(new DiagnosticInfo
                    {
                        severity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Error,
                        range = range,
                        message = innerException.Message,
                    });
                }
            }

            for (int i = diagnostics.Count - 1; i >= 0; i--) if (diagnostics[i].range.ToMinString() == "0:0") diagnostics.RemoveAt(i);

            App.Interface.PublishDiagnostics(e.Uri, diagnostics.ToArray());
        }

        CompletionInfo[] IDocument.Completion(DocumentPositionContextEventArgs e)
        {
            Logger.Log($"Completion()");

            return Array.Empty<CompletionInfo>();
        }

        HoverInfo IDocument.Hover(DocumentPositionEventArgs e)
        {
            Logger.Log($"Hover({e.Position.ToMinString()})");

            HoverInfo result = null;

            /*
            static HoverContent InfoFunctionDefinition(FunctionDefinition funcDef, bool IsDefinition)
            {
                var text = $"{funcDef.Type} {funcDef.Identifier.Content}(";

                bool addComma = false;

                for (int i = 0; i < funcDef.Parameters.Length; i++)
                {
                    if (addComma) text += ", ";

                    if (funcDef.Parameters[i].withThisKeyword)
                    { text = $"{funcDef.Type} {funcDef.Parameters[i].Type}.{funcDef.Identifier.Content}("; continue; }

                    text += $"{funcDef.Parameters[i].Type} {funcDef.Parameters[i].Identifier}";
                    addComma = true;
                }

                text += ")";

                return new HoverContent()
                {
                    Lang = "csharp",
                    Text = text,
                };
            }
            static HoverContent InfoFunction(AnalysedToken_Function function, bool IsDefinition)
            {
                string text = $"{function.Type} {function.Name}(";

                bool addComma = false;

                for (int i = 0; i < function.ParameterTypes.Length; i++)
                {
                    if (addComma) text += ", ";

                    text += $"{function.ParameterTypes[i]} {function.ParameterNames[i]}";
                    addComma = true;
                }

                text += ")";

                return new HoverContent()
                {
                    Lang = "csharp",
                    Text = text,
                };
            }
            static HoverContent InfoBuiltinFunction(AnalysedToken_Function function, bool IsDefinition)
            {
                string text = $"{function.Type} {function.Name}(";

                bool addComma = false;

                for (int i = 0; i < function.ParameterTypes.Length; i++)
                {
                    if (addComma) text += ", ";

                    text += $"{function.ParameterTypes[i]} {function.ParameterNames[i]}";
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
                    Text = $"struct {structDef.Name.Content}",
                };
            }
            static HoverContent InfoMethod(AnalysedToken_Method function, bool IsDefinition)
            {
                string text = $"{function.Type} {function.PrevType}{function.Name}(";

                bool addComma = false;

                for (int i = 0; i < function.ParameterTypes.Length; i++)
                {
                    if (addComma) text += ", ";

                    text += $"{function.ParameterTypes[i]} {function.ParameterNames[i]}";
                    addComma = true;
                }

                text += ")";

                return new HoverContent()
                {
                    Lang = "csharp",
                    Text = text,
                };
            }
            static HoverContent InfoBuiltinMethod(AnalysedToken_Method function, bool IsDefinition)
            {
                string text = $"{function.Type} {function.PrevType}{function.Name}(";

                bool addComma = false;

                for (int i = 0; i < function.ParameterTypes.Length; i++)
                {
                    if (addComma) text += ", ";

                    text += $"{function.ParameterTypes[i]} {function.ParameterNames[i]}";
                    addComma = true;
                }

                text += ")";

                return new HoverContent()
                {
                    Lang = "csharp",
                    Text = text,
                };
            }

            List<HoverContent> result = new();

            if (AnalysisResult.ParserFatalError == null && AnalysisResult.Parsed)
            {
                {
                    Range<SinglePosition> range = new();

                    StatementFinder.GetAllStatement(AnalysisResult.ParserResult, statement =>
                    {
                        if (statement is Statement_FunctionCall functionCall)
                        {
                            if (!functionCall.Identifier.Position.Contains(pos)) return false;
                            if (functionCall.Identifier.Content == "return") return false;

                            Logger.Log($"Hover: Func. call found");

                            range = functionCall.Identifier.Position;

                            if (functionCall.Identifier is AnalysedToken_Function function)
                            {
                                switch (function.Kind)
                                {
                                    case FunctionKind.UserDefined:
                                        result.Add(InfoFunction(function, false));
                                        break;
                                    case FunctionKind.Builtin:
                                        result.Add(InfoBuiltinFunction(function, false));
                                        break;
                                    default: break;
                                }
                                return true;
                            }
                            else if (functionCall.Identifier is AnalysedToken_Method method)
                            {
                                switch (method.Kind)
                                {
                                    case FunctionKind.UserDefined:
                                        result.Add(InfoMethod(method, false));
                                        break;
                                    case FunctionKind.Builtin:
                                        result.Add(InfoBuiltinMethod(method, false));
                                        break;
                                    default: break;
                                }
                                return true;
                            }

                            return true;
                        }
                        else if (statement is Statement_Variable variable)
                        {
                            if (!variable.VariableName.Position.Contains(pos)) return false;

                            Logger.Log($"Hover: Variable found {variable.VariableName}");

                            range = variable.VariableName.Position;

                            if (variable.VariableName is AnalysedToken_Variable variable1)
                            {
                                switch (variable1.Kind)
                                {
                                    case VariableKind.Local:
                                        result.Add(new()
                                        {
                                            Lang = "csharp",
                                            Text = $"{variable1.Type} {variable1.Name}; // Local Variable",
                                        });
                                        break;
                                    case VariableKind.Global:
                                        result.Add(new()
                                        {
                                            Lang = "csharp",
                                            Text = $"{variable1.Type} {variable1.Name}; // Global Variable",
                                        });
                                        break;
                                    case VariableKind.Parameter:
                                        result.Add(new()
                                        {
                                            Lang = "csharp",
                                            Text = $"{variable1.Type} {variable1.Name} // Parameter",
                                        });
                                        break;
                                    default:
                                        break;
                                }
                            }
                            else
                            {
                                result.Add(new()
                                {
                                    Lang = "csharp",
                                    Text = $"var {variable.VariableName.Content};",
                                });
                            }

                            return true;
                        }
                        else if (statement is Statement_NewVariable newVariable)
                        {
                            if (!newVariable.VariableName.Position.Contains(pos)) return false;

                            Logger.Log($"Hover: NewVariable found {newVariable.VariableName}");

                            range = newVariable.VariableName.Position;

                            result.Add(new()
                            {
                                Lang = "csharp",
                                Text = $"{newVariable.Type} {newVariable.VariableName.Content}; // Variable",
                            });

                            return true;
                        }
                        else if (statement is Statement_Field field)
                        {
                            if (!field.FieldName.Position.Contains(pos)) return false;

                            Logger.Log($"Hover: Field found {field.FieldName}");

                            range = field.FieldName.Position;

                            if (field.FieldName is AnalysedToken_Field field1)
                            {
                                switch (field1.Kind)
                                {
                                    case ComplexTypeKind.Struct:
                                        result.Add(new()
                                        {
                                            Lang = "csharp",
                                            Text = $"struct {"?"}\n{{\n  {field1.Type} {field1.Name}; // Field\n}}",
                                        });
                                        break;
                                    case ComplexTypeKind.Class:
                                        result.Add(new()
                                        {
                                            Lang = "csharp",
                                            Text = $"class {"?"}\n{{\n  {field1.Type} {field1.Name}; // Field\n}}",
                                        });
                                        break;
                                    default: break;
                                }
                            }
                            else
                            {
                                result.Add(new()
                                {
                                    Lang = "csharp",
                                    Text = $"var {field.FieldName.Content}; // Field",
                                });
                            }

                            return true;
                        }

                        return false;
                    });

                    if (result.Count > 0 && !range.IsUnset())
                    {
                        return new HoverInfo()
                        {
                            Range = range,
                            Contents = result.ToArray(),
                        };
                    }
                }

                foreach (var funcDef in AnalysisResult.ParserResult.Functions)
                {
                    if (!funcDef.Identifier.Position.Contains(pos))
                    {
                        foreach (var paramDef in funcDef.Parameters)
                        {
                            if (!paramDef.Identifier.Position.Contains(pos)) continue;
                            Logger.Log($"Hover: Param. def. found");

                            result.Add(new()
                            {
                                Lang = "csharp",
                                Text = $"{paramDef.Type.Identifier} {paramDef.Identifier.Content}; // Parameter",
                            });

                            return new HoverInfo()
                            {
                                Range = paramDef.Identifier.Position,
                                Contents = result.ToArray(),
                            };
                        }
                        continue;
                    }

                    Logger.Log($"Hover: Func. def. found");

                    result.Add(InfoFunctionDefinition(funcDef, true));

                    return new HoverInfo()
                    {
                        Range = funcDef.Identifier.Position,
                        Contents = result.ToArray(),
                    };
                }

                foreach (var pair in AnalysisResult.ParserResult.Structs)
                {
                    var structDef = pair;
                    if (!structDef.Name.Position.Contains(pos))
                    {
                        foreach (var field in structDef.Fields)
                        {
                            if (!field.Identifier.Position.Contains(pos)) continue;

                            Logger.Log($"Hover: Struct field dec. found, {field.Identifier.Position}");

                            result.Add(new()
                            {
                                Lang = "csharp",
                                Text = $"struct {structDef.Name.Content}\n{{\n  {field.Type} {field.Identifier.Content}; // Field\n}}",
                            });

                            return new HoverInfo()
                            {
                                Range = field.Identifier.Position,
                                Contents = result.ToArray(),
                            };
                        }
                        continue;
                    }

                    Logger.Log($"Hover: Struct def. found, {structDef.Name.Position}");

                    result.Add(InfoStructDefinition(structDef));

                    return new HoverInfo()
                    {
                        Range = structDef.Name.Position,
                        Contents = result.ToArray(),
                    };
                }

                foreach (var pair in AnalysisResult.ParserResult.Classes)
                {
                    var classDef = pair;
                    if (!classDef.Name.Position.Contains(pos))
                    {
                        foreach (var field in classDef.Fields)
                        {
                            if (!field.Identifier.Position.Contains(pos)) continue;

                            Logger.Log($"Hover: Class field dec. found, {field.Identifier.Position}");

                            result.Add(new()
                            {
                                Lang = "csharp",
                                Text = $"class {classDef.Name.Content}\n{{\n  {field.Type} {field.Identifier.Content}; // Field\n}}",
                            });

                            return new HoverInfo()
                            {
                                Range = field.Identifier.Position,
                                Contents = result.ToArray(),
                            };
                        }
                        continue;
                    }

                    Logger.Log($"Hover: Class def. found, {classDef.Name.Position}");

                    result.Add(new HoverContent()
                    {
                        Lang = "csharp",
                        Text = $"class {classDef.Name.Content}",
                    });

                    return new HoverInfo()
                    {
                        Range = classDef.Name.Position,
                        Contents = result.ToArray(),
                    };
                }

                for (int i = 0; i < AnalysisResult.ParserResult.Usings.Length; i++)
                {
                    UsingDefinition usingItem = AnalysisResult.ParserResult.Usings[i];

                    foreach (var pathToken in usingItem.Path)
                    {
                        if (pathToken.Position.Contains(pos))
                        {
                            Logger.Log($"Hover: Using def. found, {pathToken.Position}");

                            if (usingItem.Path.Length == 1 && pathToken.TokenType == TokenType.LITERAL_STRING)
                            {
                                result.Add(new HoverContent()
                                {
                                    Lang = "csharp",
                                    Text = $"using \"{pathToken.Content}\";",
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
            */

            return result;
        }

        CodeLensInfo[] IDocument.CodeLens(DocumentEventArgs e)
        {
            List<CodeLensInfo> result = new();

            if (Functions != null)
            {
                foreach (var function in Functions)
                {
                    if (function.CompiledAttributes.ContainsKey("CodeEntry"))
                    { result.Add(new CodeLensInfo($"This is the code entry", function.Identifier)); }

                    result.Add(new CodeLensInfo($"{function.TimesUsedTotal} reference", function.Identifier));
                }
            }

            /*
            if (GeneralFunctions != null)
            {
                foreach (var function in GeneralFunctions)
                {
                    result.Add(new CodeLensInfo($"{function.TimesUsedTotal} reference", function.Identifier));
                }
            }

            if (Classes != null)
            {
                foreach (var @class in Classes)
                {
                    result.Add(new CodeLensInfo($"{@class.References.Count} reference", @class.Name));
                }
            }

            if (Structs != null)
            {
                foreach (var @struct in Structs)
                {
                    result.Add(new CodeLensInfo($"{@struct.References.Count} reference", @struct.Name));
                }
            }
            */

            return result.ToArray();
        }

        SingleOrArray<FilePosition>? IDocument.GotoDefinition(DocumentPositionEventArgs e)
        {
            Logger.Log($"GotoDefinition({e.Position.ToMinString()})");

            return null;
        }

        SymbolInformationInfo[] IDocument.Symbols(DocumentEventArgs e)
        {
            Logger.Log($"Symbols()");

            return Array.Empty<SymbolInformationInfo>();
        }

        FilePosition[] IDocument.References(FindReferencesEventArgs e)
        {
            Logger.Log($"References({e.Position.ToMinString()})");

            return Array.Empty<FilePosition>();
        }

        SignatureHelpInfo IDocument.SignatureHelp(SignatureHelpEventArgs e)
        {
            return null;
        }

        SemanticToken[] IDocument.GetSemanticTokens(DocumentEventArgs e)
        {
            if (Tokens == null) return Array.Empty<SemanticToken>();

            List<SemanticToken> result = new();

            foreach (var token in Tokens)
            {
                switch (token.AnalysedType)
                {
                    case TokenAnalysedType.Attribute:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Type));
                        break;
                    case TokenAnalysedType.Type:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Type));
                        break;
                    case TokenAnalysedType.Struct:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Struct));
                        break;
                    case TokenAnalysedType.Keyword:
                        break;
                    case TokenAnalysedType.FunctionName:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Function));
                        break;
                    case TokenAnalysedType.VariableName:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Variable));
                        break;
                    case TokenAnalysedType.ParameterName:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Parameter));
                        break;
                    case TokenAnalysedType.Namespace:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Namespace));
                        break;
                    case TokenAnalysedType.Library:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Namespace));
                        break;
                    case TokenAnalysedType.Class:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Class));
                        break;
                    case TokenAnalysedType.Statement:
                        break;
                    case TokenAnalysedType.BuiltinType:
                        break;
                    case TokenAnalysedType.Enum:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.Enum));
                        break;
                    case TokenAnalysedType.EnumMember:
                        result.Add(new SemanticToken(token,
                            OmniSharp.Extensions.LanguageServer.Protocol.Models.SemanticTokenType.EnumMember));
                        break;

                    case TokenAnalysedType.Hash:
                    case TokenAnalysedType.HashParameter:

                    case TokenAnalysedType.None:
                    case TokenAnalysedType.FieldName:
                    default:
                        break;
                }
            }

            return result.ToArray();
        }
    }
}
