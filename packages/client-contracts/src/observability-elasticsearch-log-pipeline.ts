export type LogDeliveryStatus =
  | 'legacy-console'
  | 'legacy-direct'
  | 'disabled'
  | 'external-collector'
  | 'application-kafka';

export type LogDeliveryConfirmationBoundary =
  | 'configuration-only'
  | 'sink-registered-only';

export interface ElasticsearchLogPipelineHealth {
  adapterKind: string;
  isEnabled: boolean;
  isSinkRegistered: boolean;
  indexFormat: string;
  nodeEndpoints: string[];
  openTelemetryOtlpEndpointConfigured: boolean;
  pipelineNotice: string;
  clusterStatus: string;
  clusterName: string | null;
  numberOfNodes: number | null;
  probeErrorMessage: string | null;
  deliveryStatus?: LogDeliveryStatus;
  deliveryConfirmationBoundary?: LogDeliveryConfirmationBoundary;
}

export function isElasticsearchLogPipelineHealth(
  value: unknown
): value is ElasticsearchLogPipelineHealth {
  return isRecord(value)
    && typeof value.adapterKind === 'string'
    && typeof value.isEnabled === 'boolean'
    && typeof value.isSinkRegistered === 'boolean'
    && typeof value.indexFormat === 'string'
    && Array.isArray(value.nodeEndpoints)
    && value.nodeEndpoints.every(item => typeof item === 'string')
    && typeof value.openTelemetryOtlpEndpointConfigured === 'boolean'
    && typeof value.pipelineNotice === 'string'
    && typeof value.clusterStatus === 'string'
    && (value.clusterName === null || typeof value.clusterName === 'string')
    && (value.numberOfNodes === null || typeof value.numberOfNodes === 'number')
    && (value.probeErrorMessage === null || typeof value.probeErrorMessage === 'string')
    && ((value.deliveryStatus === undefined && value.deliveryConfirmationBoundary === undefined)
      || (isLogDeliveryStatus(value.deliveryStatus)
        && isLogDeliveryConfirmationBoundary(value.deliveryConfirmationBoundary)));
}

function isLogDeliveryStatus(value: unknown): value is LogDeliveryStatus {
  return value === 'legacy-console'
    || value === 'legacy-direct'
    || value === 'disabled'
    || value === 'external-collector'
    || value === 'application-kafka';
}

function isLogDeliveryConfirmationBoundary(
  value: unknown
): value is LogDeliveryConfirmationBoundary {
  return value === 'configuration-only' || value === 'sink-registered-only';
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
