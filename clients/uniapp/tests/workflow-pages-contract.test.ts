import { readFile } from 'node:fs/promises';
import { describe, expect, it } from 'vitest';

async function readPage(name: string): Promise<string> {
  return await readFile(new URL(`../src/pages/${name}.vue`, import.meta.url), 'utf8');
}

describe('workflow mobile page contract', () => {
  it('keeps password login H5-only and presents an explicit unavailable state to mini programs', async () => {
    const source = await readPage('identity/login');

    expect(source).toContain('// #ifdef H5');
    expect(source).toContain('h5IdentitySession.login');
    expect(source).toContain("t('identity.login.platformUnavailable')");
    expect(source).not.toMatch(/setStorageSync\([^\n]*(?:token|permission)/i);
  });

  it('checks the page permission before loading todos', async () => {
    const source = await readPage('workflow/todos');

    expect(source).toContain("h5IdentitySession.can('workflow.todos.read')");
    expect(source).toContain('todoClient.listMine()');
  });

  it('does not create approval actions without their exact permissions', async () => {
    const source = await readPage('workflow/todo-detail');

    expect(source).toContain("h5IdentitySession.can('workflow.todos.approve')");
    expect(source).toContain("h5IdentitySession.can('workflow.todos.reject')");
    expect(source).toMatch(/v-if="canApprove"/);
    expect(source).toMatch(/v-if="canReject"/);
    expect(source).toContain('crypto.randomUUID()');
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
