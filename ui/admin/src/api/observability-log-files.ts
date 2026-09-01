import {
  observabilityDownloadLogFile,
  observabilityListLogFiles,
  observabilityTailLogFile,
  type LogFileSummary,
  type LogFileTail
} from '@fullnet/client-contracts';
import { http } from './http';

/** 列出当前实例允许浏览的日志文件摘要，供日志文件管理页初始化目录。 */
export function listObservabilityLogFiles(
  signal?: AbortSignal
): Promise<LogFileSummary[]> {
  return observabilityListLogFiles(http, {}, signal);
}

/** 读取日志文件尾部片段，并对返回行数与字节数做显式上限约束。 */
export function tailObservabilityLogFile(
  id: string,
  maximumLines = 200,
  maximumBytes = 262144,
  signal?: AbortSignal
): Promise<LogFileTail> {
  return observabilityTailLogFile(
    http,
    { id, maximumLines, maximumBytes },
    signal
  );
}

/** 下载指定日志文件的二进制内容，交由调用方决定保存或预览方式。 */
export function downloadObservabilityLogFile(
  id: string,
  signal?: AbortSignal
): Promise<Blob> {
  return observabilityDownloadLogFile(http, { id }, signal);
}

/** 导出日志文件摘要与尾部片段模型，供日志目录页、尾部预览面板与下载流程共享同一契约。 */
export type { LogFileSummary, LogFileTail };
