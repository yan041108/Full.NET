import {
  isCodeGenerationRunApplyResponse,
  isCodeGenerationRunPage,
  isCodeGenerationRunRollbackResponse,
  isCodeGenerationRunPreviewResponse
} from '@fullnet/client-contracts';

const runsPath = '/api/v1/code-generation/runs';

export function createCodeGenerationRunsApi(request) {
  return {
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
