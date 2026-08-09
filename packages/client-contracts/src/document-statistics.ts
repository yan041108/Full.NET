export interface HostDocumentStatisticsSummaryResponse {
  totalItems: number;
  totalVersions: number;
  totalSizeKb: number;
  totalSizeInfo: string;
}

export interface HostDocumentStatisticsTypeItem {
  extension: string | null;
  count: number;
  totalSizeKb: number;
}

export interface HostDocumentStatisticsCategoryItem {
  categoryId: string | null;
  categoryName: string | null;
  count: number;
}

export interface HostDocumentStatisticsResponse {
  summary: HostDocumentStatisticsSummaryResponse;
  byType: HostDocumentStatisticsTypeItem[];
  byCategory: HostDocumentStatisticsCategoryItem[];
  shareCount: number;
  todayAccessCount: number;
  todayDownloadCount: number;
  todayCreatedCount: number;
  recycleBinCount: number;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isNullableGuid(value: unknown): value is string | null {
  return value === null || isGuid(value);
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string';
}

export function isHostDocumentStatisticsSummaryResponse(value: unknown): value is HostDocumentStatisticsSummaryResponse {
  return isRecord(value)
    && Number.isInteger(value.totalItems)
    && Number.isInteger(value.totalVersions)
    && typeof value.totalSizeKb === 'number'
    && typeof value.totalSizeInfo === 'string';
}

export function isHostDocumentStatisticsTypeItem(value: unknown): value is HostDocumentStatisticsTypeItem {
  return isRecord(value)
    && isNullableString(value.extension)
    && Number.isInteger(value.count)
    && typeof value.totalSizeKb === 'number';
}

export function isHostDocumentStatisticsCategoryItem(value: unknown): value is HostDocumentStatisticsCategoryItem {
  return isRecord(value)
    && isNullableGuid(value.categoryId)
    && isNullableString(value.categoryName)
    && Number.isInteger(value.count);
}

export function isHostDocumentStatisticsResponse(value: unknown): value is HostDocumentStatisticsResponse {
  return isRecord(value)
    && isHostDocumentStatisticsSummaryResponse(value.summary)
    && Array.isArray(value.byType)
    && value.byType.every(isHostDocumentStatisticsTypeItem)
    && Array.isArray(value.byCategory)
    && value.byCategory.every(isHostDocumentStatisticsCategoryItem)
    && Number.isInteger(value.shareCount)
    && Number.isInteger(value.todayAccessCount)
    && Number.isInteger(value.todayDownloadCount)
    && Number.isInteger(value.todayCreatedCount)
    && Number.isInteger(value.recycleBinCount);
}
