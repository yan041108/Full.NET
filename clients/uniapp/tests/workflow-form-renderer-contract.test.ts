import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const rendererSource = readFileSync(
  fileURLToPath(new URL('../src/features/workflow/FullNetFormRenderer.vue', import.meta.url)),
  'utf8'
);

describe('FullNetFormRenderer static rendering contract', () => {
  it('uses explicit static branches for every approved field type', () => {
    for (const fieldType of [
      'text',
      'textarea',
      'integer',
      'decimal',
      'money',
      'date',
      'time',
      'datetime',
      'radio',
      'checkbox',
      'select',
      'switch'
    ]) {
      expect(rendererSource).toContain(`field.fieldTypeKey === '${fieldType}'`);
    }
  });

  it('does not expose runtime component, HTML or code execution paths', () => {
    expect(rendererSource).not.toMatch(/<component\b/i);
    expect(rendererSource).not.toMatch(/v-html\s*=/i);
    expect(rendererSource).not.toMatch(/\beval\s*\(/);
    expect(rendererSource).not.toMatch(/new\s+Function\s*\(/);
    expect(rendererSource).not.toMatch(/\bimport\s*\(/);
  });
});
