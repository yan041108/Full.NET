import {
  observabilityDownloadLogFile,
  observabilityListLogFiles,
  observabilityTailLogFile,
  type LogFileSummary,
  type LogFileTail
} from '@fullnet/client-contracts';
import { http } from './http';

export function listObservabilityLogFiles(
  signal?: AbortSignal
): Promise<LogFileSummary[]> {
  return observabilityListLogFiles(http, {}, signal);
}

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

export function downloadObservabilityLogFile(
  id: string,
  signal?: AbortSignal
): Promise<Blob> {
  return observabilityDownloadLogFile(http, { id }, signal);
}
