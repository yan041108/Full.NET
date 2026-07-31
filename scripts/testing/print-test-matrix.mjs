import path from 'node:path';
import { pathToFileURL } from 'node:url';

import { loadTestMatrix } from './run-dotnet-test-suite.mjs';

export function mainIntegrationPartitionsJson() {
  return JSON.stringify(loadTestMatrix().integration.mainPartitions);
}

if (
  process.argv[1]
  && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href
) {
  process.stdout.write(`${mainIntegrationPartitionsJson()}\n`);
}
