import { describe, expect, it } from 'vitest';
import { readWorkflowResourceTarget, workflowResourceIdFromHash, workflowResourceLoginUrl, workflowResourcePermission, workflowResourceUrl } from '../src/features/workflow/workflow-resource-links';
import { createWorkflowInstanceClient } from '../src/features/workflow/workflow-instance-client';
import { createInboxMessagesClient } from '../src/features/notifications/inbox-messages-client';
import type { HttpClient, HttpRequestOptions } from '../src/api/http';

const id = '018f0000-0000-7000-8000-000000000010';
describe('closed resource links and authoritative readers', () => {
  it('binds same-page query changes and rejects ambiguous ids without taking another page resource', () => {
    expect(workflowResourceIdFromHash(`#/pages/notifications/inbox-detail?id=${id}`, 'inbox_message')).toBe(id);
    expect(workflowResourceIdFromHash(`#/pages/notifications/inbox-detail?id=${id}&id=${id}`, 'inbox_message')).toBe('');
    expect(workflowResourceIdFromHash('#/pages/notifications/inbox-detail?id=bad', 'inbox_message')).toBe('');
    expect(workflowResourceIdFromHash(`#/pages/workflow/todo-detail?id=${id}`, 'inbox_message')).toBeUndefined();
  });
  it('preserves only a closed kind and normalized identifier across login', () => {
    const target = readWorkflowResourceTarget({ resourceTypeKey: 'workflow_todo', resourceId: id.toUpperCase(),
      tenantId: id, url: 'https://attacker.example', userId: id });
    expect(target).toEqual({ resourceTypeKey: 'workflow_todo', resourceId: id });
    expect(workflowResourceLoginUrl(target!)).toBe(`/pages/identity/login?resourceTypeKey=workflow_todo&resourceId=${id}`);
    expect(workflowResourceUrl(target!)).toBe(`/pages/workflow/todo-detail?id=${id}`);
    expect(workflowResourcePermission(target!)).toBe('workflow.todos.read');
  });
  it('rejects URLs, paths, malformed ids and unsupported resources', () => {
    for (const value of [null, '/pages/workflow/todos', { resourceTypeKey: 'https://attacker.example', resourceId: id },
      { resourceTypeKey: 'workflow_todo', resourceId: `${id}&tenantId=${id}` },
      { resourceTypeKey: 'workflow_todo', resourceId: '../admin' }, { resourceTypeKey: 'workflow_todo', resourceId: ['x'] }]) {
      expect(readWorkflowResourceTarget(value)).toBeUndefined();
    }
  });
  it('resolves notification and instance kinds to separate exact readers', () => {
    for (const [kind, path, permission] of [['workflow_instance', 'workflow/instance-detail', 'workflow.instances.read'],
      ['inbox_message', 'notifications/inbox-detail', 'notifications.inbox.read']]) {
      const target = readWorkflowResourceTarget({ resourceTypeKey: kind, resourceId: id })!;
      expect(workflowResourceUrl(target)).toBe(`/pages/${path}?id=${id}`);
      expect(workflowResourcePermission(target)).toBe(permission);
    }
  });
  it('reads one message without paging or marking it read and fails closed on malformed data', async () => {
    const calls: HttpRequestOptions[] = [];
    const message = { id, title: 'message', content: 'body', status: 'unread', readAtUtc: null,
      createdAtUtc: '2026-10-29T00:00:00Z', createdByUserId: null };
    let response: unknown = message;
    const http: HttpClient = { request: async <T>(options: HttpRequestOptions) => { calls.push(options); return response as T; } };
    const client = createInboxMessagesClient(http);
    expect(await client.get(id)).toEqual(message);
    expect(calls).toEqual([{ path: `/api/v1/notifications/my-inbox-messages/${id}` }]);
    response = { ...message, content: '', revokedAtUtc: '2026-10-29T01:00:00Z', deepLink: null };
    expect((await client.get(id)).content).toBe('');
    response = { ...message, status: 7 }; await expect(client.get(id)).rejects.toThrow();
    response = { ...message, revokedAtUtc: {} }; await expect(client.get(id)).rejects.toThrow();
    await expect(client.get(`${id}/read`)).rejects.toThrow(); expect(calls).toHaveLength(4);
  });
  it('uses only the current instance response and rejects malformed ids before sending', async () => {
    const calls: HttpRequestOptions[] = [];
    const instance = { id, activeTodoId: null, businessId: 'record', businessTitle: null, businessType: 'example',
      definitionVersionId: id, formVersionId: id, revision: 1, startedAtUtc: '2026-10-29T00:00:00Z', statusKey: 'cancelled' };
    let response: unknown = instance;
    const client = createWorkflowInstanceClient({ request: async <T>(options: HttpRequestOptions) => { calls.push(options); return response as T; } });
    expect((await client.get(id)).statusKey).toBe('cancelled');
    expect(calls).toEqual([{ path: `/api/v1/workflow/instances/${id}` }]);
    await expect(client.get('bad')).rejects.toThrow(); expect(calls).toHaveLength(1);
    response = { ...instance, activeTodoId: 42 }; await expect(client.get(id)).rejects.toThrow();
  });
});
