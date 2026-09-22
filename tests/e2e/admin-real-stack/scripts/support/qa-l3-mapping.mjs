import { readdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const testsDir = join(dirname(fileURLToPath(import.meta.url)), '../../tests');

/** componentKey → admin-real-stack Playwright spec（L3 深度回归证据）。 */
const L3_BY_KEY = {
  users: ['host-users.spec.mjs', 'host-users-import-bulk.spec.mjs'],
  'api-keys': ['host-api-keys.spec.mjs'],
  roles: ['host-roles.spec.mjs'],
  menus: ['host-menus.spec.mjs'],
  tenants: ['host-tenants.spec.mjs', 'host-tenant-packages.spec.mjs'],
  'tenant-packages': ['host-tenant-packages.spec.mjs'],
  'tenant-dict-types': ['host-tenant-dict-types.spec.mjs'],
  'dict-types': ['host-dict-types.spec.mjs'],
  'enum-catalogs': ['host-enum-catalogs.spec.mjs'],
  'org-units': ['host-org-units.spec.mjs'],
  'org-positions': ['host-org-positions.spec.mjs'],
  'org-position-levels': ['host-org-position-levels.spec.mjs'],
  'org-user-units': ['host-org-user-units.spec.mjs'],
  'org-user-positions': ['host-org-user-positions.spec.mjs'],
  'host-files': ['host-files.spec.mjs'],
  'host-documents': ['host-documents.spec.mjs'],
  'document-categories': ['document-categories.spec.mjs'],
  'document-tags': ['document-tags.spec.mjs'],
  'document-shares': ['document-shares.spec.mjs'],
  'document-permissions': ['document-permissions.spec.mjs'],
  'document-statistics': ['document-statistics.spec.mjs'],
  'document-recycle-bin': ['document-recycle-bin.spec.mjs'],
  'host-jobs': ['host-jobs.spec.mjs', 'host-jobs-b1-closeout.spec.mjs'],
  'host-job-schedules': ['host-job-schedules.spec.mjs'],
  'host-job-executions': ['host-jobs.spec.mjs'],
  'access-logs': ['host-access-logs.spec.mjs'],
  'operation-logs': ['host-operation-logs.spec.mjs'],
  'exception-logs': ['host-exception-logs.spec.mjs'],
  'online-sessions': ['host-online-sessions.spec.mjs'],
  'super-administrators': [
    'host-super-administrators.spec.mjs',
    'host-super-administrators-production-totp.spec.mjs'
  ],
  'host-announcements': ['host-announcements.spec.mjs'],
  overview: ['host-overview-probe.spec.mjs'],
  'code-generation-templates': ['host-code-generation-templates.spec.mjs'],
  'code-generation-previews': ['host-code-generation-previews.spec.mjs'],
  'code-generation-download': ['host-code-generation-download.spec.mjs'],
  'observability-log-files': ['host-observability-log-files.spec.mjs'],
  'serial-number-rules': ['serial-number-rules.spec.mjs'],
  'workflow-forms': ['workflow-forms.spec.mjs'],
  'inbox-messages': ['inbox-messages.spec.mjs'],
  'notification-platform': ['notification-platform.spec.mjs'],
  'enterprise-requests': ['enterprise-request.spec.mjs']
};

const L3_PREFIX = [
  { prefix: 'workflow-', specs: ['workflow-approval.spec.mjs', 'workflow-approval-tenant.spec.mjs'] },
  { prefix: 'notifications-', specs: ['notification-platform.spec.mjs', 'notifications-reconnect.spec.mjs'] }
];

export function l3SpecsForComponentKey(componentKey) {
  if (L3_BY_KEY[componentKey]) {
    return [...L3_BY_KEY[componentKey]];
  }
  for (const entry of L3_PREFIX) {
    if (componentKey.startsWith(entry.prefix)) {
      return [...entry.specs];
    }
  }
  return [];
}

export function allUniqueL3Specs(manifest) {
  const set = new Set();
  for (const item of manifest) {
    for (const spec of l3SpecsForComponentKey(item.componentKey)) {
      set.add(spec);
    }
  }
  return [...set].sort();
}

/** admin-real-stack/tests 下全部 Playwright spec（当前 59 个）。 */
export function listAllL3SpecFiles() {
  return readdirSync(testsDir)
    .filter(name => name.endsWith('.spec.mjs'))
    .sort();
}
