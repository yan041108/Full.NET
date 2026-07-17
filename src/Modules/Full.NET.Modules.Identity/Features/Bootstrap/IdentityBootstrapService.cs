using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.Modules.Identity.Features.Bootstrap;

internal sealed class IdentityBootstrapService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    Microsoft.AspNetCore.Identity.IPasswordHasher<IdentityUser> passwordHasher,
    IClock clock,
    IIdGenerator idGenerator) : IIdentityBootstrapService
{
    private const string HostScope = "host";

    public Task<Result<BootstrapHostAdminResult>> BootstrapHostAdminAsync(
        BootstrapHostAdminRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var passwordViolations = IdentityPasswordPolicy.Validate(request.Password);
        if (passwordViolations.Count > 0)
        {
            return Task.FromResult(Result<BootstrapHostAdminResult>.Failure(new Error(
                "identity.bootstrap.invalid-password",
                "The bootstrap password does not satisfy the password policy.",
                ErrorType.Validation,
                new Dictionary<string, string[]>
                {
                    [nameof(request.Password)] = passwordViolations.ToArray(),
                })));
        }

        var username = request.Username?.Trim() ?? string.Empty;
        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        if (username.Length is < 3 or > 128 || displayName.Length is < 1 or > 128)
        {
            return Task.FromResult(Result<BootstrapHostAdminResult>.Failure(new Error(
                "identity.bootstrap.invalid-profile",
                "Bootstrap username or display name is invalid.",
                ErrorType.Validation)));
        }

        return transaction.ExecuteAsync(
            token => BootstrapCoreAsync(
                username,
                displayName,
                request.Password,
                token),
            cancellationToken);
    }

    private async Task<Result<BootstrapHostAdminResult>> BootstrapCoreAsync(
        string username,
        string displayName,
        string password,
        CancellationToken cancellationToken)
    {
        var normalizedUsername = username.ToUpperInvariant();
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindUserByScopeAndUsername,
                new { ScopeKey = HostScope, NormalizedUsername = normalizedUsername },
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return Result<BootstrapHostAdminResult>.Success(
                new BootstrapHostAdminResult(existing.Id, false));
        }

        var now = clock.UtcNow;
        var user = new IdentityUser(
            idGenerator.NewId(),
            null,
            HostScope,
            username,
            normalizedUsername,
            displayName,
            string.Empty,
            true,
            0,
            null,
            idGenerator.NewId().ToString("N"),
            now,
            null,
            1);
        user = user with { PasswordHash = passwordHasher.HashPassword(user, password) };

        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.InsertUser,
                user,
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Identity bootstrap insert affected {affectedRows} rows instead of one.");
        }

        return Result<BootstrapHostAdminResult>.Success(
            new BootstrapHostAdminResult(user.Id, true));
    }
}
