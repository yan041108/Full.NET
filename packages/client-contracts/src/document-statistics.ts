export interface HostDocumentTypeStatisticsItem {
  documentType: number;
  count: number;
  sizeKb: number;
}

export interface HostDocumentCategoryStatisticsItem {
  categoryId: string;
  categoryName: string;
  count: number;
  sizeKb: number;
}

export interface HostDocumentStatisticsResponse {
  totalCount: number;
  totalSizeKb: number;
  totalSizeInfo: string;
  todayUploadCount: number;
  todayDownloadCount: number;
  todayAccessCount: number;
  totalShareCount: number;
  recycleCount: number;
  typeStatistics: HostDocumentTypeStatisticsItem[];
  categoryStatistics: HostDocumentCategoryStatisticsItem[];
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function isHostDocumentTypeStatisticsItem(value: unknown): value is HostDocumentTypeStatisticsItem {
  return isRecord(value)
    && Number.isInteger(value.documentType)
    && Number.isInteger(value.count)
    && typeof value.sizeKb === 'number';
}

export function isHostDocumentCategoryStatisticsItem(value: unknown): value is HostDocumentCategoryStatisticsItem {
  return isRecord(value)
    && typeof value.categoryId === 'string'
    && typeof value.categoryName === 'string'
    && Number.isInteger(value.count)
    && typeof value.sizeKb === 'number';
}

export function isHostDocumentStatisticsResponse(value: unknown): value is HostDocumentStatisticsResponse {
  return isRecord(value)
    && Number.isInteger(value.totalCount)
    && typeof value.totalSizeKb === 'number'
    && typeof value.totalSizeInfo === 'string'
    && Number.isInteger(value.todayUploadCount)
    && Number.isInteger(value.todayDownloadCount)
    && Number.isInteger(value.todayAccessCount)
    && Number.isInteger(value.totalShareCount)
    && Number.isInteger(value.recycleCount)
    && Array.isArray(value.typeStatistics)
    && value.typeStatistics.every(isHostDocumentTypeStatisticsItem)
    && Array.isArray(value.categoryStatistics)
    && value.categoryStatistics.every(isHostDocumentCategoryStatisticsItem);
}
