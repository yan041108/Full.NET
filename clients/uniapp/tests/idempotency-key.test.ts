import { describe, expect, it, vi } from 'vitest';

import { createIdempotencyKey } from '../src/features/workflow/idempotency-key';

describe('idempotency key', () => {
  it('uses crypto.randomUUID when available', () => {
    const randomUUID = vi.fn(() => '018f0000-0000-7000-8000-000000000099');
    vi.stubGlobal('crypto', { randomUUID });

    expect(createIdempotencyKey()).toBe('018f0000-0000-7000-8000-000000000099');
  });

  it('falls back when crypto.randomUUID is unavailable', () => {
    vi.stubGlobal('crypto', undefined);

    expect(createIdempotencyKey()).toMatch(/^idem-/);
  });
});
