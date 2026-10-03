// 知识库协议只从运行时 OpenAPI 生成；共享入口不复制 HTTP、权限或租户决策。
export {
  aiGetKnowledgeMembers,
  aiSetKnowledgeMembers,
  aiListKnowledgeBases,
  aiGetKnowledgeBase,
  aiCreateKnowledgeBase,
  aiUpdateKnowledgeBase,
  aiUpdateKnowledgePolicy,
  aiListKnowledgeDocuments,
  aiGetKnowledgeDocument,
  aiCreateKnowledgeDocument,
  aiUpdateKnowledgeDocument,
  aiDeleteKnowledgeDocument,
  aiGetKnowledgeDocumentMembers,
  aiSetKnowledgeDocumentMembers,
  aiUploadKnowledgeVersion,
  aiListKnowledgeVersions,
  aiDownloadKnowledgeSource,
  aiGetKnowledgeImport,
  aiRetryKnowledgeImport,
  aiCleanupKnowledgeImports
} from './generated/operations.generated.js';

export type {
  AiKnowledgeBaseResponse,
  AiKnowledgeMembersResponse,
  AiKnowledgeDocumentResponse,
  AiKnowledgeDocumentMembersResponse,
  AiKnowledgeVersionResponse,
  AiKnowledgeImportResponse,
  PagedResultOfAiKnowledgeBaseResponse,
  PagedResultOfAiKnowledgeDocumentResponse,
  PagedResultOfAiKnowledgeVersionResponse,
  CreateAiKnowledgeBaseRequest,
  UpdateAiKnowledgeBaseRequest,
  UpdateAiKnowledgePolicyRequest,
  SetAiKnowledgeMembersRequest,
  CreateAiKnowledgeDocumentRequest,
  UpdateAiKnowledgeDocumentRequest,
  DeleteAiKnowledgeDocumentRequest,
  SetAiKnowledgeDocumentMembersRequest
} from './generated/models.generated.js';

export {
  readAiKnowledgeBaseResponse,
  readAiKnowledgeMembersResponse,
  readAiKnowledgeDocumentResponse,
  readAiKnowledgeDocumentMembersResponse,
  readAiKnowledgeVersionResponse,
  readAiKnowledgeImportResponse
} from './generated/guards.generated.js';

export type {
  AiGetKnowledgeMembersParameters,
  AiSetKnowledgeMembersParameters,
  AiListKnowledgeBasesParameters,
  AiGetKnowledgeBaseParameters,
  AiCreateKnowledgeBaseParameters,
  AiUpdateKnowledgeBaseParameters,
  AiUpdateKnowledgePolicyParameters,
  AiListKnowledgeDocumentsParameters,
  AiGetKnowledgeDocumentParameters,
  AiCreateKnowledgeDocumentParameters,
  AiUpdateKnowledgeDocumentParameters,
  AiDeleteKnowledgeDocumentParameters,
  AiGetKnowledgeDocumentMembersParameters,
  AiSetKnowledgeDocumentMembersParameters,
  AiUploadKnowledgeVersionParameters,
  AiListKnowledgeVersionsParameters,
  AiDownloadKnowledgeSourceParameters,
  AiGetKnowledgeImportParameters,
  AiRetryKnowledgeImportParameters,
  AiCleanupKnowledgeImportsParameters
} from './generated/operations.generated.js';
