import { createHash } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { expect } from '@playwright/test';

export function readStackState() {
  const statePath = new URL('../../.stack-state.json', import.meta.url);
  expect(existsSync(statePath)).toBeTruthy();
  return JSON.parse(readFileSync(statePath, 'utf8'));
}

export function readAppliedWorkspaceArtifact(relativePath) {
  const state = readStackState();
  expect(state.codeGenerationWorkspaceRoot).toBeTruthy();

  const manifestPath = path.join(
    state.codeGenerationWorkspaceRoot,
    '.fullnet',
    'codegeneration-manifest.json'
  );
  expect(existsSync(manifestPath)).toBeTruthy();
  const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
  const entry = manifest.artifacts.find(
    artifact => artifact.relativePath === relativePath
  );
  expect(entry).toBeTruthy();

  const artifactPath = path.join(
    state.codeGenerationWorkspaceRoot,
    ...relativePath.split('/')
  );
  expect(existsSync(artifactPath)).toBeTruthy();
  const content = readFileSync(artifactPath, 'utf8');
  const hash = createHash('sha256').update(content).digest('hex');
  expect(hash).toBe(entry.sha256);
  return content;
}
