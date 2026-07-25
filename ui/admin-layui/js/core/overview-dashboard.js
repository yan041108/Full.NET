/**
 * 装配工作台指标与最近活动；从 Host 汇总 API 拉取真实数据。
 */
import {
  FULLNET_SCALAR_UI_PATH,
  resolveFullNetApiUrl
} from '@fullnet/client-contracts';

const apiBaseUrl = globalThis.FULLNET_CONFIG?.apiBaseUrl
  ?? import.meta.env.VITE_API_BASE_URL
  ?? '';

export function createOverviewDashboardController(root, options) {
  const request = options.request;
  const translation = options.translation;
  const metricsRoot = root.querySelector('[data-overview-metrics]');
  const activitiesRoot = root.querySelector('[data-overview-activities]');

  wireApiDocumentationLink(root);

  const load = async () => {
    try {
      const summary = await request('/api/v1/platform/host-dashboard-summary');
      renderMetrics(metricsRoot, summary, translation());
      renderActivities(activitiesRoot, summary, translation());
      hideProblem(root);
    } catch (problem) {
      showProblem(root, problem, translation().t('overview.clientFailure'));
    }
  };

  return { load };
}

function wireApiDocumentationLink(root) {
  const link = root.querySelector('[data-api-docs-link]');
  if (!link) {
    return;
  }

  link.href = resolveFullNetApiUrl(apiBaseUrl, FULLNET_SCALAR_UI_PATH);
}

function renderMetrics(container, summary, translation) {
  if (!container) return;
  const cards = container.querySelectorAll('[data-overview-metric]');
  const values = [
    summary?.activeTenantCount ?? 0,
    summary?.onlineSessionCount ?? 0,
    summary?.todayRequestCount ?? 0,
    formatPercent(summary?.todayErrorRate ?? 0)
  ];
  cards.forEach((card, index) => {
    const strong = card.querySelector('strong');
    if (strong) strong.textContent = String(values[index] ?? 0);
    const em = card.querySelector('em');
    if (em) em.hidden = true;
  });
}

function renderActivities(container, summary, translation) {
  if (!container) return;
  const items = Array.isArray(summary?.recentActivities) ? summary.recentActivities : [];
  if (!items.length) {
    container.innerHTML = `<p>${escapeHtml(translation.t('overview.emptyActivities'))}</p>`;
    return;
  }
  container.innerHTML = `<ul>${items.map((item, index) => `
    <li>
      <span>${String(index + 1).padStart(2, '0')}</span>
      <div>
        <strong>${escapeHtml(`${item.httpMethod} ${item.requestPath}`)}</strong>
        <small>${escapeHtml(formatDateTime(item.occurredAtUtc))}</small>
      </div>
      <em>${escapeHtml(item.succeeded ? translation.t('overview.status.success') : translation.t('overview.status.failed'))}</em>
    </li>
  `).join('')}</ul>`;
}

function formatPercent(value) {
  return `${(Number(value) * 100).toFixed(2)}%`;
}

function formatDateTime(value) {
  try {
    return new Intl.DateTimeFormat(undefined, {
      dateStyle: 'short',
      timeStyle: 'short'
    }).format(new Date(value));
  } catch {
    return String(value ?? '');
  }
}

function showProblem(root, problem, fallback) {
  const panel = root.querySelector('[data-overview-problem]');
  if (!panel) return;
  panel.hidden = false;
  panel.querySelector('strong').textContent = problem?.title || fallback;
  panel.querySelector('span').textContent = problem?.detail || problem?.code || '';
}

function hideProblem(root) {
  const panel = root.querySelector('[data-overview-problem]');
  if (panel) panel.hidden = true;
}

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
}
