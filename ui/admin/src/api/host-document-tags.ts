import {
  documentHostCreateTag,
  documentHostDeleteTag,
  documentHostListTags,
  documentHostUpdateTag,
  isHostDocumentTagResponse,
  isHostDocumentTagResponseList,
  type HostDocumentTagResponse
} from '@fullnet/client-contracts';
import { http } from './http';

export async function listDocumentTags(
  signal?: AbortSignal
): Promise<HostDocumentTagResponse[]> {
  const value = await documentHostListTags(http, {}, signal);
  if (!isHostDocumentTagResponseList(value)) {
    throw new Error('client.invalid_document_tag_list');
  }
  return value;
}

export async function createDocumentTag(
  name: string,
  code: string | null,
  icon: string | null,
  color: string | null,
  description: string | null,
  signal?: AbortSignal
): Promise<HostDocumentTagResponse> {
  const value = await documentHostCreateTag(
    http,
    { body: { name, code, icon, color, description } },
    signal
  );
  if (!isHostDocumentTagResponse(value)) {
    throw new Error('client.invalid_document_tag');
  }
  return value;
}

export async function updateDocumentTag(
  id: string,
  name: string,
  code: string | null,
  icon: string | null,
  color: string | null,
  description: string | null,
  version: number,
  signal?: AbortSignal
): Promise<HostDocumentTagResponse> {
  const value = await documentHostUpdateTag(
    http,
    {
      tagId: id,
      body: { name, code, icon, color, description, version }
    },
    signal
  );
  if (!isHostDocumentTagResponse(value)) {
    throw new Error('client.invalid_document_tag');
  }
  return value;
}

export async function deleteDocumentTag(
  id: string,
  version: number,
  signal?: AbortSignal
): Promise<boolean> {
  return documentHostDeleteTag(
    http,
    {
      tagId: id,
      body: { version }
    },
    signal
  );
}

export type { HostDocumentTagResponse };
