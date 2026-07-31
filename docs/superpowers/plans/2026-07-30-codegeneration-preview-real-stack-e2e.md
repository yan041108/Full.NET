# CodeGeneration Host Preview Real-Stack E2E Implementation Plan

> **For agentic workers:** Execute this test-only plan inline in the current shared workspace. Do not create a worktree or delegate; coordinate the single Docker/Integration queue before running Playwright.

**Goal:** Verify the existing CodeGeneration Host read-only preview through the real API from both Vue and Layui, including authorization denial and navigation trimming for a restricted Host account.

**Architecture:** Add one focused Playwright spec to the existing admin real-stack harness. The success path logs in as the protected Host administrator, follows dynamic navigation, submits the page’s valid schema, and inspects a real generated artifact; the denial path obtains the restricted viewer’s real token, verifies the API returns `authorization.permission_denied`, and verifies both navigation trimming and the guarded direct route.

**Tech Stack:** Playwright 1.61, existing SQL Server real-stack bootstrap, Vue 3 admin, Layui admin, Full.NET Host API.

## Global Constraints

- Do not mock `/api/v1/code-generation/previews`, authentication, navigation, or ProblemDetails.
- Run the same spec in the existing `vue-admin` and `layui-admin` projects.
- Keep the feature Host-only; do not enter a tenant context.
- Assert stable machine contracts (`codegen.preview.invalid_schema`, `authorization.permission_denied`, permission codes, paths), not translated error prose.
- Do not modify generator, schema, migration, benchmark, Jobs, Files, or Realtime files.
- Do not claim the broader CodeGeneration capability `Verified`; only the “Host read-only preview” client row may advance after both projects pass.

---

### Task 1: Real-stack success and denial paths

**Files:**

- Create: `tests/e2e/admin-real-stack/tests/host-code-generation-previews.spec.mjs`
- Modify after RED: `ui/admin-layui/js/app.js`
- Modify after RED: `ui/admin-layui/tests/app.test.js`
- Modify after GREEN: `docs/roadmap/client-delivery-roadmap.md`

**Interfaces:**

- Consumes: `loginAsHostAdmin(page)`, `loginAsHostViewer(page)`, `loginAccessToken(request, clientKind)`, `adminOrigin(clientKind)`, and `statusPath(clientKind, code)`.
- Consumes: `POST /api/v1/code-generation/previews`.
- Produces: two Playwright scenarios, discovered once for Vue and once for Layui.

- [x] Create a focused spec with a valid legacy-shape schema fixture and client-specific root locator.
- [x] In the administrator scenario, follow the dynamic “代码生成” navigation link, assert the read-only workbench, submit the real schema, select `clients/vue/products.generated.ts`, and assert the generated content contains `/api/v1/catalog/products`.
- [x] In the restricted viewer scenario, call the real preview API and assert HTTP 403 plus `authorization.permission_denied`, then verify the navigation link is absent and direct route renders the standard 403 page.
- [x] Run `pnpm --filter @fullnet/admin-real-stack-e2e test:provisioner`; expect all harness contracts to pass.
- [x] Run Playwright discovery for the new file; expect four cases across `vue-admin` and `layui-admin`.
- [x] After Jobs and CodeGeneration Task 2 release shared build and Docker, run the new spec against the default real SQL Server stack; expect four passed tests and complete teardown.
- [x] Fix the Layui route-classification defect exposed by the first real-stack run with a focused RED→GREEN unit test; keep unknown routes at 404 while known unauthorized routes render 403.
- [x] Change only the C2.3 Vue/Layui cells in `docs/roadmap/client-delivery-roadmap.md` from `Build-verified` to `Verified`; keep template persistence, task records, Apply, and broader capability status open.
- [x] Run task-snapshot affected planning, `git diff --check`, branch/status checks, and confirm Docker/Testcontainers/Ryuk have no running or stopped residuals.
