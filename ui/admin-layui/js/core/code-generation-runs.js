import {
  isCodeGenerationRunApplyResponse,
  isCodeGenerationRunPage,
  isCodeGenerationRunRollbackResponse,
  isCodeGenerationRunRollbackChainResponse,
  isCodeGenerationRunPreviewResponse,
  buildCodeGenerationRollbackApplyRunIds,
  isPendingCodeGenerationRollbackApply
} from '@fullnet/client-contracts';

const runsPath = '/api/v1/code-generation/runs';

export function createCodeGenerationRunsApi(request) {
  return {
    async rollbackChain(input) {
      const value = await request(
        `${runsPath}/rollback-chain`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(input)
        }
      );
      if (!isCodeGenerationRunRollbackChainResponse(value)) {
        throw new Error('client.invalid_code_generation_run_rollback_chain');
      }
      return value;
    },
    async rollbackApply(runs, targetApplyRunId) {
      const applyRunIds = buildCodeGenerationRollbackApplyRunIds(
        runs,
        targetApplyRunId
      );
      if (applyRunIds.length === 1) {
        return this.rollback({ applyRunId: applyRunIds[0] });
      }
      return this.rollbackChain({ applyRunIds });
    },
    isPendingRollbackApply: isPendingCodeGenerationRollbackApply,
    async rollback(input) {
      const value = await request(
        `${runsPath}/rollback`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(input)
        }
      );
      if (!isCodeGenerationRunRollbackResponse(value)) {
        throw new Error('client.invalid_code_generation_run_rollback');
      }
      return value;
    },
    async apply(input) {
      const value = await request(
        `${runsPath}/apply`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(input)
        }
      );
      if (!isCodeGenerationRunApplyResponse(value)) {
        throw new Error('client.invalid_code_generation_run_apply');
      }
      return value;
    },
    async preview(input) {
      const value = await request(
        `${runsPath}/preview`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(input)
        }
      );
      if (!isCodeGenerationRunPreviewResponse(value)) {
        throw new Error('client.invalid_code_generation_run_preview');
      }
      return value;
    },
    async list(status) {
      const query = new URLSearchParams({
        page: '1',
        pageSize: '20'
      });
      if (status) {
        query.set('status', status);
      }
      const value = await request(`${runsPath}?${query.toString()}`);
      if (!isCodeGenerationRunPage(value)) {
        throw new Error('client.invalid_code_generation_run_page');
      }
      return value;
    }
  };
}
