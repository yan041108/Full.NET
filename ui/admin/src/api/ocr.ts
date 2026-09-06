import {
  isOcrIdCardTask,
  isOcrIdCardTaskPage,
  isOcrProviderConfig,
  type ConfirmOcrIdCardTaskRequest,
  type CreateOcrIdCardTaskRequest,
  type OcrIdCardTask,
  type OcrIdCardTaskPage,
  type OcrProviderConfig,
  type TestOcrProviderConfigResult,
  type UpdateOcrProviderConfigRequest
} from '@fullnet/client-contracts';
import { request } from './http';

export const PADDLE_OCR_ID_CARD_PROVIDER_KEY = 'paddle_ocr_id_card';

export async function getOcrProviderConfig(
  providerKey: string,
  signal?: AbortSignal
): Promise<OcrProviderConfig> {
  const value = await request<unknown>(
    `/api/v1/ocr/provider-configs/${encodeURIComponent(providerKey)}`,
    { method: 'GET' },
    signal
  );
  if (!isOcrProviderConfig(value)) {
    throw new Error('client.invalid_ocr_provider_config');
  }
  return value;
}

export async function updateOcrProviderConfig(
  providerKey: string,
  body: UpdateOcrProviderConfigRequest,
  signal?: AbortSignal
): Promise<OcrProviderConfig> {
  const value = await request<unknown>(
    `/api/v1/ocr/provider-configs/${encodeURIComponent(providerKey)}`,
    { method: 'PUT', body },
    signal
  );
  if (!isOcrProviderConfig(value)) {
    throw new Error('client.invalid_ocr_provider_config');
  }
  return value;
}

export async function testOcrProviderConfig(
  providerKey: string,
  signal?: AbortSignal
): Promise<TestOcrProviderConfigResult> {
  const value = await request<unknown>(
    `/api/v1/ocr/provider-configs/${encodeURIComponent(providerKey)}/test`,
    { method: 'POST' },
    signal
  );
  if (
    typeof value !== 'object' ||
    value === null ||
    typeof (value as TestOcrProviderConfigResult).succeeded !== 'boolean' ||
    typeof (value as TestOcrProviderConfigResult).message !== 'string'
  ) {
    throw new Error('client.invalid_ocr_provider_test_result');
  }
  return value as TestOcrProviderConfigResult;
}

export async function listOcrIdCardTasks(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<OcrIdCardTaskPage> {
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize)
  });
  const value = await request<unknown>(
    `/api/v1/ocr/id-card-tasks?${params.toString()}`,
    { method: 'GET' },
    signal
  );
  if (!isOcrIdCardTaskPage(value)) {
    throw new Error('client.invalid_ocr_id_card_task_page');
  }
  return value;
}

export async function getOcrIdCardTask(
  taskId: string,
  signal?: AbortSignal
): Promise<OcrIdCardTask> {
  const value = await request<unknown>(
    `/api/v1/ocr/id-card-tasks/${encodeURIComponent(taskId)}`,
    { method: 'GET' },
    signal
  );
  if (!isOcrIdCardTask(value)) {
    throw new Error('client.invalid_ocr_id_card_task');
  }
  return value;
}

export async function createOcrIdCardTask(
  body: CreateOcrIdCardTaskRequest,
  signal?: AbortSignal
): Promise<OcrIdCardTask> {
  const value = await request<unknown>('/api/v1/ocr/id-card-tasks', { method: 'POST', body }, signal);
  if (!isOcrIdCardTask(value)) {
    throw new Error('client.invalid_ocr_id_card_task');
  }
  return value;
}

export async function confirmOcrIdCardTask(
  taskId: string,
  body: ConfirmOcrIdCardTaskRequest,
  signal?: AbortSignal
): Promise<OcrIdCardTask> {
  const value = await request<unknown>(
    `/api/v1/ocr/id-card-tasks/${encodeURIComponent(taskId)}/confirm`,
    { method: 'POST', body },
    signal
  );
  if (!isOcrIdCardTask(value)) {
    throw new Error('client.invalid_ocr_id_card_task');
  }
  return value;
}

export async function rejectOcrIdCardTask(
  taskId: string,
  version: number,
  signal?: AbortSignal
): Promise<OcrIdCardTask> {
  const value = await request<unknown>(
    `/api/v1/ocr/id-card-tasks/${encodeURIComponent(taskId)}/reject`,
    {
      method: 'POST',
      body: {
        name: '',
        idNumber: '',
        version
      }
    },
    signal
  );
  if (!isOcrIdCardTask(value)) {
    throw new Error('client.invalid_ocr_id_card_task');
  }
  return value;
}
