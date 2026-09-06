import { readFile } from 'node:fs/promises';
import { describe, expect, it } from 'vitest';

async function readPage(name: string): Promise<string> {
  return await readFile(new URL(`../src/pages/${name}.vue`, import.meta.url), 'utf8');
}

describe('workflow mobile page contract', () => {
  it('uses the shared application session and keeps tokens out of storage', async () => {
    const loginSource = await readPage('identity/login');
    const mpSessionSource = await readFile(
      new URL('../src/features/identity/mp-weixin-identity-session.ts', import.meta.url),
      'utf8'
    );

    expect(loginSource).toContain('identitySession.login');
    expect(loginSource).toContain('isBusinessRuntimeAvailable');
    expect(loginSource).not.toMatch(/setStorageSync\([^\n]*(?:token|permission)/i);
    expect(mpSessionSource).not.toMatch(/setStorageSync\([^\n]*(?:token|permission)/i);
  });

  it('checks the page permission before loading todos', async () => {
    const source = await readPage('workflow/todos');

    expect(source).toContain("identitySession.can('workflow.todos.read')");
    expect(source).toContain('todoClient.listMine()');
  });

  it('does not create approval actions without their exact permissions', async () => {
    const source = await readPage('workflow/todo-detail');

    expect(source).toContain("identitySession.can('workflow.todos.approve')");
    expect(source).toContain("identitySession.can('workflow.todos.reject')");
    expect(source).toMatch(/v-if="canApprove"/);
    expect(source).toMatch(/v-if="canReject"/);
    expect(source).toContain('createIdempotencyKey()');
    expect(source).toContain("detail.value?.statusKey === 'active'");
  });

  it('uses stable failure recovery rules for conflict and retryable approval failures', async () => {
    const source = await readPage('workflow/todo-detail');

    expect(source).toContain('classifyWorkflowTodoActionFailure');
    expect(source).toContain('failure.retainIdempotencyKey');
    expect(source).toContain('failure.refreshTodo');
    expect(source).toContain('await refreshTodo()');
  });
});
