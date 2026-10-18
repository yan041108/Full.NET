import { aiListKnowledgeIndexGrants, aiCreateKnowledgeIndexGrant, aiRevokeKnowledgeIndexGrant,
  type AiListKnowledgeIndexGrantsParameters, type AiCreateKnowledgeIndexGrantParameters, type AiRevokeKnowledgeIndexGrantParameters } from '@fullnet/client-contracts';
import { http } from './http';
export const listIndexGrants = (parameters: AiListKnowledgeIndexGrantsParameters, signal?: AbortSignal) => aiListKnowledgeIndexGrants(http, parameters, signal);
export const createIndexGrant = (parameters: AiCreateKnowledgeIndexGrantParameters, signal?: AbortSignal) => aiCreateKnowledgeIndexGrant(http, parameters, signal);
export const revokeIndexGrant = (parameters: AiRevokeKnowledgeIndexGrantParameters, signal?: AbortSignal) => aiRevokeKnowledgeIndexGrant(http, parameters, signal);
