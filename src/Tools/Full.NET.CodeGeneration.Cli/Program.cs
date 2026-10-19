/// <summary>代码生成 CLI 入口；标准输出使用 UTF-8，避免中文诊断乱码。</summary>
using System.Text;
using Full.NET.CodeGeneration.Cli;

// 管道消费者统一按 UTF-8 读取，避免 Windows 系统代码页破坏中文诊断。
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

return await CodeGenerationCli.RunAsync(
    args,
    Console.Out,
    Console.Error);
