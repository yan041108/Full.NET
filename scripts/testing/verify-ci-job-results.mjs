import path from 'node:path';
import { pathToFileURL } from 'node:url';

// 保留既有 build-test 检查名称；拆分后的任一必需作业失败、取消或遗漏都不能汇总为成功。
export function verifyBuildTestResults(eventName, buildResult, migrationResult) {
  if (eventName !== 'pull_request' && eventName !== 'push') throw new Error('未知 CI 事件。');
  if (buildResult !== 'success') throw new Error(`构建与模块验收未通过：${buildResult}`);
  if (eventName === 'pull_request' && migrationResult !== 'success') {
    throw new Error(`迁移恢复验收未通过：${migrationResult}`);
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  verifyBuildTestResults(process.env.CI_EVENT_NAME, process.env.BUILD_RESULT, process.env.MIGRATION_RESULT);
}
