using System.Resources;
using Full.NET.Hosting.Api;

namespace Full.NET.Modules.ImportExport.Resources;

/// <summary>列映射、个人配置与持久任务授权的稳定错误码资源。</summary>
internal sealed class ImportExportErrorResourceSource() : ResourceManagerErrorResourceSource(
    "import_export.", new ResourceManager("Full.NET.Modules.ImportExport.Resources.ImportExportErrors", typeof(ImportExportErrorResourceSource).Assembly));
