import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { areBundleInputsClean } from '../../scripts/templates/build-source-bundle.mjs';
import { buildAppTemplate } from '../../scripts/templates/build-app-template.mjs';
import { resolvePresetModules } from '../../scripts/templates/preset-modules.mjs';
import { verifyCreatedApp } from '../../scripts/templates/verify-created-app.mjs';
import { verifyApplicationComposition } from './support/application-composition-probe.mjs';
import { verifyApplicationCrudGeneration } from './support/application-crud-generation.mjs';
import { verifyApplicationCrudModule } from './support/application-crud-module.mjs';
import { verifyApplicationCrudHostWiring } from './support/application-crud-host-wiring.mjs';
import { verifyApplicationCrudRuntime } from './support/application-crud-runtime.mjs';
import { verifyApplicationCrudAuthorization } from './support/application-crud-authorization.mjs';
import { verifyApplicationCrudSchemaSourceUpgrade } from './support/application-crud-schema-source-upgrade.mjs';

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
    const upgradeHelp = spawnSync(process.execPath, [join(templateRoot, '.fullnet-tools/upgrade-framework.mjs'), '--help'], { encoding: 'utf8' });
    assert.equal(upgradeHelp.status, 0, upgradeHelp.stderr || upgradeHelp.stdout);
    assert.match(upgradeHelp.stdout, /--dry-run\|--apply/);
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
    const clientTool = join(appRoot, '.fullnet-tools/openapi/generate-fullnet-client.mjs');
    const clientCheck = spawnSync(process.execPath, [clientTool, '--check'], { cwd: tmpdir(), encoding: 'utf8', windowsHide: true });
    assert.equal(clientCheck.status, 0, clientCheck.stderr || clientCheck.stdout);
    for (const relative of ['scripts/openapi/generate-fullnet-client.mjs', 'scripts/openapi/validate-client-generation-readiness.mjs',
      'contracts/openapi/fullnet-client-v1.openapi.json', 'contracts/openapi/client-generation-manifest-v1.json']) {
      assert.match(manifest.managedFiles[relative], /^[0-9a-f]{64}$/u);
      const applicationPath = relative.startsWith('scripts/openapi/') ? relative.replace('scripts/openapi/', '.fullnet-tools/openapi/') : relative;
      assert.deepEqual(readFileSync(join(appRoot, applicationPath)), readFileSync(join(templateRoot, 'framework/fullnet', relative)),
        'template replacements changed frozen client tool content: ' + relative);
    }
    assert.deepEqual(readdirSync(workspace).filter((entry) => entry.startsWith('.fullnet-create-')), []);
    assert.ok(existsSync(join(appRoot, 'src/Demo.Host.Api/Demo.Host.Api.csproj')));
    assert.ok(existsSync(join(appRoot, 'src/Demo.Host.Migrator/Demo.Host.Migrator.csproj')));
    assert.ok(existsSync(join(appRoot, 'src/Demo.Host.Worker/Demo.Host.Worker.csproj')));
    const apiConfig = JSON.parse(readFileSync(join(appRoot, 'src/Demo.Host.Api/appsettings.json'), 'utf8'));
    const workerConfig = JSON.parse(readFileSync(join(appRoot, 'src/Demo.Host.Worker/appsettings.json'), 'utf8'));
    assert.equal(apiConfig.Kestrel.Endpoints.Http.Url, 'http://localhost:5500');
    assert.equal(workerConfig.Kestrel.Endpoints.Http.Url, 'http://localhost:5501');
    assert.ok(existsSync(join(appRoot, 'src/Demo.Composition/Demo.Composition.csproj')));
    assert.match(readFileSync(join(appRoot, 'src/Demo.Composition/ApplicationModuleCatalog.cs'), 'utf8'), /namespace Demo\.Composition;/u);
    assert.ok(existsSync(join(appRoot, 'ui/admin/src/App.vue')));
    assert.match(readFileSync(join(appRoot, 'ui/admin/vite.config.ts'), 'utf8'), /http:\/\/localhost:5500/);
    assert.ok(existsSync(join(appRoot, 'packages/admin-form-designer/package.json')));
    assert.equal(existsSync(join(appRoot, '.fullnet-tools/create-app.mjs')), false);
    assert.equal(existsSync(join(appRoot, '.fullnet-tools/upgrade-framework.mjs')), false);
    assert.deepEqual(readdirSync(join(appRoot, '.fullnet-tools')), ['openapi']);
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

    const verifyManagedFiles = () => {
      for (const [managedPath, digest] of Object.entries(generatedManifest.managedFiles)) {
        const generatedPath = join(appRoot, 'framework/fullnet', managedPath);
        assert.ok(existsSync(generatedPath), `managed framework path missing: ${managedPath}`);
        const actualHash = createHash('sha256').update(readFileSync(generatedPath)).digest('hex');
        assert.equal(actualHash, digest, `managed framework content changed: ${managedPath}`);
      }
    };
    verifyManagedFiles();

    const build = spawnSync('dotnet', [
      'build', join(appRoot, 'src/Demo.Host.Api/Demo.Host.Api.csproj'), '-c', 'Release', '-v', 'quiet',
    ], { cwd: appRoot, encoding: 'utf8', timeout: 300_000 });
    assert.equal(build.status, 0, build.stderr || build.stdout);
    const migratorBuild = spawnSync('dotnet', [
      'build', join(appRoot, 'src/Demo.Host.Migrator/Demo.Host.Migrator.csproj'), '-c', 'Release', '-v', 'quiet',
    ], { cwd: appRoot, encoding: 'utf8', timeout: 300_000, windowsHide: true });
    assert.equal(migratorBuild.status, 0, migratorBuild.stderr || migratorBuild.stdout);
    const workerBuild = spawnSync('dotnet', [
      'build', join(appRoot, 'src/Demo.Host.Worker/Demo.Host.Worker.csproj'), '-c', 'Release', '-v', 'quiet',
    ], { cwd: appRoot, encoding: 'utf8', timeout: 300_000, windowsHide: true });
    assert.equal(workerBuild.status, 0, workerBuild.stderr || workerBuild.stdout);
    const diagnosticCli = join(appRoot, 'framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli');
    const configuredDiagnosis = spawnSync('dotnet', [
      'run', '--project', diagnosticCli, '-c', 'Release', '--',
      'diagnose', '--workspace', appRoot, '--profile', 'production',
    ], {
      cwd: appRoot, encoding: 'utf8', timeout: 300_000, windowsHide: true,
      env: {
        ...process.env,
        [`ConnectionStrings__${apiConfig.Database.ConnectionName}`]: 'Server=example.invalid;Password=credential-probe',
        Cache__RedisConnectionString: 'cache.example.invalid:6379,password=credential-probe',
        Realtime__RedisBackplaneConnectionString: 'realtime.example.invalid:6379,password=credential-probe',
        'FullNet__Cryptography__Sm2PrivateKeys__host-integration-signing': 'credential-probe',
      },
    });
    assert.equal(configuredDiagnosis.status, 0, configuredDiagnosis.stderr || configuredDiagnosis.stdout);
    assert.match(configuredDiagnosis.stdout, /DIAG_SECRETS_OK ok/u);
    assert.match(configuredDiagnosis.stdout, /DIAG_SDK_OK ok 检测到 \.NET SDK 10\.0\./u);
    assert.doesNotMatch(configuredDiagnosis.stdout + configuredDiagnosis.stderr, /credential-probe/u);
    const productionSettings = join(appRoot, 'src/Demo.Host.Api/appsettings.Production.json');
    const rootProductionSettings = join(appRoot, 'appsettings.Production.json');
    const diagnosisEnvironment = Object.fromEntries(Object.entries(process.env).filter(([key]) =>
      !['database:provider', 'database:commandtimeoutseconds', 'database:mysqlguidstoragemode', 'database:connectionname', 'database:connectionstring', `connectionstrings:${apiConfig.Database.ConnectionName}`,
        'cache:redisconnectionstring', 'realtime:redisbackplaneconnectionstring',
        'fullnet:cryptography:sm2privatekeys:host-integration-signing']
        .includes(key.replaceAll('__', ':').toLowerCase())));
    const runProfileDiagnosis = (overrides = {}) => spawnSync('dotnet', [
      'run', '--project', diagnosticCli, '-c', 'Release', '--no-build', '--',
      'diagnose', '--workspace', appRoot, '--profile', 'production',
    ], { cwd: appRoot, encoding: 'utf8', timeout: 60_000, windowsHide: true,
      env: { ...diagnosisEnvironment, ...overrides } });
    try {
      writeFileSync(rootProductionSettings, '{invalid-json-credential-probe');
      writeFileSync(productionSettings, JSON.stringify({
        ConnectionStrings: { [apiConfig.Database.ConnectionName]: 'Server=example.invalid;Password=credential-probe' },
        Cache: { RedisConnectionString: 'cache.example.invalid:6379,password=credential-probe' },
        Realtime: { RedisBackplaneConnectionString: 'realtime.example.invalid:6379,password=credential-probe' },
        FullNet: { Cryptography: { Sm2PrivateKeys: { 'host-integration-signing': 'credential-probe' } } },
      }));
      const profileBefore = readFileSync(productionSettings);
      const profileDiagnosis = runProfileDiagnosis();
      assert.equal(profileDiagnosis.status, 0, profileDiagnosis.stderr || profileDiagnosis.stdout);
      assert.match(profileDiagnosis.stdout, /DIAG_CONNECTION_CONFIGURED ok/u);
      assert.match(profileDiagnosis.stdout, /DIAG_SECRETS_OK ok/u);
      assert.doesNotMatch(profileDiagnosis.stdout + profileDiagnosis.stderr, /credential-probe/u);
      assert.deepEqual(readFileSync(productionSettings), profileBefore);
      for (const relativePath of ['appsettings.json', 'src/Demo.Host.Api/appsettings.json',
        'src/Demo.Host.Migrator/appsettings.json', 'src/Demo.Host.Worker/appsettings.json']) {
        const settingsPath = join(appRoot, relativePath);
        const originalSettings = readFileSync(settingsPath);
        const originalJson = originalSettings.toString('utf8').trimEnd();
        try {
          writeFileSync(settingsPath, `// credential-probe comment\n${originalJson.slice(0, -1)},}`);
          const tolerantBefore = readFileSync(settingsPath);
          const tolerantDiagnosis = runProfileDiagnosis();
          assert.equal(tolerantDiagnosis.status, 0, tolerantDiagnosis.stderr || tolerantDiagnosis.stdout);
          assert.match(tolerantDiagnosis.stdout, /DIAG_APP_PROFILE_OK ok/u);
          assert.doesNotMatch(tolerantDiagnosis.stdout + tolerantDiagnosis.stderr, /credential-probe/u);
          assert.deepEqual(readFileSync(settingsPath), tolerantBefore);
          const baseProfile = JSON.parse(originalJson);
          const frozenValues = [
            ['FullNet:Modules:Preset', baseProfile.FullNet.Modules.Preset],
            ['Database:Provider', baseProfile.Database.Provider],
          ];
          if (relativePath.includes('.Host.Worker/')) {
            frozenValues.push(['Kestrel:Endpoints:Http:Url', baseProfile.Kestrel.Endpoints.Http.Url]);
          }
          const profileParent = (settings, path) => {
            const parts = path.split(':');
            let parent = settings;
            for (const part of parts.slice(0, -1)) parent = parent[part] ??= {};
            return [parent, parts.at(-1)];
          };
          for (const layout of ['flat', 'flat-case', 'nested-case']) {
            const flattenedProfile = structuredClone(baseProfile);
            for (const [path, value] of frozenValues) {
              const [parent, leaf] = profileParent(flattenedProfile, path);
              delete parent[leaf];
              if (layout === 'nested-case') {
                const [caseParent, caseLeaf] = profileParent(flattenedProfile, path.toLowerCase());
                caseParent[caseLeaf] = value;
              } else {
                flattenedProfile[layout === 'flat-case' ? path.toUpperCase() : path] = value;
              }
            }
            writeFileSync(settingsPath, JSON.stringify(flattenedProfile));
            const flattenedBefore = readFileSync(settingsPath);
            const flattenedDiagnosis = runProfileDiagnosis();
            assert.equal(flattenedDiagnosis.status, 0, flattenedDiagnosis.stderr || flattenedDiagnosis.stdout);
            assert.match(flattenedDiagnosis.stdout, /DIAG_APP_PROFILE_OK ok/u);
            assert.doesNotMatch(flattenedDiagnosis.stdout + flattenedDiagnosis.stderr, /credential-probe/iu);
            assert.deepEqual(readFileSync(settingsPath), flattenedBefore);
            assert.deepEqual(readFileSync(productionSettings), profileBefore);
          }
          // 环境凭据不能掩盖基础预设或 Worker 端口被空集合覆盖后的漂移。
          for (const [path] of frozenValues) {
            const clearedProfile = structuredClone(baseProfile);
            clearedProfile[path.toUpperCase()] = {};
            writeFileSync(settingsPath, JSON.stringify(clearedProfile));
            const clearedBefore = readFileSync(settingsPath);
            const clearedDiagnosis = runProfileDiagnosis();
            assert.equal(clearedDiagnosis.status, 1);
            assert.match(clearedDiagnosis.stdout, /DIAG_APP_PROFILE_MISMATCH error/u);
            assert.doesNotMatch(clearedDiagnosis.stdout, /DIAG_APP_PROFILE_OK/u);
            assert.doesNotMatch(clearedDiagnosis.stdout + clearedDiagnosis.stderr, /credential-probe/iu);
            assert.deepEqual(readFileSync(settingsPath), clearedBefore);
            assert.deepEqual(readFileSync(productionSettings), profileBefore);
          }
          // 即使有效环境变量可覆盖数据库值，宿主也必须先成功加载每个基础配置文件。
          for (const duplicate of ['"credential-probe":1,"CREDENTIAL-PROBE":2',
            '"Probe":{"Value":1},"probe:value":"credential-probe"']) {
            writeFileSync(settingsPath, `${originalJson.slice(0, -1)},${duplicate}}`);
            const duplicateBefore = readFileSync(settingsPath);
            const duplicateDiagnosis = runProfileDiagnosis({
              Database__ConnectionString: 'Server=override.invalid;Password=credential-probe',
            });
            assert.equal(duplicateDiagnosis.status, 1);
            assert.match(duplicateDiagnosis.stdout, /DIAG_APP_PROFILE_INVALID error/u);
            assert.doesNotMatch(duplicateDiagnosis.stdout, /DIAG_APP_PROFILE_OK/u);
            assert.doesNotMatch(duplicateDiagnosis.stdout + duplicateDiagnosis.stderr, /credential-probe/iu);
            assert.deepEqual(readFileSync(settingsPath), duplicateBefore);
          }
        } finally {
          writeFileSync(settingsPath, originalSettings);
        }
      }
      const diagnosticBaseSettings = join(appRoot, 'src/Demo.Host.Api/appsettings.json');
      const diagnosticBaseBefore = readFileSync(diagnosticBaseSettings);
      try {
        const namedProfile = JSON.parse(profileBefore.toString('utf8'));
        delete namedProfile.ConnectionStrings;
        writeFileSync(productionSettings, JSON.stringify(namedProfile));
        const baseName = apiConfig.Database.ConnectionName;
        for (const layout of ['flat', 'lowercase', 'nested-colon', 'empty-overwrite']) {
          const namedBase = { ...apiConfig };
          delete namedBase.ConnectionStrings;
          const credential = 'Server=base.invalid;Password=credential-probe';
          if (layout === 'flat') namedBase[`connectionstrings:${baseName.toUpperCase()}`] = credential;
          if (layout === 'lowercase') namedBase.connectionstrings = { [baseName.toUpperCase()]: credential };
          if (layout === 'nested-colon') {
            namedBase.Database = { ...apiConfig.Database, ConnectionName: `${baseName}:read` };
            namedBase.ConnectionStrings = { [baseName]: { read: credential } };
          }
          if (layout === 'empty-overwrite') {
            namedBase.ConnectionStrings = { [baseName]: credential };
            namedBase[`connectionstrings:${baseName}`] = {};
          }
          writeFileSync(diagnosticBaseSettings, JSON.stringify(namedBase));
          const namedBaseBefore = readFileSync(diagnosticBaseSettings);
          const namedDiagnosis = runProfileDiagnosis();
          assert.equal(namedDiagnosis.status, layout === 'empty-overwrite' ? 1 : 0,
            namedDiagnosis.stderr || namedDiagnosis.stdout);
          assert.match(namedDiagnosis.stdout, layout === 'empty-overwrite'
            ? /DIAG_CONNECTION_MISSING error/u : /DIAG_CONNECTION_CONFIGURED ok/u);
          assert.doesNotMatch(namedDiagnosis.stdout + namedDiagnosis.stderr, /credential-probe/u);
          assert.deepEqual(readFileSync(diagnosticBaseSettings), namedBaseBefore);
          if (layout === 'empty-overwrite') {
            const overriddenNameDiagnosis = runProfileDiagnosis({ [`ConnectionStrings__${baseName}`]: credential });
            assert.equal(overriddenNameDiagnosis.status, 0,
              overriddenNameDiagnosis.stderr || overriddenNameDiagnosis.stdout);
            assert.match(overriddenNameDiagnosis.stdout, /DIAG_CONNECTION_CONFIGURED ok/u);
            assert.doesNotMatch(overriddenNameDiagnosis.stdout + overriddenNameDiagnosis.stderr, /credential-probe/u);
            assert.deepEqual(readFileSync(diagnosticBaseSettings), namedBaseBefore);
          }
        }
        writeFileSync(diagnosticBaseSettings, JSON.stringify({ ...apiConfig,
          ConnectionStrings: undefined, [`connectionstrings:${baseName}`]: 'Server=base.invalid;Password=credential-probe',
        }));
        writeFileSync(productionSettings, JSON.stringify({ ...namedProfile,
          [`ConnectionStrings:${baseName}`]: null,
        }));
        const nullNamedBaseBefore = readFileSync(diagnosticBaseSettings);
        const nullNamedProfileBefore = readFileSync(productionSettings);
        const nullNamedDiagnosis = runProfileDiagnosis();
        assert.equal(nullNamedDiagnosis.status, 1);
        assert.match(nullNamedDiagnosis.stdout, /DIAG_CONNECTION_MISSING error/u);
        assert.doesNotMatch(nullNamedDiagnosis.stdout + nullNamedDiagnosis.stderr, /credential-probe/u);
        assert.deepEqual(readFileSync(diagnosticBaseSettings), nullNamedBaseBefore);
        assert.deepEqual(readFileSync(productionSettings), nullNamedProfileBefore);
        writeFileSync(diagnosticBaseSettings, diagnosticBaseBefore);
        writeFileSync(productionSettings, profileBefore);
        const secretPaths = ['Cache:RedisConnectionString', 'Realtime:RedisBackplaneConnectionString',
          'FullNet:Cryptography:Sm2PrivateKeys:host-integration-signing'];
        const removeSecret = (settings, path) => {
          const segments = path.split(':');
          let parent = settings;
          for (const segment of segments.slice(0, -1)) parent = parent?.[segment];
          if (parent && typeof parent === 'object') delete parent[segments.at(-1)];
        };
        const setSecret = (settings, path, value) => {
          const segments = path.split(':');
          let parent = settings;
          for (const segment of segments.slice(0, -1)) {
            parent[segment] ??= {};
            parent = parent[segment];
          }
          parent[segments.at(-1)] = value;
        };
        const secretProfile = JSON.parse(profileBefore.toString('utf8'));
        for (const path of secretPaths) removeSecret(secretProfile, path);
        writeFileSync(productionSettings, JSON.stringify(secretProfile));
        const secretProfileBefore = readFileSync(productionSettings);
        for (const shape of ['placeholder', 'valid', 'null', 'empty-overwrite']) {
          const secretBase = structuredClone(apiConfig);
          for (const path of secretPaths) {
            if (shape === 'empty-overwrite') setSecret(secretBase, path, 'credential-probe');
            else removeSecret(secretBase, path);
            secretBase[path.toUpperCase()] = shape === 'null' ? null : shape === 'empty-overwrite' ? {}
              : shape === 'valid' ? 'credential-probe' : 'CHANGEME';
          }
          writeFileSync(diagnosticBaseSettings, JSON.stringify(secretBase));
          const secretBaseBefore = readFileSync(diagnosticBaseSettings);
          const secretDiagnosis = runProfileDiagnosis();
          assert.equal(secretDiagnosis.status, shape === 'valid' ? 0 : 1,
            secretDiagnosis.stderr || secretDiagnosis.stdout);
          assert.match(secretDiagnosis.stdout, shape === 'valid' ? /DIAG_SECRETS_OK ok/u
            : /DIAG_SECRETS_PLACEHOLDER error.*有 3 个秘密/u);
          assert.doesNotMatch(secretDiagnosis.stdout + secretDiagnosis.stderr, /credential-probe/u);
          assert.deepEqual(readFileSync(diagnosticBaseSettings), secretBaseBefore);
          assert.deepEqual(readFileSync(productionSettings), secretProfileBefore);
          if (shape === 'null') {
            const repairedSecretDiagnosis = runProfileDiagnosis(Object.fromEntries(secretPaths.map(path =>
              [path.replaceAll(':', '__'), 'credential-probe'])));
            assert.equal(repairedSecretDiagnosis.status, 0,
              repairedSecretDiagnosis.stderr || repairedSecretDiagnosis.stdout);
            assert.match(repairedSecretDiagnosis.stdout, /DIAG_SECRETS_OK ok/u);
            assert.doesNotMatch(repairedSecretDiagnosis.stdout + repairedSecretDiagnosis.stderr, /credential-probe/u);
            assert.deepEqual(readFileSync(diagnosticBaseSettings), secretBaseBefore);
            assert.deepEqual(readFileSync(productionSettings), secretProfileBefore);
          }
        }
        writeFileSync(diagnosticBaseSettings, diagnosticBaseBefore);
        writeFileSync(productionSettings, profileBefore);
        writeFileSync(productionSettings, JSON.stringify({ ...JSON.parse(profileBefore.toString('utf8')),
          Database: {
            Provider: { Probe: 'credential-probe' }, CommandTimeoutSeconds: ['credential-probe'],
            MySqlGuidStorageMode: { Probe: 'credential-probe' }, ConnectionName: ['credential-probe'],
            ConnectionString: { Probe: 'credential-probe' },
          },
        }));
        const childProfileBefore = readFileSync(productionSettings);
        const childProfileDiagnosis = runProfileDiagnosis();
        assert.equal(childProfileDiagnosis.status, 0, childProfileDiagnosis.stderr || childProfileDiagnosis.stdout);
        assert.match(childProfileDiagnosis.stdout, /DIAG_CONNECTION_CONFIGURED ok/u);
        assert.doesNotMatch(childProfileDiagnosis.stdout + childProfileDiagnosis.stderr, /credential-probe/u);
        assert.deepEqual(readFileSync(productionSettings), childProfileBefore);
        writeFileSync(productionSettings, JSON.stringify({ ...JSON.parse(profileBefore.toString('utf8')),
          ConnectionStrings: { fullnet: 'Server=named.invalid;Password=credential-probe' },
        }));
        writeFileSync(diagnosticBaseSettings, JSON.stringify({ ...apiConfig,
          Database: { ...apiConfig.Database, ConnectionName: { Probe: 'credential-probe' } },
        }));
        const childBaseBefore = readFileSync(diagnosticBaseSettings);
        const childBaseDiagnosis = runProfileDiagnosis();
        assert.equal(childBaseDiagnosis.status, 0, childBaseDiagnosis.stderr || childBaseDiagnosis.stdout);
        assert.match(childBaseDiagnosis.stdout, /DIAG_CONNECTION_CONFIGURED ok/u);
        assert.doesNotMatch(childBaseDiagnosis.stdout + childBaseDiagnosis.stderr, /credential-probe/u);
        assert.deepEqual(readFileSync(diagnosticBaseSettings), childBaseBefore);
        writeFileSync(diagnosticBaseSettings, JSON.stringify({ ...apiConfig,
          Database: { ...apiConfig.Database, ConnectionName: null },
        }));
        const nullNameBefore = readFileSync(diagnosticBaseSettings);
        const nullNameDiagnosis = runProfileDiagnosis();
        assert.equal(nullNameDiagnosis.status, 1);
        assert.match(nullNameDiagnosis.stdout, /DIAG_APPSETTINGS_INVALID error/u);
        const nullNameWithChildDiagnosis = runProfileDiagnosis({ Database__ConnectionName__Probe: 'credential-probe' });
        assert.equal(nullNameWithChildDiagnosis.status, 1);
        assert.match(nullNameWithChildDiagnosis.stdout, /DIAG_APPSETTINGS_INVALID error/u);
        assert.doesNotMatch(nullNameWithChildDiagnosis.stdout + nullNameWithChildDiagnosis.stderr, /credential-probe/u);
        const restoredNameDiagnosis = runProfileDiagnosis({ Database__ConnectionName: 'fullnet' });
        assert.equal(restoredNameDiagnosis.status, 0, restoredNameDiagnosis.stderr || restoredNameDiagnosis.stdout);
        assert.deepEqual(readFileSync(diagnosticBaseSettings), nullNameBefore);
        writeFileSync(diagnosticBaseSettings, JSON.stringify({ ...apiConfig,
          Database: { ...apiConfig.Database, ConnectionName: 42 },
        }));
        writeFileSync(productionSettings, JSON.stringify({ ...JSON.parse(profileBefore.toString('utf8')),
          ConnectionStrings: { 42: 'Server=named.invalid;Password=credential-probe' },
        }));
        const numericNameBefore = readFileSync(diagnosticBaseSettings);
        const numericNameDiagnosis = runProfileDiagnosis();
        assert.equal(numericNameDiagnosis.status, 0, numericNameDiagnosis.stderr || numericNameDiagnosis.stdout);
        assert.match(numericNameDiagnosis.stdout, /DIAG_CONNECTION_CONFIGURED ok/u);
        assert.doesNotMatch(numericNameDiagnosis.stdout + numericNameDiagnosis.stderr, /credential-probe/u);
        assert.deepEqual(readFileSync(diagnosticBaseSettings), numericNameBefore);
      } finally {
        writeFileSync(diagnosticBaseSettings, diagnosticBaseBefore);
        writeFileSync(productionSettings, profileBefore);
      }
      const directProfile = { ...JSON.parse(profileBefore.toString('utf8')),
        Database: { ConnectionString: 'Server=direct.invalid;Password=credential-probe', ConnectionName: null },
        ConnectionStrings: { [apiConfig.Database.ConnectionName]: 'CHANGEME' },
      };
      writeFileSync(productionSettings, JSON.stringify(directProfile));
      const directBefore = readFileSync(productionSettings);
      const directDiagnosis = runProfileDiagnosis();
      assert.equal(directDiagnosis.status, 0, directDiagnosis.stderr || directDiagnosis.stdout);
      assert.match(directDiagnosis.stdout, /DIAG_CONNECTION_CONFIGURED ok/u);
      assert.doesNotMatch(directDiagnosis.stdout + directDiagnosis.stderr, /credential-probe/u);
      assert.deepEqual(readFileSync(productionSettings), directBefore);
      writeFileSync(productionSettings, JSON.stringify({ ...directProfile,
        Database: { ConnectionString: 'CHANGEME', ConnectionName: apiConfig.Database.ConnectionName },
        ConnectionStrings: { [apiConfig.Database.ConnectionName]: 'Server=named.invalid;Password=credential-probe' },
      }));
      const placeholderBefore = readFileSync(productionSettings);
      const directPlaceholderDiagnosis = runProfileDiagnosis();
      assert.equal(directPlaceholderDiagnosis.status, 1);
      assert.match(directPlaceholderDiagnosis.stdout, /DIAG_CONNECTION_MISSING error/u);
      const directEnvironmentDiagnosis = runProfileDiagnosis({
        Database__ConnectionString: 'Server=environment.invalid;Password=credential-probe',
      });
      assert.equal(directEnvironmentDiagnosis.status, 0, directEnvironmentDiagnosis.stderr || directEnvironmentDiagnosis.stdout);
      assert.match(directEnvironmentDiagnosis.stdout, /DIAG_CONNECTION_CONFIGURED ok/u);
      assert.doesNotMatch(directEnvironmentDiagnosis.stdout + directEnvironmentDiagnosis.stderr, /credential-probe/u);
      const blankDirectDiagnosis = runProfileDiagnosis({ Database__ConnectionString: ' ' });
      assert.equal(blankDirectDiagnosis.status, 0, blankDirectDiagnosis.stderr || blankDirectDiagnosis.stdout);
      assert.match(blankDirectDiagnosis.stdout, /DIAG_CONNECTION_CONFIGURED ok/u);
      assert.deepEqual(readFileSync(productionSettings), placeholderBefore);
      writeFileSync(productionSettings, JSON.stringify({ ...JSON.parse(placeholderBefore.toString('utf8')),
        Database: { ConnectionString: null, ConnectionName: apiConfig.Database.ConnectionName },
      }));
      const nullDirectBefore = readFileSync(productionSettings);
      const nullDirectDiagnosis = runProfileDiagnosis();
      assert.equal(nullDirectDiagnosis.status, 0, nullDirectDiagnosis.stderr || nullDirectDiagnosis.stdout);
      assert.match(nullDirectDiagnosis.stdout, /DIAG_CONNECTION_CONFIGURED ok/u);
      assert.deepEqual(readFileSync(productionSettings), nullDirectBefore);
      writeFileSync(productionSettings, profileBefore);
      const invalidProviderDiagnosis = runProfileDiagnosis({ Database__Provider: 'credential-probe' });
      assert.equal(invalidProviderDiagnosis.status, 1);
      assert.match(invalidProviderDiagnosis.stdout, /DIAG_DATABASE_PROVIDER_INVALID error/u);
      assert.doesNotMatch(invalidProviderDiagnosis.stdout + invalidProviderDiagnosis.stderr, /credential-probe/u);
      const undefinedProviderDiagnosis = runProfileDiagnosis({ Database__Provider: '2' });
      assert.equal(undefinedProviderDiagnosis.status, 1);
      assert.match(undefinedProviderDiagnosis.stdout, /DIAG_DATABASE_PROVIDER_INVALID error/u);
      const invalidTimeoutDiagnosis = runProfileDiagnosis({ Database__CommandTimeoutSeconds: '0' });
      assert.equal(invalidTimeoutDiagnosis.status, 1);
      assert.match(invalidTimeoutDiagnosis.stdout, /DIAG_DATABASE_TIMEOUT_INVALID error/u);
      const legacyGuidDiagnosis = runProfileDiagnosis({ Database__MySqlGuidStorageMode: '0' });
      assert.equal(legacyGuidDiagnosis.status, 1);
      assert.match(legacyGuidDiagnosis.stdout, /DIAG_DATABASE_GUID_STORAGE_INVALID error/u);
      const validOptionsDiagnosis = runProfileDiagnosis({
        Database__CommandTimeoutSeconds: '0x1', Database__MySqlGuidStorageMode: '1',
      });
      assert.equal(validOptionsDiagnosis.status, 0, validOptionsDiagnosis.stderr || validOptionsDiagnosis.stdout);
      const sqlServerLegacyDiagnosis = runProfileDiagnosis({ Database__Provider: '0', Database__MySqlGuidStorageMode: '0' });
      assert.equal(sqlServerLegacyDiagnosis.status, 0, sqlServerLegacyDiagnosis.stderr || sqlServerLegacyDiagnosis.stdout);
      writeFileSync(productionSettings, JSON.stringify({ ...JSON.parse(profileBefore.toString('utf8')),
        Database: { CommandTimeoutSeconds: null, MySqlGuidStorageMode: null },
      }));
      const nullOptionsBefore = readFileSync(productionSettings);
      const nullOptionsDiagnosis = runProfileDiagnosis();
      assert.equal(nullOptionsDiagnosis.status, 1);
      assert.match(nullOptionsDiagnosis.stdout, /DIAG_DATABASE_TIMEOUT_INVALID error/u);
      assert.match(nullOptionsDiagnosis.stdout, /DIAG_DATABASE_GUID_STORAGE_INVALID error/u);
      assert.deepEqual(readFileSync(productionSettings), nullOptionsBefore);
      writeFileSync(productionSettings, profileBefore);
      const numericProviderDiagnosis = runProfileDiagnosis({ Database__Provider: '1' });
      assert.equal(numericProviderDiagnosis.status, 0, numericProviderDiagnosis.stderr || numericProviderDiagnosis.stdout);
      assert.doesNotMatch(numericProviderDiagnosis.stdout, /DIAG_DATABASE_PROVIDER_INVALID/u);
      assert.deepEqual(readFileSync(productionSettings), profileBefore);
      writeFileSync(productionSettings, JSON.stringify({
        ConnectionStrings: { [apiConfig.Database.ConnectionName]: '<your-connection>' },
        Cache: { RedisConnectionString: null },
      }));
      const missingDiagnosis = runProfileDiagnosis();
      assert.equal(missingDiagnosis.status, 1, missingDiagnosis.stderr || missingDiagnosis.stdout);
      assert.match(missingDiagnosis.stdout, /DIAG_CONNECTION_MISSING error/u);
      assert.match(missingDiagnosis.stdout, /DIAG_SECRETS_PLACEHOLDER error/u);
      assert.doesNotMatch(missingDiagnosis.stdout + missingDiagnosis.stderr, /credential-probe/u);
      const misplacedCredentialDiagnosis = runProfileDiagnosis({
        Database__ConnectionName: 'Server=example.invalid;Password=credential-probe',
      });
      assert.equal(misplacedCredentialDiagnosis.status, 1);
      assert.match(misplacedCredentialDiagnosis.stdout, /DIAG_CONNECTION_MISSING error/u);
      assert.doesNotMatch(misplacedCredentialDiagnosis.stdout + misplacedCredentialDiagnosis.stderr, /credential-probe/u);
    } finally {
      rmSync(productionSettings, { force: true });
      rmSync(rootProductionSettings, { force: true });
    }
    const applicationSdkProfile = join(appRoot, 'global.json');
    const applicationSdkBefore = readFileSync(applicationSdkProfile);
    const diagnosticAssembly = join(diagnosticCli, 'bin/Release/net10.0/Full.NET.CodeGeneration.Cli.dll');
    const installedSdks = spawnSync('dotnet', ['--list-sdks'], {
      cwd: appRoot, encoding: 'utf8', timeout: 60_000, windowsHide: true,
    });
    assert.equal(installedSdks.status, 0, installedSdks.stderr || installedSdks.stdout);
    const olderSdk = installedSdks.stdout.match(/^([1-9]\.0\.\d+)\s/gmu)?.at(-1)?.trim();
    const diagnoseSdk = () => spawnSync('dotnet', [
      'exec', diagnosticAssembly, 'diagnose', '--workspace', appRoot, '--profile', 'development',
    ], { cwd: appRoot, encoding: 'utf8', timeout: 60_000, windowsHide: true, env: diagnosisEnvironment });
    try {
      writeFileSync(applicationSdkProfile, JSON.stringify({ sdk: { version: '99.0.100', rollForward: 'disable' } }));
      const missingSdkDiagnosis = diagnoseSdk();
      assert.equal(missingSdkDiagnosis.status, 1);
      assert.match(missingSdkDiagnosis.stdout, /DIAG_SDK_MISSING error/u);
      assert.doesNotMatch(missingSdkDiagnosis.stdout, /DIAG_SDK_OK/u);
      if (olderSdk) {
        writeFileSync(applicationSdkProfile, JSON.stringify({ sdk: { version: olderSdk, rollForward: 'disable' } }));
        const olderSdkBefore = readFileSync(applicationSdkProfile);
        const incompatibleSdkDiagnosis = diagnoseSdk();
        assert.equal(incompatibleSdkDiagnosis.status, 1);
        assert.match(incompatibleSdkDiagnosis.stdout, /DIAG_SDK_INCOMPATIBLE error/u);
        assert.doesNotMatch(incompatibleSdkDiagnosis.stdout, /DIAG_SDK_OK/u);
        assert.deepEqual(readFileSync(applicationSdkProfile), olderSdkBefore);
      }
    } finally {
      writeFileSync(applicationSdkProfile, applicationSdkBefore);
    }
    const assets = JSON.parse(readFileSync(join(appRoot, 'src/Demo.Host.Api/obj/project.assets.json'), 'utf8'));
    const implementationModules = Object.keys(assets.libraries)
      .map((name) => /^Full\.NET\.Modules\.([A-Za-z0-9]+)\//.exec(name)?.[1])
      .filter(Boolean);
    for (const module of implementationModules) {
      assert.ok(['Identity', 'Tenancy', 'Settings', 'Organization'].includes(module),
        `unexpected implementation module in minimal build: ${module}`);
    }
    verifyApplicationComposition(appRoot);
    verifyApplicationCrudGeneration(appRoot);
    verifyApplicationCrudModule(appRoot, { removeTestSqlComment: true });
    verifyApplicationCrudHostWiring(appRoot);
    verifyApplicationCrudAuthorization(appRoot);
    verifyApplicationCrudRuntime(appRoot);
    verifyManagedFiles();
    const pnpm = 'pnpm';
    const install = spawnSync(pnpm, [
      'install', '--filter', '@fullnet/admin...', '--frozen-lockfile', '--ignore-scripts',
    ], { cwd: appRoot, encoding: 'utf8', timeout: 180_000, shell: process.platform === 'win32' });
    assert.equal(install.status, 0, install.stderr || install.stdout || install.error?.message);
    const frontendBuild = spawnSync(pnpm, [
      '--filter', '@fullnet/admin', 'build',
    ], { cwd: appRoot, encoding: 'utf8', timeout: 180_000, shell: process.platform === 'win32' });
    assert.equal(frontendBuild.status, 0, frontendBuild.stderr || frontendBuild.stdout || frontendBuild.error?.message);

    verifyApplicationCrudSchemaSourceUpgrade(appRoot);

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
        '--http-port', preset === 'platform' ? '5181' : preset === 'saas' ? '65535' : '5180',
      ], { encoding: 'utf8', timeout: 150_000 });
      assert.equal(generated.status, 0, `${preset}: ${generated.stderr || generated.stdout}`);
      if (preset === 'platform') {
        const platformApiConfig = JSON.parse(readFileSync(join(presetRoot, 'src/Demo.Host.Api/appsettings.json'), 'utf8'));
        const platformWorkerConfig = JSON.parse(readFileSync(join(presetRoot, 'src/Demo.Host.Worker/appsettings.json'), 'utf8'));
        assert.equal(platformApiConfig.Kestrel.Endpoints.Http.Url, 'http://localhost:5181');
        assert.equal(platformWorkerConfig.Kestrel.Endpoints.Http.Url, 'http://localhost:5182');
      }
      if (preset === 'saas') {
        const saasApiConfig = JSON.parse(readFileSync(join(presetRoot, 'src/Demo.Host.Api/appsettings.json'), 'utf8'));
        const saasWorkerConfig = JSON.parse(readFileSync(join(presetRoot, 'src/Demo.Host.Worker/appsettings.json'), 'utf8'));
        assert.equal(saasApiConfig.Kestrel.Endpoints.Http.Url, 'http://localhost:65535');
        assert.equal(saasWorkerConfig.Kestrel.Endpoints.Http.Url, 'http://localhost:65534');
      }
      const presetBuild = spawnSync('dotnet', [
        'build', join(presetRoot, 'src/Demo.Host.Api/Demo.Host.Api.csproj'), '-c', 'Release', '-v', 'quiet',
      ], { cwd: presetRoot, encoding: 'utf8', timeout: 300_000 });
      assert.equal(presetBuild.status, 0, `${preset}: ${presetBuild.stderr || presetBuild.stdout}`);
      const presetMigratorBuild = spawnSync('dotnet', [
        'build', join(presetRoot, 'src/Demo.Host.Migrator/Demo.Host.Migrator.csproj'), '-c', 'Release', '-v', 'quiet',
      ], { cwd: presetRoot, encoding: 'utf8', timeout: 300_000, windowsHide: true });
      assert.equal(presetMigratorBuild.status, 0, `${preset} migrator: ${presetMigratorBuild.stderr || presetMigratorBuild.stdout}`);
      const presetWorkerBuild = spawnSync('dotnet', [
        'build', join(presetRoot, 'src/Demo.Host.Worker/Demo.Host.Worker.csproj'), '-c', 'Release', '-v', 'quiet',
      ], { cwd: presetRoot, encoding: 'utf8', timeout: 300_000, windowsHide: true });
      assert.equal(presetWorkerBuild.status, 0, `${preset} worker: ${presetWorkerBuild.stderr || presetWorkerBuild.stdout}`);
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
