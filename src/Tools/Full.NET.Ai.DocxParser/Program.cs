using Full.NET.Ai.DocxParser;

// 解析工具只接收标准流字节，不接受路径、网络、凭据或放宽预算的参数。
if (args.Length != 0) { Console.Error.WriteLine("fn_ai_docx_invalid_arguments"); return 2; }
return await DocxParser.RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput(), Console.Error);
