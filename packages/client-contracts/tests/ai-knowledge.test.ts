import { afterEach, describe, expect, it, vi } from 'vitest';
import { createHttpClient } from '../src/http';
import {
  aiUploadKnowledgeVersion, aiDownloadKnowledgeSource, aiDeleteKnowledgeDocument,
  aiRetryKnowledgeImport, aiListKnowledgeVersions, aiListKnowledgeRoleCandidates, aiListKnowledgeDocumentRoleCandidates
} from '../src/ai-knowledge';

const id = '00000000-0000-0000-0000-000000000001';
const resource = { knowledgeBaseId: id, documentId: id };
const imported = {
  id, documentId: id, versionId: id, state: 'pending_confirmation', failureCode: null,
  createdAtUtc: '2026-10-03T00:00:00Z', updatedAtUtc: '2026-10-03T00:00:00Z'
};
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), {
  status, headers: { 'content-type': 'application/json' }
});

afterEach(() => vi.unstubAllGlobals());

describe('知识库共享生成客户端', () => {
  it('两个角色候选操作传递分页、认证、语言与取消且只返回最小角色投影', async () => {
    const page = { items: [{ id, code: 'reader', name: '阅读角色' }], page: 2, pageSize: 10, total: '21' };
    const fetchMock = vi.fn().mockImplementation(async () => json(page));
    vi.stubGlobal('fetch', fetchMock);
    const http = createHttpClient();
    http.configureRequestLocale(() => 'zh-CN');
    http.configureAuthentication({ getAccessToken: () => 'access', refresh: async () => false });
    const signal = new AbortController().signal;
    expect(await aiListKnowledgeRoleCandidates(http, { knowledgeBaseId: id, page: 2, pageSize: 10 }, signal)).toEqual({ ...page, total: 21 });
    expect(await aiListKnowledgeDocumentRoleCandidates(http, { ...resource, page: 2, pageSize: 10 }, signal)).toEqual({ ...page, total: 21 });
    for (const [index, [url, request]] of fetchMock.mock.calls.entries()) {
      const parsed = new URL(url, 'https://example.test');
      expect(parsed.pathname).toBe(`/api/v1/ai/knowledge-bases/${id}${index === 1 ? `/documents/${id}` : ''}/role-candidates`);
      expect([...parsed.searchParams.entries()]).toEqual([['page', '2'], ['pageSize', '10']]);
      expect(request.signal).toBe(signal);
      expect(new Headers(request.headers).get('authorization')).toBe('Bearer access');
      expect(new Headers(request.headers).get('accept-language')).toBe('zh-CN');
      expect(request.body).toBeUndefined();
    }
  });

  it('角色候选拒绝损坏 UUID 并传播权威撤权 ProblemDetails', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(json({ items: [{ id: 'invalid', code: 'role', name: '角色' }], page: 1, pageSize: 20, total: '1' }))
      .mockResolvedValueOnce(json({ status: 403, code: 'authorization.permission_denied', title: 'Forbidden' }, 403));
    vi.stubGlobal('fetch', fetchMock);
    const http = createHttpClient();
    await expect(aiListKnowledgeRoleCandidates(http, { knowledgeBaseId: id })).rejects.toThrow(/invalid_paged_result_of_ai_knowledge_role_candidate_response/);
    await expect(aiListKnowledgeDocumentRoleCandidates(http, resource)).rejects.toMatchObject({ status: 403, code: 'authorization.permission_denied' });
  });

  it('文件版本分页读取整数线编码并拒绝精度丢失和损坏行', async () => {
    const version = {
      id, documentId: id, versionNumber: 1, originalFileName: '来源.txt', contentType: 'text/plain',
      contentHash: 'A'.repeat(64), sizeBytes: '8', processingStatus: 'pending_processing',
      createdAtUtc: '2026-10-03T00:00:00Z'
    };
    const page = { items: [version], page: 1, pageSize: 20, total: '1' };
    const fetchMock = vi.fn().mockResolvedValueOnce(json(page))
      .mockResolvedValueOnce(json({ ...page, items: [{ ...version, sizeBytes: '9007199254740993' }] }));
    vi.stubGlobal('fetch', fetchMock);
    const http = createHttpClient();
    expect(await aiListKnowledgeVersions(http, resource)).toEqual({
      ...page, total: 1, items: [{ ...version, sizeBytes: 8 }]
    });
    await expect(aiListKnowledgeVersions(http, resource)).rejects.toThrow(/invalid_paged_result_of_ai_knowledge_version_response/);
  });

  it('认证刷新重试保留原始文件、媒体类型和幂等键，并透传取消及语言', async () => {
    const body = new Blob([new Uint8Array([0, 255, 42])]);
    const signal = new AbortController().signal;
    const fetchMock = vi.fn().mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(json(imported, 202));
    vi.stubGlobal('fetch', fetchMock);
    const http = createHttpClient();
    let token = 'old';
    http.configureAuthentication({ getAccessToken: () => token, refresh: async () => { token = 'new'; return true; } });
    http.configureRequestLocale(() => 'zh-CN');
    expect(await aiUploadKnowledgeVersion(http, {
      ...resource, version: 3, requestKey: id, fileName: '来源 +.pdf', contentType: 'application/pdf', body
    }, signal)).toEqual(imported);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    const [url, request] = fetchMock.mock.calls[1];
    const query = new URL(url, 'https://example.test').searchParams;
    expect(query.get('version')).toBe('3');
    expect(query.get('requestKey')).toBe(id);
    expect(query.get('fileName')).toBe('来源 +.pdf');
    expect([...query.keys()].sort()).toEqual(['fileName', 'requestKey', 'version']);
    expect(request.body).toBe(body);
    expect(request.signal).toBe(signal);
    expect(new Headers(request.headers).get('content-type')).toBe('application/pdf');
    expect(new Headers(request.headers).get('authorization')).toBe('Bearer new');
    expect(new Headers(request.headers).get('accept-language')).toBe('zh-CN');
  });

  it('来源走认证 Blob 响应；撤权 ProblemDetails 原样交给共享错误边界', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(new Response('source bytes', {
      headers: { 'content-type': 'application/octet-stream' }
    })).mockResolvedValueOnce(json({ status: 403, code: 'authorization.permission_denied', title: 'Forbidden' }, 403));
    vi.stubGlobal('fetch', fetchMock);
    const http = createHttpClient();
    http.configureAuthentication({ getAccessToken: () => 'access', refresh: async () => false });
    const parameters = { ...resource, versionId: id };
    expect(await (await aiDownloadKnowledgeSource(http, parameters)).text()).toBe('source bytes');
    expect(new Headers(fetchMock.mock.calls[0][1].headers).get('authorization')).toBe('Bearer access');
    await expect(aiDownloadKnowledgeSource(http, parameters)).rejects.toMatchObject({
      status: 403, code: 'authorization.permission_denied'
    });
  });

  it('文档删除接受 204，导入重试拒绝不符合 DTO 的 JSON', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(json({ ...imported, versionId: 'invalid-guid' }));
    vi.stubGlobal('fetch', fetchMock);
    const http = createHttpClient();
    await expect(aiDeleteKnowledgeDocument(http, { ...resource, body: { version: 3 } })).resolves.toBeUndefined();
    expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toEqual({ version: 3 });
    await expect(aiRetryKnowledgeImport(http, { ...resource, operationId: id })).rejects.toThrow(/invalid_ai_knowledge_import_response/);
  });
});
