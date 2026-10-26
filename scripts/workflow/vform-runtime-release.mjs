import { writeFile, mkdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { buildRuntimeOutput, createRuntimeRelease, verifyRuntimeRelease } from '../../packages/admin-form-designer/runtime-release.mjs';
const root = fileURLToPath(new URL('../../', import.meta.url));
const update = process.argv[2] === '--update';
if (process.argv[2] && !['--update', '--verify'].includes(process.argv[2])) throw new Error('Use --update or --verify <runtime directory>');
if (update && process.argv[3]) throw new Error('Runtime updates use only the task-owned package dist/runtime directory');
const directory = process.argv[3] ? path.resolve(process.argv[3]) : path.join(root, 'packages/admin-form-designer/dist/runtime');
if (update) {
  await buildRuntimeOutput(path.join(root, 'packages/admin-form-designer/vite.runtime.config.mjs'), directory);
  const release = await createRuntimeRelease(directory);
  await mkdir(path.join(root, 'contracts/workflow'), { recursive: true });
  await writeFile(path.join(root, 'contracts/workflow/vform3-runtime-release.json'), JSON.stringify(release, null, 2) + '\n');
  await writeFile(path.join(root, 'packages/admin-form-designer/src/runtime-release.generated.ts'), `// 由 scripts/workflow/vform-runtime-release.mjs 生成，禁止手工改写。\nexport const VFORM_RUNTIME_HASH = '${release.contentHash}';\n`);
}
console.log('VForm runtime release verified:', (await verifyRuntimeRelease(directory)).contentHash);
