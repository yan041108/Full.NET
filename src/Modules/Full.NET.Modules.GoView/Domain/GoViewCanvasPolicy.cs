namespace Full.NET.Modules.GoView.Domain;

/// <summary>GoView 画布 JSON 大小与结构约束。</summary>
internal static class GoViewCanvasPolicy
{
    /// <summary>画布 JSON 最大字节数（UTF-8 编码后）。</summary>
    public const int MaxCanvasJsonLength = 2 * 1024 * 1024;

    /// <summary>新建项目时的默认空白画布。</summary>
    public const string DefaultCanvasJson =
        """{"width":1920,"height":1080,"backgroundColor":"#0a1628","components":[]}""";
}
