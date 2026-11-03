import { aiGetKnowledgePublicationHead, aiGetKnowledgePublication, aiRequestKnowledgePublication,
  aiReviewKnowledgePublication, aiPublishKnowledgePublication, aiWithdrawKnowledgePublication,
  aiListKnowledgeFolders, aiCreateKnowledgeFolder, aiUpdateKnowledgeFolder, aiDeleteKnowledgeFolder,
  aiListKnowledgeDirectory, aiSetKnowledgeDirectoryBatch,
  type RequestAiKnowledgePublication, type ReviewAiKnowledgePublication, type PublishAiKnowledgePublication,
  type WithdrawAiKnowledgePublication, type SaveAiKnowledgeFolder, type SetAiKnowledgeDirectoryBatch
} from '@fullnet/client-contracts';
import { http } from './http';

type Document = { knowledgeBaseId: string; documentId: string };
export const readPublicationHead = (ids: Document, signal?: AbortSignal) => aiGetKnowledgePublicationHead(http, ids, signal);
export const readPublication = (ids: Document, publicationId: string, signal?: AbortSignal) => aiGetKnowledgePublication(http, { ...ids, publicationId }, signal);
export const requestPublication = (ids: Document, body: RequestAiKnowledgePublication, signal?: AbortSignal) => aiRequestKnowledgePublication(http, { ...ids, body }, signal);
export const reviewPublication = (ids: Document, publicationId: string, body: ReviewAiKnowledgePublication, signal?: AbortSignal) => aiReviewKnowledgePublication(http, { ...ids, publicationId, body }, signal);
export const publishPublication = (ids: Document, publicationId: string, body: PublishAiKnowledgePublication, signal?: AbortSignal) => aiPublishKnowledgePublication(http, { ...ids, publicationId, body }, signal);
export const withdrawPublication = (ids: Document, body: WithdrawAiKnowledgePublication, signal?: AbortSignal) => aiWithdrawKnowledgePublication(http, { ...ids, body }, signal);
export const listFolders = (knowledgeBaseId: string, signal?: AbortSignal) => aiListKnowledgeFolders(http, { knowledgeBaseId }, signal);
export const createFolder = (knowledgeBaseId: string, body: SaveAiKnowledgeFolder, signal?: AbortSignal) => aiCreateKnowledgeFolder(http, { knowledgeBaseId, body }, signal);
export const updateFolder = (knowledgeBaseId: string, folderId: string, body: SaveAiKnowledgeFolder, signal?: AbortSignal) => aiUpdateKnowledgeFolder(http, { knowledgeBaseId, folderId, body }, signal);
export const deleteFolder = (knowledgeBaseId: string, folderId: string, version: number, signal?: AbortSignal) => aiDeleteKnowledgeFolder(http, { knowledgeBaseId, folderId, body: { version } }, signal);
export const listDirectory = (knowledgeBaseId: string, query: { folderId?: string; tag?: string; responsibleUserId?: string; state?: string; page: number }, signal?: AbortSignal) => aiListKnowledgeDirectory(http, { knowledgeBaseId, ...query, pageSize: 20 }, signal);
export const setDirectoryBatch = (knowledgeBaseId: string, body: SetAiKnowledgeDirectoryBatch, signal?: AbortSignal) => aiSetKnowledgeDirectoryBatch(http, { knowledgeBaseId, body }, signal);
