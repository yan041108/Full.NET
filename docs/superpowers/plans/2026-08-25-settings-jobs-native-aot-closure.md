# Settings / Jobs Native AOT Closure Implementation Plan

> **For agentic workers:** Follow this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Close Settings and Jobs Native AOT data, JSON and HTTP paths for Host.Api on SQL Server and MySQL. Do not mark verified until the same commit has a fully green `api-native-aot-linux` run that actually executes the new native tests.

**Architecture:** Keep both modules inside existing boundaries. Replace runtime-shaped Dapper parameters with static dictionaries, register explicit AOT row materializers for every non-scalar query type compiled into Host.Api, and exercise the published native executable. Jobs Worker-only hosted polling stays out of this slice, but Host.Api trigger already runs `JobExecutionRunner` in-process, so claim/list execution projections must share one ordinal materializer.

**Tech Stack:** .NET 10, ASP.NET Core Native AOT, Dapper AOT boundary, System.Text.Json source generation, MSTest v4, GitHub Actions, SQL Server, MySQL.

## Global Constraints

- [ ] Record `git rev-parse HEAD` and use snapshot `settings-jobs-native-aot-closure` for affected tests.
- [ ] Test-first: failing architecture/governance gates before production/CI wiring.
- [ ] Do not add reflection fallbacks or mark capability verified from Windows discovery, analyzers, or publish alone.
- [ ] Evidence threshold: green Linux `api-native-aot-linux` executing Settings and Jobs native tests against the published binary.

---

## Task 1: Static-binding gates

- [ ] `SettingsModule_UsesAotSafeSqlParameters` / `JobsModule_UsesAotSafeSqlParameters`
- [ ] `SettingsModule_RegistersAllNativeAotRowMaterializers` / `JobsModule_RegistersAllNativeAotRowMaterializers`
- [ ] Projection-order assertions for dict/config/grid and job definition/execution/schedule/health families

## Task 2: Close Dapper parameters and materializers

- [ ] Convert every Settings/Jobs `new { ... }` SQL argument to `Dictionary<string, object?>`
- [ ] Register AOT materializers; unify Jobs execution acquire OUTPUT with list/find projection
- [ ] Align `FindScheduleById` with due-schedule `JobScheduleRecord` columns including `AllowConcurrentExecutions`

## Task 3: Native E2E + CI

- [ ] Dual-DB Settings HTTP/JSON native tests (dict, config, diagnostic, grid, tenant dict list)
- [ ] Dual-DB Jobs HTTP/JSON native tests (definition, trigger/ping, executions, schedules, health)
- [ ] Matrix, runner, workflow, governance; exclude from general native E2E filter

## Task 4: Verify on Linux CI, then evidence

- [ ] Push and wait for `api-native-aot-linux`
- [ ] Only then update verification docs and capability status
