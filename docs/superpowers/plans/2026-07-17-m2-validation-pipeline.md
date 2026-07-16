# M2 Validation Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a transport-independent FluentValidation pipeline to Full.NET and migrate tenant provisioning structural validation into it without changing the modular-monolith or Dapper transaction design.

**Architecture:** `Full.NET.Abstractions` defines a general dispatch behavior contract. Command and Query dispatchers compose registered behaviors before invoking the transaction/handler terminal delegate. A separate `Full.NET.Validation.FluentValidation` BuildingBlock adapts explicit FluentValidation validators into Full.NET `Result` errors; business modules opt in and register validators without assembly scanning.

**Tech Stack:** .NET 10, C# 14, FluentValidation 12.1.0, Microsoft.Extensions.DependencyInjection, MSTest 4, NSubstitute, SQL Server/MySQL Testcontainers

## Global Constraints

- Full.NET remains MIT; FluentValidation is Apache-2.0 and must be recorded in `THIRD-PARTY-NOTICES`.
- Do not add FastEndpoints, MediatR, Mapster, Hangfire, MassTransit, YARP, Swashbuckle, Workflow Core, Elsa, Durable Task, Envoy, or Linkerd runtime dependencies in this plan.
- Do not use FluentValidation assembly scanning or runtime reflection registration.
- HTTP DTOs and internal Commands remain separate types.
- Validation executes before transactional commands open a Dapper transaction.
- Standard failures use `ErrorType.Validation`, code `validation.failed`, and retain real HTTP 400 through the existing mapper.
- Every production behavior change follows red-green-refactor and is committed only after focused tests pass.
- Work in `G:\wwwroot\github_fork\Full.NET` on branch `feature/m2-validation-roadmap`.

---

### Task 1: General Dispatch Behavior Pipeline

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Abstractions/Messaging/IDispatchBehavior.cs`
- Modify: `src/BuildingBlocks/Full.NET.Modularity/Messaging/CommandDispatcher.cs`
- Modify: `src/BuildingBlocks/Full.NET.Modularity/Messaging/QueryDispatcher.cs`
- Modify: `tests/Full.NET.UnitTests/Messaging/DispatcherTests.cs`

**Interfaces:**
- Consumes: existing `Result<TResult>`, `ICommand<TResult>`, `IQuery<TResult>`, and `ICommandTransaction`.
- Produces: `DispatchHandlerDelegate<TResult>` and `IDispatchBehavior<TMessage, TResult>`; both dispatchers execute all registered behaviors in DI registration order.

- [ ] **Step 1: Write failing dispatcher behavior tests**

Add three tests to `DispatcherTests`: command ordering, query behavior execution, and transaction/handler short-circuiting. The desired behavior contract used by the tests is:

```csharp
private sealed class RecordingBehavior<TMessage, TResult>(
    string name,
    IList<string> calls)
    : IDispatchBehavior<TMessage, TResult>
{
    public async Task<Result<TResult>> HandleAsync(
        TMessage message,
        DispatchHandlerDelegate<TResult> next,
        CancellationToken cancellationToken)
    {
        calls.Add($"{name}:before");
        var result = await next(cancellationToken);
        calls.Add($"{name}:after");
        return result;
    }
}

private sealed class RejectingBehavior<TMessage, TResult>
    : IDispatchBehavior<TMessage, TResult>
{
    public Task<Result<TResult>> HandleAsync(
        TMessage message,
        DispatchHandlerDelegate<TResult> next,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result<TResult>.Failure(new Error(
            "rejected",
            "Rejected before the handler.",
            ErrorType.Validation)));
}
```

The command ordering test registers `first` and `second` and asserts:

```csharp
CollectionAssert.AreEqual(
    new[] { "first:before", "second:before", "handler", "second:after", "first:after" },
    calls.ToArray());
```

The query test asserts `query:before`, `query-handler`, `query:after`. The rejection test uses a transactional command and asserts that both `RecordingTransaction.Executed` and a recording handler's `Executed` flag remain false.

- [ ] **Step 2: Run the tests and verify the red state**

Run:

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj
```

Expected: build fails with missing `IDispatchBehavior<,>` or `DispatchHandlerDelegate<>`, proving the tests require the new public contract.

- [ ] **Step 3: Add the minimal behavior contract**

Create `IDispatchBehavior.cs`:

```csharp
using Full.NET.Abstractions.Results;

namespace Full.NET.Abstractions.Messaging;

public delegate Task<Result<TResult>> DispatchHandlerDelegate<TResult>(
    CancellationToken cancellationToken);

public interface IDispatchBehavior<in TMessage, TResult>
{
    Task<Result<TResult>> HandleAsync(
        TMessage message,
        DispatchHandlerDelegate<TResult> next,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Compose behaviors in both dispatchers**

In each dispatcher, create the terminal delegate without resolving the Handler until it is invoked, then wrap it in reverse enumeration order:

```csharp
var pipeline = new DispatchHandlerDelegate<TResult>(
    ct => HandleCoreAsync<TMessage, TResult>(message, ct));

foreach (var behavior in services
             .GetServices<IDispatchBehavior<TMessage, TResult>>()
             .Reverse())
{
    var next = pipeline;
    pipeline = ct => behavior.HandleAsync(message, next, ct);
}

return pipeline(cancellationToken);
```

`CommandDispatcher.HandleCoreAsync` resolves the command Handler and preserves the existing `ITransactionalCommand` transaction branch. `QueryDispatcher.HandleCoreAsync` resolves and invokes the query Handler directly.

- [ ] **Step 5: Build and run focused tests**

Run:

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj
dotnet tests/Full.NET.UnitTests/bin/Debug/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --filter "FullyQualifiedName~DispatcherTests"
```

Expected: all seven `DispatcherTests` pass, including the three new behavior tests.

- [ ] **Step 6: Commit the dispatch pipeline**

```powershell
git add src/BuildingBlocks/Full.NET.Abstractions/Messaging src/BuildingBlocks/Full.NET.Modularity/Messaging tests/Full.NET.UnitTests/Messaging/DispatcherTests.cs
git commit -m "feat: add dispatch behavior pipeline"
```

---

### Task 2: FluentValidation BuildingBlock

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `Full.NET.slnx`
- Create: `src/BuildingBlocks/Full.NET.Validation.FluentValidation/Full.NET.Validation.FluentValidation.csproj`
- Create: `src/BuildingBlocks/Full.NET.Validation.FluentValidation/FluentValidationBehavior.cs`
- Create: `src/BuildingBlocks/Full.NET.Validation.FluentValidation/ServiceCollectionExtensions.cs`
- Modify: `tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj`
- Create: `tests/Full.NET.UnitTests/Validation/FluentValidationBehaviorTests.cs`

**Interfaces:**
- Consumes: `IDispatchBehavior<TMessage, TResult>`, FluentValidation `IValidator<TMessage>`, and Full.NET `Error`.
- Produces: `AddFullNetFluentValidation()` and an open-generic behavior registration that maps validation failures to `Result<TResult>`.

- [ ] **Step 1: Add dependency and empty project wiring**

Add centrally managed `FluentValidation` version `12.1.0`. Create the project with references to `Full.NET.Abstractions`, `FluentValidation`, and `Microsoft.Extensions.DependencyInjection.Abstractions`; add it under `/src/BuildingBlocks/` in `Full.NET.slnx`, and reference it from the unit-test project. Do not add production source files yet.

- [ ] **Step 2: Write failing provider tests**

Create four tests:

```csharp
[TestMethod]
public async Task No_validators_invokes_handler();

[TestMethod]
public async Task Validation_failure_returns_error_and_skips_handler();

[TestMethod]
public async Task Multiple_validators_merge_and_deduplicate_messages();

[TestMethod]
public void Registration_is_idempotent();
```

Use a real `AbstractValidator<TestCommand>`, the real `CommandDispatcher`, and an in-memory recording Handler. Assert the failure contract exactly:

```csharp
Assert.AreEqual("validation.failed", result.Error.Code);
Assert.AreEqual(ErrorType.Validation, result.Error.Type);
CollectionAssert.AreEqual(
    new[] { "Value is required.", "Value has an invalid format." },
    result.Error.ValidationErrors![nameof(TestCommand.Value)]);
Assert.IsFalse(handler.Executed);
```

Call `AddFullNetFluentValidation()` twice in the idempotency test and assert exactly one descriptor whose service type is `typeof(IDispatchBehavior<,>)`; verify the internal implementation by its descriptor type name instead of exposing it as public API.

- [ ] **Step 3: Run tests and verify the red state**

Run:

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj
```

Expected: compile failure because `AddFullNetFluentValidation` and `FluentValidationBehavior<,>` do not exist.

- [ ] **Step 4: Implement the behavior**

The behavior must execute validators serially, skip blank messages, preserve first-seen property/message ordering, and return without calling `next` when errors exist:

```csharp
internal sealed class FluentValidationBehavior<TMessage, TResult>(
    IEnumerable<IValidator<TMessage>> validators)
    : IDispatchBehavior<TMessage, TResult>
{
    public async Task<Result<TResult>> HandleAsync(
        TMessage message,
        DispatchHandlerDelegate<TResult> next,
        CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(
                new ValidationContext<TMessage>(message),
                cancellationToken);
            failures.AddRange(result.Errors.Where(failure =>
                !string.IsNullOrWhiteSpace(failure.ErrorMessage)));
        }

        if (failures.Count == 0)
        {
            return await next(cancellationToken);
        }

        var errors = failures
            .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        return Result<TResult>.Failure(new Error(
            "validation.failed",
            "One or more validation errors occurred.",
            ErrorType.Validation,
            errors));
    }
}
```

- [ ] **Step 5: Implement idempotent explicit registration**

Use `TryAddEnumerable` with an open-generic scoped descriptor:

```csharp
public static IServiceCollection AddFullNetFluentValidation(
    this IServiceCollection services)
{
    services.TryAddEnumerable(ServiceDescriptor.Scoped(
        typeof(IDispatchBehavior<,>),
        typeof(FluentValidationBehavior<,>)));
    return services;
}
```

Do not add `AddValidatorsFromAssembly`, assembly scanning, or `FluentValidation.DependencyInjectionExtensions`.

- [ ] **Step 6: Run focused tests**

Run:

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj
dotnet tests/Full.NET.UnitTests/bin/Debug/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --filter "FullyQualifiedName~FluentValidationBehaviorTests"
```

Expected: all four provider tests pass.

- [ ] **Step 7: Commit the provider**

```powershell
git add Directory.Packages.props Full.NET.slnx src/BuildingBlocks/Full.NET.Validation.FluentValidation tests/Full.NET.UnitTests
git commit -m "feat: add FluentValidation dispatch provider"
```

---

### Task 3: Migrate Tenant Provisioning Validation

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Full.NET.Modules.Tenancy.csproj`
- Create: `src/Modules/Full.NET.Modules.Tenancy/Features/ProvisionTenant/ProvisionTenantCommandValidator.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Features/ProvisionTenant/Handler.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/TenancyModule.cs`
- Create: `tests/Full.NET.UnitTests/Tenancy/ProvisionTenantCommandValidatorTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Tenancy/TenantProvisioningTests.cs`
- Modify: `tests/Full.NET.ArchitectureTests/DependencyRulesTests.cs`
- Modify: `tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj`

**Interfaces:**
- Consumes: `AddFullNetFluentValidation()`, `IValidator<ProvisionTenantCommand>`, and existing tenant Handler.
- Produces: explicit tenant command validation while the Handler retains normalization, uniqueness checks, persistence, and Outbox behavior.

- [ ] **Step 1: Write failing tenant validator and contract tests**

Create four unit tests that instantiate `ProvisionTenantCommandValidator` directly:

```csharp
[TestMethod]
public async Task Valid_trimmed_command_passes();

[TestMethod]
public async Task Invalid_identifier_is_rejected();

[TestMethod]
public async Task Blank_or_long_name_is_rejected();

[TestMethod]
public async Task Blank_or_long_domain_is_rejected();
```

Use `" ACME "`, `" Acme Corporation "`, and `" ACME.LOCALHOST "` for the valid case. Assert exact property names and messages. Change the existing integration assertion from `tenancy.validation` to `validation.failed` before production changes.

- [ ] **Step 2: Verify the red state**

Run:

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj
```

Expected: compile failure because `ProvisionTenantCommandValidator` does not exist.

- [ ] **Step 3: Add and register the validator**

Reference `Full.NET.Validation.FluentValidation` from the Tenancy module. Implement an internal `AbstractValidator<ProvisionTenantCommand>` with these exact rules:

```csharp
RuleFor(command => command.Identifier)
    .Must(value => IdentifierPattern().IsMatch(
        value?.Trim().ToLowerInvariant() ?? string.Empty))
    .WithMessage("Identifier must be 3-64 lowercase letters, numbers, or hyphens.");

RuleFor(command => command.Name)
    .Must(value => !string.IsNullOrWhiteSpace(value)
        && value.Trim().Length <= 128)
    .WithMessage("Name is required and must not exceed 128 characters.");

RuleFor(command => command.Domain)
    .Must(value => !string.IsNullOrWhiteSpace(value)
        && value.Trim().Length <= 253)
    .WithMessage("Domain is required and must not exceed 253 characters.");
```

Keep the existing generated identifier regex. In `TenancyModule.AddServices`, call `services.AddFullNetFluentValidation()` and explicitly register the scoped validator with `TryAddScoped<IValidator<ProvisionTenantCommand>, ProvisionTenantCommandValidator>()`.

- [ ] **Step 4: Remove structural validation from the Handler**

Remove the `Validate` method and generated regex from `Handler`. Keep normalization as the first Handler operation, because persistence and uniqueness checks use canonical lowercase identifier/domain and trimmed name. The first database operation must now follow normalization directly.

- [ ] **Step 5: Add the provider assembly to architecture coverage**

Reference the validation BuildingBlock in `Full.NET.ArchitectureTests.csproj`. Add `typeof(Full.NET.Validation.FluentValidation.ServiceCollectionExtensions).Assembly` to both `BuildingBlockAssemblies` and `ProductionAssemblies.All`, so the existing no-BuildingBlock-to-Module rule covers it.

- [ ] **Step 6: Run tenant and dispatcher tests**

Run:

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj
dotnet tests/Full.NET.UnitTests/bin/Debug/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --filter "FullyQualifiedName~ProvisionTenantCommandValidatorTests|FullyQualifiedName~FluentValidationBehaviorTests|FullyQualifiedName~DispatcherTests"
```

Expected: 15 focused tests pass: seven dispatcher, four provider, and four tenant-validator tests.

- [ ] **Step 7: Run the real tenant integration tests**

Build Release and run only the two provisioning tests against SQL Server and MySQL:

```powershell
dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --configuration Release --no-restore
dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --no-ansi --progress off --filter "FullyQualifiedName~TenantProvisioningTests" --minimum-expected-tests 2 --timeout 6m
```

Expected: both providers pass; invalid input returns `validation.failed`, valid input writes one tenant and one MessagePack Outbox event, and failed Outbox writing still rolls back the transaction.

- [ ] **Step 8: Commit the tenant migration**

```powershell
git add src/Modules/Full.NET.Modules.Tenancy tests/Full.NET.UnitTests/Tenancy tests/Full.NET.IntegrationTests/Tenancy tests/Full.NET.ArchitectureTests
git commit -m "feat: validate tenant commands in dispatch pipeline"
```

---

### Task 4: Documentation, CI, Licensing, and Full Verification

**Files:**
- Modify: `README.md`
- Modify: `docs/development/getting-started.md`
- Modify: `.github/workflows/ci.yml`
- Modify: `THIRD-PARTY-NOTICES`

**Interfaces:**
- Consumes: the completed validation pipeline and final test inventory.
- Produces: accurate developer guidance, dependency notice, and CI minimum-test gates.

- [ ] **Step 1: Document validation conventions**

Add an M2 validation bullet to README. Add a “验证管道” section to the development guide stating:

- modules register `IValidator<TCommand>` explicitly;
- validators contain structural/input rules only;
- Handler/Domain retain database and state-dependent business rules;
- validation failures use `validation.failed` and ProblemDetails HTTP 400;
- assembly scanning and duplicate HTTP-only validation are forbidden.

- [ ] **Step 2: Update test-count gates and third-party notice**

Change the unit-test minimum from 37 to 48 in README, the development guide, and CI. Add FluentValidation — Apache-2.0 with `https://github.com/FluentValidation/FluentValidation` to `THIRD-PARTY-NOTICES`.

- [ ] **Step 3: Run dependency audit and full Release build**

```powershell
dotnet restore Full.NET.slnx
dotnet list Full.NET.slnx package --vulnerable --include-transitive
dotnet build Full.NET.slnx --configuration Release --no-restore
```

Expected: restore succeeds, every project reports no known vulnerable packages, and build completes with zero warnings and zero errors. The installed preview SDK may emit informational `NETSDK1057` messages; they are not compiler warnings.

- [ ] **Step 4: Run all fast test projects**

```powershell
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --minimum-expected-tests 48
dotnet tests/Full.NET.CompatibilityTests/bin/Release/net10.0/Full.NET.CompatibilityTests.dll --no-ansi --progress off --minimum-expected-tests 4
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --minimum-expected-tests 7
```

Expected: 48 unit, 4 compatibility, and 7 architecture tests pass with zero failures.

- [ ] **Step 5: Run the full dual-database integration suite**

```powershell
dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --no-ansi --progress off --minimum-expected-tests 6 --timeout 10m
```

Expected: all six SQL Server/MySQL migration, provisioning/Outbox, and HTTP vertical tests pass.

- [ ] **Step 6: Verify repository state and commit**

```powershell
git diff --check
git status --short
docker ps --format "{{.ID}} {{.Names}} {{.Status}}"
git add README.md docs/development/getting-started.md .github/workflows/ci.yml THIRD-PARTY-NOTICES
git commit -m "docs: document validation and provider roadmap"
git status --short
```

Expected: no whitespace errors, no residual test containers, documentation commit succeeds, and the final working tree is clean.
