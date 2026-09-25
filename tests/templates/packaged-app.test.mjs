import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { buildAppTemplate } from '../../scripts/templates/build-app-template.mjs';
import { verifyCreatedApp } from '../../scripts/templates/verify-created-app.mjs';

test('application template package includes framework sources and root manifest', () => {
  const workspace = mkdtempSync(join(tmpdir(), 'fullnet-app-template-'));
  try {
    const { templateRoot } = buildAppTemplate({ output: join(workspace, 'package') });
    const manifest = JSON.parse(readFileSync(join(templateRoot, 'framework-manifest.json'), 'utf8'));
    assert.ok(existsSync(join(templateRoot, 'framework/fullnet/src/Hosts/Full.NET.Host.Api/Program.cs')));
    assert.ok(existsSync(join(templateRoot, 'framework/fullnet/src/Composition/Full.NET.Composition/Full.NET.Composition.csproj')));
    assert.ok(existsSync(join(templateRoot, 'src/FullNetAppNameToken.Host.Api/appsettings.json')));
    assert.ok(Object.keys(manifest.managedFiles).length > 0);

    const hive = join(workspace, 'hive');
    const appRoot = join(workspace, 'created');
    const install = spawnSync('dotnet', ['new', 'install', templateRoot, '--debug:custom-hive', hive], { encoding: 'utf8' });
    assert.equal(install.status, 0, install.stderr || install.stdout);
    const create = spawnSync('dotnet', [
      'new', 'fullnet-app', '--name', 'Demo', '--owner-key', 'acme', '--database', 'mysql',
      '--preset', 'minimal', '--output', appRoot, '--debug:custom-hive', hive,
    ], { encoding: 'utf8', timeout: 120_000 });
    assert.equal(create.status, 0, create.stderr || create.stdout);
    assert.ok(existsSync(join(appRoot, 'src/Demo.Host.Api/Demo.Host.Api.csproj')));
    const verification = verifyCreatedApp(appRoot);
    assert.equal(verification.ok, true, verification.errors.join('; '));
    const appProfile = JSON.parse(readFileSync(join(appRoot, 'fullnet-app.json'), 'utf8'));
    assert.equal(appProfile.ownerKey, 'acme');
    assert.equal(appProfile.databaseProvider, 'mysql');

    for (const [managedPath, digest] of Object.entries(manifest.managedFiles)) {
      const generatedPath = join(appRoot, 'framework/fullnet', managedPath);
      assert.ok(existsSync(generatedPath), `framework path changed during instantiation: ${managedPath}`);
      const actualHash = createHash('sha256').update(readFileSync(generatedPath)).digest('hex');
      assert.equal(actualHash, digest, `framework content changed during instantiation: ${managedPath}`);
    }

    const build = spawnSync('dotnet', [
      'build', join(appRoot, 'src/Demo.Host.Api/Demo.Host.Api.csproj'), '-c', 'Release', '-v', 'quiet',
    ], { cwd: appRoot, encoding: 'utf8', timeout: 300_000 });
    assert.equal(build.status, 0, build.stderr || build.stdout);

    const repeat = spawnSync('dotnet', [
      'new', 'fullnet-app', '--name', 'Second', '--owner-key', 'acme', '--database', 'mysql',
      '--preset', 'minimal', '--output', appRoot, '--debug:custom-hive', hive,
    ], { encoding: 'utf8', timeout: 120_000 });
    assert.notEqual(repeat.status, 0, 'repeated creation must reject an occupied directory');
    assert.ok(existsSync(join(appRoot, 'src/Demo.Host.Api/Demo.Host.Api.csproj')));
    assert.equal(existsSync(join(appRoot, 'src/Second.Host.Api')), false);
    assert.equal(readFileSync(join(appRoot, 'framework-manifest.json'), 'utf8'),
      readFileSync(join(templateRoot, 'framework-manifest.json'), 'utf8'));
  } finally {
    rmSync(workspace, { recursive: true, force: true });
  }
});
