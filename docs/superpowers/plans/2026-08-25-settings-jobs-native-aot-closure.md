# Settings / Jobs Native AOT Closure Implementation Plan

> **For agentic workers:** Follow this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Close Settings and Jobs Native AOT data, JSON and HTTP paths for Host.Api on SQL Server and MySQL. Do not mark verified until the same commit has a fully green `api-native-aot-linux` run that actually executes the new native tests.

**Architecture:** Keep both modules inside existing boundaries. Replace runtime-shaped Dapper parameters with static dictionaries, register explicit AOT row materializers for every non-scalar query type compiled into Host.Api, and exercise the published native executable. Jobs Worker-only hosted polling stays out of this slice, but Host.Api trigger already runs `JobExecutionRunner` in-process, so claim/list execution projections must share one ordinal materializer.

**Tech Stack:** .NET 10, ASP.NET Core Native AOT, Dapper AOT boundary, System.Text.Json source generation, MSTest v4, GitHub Actions, SQL Server, MySQL.

## Global Constraints

- [x] Record `git rev-parse HEAD` and use snapshot `settings-jobs-native-aot-closure` for affected tests.
- [x] Test-first: failing architecture/governance gates before production/CI wiring.
- [x] Do not add reflection fallbacks or mark capability verified from Windows discovery, analyzers, or publish alone.
- [x] Evidence threshold: green Linux `api-native-aot-linux` executing Settings and Jobs native tests against the published binary.

---

## Task 1: Static-binding gates

- [x] `SettingsModule_UsesAotSafeSqlParameters` / `JobsModule_UsesAotSafeSqlParameters`
- [x] `SettingsModule_RegistersAllNativeAotRowMaterializers` / `JobsModule_RegistersAllNativeAotRowMaterializers`
- [x] Projection-order assertions for dict/config/grid and job definition/execution/schedule/health families

## Task 2: Close Dapper parameters and materializers

- [x] Convert every Settings/Jobs `new { ... }` SQL argument to `Dictionary<string, object?>`
- [x] Register AOT materializers; unify Jobs execution acquire OUTPUT with list/find projection
- [x] Align `FindScheduleById` with due-schedule `JobScheduleRecord` columns including `AllowConcurrentExecutions`

## Task 3: Native E2E + CI

- [x] Dual-DB Settings HTTP/JSON native tests (dict, config, diagnostic, grid, tenant dict list)
- [x] Dual-DB Jobs HTTP/JSON native tests (definition, trigger/ping, executions, schedules, health)
- [x] Matrix, runner, workflow, governance; exclude from general native E2E filter

## Task 4: Verify on Linux CI, then evidence

- [x] Push and wait for `api-native-aot-linux`
- [x] Only then update verification docs and capability status

## Evidence

- Module/CI wiring: `bc7727d68b4735517208f1964e3337560a1bd3ea`
- AOT `IN @Ids` collection expand fix + green dual-DB Settings/Jobs native E2E: `7162c3297c17580544a61b7cc0cab0f5694847c4`
- CI: https://github.com/yan041108/Full.NET/actions/runs/32872774812 (`4/4` Settings/Jobs)
- Verification: [`api-native-aot-settings-jobs-2026-08-25.md`](../../verification/api-native-aot-settings-jobs-2026-08-25.md)
