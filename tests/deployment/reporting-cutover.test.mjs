import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const root = fileURLToPath(new URL('../..', import.meta.url));

function runEvidence(scenario, provider = 'SqlServer') {
  const script = String.raw`
$tenant = '00000000-0000-7000-8000-000000000001'
$task = '00000000-0000-7000-8000-000000000002'
$scenario = $env:FULLNET_CUTOVER_TEST_CASE
function Reply {
    if ($scenario -eq 'invalid-id') { throw 'Database must not be called.' }
    if ($args[0] -notlike "*$tenant*" -or $args[0] -notlike "*$task*" -or $args[0] -like '*{{*') { throw 'UUID bindings missing.' }
    $global:LASTEXITCODE = 0
    switch ($scenario) {
        'missing' { 'null' }
        'malformed' { 'malformed-result' }
        'wrong-tenant' { @{tenantId=$task;taskId=$task} | ConvertTo-Json -Compress }
        default { @{tenantId=$tenant;taskId=$task;hasLease=1;hasRowIdentityPrefix=0;fileCount=2} | ConvertTo-Json -Compress }
    }
}
function sqlcmd {
    if ('-N' -notin $args -or '-C' -in $args -or '-x' -notin $args -or '-Q' -notin $args) { throw 'Unexpected SQL Server transport.' }
    Reply $args[-1]
}
function mysql {
    if ('--ssl-mode=VERIFY_IDENTITY' -notin $args -or '--raw' -notin $args) { throw 'Unexpected MySQL transport.' }
    Reply $args[-1]
}
function helm { throw 'Evidence must not call Helm.' }
$parameters = @{Provider=$env:FULLNET_CUTOVER_TEST_PROVIDER;Server='fixture.invalid';Database='fixture';TenantId=$tenant;TaskId=$task;
    MySqlLoginPath='fixture-reader';MySqlCaFile='./eng/deploy/reporting-cutover/mysql.sql'}
if ($scenario -eq 'invalid-id') { $parameters.TaskId = 'invalid-id' }
& './eng/deploy/reporting-cutover/Get-ReportingCutoverEvidence.ps1' @parameters
`;
  const result = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', script], {
    cwd: root, encoding: 'utf8', timeout: 20_000, windowsHide: true,
    env: { ...process.env, FULLNET_CUTOVER_TEST_CASE: scenario, FULLNET_CUTOVER_TEST_PROVIDER: provider }
  });
  assert.ifError(result.error);
  return result;
}

for (const provider of ['SqlServer', 'MySql']) {
  test(`${provider} evidence returns the requested task without invoking deployment`, () => {
    const { status, stdout, stderr } = runEvidence('ready', provider);
    assert.equal(status, 0, stderr);
    const record = JSON.parse(stdout);
    assert.equal(record.fileCount, 2);
    assert.equal(record.hasLease, true);
    assert.equal(record.hasRowIdentityPrefix, false);
  });
}

for (const [scenario, message] of [
  ['missing', /No reporting task found/],
  ['malformed', /Invalid reporting cutover evidence result/],
  ['wrong-tenant', /Invalid reporting cutover evidence result/],
  ['invalid-id', /Cannot process argument transformation on parameter 'TaskId'/],
]) {
  test(`${scenario} evidence fails without returning a record`, () => {
    const { status, stdout, stderr } = runEvidence(scenario);
    assert.notEqual(status, 0);
    assert.equal(stdout.trim(), '');
    assert.match(stderr, message);
    assert.doesNotMatch(stderr, /Database must not be called|malformed-result/);
  });
}

function run(scenario, provider = 'SqlServer') {
  const script = String.raw`
$scenario = $env:FULLNET_CUTOVER_TEST_CASE
$provider = $env:FULLNET_CUTOVER_TEST_PROVIDER
$global:probeRan = $false
function kubectl {
    $global:LASTEXITCODE = 0
    if ($args[1] -ne 'isolated-test' -or $args[3] -ne 'fixture') { throw 'Unexpected Kubernetes target.' }
    $role = if ($args[-1] -like '*fullnet-worker') { 'worker' } else { 'api' }
    $items = @()
    switch ($args[6]) {
        'hpa' {
            if ($scenario -eq 'autoscaler') { $items = @(@{spec=@{scaleTargetRef=@{kind='Deployment';name='worker-fixture'}}}) }
        }
        'deployments' {
            $replicas = if ($role -eq 'api' -and ($scenario -eq 'active' -or ($scenario -eq 'restarted' -and $global:probeRan))) { 1 } else { 0 }
            $observed = if ($scenario -eq 'stale') { 1 } else { 2 }
            $items = @(@{metadata=@{name="$role-fixture";generation=2;labels=@{'app.kubernetes.io/component'=$role}};
                spec=@{replicas=$replicas};status=@{observedGeneration=$observed}})
        }
        'pods' {
            if ($role -eq 'worker' -and $scenario -eq 'terminating') { $items = @(@{metadata=@{deletionTimestamp='2026-11-10T00:00:00Z'}}) }
            if ($scenario -eq 'cluster-unavailable') { $global:LASTEXITCODE = 1; return }
        }
        default { throw 'Unexpected fixture command.' }
    }
    @{items=$items} | ConvertTo-Json -Compress -Depth 8
}
function Invoke-TestDatabaseProbe {
    $global:probeRan = $true
    Write-Host 'database-probed'
    $global:LASTEXITCODE = if ($scenario -eq 'db-error') { 1 } else { 0 }
    switch ($scenario) {
        'pending' { '2' }
        'readonly' { '-1' }
        'unexpected' { '0'; 'more' }
        default { '0' }
    }
}
function sqlcmd {
    if ('-N' -notin $args -or '-C' -in $args -or $args[-1] -notlike '*sqlserver.sql') { throw 'Unexpected SQL Server transport.' }
    Invoke-TestDatabaseProbe
}
function mysql {
    if ('--ssl-mode=VERIFY_IDENTITY' -notin $args -or '--login-path=fixture-reader' -notin $args) { throw 'Unexpected MySQL transport.' }
    Invoke-TestDatabaseProbe
}
function helm {
    if ($args[-2] -ne '--kube-context' -or $args[-1] -ne 'isolated-test') { throw 'Unexpected Helm context.' }
    if ('--wait' -notin $args -or '--wait-for-jobs' -notin $args) { throw 'Missing Helm wait flags.' }
    $global:LASTEXITCODE = if ($scenario -eq 'migrator-failed' -and $args[2] -eq 'fullnet-migrator') { 1 } else { 0 }
    Write-Host "helm-stage=$($args[2])"
}
$parameters = @{ Namespace='fixture'; ImageTag='fixed-test'; KubeContext='isolated-test' }
if ($scenario -ne 'normal') {
    $parameters.ReportingOutputCutover = $true
    $parameters.ReportingDatabaseProvider = $provider
    $parameters.ReportingDatabaseServer = 'db.example.invalid'
    $parameters.ReportingDatabaseName = 'fixture'
    if ($provider -eq 'MySql') {
        $parameters.ReportingMySqlLoginPath = 'fixture-reader'
        $parameters.ReportingMySqlCaFile = './eng/deploy/reporting-cutover/mysql.sql'
    }
    if ($scenario -eq 'missing-context') { $parameters.Remove('KubeContext') }
}
& './eng/deploy/Invoke-FullNetRelease.ps1' @parameters
`;
  const result = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', script], {
    cwd: root, encoding: 'utf8', timeout: 20_000, windowsHide: true,
    env: { ...process.env, FULLNET_CUTOVER_TEST_CASE: scenario, FULLNET_CUTOVER_TEST_PROVIDER: provider }
  });
  assert.ifError(result.error);
  return result;
}

for (const provider of ['SqlServer', 'MySql']) {
  test(`${provider} zero pending after stop allows the exact three release stages`, () => {
    const { status, stdout, stderr } = run('ready', provider);
    assert.equal(status, 0, stderr);
    assert.match(stdout, /database-probed[\s\S]+preflight passed/);
    assert.deepEqual([...stdout.matchAll(/helm-stage=(fullnet-[a-z]+)/g)].map(match => match[1]),
      ['fullnet-migrator', 'fullnet-worker', 'fullnet-api']);
  });
}

for (const [scenario, message, probesDatabase] of [
  ['missing-context', /explicit Kubernetes context/, false],
  ['active', /stopped and reconciled/, false],
  ['stale', /stopped and reconciled/, false],
  ['terminating', /pods to terminate/, false],
  ['autoscaler', /removing the autoscaler/, false],
  ['cluster-unavailable', /cannot read Kubernetes pods/, false],
  ['pending', /blocked by 2 unfinished/, true],
  ['readonly', /writable primary/, true],
  ['unexpected', /Invalid reporting cutover database result/, true],
  ['db-error', /could not read the export state/, true],
  ['restarted', /stopped and reconciled/, true]
]) {
  test(`${scenario} blocks all Helm mutations`, () => {
    const { status, stdout, stderr } = run(scenario);
    assert.notEqual(status, 0, stdout);
    assert.match(stderr, message);
    assert.doesNotMatch(stdout, /helm-stage/);
    assert.equal(stdout.includes('database-probed'), probesDatabase);
  });
}

test('ordinary release keeps the stage order without probing reporting', () => {
  const { status, stdout, stderr } = run('normal');
  assert.equal(status, 0, stderr);
  assert.doesNotMatch(stdout, /database-probed/);
  assert.match(stdout, /helm-stage=fullnet-migrator[\s\S]+helm-stage=fullnet-worker[\s\S]+helm-stage=fullnet-api/);
});

test('a failed Migrator stops the cutover before releasing Worker and API', () => {
  const { status, stdout, stderr } = run('migrator-failed');
  assert.notEqual(status, 0, stdout);
  assert.match(stderr, /Helm release 'fullnet-migrator' failed with exit code 1/);
  assert.deepEqual([...stdout.matchAll(/helm-stage=(fullnet-[a-z]+)/g)].map(match => match[1]),
    ['fullnet-migrator']);
});
