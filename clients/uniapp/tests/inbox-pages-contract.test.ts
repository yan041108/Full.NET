import { readFile } from 'node:fs/promises';
import { describe, expect, it } from 'vitest';

async function readPage(name: string): Promise<string> {
  return await readFile(new URL(`../src/pages/${name}.vue`, import.meta.url), 'utf8');
}

describe('inbox mobile page contract', () => {
  it('checks inbox read permission before loading messages', async () => {
    const source = await readPage('notifications/inbox');

    expect(source).toContain("identitySession.can('notifications.inbox.read')");
    expect(source).toContain('createInboxMessagesClient');
  });

  it('does not create mark-all-read without permission', async () => {
    const source = await readPage('notifications/inbox');

    expect(source).toContain("identitySession.can('notifications.inbox.mark_all_read')");
    expect(source).toMatch(/v-if="identitySession\.can\('notifications\.inbox\.mark_all_read'\)/);
  });
});
