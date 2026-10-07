import { describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { listKnowledgeBases, uploadKnowledgeVersion, downloadKnowledgeSource } from './ai-knowledge';
import * as api from './ai-knowledge';

vi.mock('./http', () => ({ http: { request: vi.fn(), requestBlob: vi.fn() } }));
describe('知识库生成协议适配', () => {
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
