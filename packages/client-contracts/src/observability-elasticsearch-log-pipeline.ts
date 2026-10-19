export type LogDeliveryStatus = 'local' | 'external-collector' | 'application-kafka';
export type LogDeliveryConfirmationBoundary = 'configuration-only';

export interface ElasticsearchLogPipelineHealth {
  deliveryStatus: LogDeliveryStatus;
  deliveryConfirmationBoundary: LogDeliveryConfirmationBoundary;
  elasticsearchQueryEnabled: boolean;
  elasticsearchEndpoint: string | null;
  elasticsearchIndexPattern: string | null;
  openTelemetryOtlpEndpointConfigured: boolean;
}

export function isElasticsearchLogPipelineHealth(value: unknown): value is ElasticsearchLogPipelineHealth {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return false;
  const item = value as Record<string, unknown>;
  return (item.deliveryStatus === 'local' || item.deliveryStatus === 'external-collector'
      || item.deliveryStatus === 'application-kafka')
    && item.deliveryConfirmationBoundary === 'configuration-only'
    && typeof item.elasticsearchQueryEnabled === 'boolean'
    && (item.elasticsearchEndpoint === null || typeof item.elasticsearchEndpoint === 'string')
    && (item.elasticsearchIndexPattern === null || typeof item.elasticsearchIndexPattern === 'string')
    && typeof item.openTelemetryOtlpEndpointConfigured === 'boolean';
}
