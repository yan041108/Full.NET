import { describe, expect, it, vi } from 'vitest';
import { http } from './http';
const directoryId = '01900000-0000-7000-8000-000000000001';
const directoryVersion = '01900000-0000-7000-8000-000000000002';
const directoryRow = { documentId: directoryId, versionId: directoryVersion, title: '确认来源', versionNumber: 2, createdAtUtc: '2026-10-16T00:00:00Z', updatedAtUtc: '2026-10-16T01:00:00Z' };

describe('已确认来源目录协议', () => {
  it('通过生成操作编码游标和分页，取消令牌传给统一客户端', async () => {
    vi.mocked(http.request).mockResolvedValue({ items: [directoryRow], nextDocumentId: directoryId });
    const signal = new AbortController().signal;
    expect((await api.listKnowledgeIndexChanges({ knowledgeBaseId: directoryVersion, pageSize: 1 }, signal)).items).toHaveLength(1);
    expect(http.request).toHaveBeenCalledWith(`/api/v1/ai/knowledge-bases/${directoryVersion}/index-changes?pageSize=1`, { method: 'GET' }, signal);
  });
  it.each(['duplicate', 'empty-id', 'zero-version', 'empty-title', 'bad-date', 'too-many', 'cursor-mismatch', 'cursor-loop', 'malformed'])('异常响应 %s 在数据进入页面前拒绝', async scenario => {
    const row = { ...directoryRow, ...(scenario === 'empty-id' ? { versionId: '00000000-0000-0000-0000-000000000000' }
      : scenario === 'zero-version' ? { versionNumber: 0 } : scenario === 'empty-title' ? { title: ' ' } : scenario === 'bad-date' ? { updatedAtUtc: 'not-a-date' } : {}) };
    vi.mocked(http.request).mockResolvedValue({ items: scenario === 'malformed' ? [{}] : scenario === 'duplicate' || scenario === 'too-many' ? [row, row] : [row],
      nextDocumentId: scenario === 'cursor-mismatch' ? directoryVersion : scenario === 'cursor-loop' ? directoryId : null });
    await expect(api.listKnowledgeIndexChanges({ knowledgeBaseId: directoryVersion, pageSize: scenario === 'too-many' ? 1 : 2,
      afterDocumentId: scenario === 'cursor-loop' ? directoryId : undefined })).rejects.toThrow();
  });
});
import { listKnowledgeBases, uploadKnowledgeVersion, downloadKnowledgeSource } from './ai-knowledge';
import * as api from './ai-knowledge';

vi.mock('./http', () => ({ http: { request: vi.fn(), requestBlob: vi.fn() } }));
describe('知识库生成协议适配', () => {
  it('对账复用生成操作、精确版本和取消信号，并拒绝缺失字段', async () => {
    const signal = new AbortController().signal;
    vi.mocked(http.request).mockResolvedValue({ reason: 'ready' });
    await expect(api.getKnowledgeIndexReconciliation({ knowledgeBaseId: 'base', documentId: 'doc', versionId: 'version' }, signal)).rejects.toThrow();
    expect(http.request).toHaveBeenLastCalledWith('/api/v1/ai/knowledge-bases/base/documents/doc/versions/version/index/reconciliation', { method: 'GET' }, signal);
  });
  it('索引入队仅绑定精确版本路径，不添加请求体或查询字段', async () => {
    const versionId = '01900000-0000-7000-8000-000000000001'; const taskId = '01900000-0000-7000-8000-000000000002';
    vi.mocked(http.request).mockResolvedValue({ taskId, versionId });
    await expect(api.enqueueKnowledgeIndex({ knowledgeBaseId: 'base', documentId: 'doc', versionId })).resolves.toEqual({ taskId, versionId });
    expect(http.request).toHaveBeenLastCalledWith(`/api/v1/ai/knowledge-bases/base/documents/doc/versions/${versionId}/index`, { method: 'POST' }, undefined);
  });
  it('索引重试绑定原任务，状态读取使用生成响应守卫', async () => {
    vi.mocked(http.request).mockRejectedValue(new Error('conflict'));
    await expect(api.retryKnowledgeIndex({ knowledgeBaseId: 'base', documentId: 'doc', versionId: 'version', taskId: 'task' })).rejects.toThrow('conflict');
    expect(http.request).toHaveBeenLastCalledWith('/api/v1/ai/knowledge-bases/base/documents/doc/versions/version/index/task/retry', { method: 'POST' }, undefined);
    vi.mocked(http.request).mockResolvedValue({ state: 'ready' });
    await expect(api.getKnowledgeIndexStatus({ knowledgeBaseId: 'base', documentId: 'doc', versionId: 'version' })).rejects.toThrow();
  });
  it('策略写入保留原版本及成对撤销值', async () => {
    vi.mocked(http.request).mockRejectedValue(new Error('conflict'));
    const signal = new AbortController().signal;
    const body = { dataClassification: 'restricted', embeddingModelConfigId: null, embeddingModelVersion: null, generationModelConfigId: null, generationModelVersion: null, version: 8 };
    await expect(api.updateKnowledgePolicy({ knowledgeBaseId: 'base', body }, signal)).rejects.toThrow('conflict');
    expect(http.request).toHaveBeenLastCalledWith('/api/v1/ai/knowledge-bases/base/policy', expect.objectContaining({ method: 'PUT', body: JSON.stringify(body) }), signal);
  });
  it('文档角色替换绑定原库、文档和授予版本', async () => {
    vi.mocked(http.request).mockRejectedValue(new Error('conflict'));
    const body = { roleIds: ['role'], version: 5 };
    await expect(api.setKnowledgeDocumentRoles({ knowledgeBaseId: 'base', documentId: 'doc', body })).rejects.toThrow('conflict');
    expect(http.request).toHaveBeenLastCalledWith('/api/v1/ai/knowledge-bases/base/documents/doc/roles', expect.objectContaining({ method: 'PUT', body: JSON.stringify(body) }), undefined);
  });
  it('拒绝非法成员响应和候选响应', async () => {
    vi.mocked(http.request).mockResolvedValue({ userIds: ['invalid'], version: 1 });
    await expect(api.getKnowledgeMembers({ knowledgeBaseId: 'base' })).rejects.toThrow();
    await expect(api.listKnowledgeOrganizationCandidates({ knowledgeBaseId: 'base', page: 1, pageSize: 20 })).rejects.toThrow();
  });
  it('拒绝非法目录响应', async () => {
    vi.mocked(http.request).mockResolvedValue({ items: [{ id: 'invalid' }], total: 1, page: 1, pageSize: 20 });
    await expect(listKnowledgeBases({ page: 1 })).rejects.toThrow();
  });
  it('上传保留固定请求键、版本、媒体类型及取消信号', async () => {
    vi.mocked(http.request).mockRejectedValue(new Error('network'));
    const signal = new AbortController().signal;
    const body = new Blob(['text'], { type: 'text/plain' });
    await expect(uploadKnowledgeVersion({ knowledgeBaseId: 'base', documentId: 'doc', version: 3,
      requestKey: 'request', fileName: 'a b.txt', body, contentType: 'text/plain' }, signal)).rejects.toThrow('network');
    expect(http.request).toHaveBeenLastCalledWith(expect.stringContaining('requestKey=request&fileName=a+b.txt'),
      expect.objectContaining({ method: 'POST', body, headers: { 'content-type': 'text/plain' } }), signal);
  });
  it('来源请求只使用服务器资源 ID 和共享二进制请求', async () => {
    const blob = new Blob(['source']); vi.mocked(http.requestBlob).mockResolvedValue(blob);
    const signal = new AbortController().signal;
    expect(await downloadKnowledgeSource({ knowledgeBaseId: 'base', documentId: 'doc', versionId: 'v' }, signal)).toBe(blob);
    expect(http.requestBlob).toHaveBeenLastCalledWith('/api/v1/ai/knowledge-bases/base/documents/doc/versions/v/source', expect.objectContaining({ method: 'GET' }), signal);
  });
});
