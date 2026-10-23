import type { SupportedLocale } from './locale.js';

const messages = {
  "zh-CN": {
    "fileHint": "支持文本、PDF、Word、表格、演示文稿、HTML 和图片。DOCX 上限 5 MiB，其他格式上限 8 MiB；上传后仍需完成索引。",
    "encoding": "文本编码",
    "noText": "未提取到可索引文字，请检查原文件。",
    "lowConfidence": "识别文字置信度不足，整份文件未进入索引。",
    "delimiter": "CSV 分隔符",
    "comma": "逗号",
    "tab": "制表符",
    "semicolon": "分号",
    "header": "首条记录为表头",
    "hidden": "包含隐藏工作表、行列或幻灯片",
    "grid": "{sheet} · 行 {row} · 列 {column}",
    "slide": "幻灯片 {slide} · 形状 {shape}",
    "notes": "幻灯片 {slide} · 备注 {shape}",
    "image": "帧 {frame} · 区域 ({x}, {y}, {width} × {height}) · 置信度 {confidence}%",
    "html": "HTML 节点 {path}",
    "policyHint": "解析选项在上传时冻结；修改选项会创建新的上传请求。"
  },
  "en-US": {
    "fileHint": "Text, PDF, Word, spreadsheets, presentations, HTML and images. DOCX: up to 5 MiB; other formats: up to 8 MiB. Indexing is required after upload.",
    "encoding": "Text encoding",
    "noText": "No indexable text was extracted. Check the source file.",
    "lowConfidence": "OCR confidence is insufficient. The whole file was excluded from the index.",
    "delimiter": "CSV delimiter",
    "comma": "Comma",
    "tab": "Tab",
    "semicolon": "Semicolon",
    "header": "First record is a header",
    "hidden": "Include hidden sheets, rows, columns or slides",
    "grid": "{sheet} · Row {row} · Column {column}",
    "slide": "Slide {slide} · Shape {shape}",
    "notes": "Slide {slide} · Notes {shape}",
    "image": "Frame {frame} · Region ({x}, {y}, {width} × {height}) · Confidence {confidence}%",
    "html": "HTML node {path}",
    "policyHint": "Parsing options are frozen at upload. Changing options creates a new upload request."
  }
} as const;

/** 使用服务器来源坐标或用户解析选项生成纯文本标签。 */
export function translateKnowledgeFormats(locale: SupportedLocale, key: keyof typeof messages['zh-CN'], parameters: Record<string, string | number> = {}): string {
  return messages[locale][key].replace(/\{([a-zA-Z]+)\}/g, (placeholder, name: string) => name in parameters ? String(parameters[name]) : placeholder);
}
