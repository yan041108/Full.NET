namespace Full.NET.Modules.Ai.Contracts;

/// <summary>服务器验证的多格式来源坐标；不包含正文、公式、缓存值或识别模型内部参数。</summary>
public sealed record AiKnowledgeSourceLocationResponse(int BlockSequence, string Kind, int? PageNumber,
    int? ParagraphNumber, int? TableNumber, int? RowNumber, int? CellNumber,
    AiKnowledgeGridLocationResponse? Grid, AiKnowledgeSlideLocationResponse? Slide,
    string? HtmlPath, AiKnowledgeImageLocationResponse? Image);

/// <summary>原生逻辑记录或工作表坐标，行列号从一开始。</summary>
public sealed record AiKnowledgeGridLocationResponse(int SheetNumber, string SheetName, int Row, int Column);

/// <summary>原生幻灯片、形状及可选表格单元格；备注由外层位置类型区分。</summary>
public sealed record AiKnowledgeSlideLocationResponse(int SlideNumber, int ShapeNumber, int? RowNumber,
    int? ColumnNumber, int RowSpan, int ColumnSpan);

/// <summary>规范化图片中的实际像素框及置信度；嵌入图片保留原文锚点。</summary>
public sealed record AiKnowledgeImageLocationResponse(int FrameNumber, int ImageWidth, int ImageHeight,
    int X, int Y, int Width, int Height, double Confidence, int? TableNumber, int? RowNumber,
    int? ColumnNumber, AiKnowledgeSourceLocationResponse? Parent);
