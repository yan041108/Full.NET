using System.Text;
using Full.NET.CodeGeneration.Cli;

// 命令行诊断必须按 UTF-8 写入重定向管道，避免 Windows 活动代码页破坏机器可读错误。
Console.OutputEncoding = Encoding.UTF8;
return await CodeGenerationCli.RunAsync(
    args,
    Console.Out,
    Console.Error);
