import { rmSync } from 'node:fs';
import { stopLoggedProcess } from '../../e2e/admin-real-stack/scripts/stop-logged-process.mjs';

// 进程和日志先排空；停机失败仍必须释放依赖与临时目录，并向验收调用方传播。
export async function cleanupCreatedApp({ apiProcess, apiLogStream, workerProcess, workerLogStream,
  dbContainer, redisContainer, workspace }) {
  const shutdownErrors = [];
  try {
    for (const [process, log] of [[apiProcess, apiLogStream], [workerProcess, workerLogStream]]) {
      try {
        await stopLoggedProcess(process, log);
      } catch (error) {
        shutdownErrors.push(error);
      }
    }
  } finally {
    // 容器停止保持既有尽力清理语义，避免一项失败阻断其他资源释放。
    await dbContainer?.stop().catch(() => {});
    await redisContainer?.stop().catch(() => {});
    rmSync(workspace, { recursive: true, force: true });
  }
  if (shutdownErrors.length === 1) throw shutdownErrors[0];
  if (shutdownErrors.length > 1) throw new AggregateError(shutdownErrors, 'Application host shutdown failed');
}
