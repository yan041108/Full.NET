import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, readdirSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { areBundleInputsClean } from '../../scripts/templates/build-source-bundle.mjs';
import { buildAppTemplate } from '../../scripts/templates/build-app-template.mjs';
import { resolvePresetModules } from '../../scripts/templates/preset-modules.mjs';
import { verifyCreatedApp } from '../../scripts/templates/verify-created-app.mjs';

const skipBundleIntegration = areBundleInputsClean()
  ? false
  : 'source bundle inputs have uncommitted changes';

test('application template package includes framework sources and root manifest', { skip: skipBundleIntegration }, () => {
  const workspace = mkdtempSync(join(tmpdir(), 'fullnet-app-template-'));
  try {
    const { templateRoot } = buildAppTemplate({ output: join(workspace, 'package') });
    const manifest = JSON.parse(readFileSync(join(templateRoot, 'framework-manifest.json'), 'utf8'));
    assert.ok(existsSync(join(templateRoot, 'framework/fullnet/src/Hosts/Full.NET.Host.Api/Program.cs')));
    assert.ok(existsSync(join(templateRoot, 'framework/fullnet/src/Composition/Full.NET.Composition/Full.NET.Composition.csproj')));
    assert.ok(existsSync(join(templateRoot, 'src/FullNetAppNameToken.Host.Api/appsettings.json')));
    assert.ok(existsSync(join(templateRoot, '.fullnet-tools/create-app.mjs')));
    assert.ok(existsSync(join(templateRoot, '.fullnet-tools/project-preset-composition.mjs')));
    assert.ok(existsSync(join(templateRoot, 'ui/admin/package.json')));
    assert.ok(existsSync(join(templateRoot, 'packages/client-contracts/package.json')));
    assert.ok(existsSync(join(templateRoot, 'pnpm-lock.yaml')));
    assert.ok(Object.keys(manifest.managedFiles).length > 0);

    const appRoot = join(workspace, 'created');
    const createTool = join(templateRoot, '.fullnet-tools/create-app.mjs');
    const create = spawnSync(process.execPath, [
      createTool, '--package', templateRoot, '--output', appRoot, '--name', 'Demo',
      '--owner-key', 'acme', '--database', 'mysql', '--preset', 'minimal', '--http-port', '5500',
    ], { encoding: 'utf8', timeout: 150_000 });
    assert.equal(create.status, 0, create.stderr || create.stdout);
    assert.deepEqual(readdirSync(workspace).filter((entry) => entry.startsWith('.fullnet-create-')), []);
    assert.ok(existsSync(join(appRoot, 'src/Demo.Host.Api/Demo.Host.Api.csproj')));
    assert.ok(existsSync(join(appRoot, 'ui/admin/src/App.vue')));
    assert.match(readFileSync(join(appRoot, 'ui/admin/vite.config.ts'), 'utf8'), /http:\/\/localhost:5500/);
    assert.ok(existsSync(join(appRoot, 'packages/admin-form-designer/package.json')));
    assert.equal(existsSync(join(appRoot, '.fullnet-tools')), false);
    const verification = verifyCreatedApp(appRoot);
    assert.equal(verification.ok, true, verification.errors.join('; '));
    const appProfile = JSON.parse(readFileSync(join(appRoot, 'fullnet-app.json'), 'utf8'));
    assert.equal(appProfile.ownerKey, 'acme');
    assert.equal(appProfile.databaseProvider, 'mysql');
    const generatedManifest = JSON.parse(readFileSync(join(appRoot, 'framework-manifest.json'), 'utf8'));
    assert.equal(generatedManifest.projectedPreset, 'minimal');
    const compositionProject = readFileSync(join(appRoot, 'framework/fullnet/src/Composition/Full.NET.Composition/Full.NET.Composition.csproj'), 'utf8');
    const compositionCatalog = readFileSync(join(appRoot, 'framework/fullnet/src/Composition/Full.NET.Composition/FullNetModuleCatalog.cs'), 'utf8');
    assert.doesNotMatch(compositionProject, /Full\.NET\.Modules\.Payments\\|Full\.NET\.AI\.Providers/);
    assert.doesNotMatch(compositionCatalog, /new PaymentsModule\(\)|AddAiProviderServices/);

    for (const [managedPath, digest] of Object.entries(generatedManifest.managedFiles)) {
      const generatedPath = join(appRoot, 'framework/fullnet', managedPath);
      assert.ok(existsSync(generatedPath), `framework path changed during instantiation: ${managedPath}`);
      const actualHash = createHash('sha256').update(readFileSync(generatedPath)).digest('hex');
      assert.equal(actualHash, digest, `framework content changed during instantiation: ${managedPath}`);
    }

    const build = spawnSync('dotnet', [
      'build', join(appRoot, 'src/Demo.Host.Api/Demo.Host.Api.csproj'), '-c', 'Release', '-v', 'quiet',
    ], { cwd: appRoot, encoding: 'utf8', timeout: 300_000 });
    assert.equal(build.status, 0, build.stderr || build.stdout);
    const assets = JSON.parse(readFileSync(join(appRoot, 'src/Demo.Host.Api/obj/project.assets.json'), 'utf8'));
    const implementationModules = Object.keys(assets.libraries)
      .map((name) => /^Full\.NET\.Modules\.([A-Za-z0-9]+)\//.exec(name)?.[1])
      .filter(Boolean);
    for (const module of implementationModules) {
      assert.ok(['Identity', 'Tenancy', 'Settings', 'Organization'].includes(module),
        `unexpected implementation module in minimal build: ${module}`);
    }
    const pnpm = 'pnpm';
    const install = spawnSync(pnpm, [
      'install', '--filter', '@fullnet/admin...', '--frozen-lockfile', '--ignore-scripts',
    ], { cwd: appRoot, encoding: 'utf8', timeout: 180_000, shell: process.platform === 'win32' });
    assert.equal(install.status, 0, install.stderr || install.stdout || install.error?.message);
    const frontendBuild = spawnSync(pnpm, [
      '--filter', '@fullnet/admin', 'build',
    ], { cwd: appRoot, encoding: 'utf8', timeout: 180_000, shell: process.platform === 'win32' });
    assert.equal(frontendBuild.status, 0, frontendBuild.stderr || frontendBuild.stdout || frontendBuild.error?.message);

    const repeat = spawnSync(process.execPath, [
      createTool, '--package', templateRoot, '--output', appRoot, '--name', 'Second',
      '--owner-key', 'acme', '--database', 'mysql', '--preset', 'minimal',
    ], { encoding: 'utf8', timeout: 150_000 });
    assert.notEqual(repeat.status, 0, 'repeated creation must reject an occupied directory');
    assert.ok(existsSync(join(appRoot, 'src/Demo.Host.Api/Demo.Host.Api.csproj')));
    assert.equal(existsSync(join(appRoot, 'src/Second.Host.Api')), false);
    assert.equal(readFileSync(join(appRoot, 'framework-manifest.json'), 'utf8'),
      readFileSync(join(appRoot, 'framework/fullnet/framework-manifest.json'), 'utf8'));

    for (const preset of ['platform', 'saas', 'enterprise']) {
      const presetRoot = join(workspace, preset);
      const generated = spawnSync(process.execPath, [
        createTool, '--package', templateRoot, '--output', presetRoot, '--name', 'Demo',
        '--owner-key', 'acme', '--database', 'sqlserver', '--preset', preset,
      ], { encoding: 'utf8', timeout: 150_000 });
      assert.equal(generated.status, 0, `${preset}: ${generated.stderr || generated.stdout}`);
      const presetBuild = spawnSync('dotnet', [
        'build', join(presetRoot, 'src/Demo.Host.Api/Demo.Host.Api.csproj'), '-c', 'Release', '-v', 'quiet',
      ], { cwd: presetRoot, encoding: 'utf8', timeout: 300_000 });
      assert.equal(presetBuild.status, 0, `${preset}: ${presetBuild.stderr || presetBuild.stdout}`);
      const presetAssets = JSON.parse(readFileSync(join(presetRoot, 'src/Demo.Host.Api/obj/project.assets.json'), 'utf8'));
      const selected = resolvePresetModules(preset);
      for (const library of Object.keys(presetAssets.libraries)) {
        const module = /^Full\.NET\.Modules\.([A-Za-z0-9]+)\//.exec(library)?.[1];
        if (module) assert.ok(selected.includes(module), `${preset}: unexpected implementation module ${module}`);
      }
    }
  } finally {
    rmSync(workspace, { recursive: true, force: true });
  }
});
