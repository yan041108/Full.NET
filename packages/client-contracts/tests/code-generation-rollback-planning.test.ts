import { describe, expect, it } from 'vitest';
import type { CodeGenerationRunResponse } from '../src/code-generation-runs.js';
import {
  buildCodeGenerationRollbackApplyRunIds,
  isPendingCodeGenerationRollbackApply
} from '../src/code-generation-rollback-planning.js';

const apply = (
  id: string,
  startedAtUtc: string
): CodeGenerationRunResponse => ({
  id,
  templateId: '0198f36e-f7a7-7c52-9cbb-774e67411205',
  templateVersion: 1,
  operationKind: 'apply',
  status: 'succeeded',
  moduleKey: 'catalog',
  entityKey: 'product',
  schemaSha256: 'a'.repeat(64),
  artifactCount: 1,
  manifestSha256: 'b'.repeat(64),
  errorCode: null,
  requestedByUserId: '0198f36e-f7a7-7c52-9cbb-774e67411211',
  startedAtUtc,
  finishedAtUtc: startedAtUtc,
  sourceApplyRunId: null
});

describe('code-generation rollback planning', () => {
  it('builds a LIFO prefix for the selected apply', () => {
    const runs = [
      apply('0198f36e-f7a7-7c52-9cbb-774e67411216', '2026-08-02T08:00:02Z'),
      apply('0198f36e-f7a7-7c52-9cbb-774e67411215', '2026-08-02T08:00:01Z'),
      apply('0198f36e-f7a7-7c52-9cbb-774e67411214', '2026-08-02T08:00:00Z')
    ];

    expect(buildCodeGenerationRollbackApplyRunIds(
      runs,
      '0198f36e-f7a7-7c52-9cbb-774e67411215'
    )).toEqual([
      '0198f36e-f7a7-7c52-9cbb-774e67411216',
      '0198f36e-f7a7-7c52-9cbb-774e67411215'
    ]);
  });

  it('excludes applies that already have a succeeded rollback', () => {
    const runs = [
      apply('0198f36e-f7a7-7c52-9cbb-774e67411216', '2026-08-02T08:00:02Z'),
      apply('0198f36e-f7a7-7c52-9cbb-774e67411215', '2026-08-02T08:00:01Z'),
      {
        ...apply('0198f36e-f7a7-7c52-9cbb-774e67411217', '2026-08-02T08:00:03Z'),
        operationKind: 'rollback',
        sourceApplyRunId: '0198f36e-f7a7-7c52-9cbb-774e67411216'
      }
    ];

    expect(isPendingCodeGenerationRollbackApply(
      runs,
      apply('0198f36e-f7a7-7c52-9cbb-774e67411216', '2026-08-02T08:00:02Z')
    )).toBe(false);
    expect(buildCodeGenerationRollbackApplyRunIds(
      runs,
      '0198f36e-f7a7-7c52-9cbb-774e67411215'
    )).toEqual(['0198f36e-f7a7-7c52-9cbb-774e67411215']);
  });
});