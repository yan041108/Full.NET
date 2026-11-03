using System.Resources;
using Full.NET.Hosting.Api;

namespace Full.NET.Modules.Printing.Resources;

/// <summary>模板输入、发布和版式读取使用稳定机器码，响应按当前请求语言解释。</summary>
internal sealed class PrintingErrorResourceSource() : ResourceManagerErrorResourceSource("printing.",
    new ResourceManager("Full.NET.Modules.Printing.Resources.PrintingErrors", typeof(PrintingErrorResourceSource).Assembly));
