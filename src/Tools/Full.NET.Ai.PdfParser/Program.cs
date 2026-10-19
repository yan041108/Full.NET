/// <summary>沙箱 PDF 解析子进程入口；仅 stdin/stdout，不接受路径或网络参数。</summary>
using Full.NET.Ai.PdfParser;

// 解析进程只接收已授权字节，不接受路径、凭据、模型或可放宽预算的参数。
if (args.Length != 0) { Console.Error.WriteLine("fn_ai_pdf_invalid_arguments"); return 2; }
return await PdfParser.RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput(), Console.Error);
