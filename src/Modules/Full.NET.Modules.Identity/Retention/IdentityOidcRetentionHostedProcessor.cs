using Full.NET.Abstractions.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Retention;

internal sealed class IdentityOidcRetentionHostedProcessor(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<IdentityOidcRetentionOptions> options,
    ILogger<IdentityOidcRetentionHostedProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                IdentityOidcRetentionHostedProcessorLog.IterationFailed(
                    logger,
                    exception);
            }

            var delay = TimeSpan.FromSeconds(options.CurrentValue.PollSeconds);
            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task<IdentityOidcRetentionResult> ProcessOnceAsync(
        CancellationToken cancellationToken)
    {
        var currentOptions = options.CurrentValue;
        if (!currentOptions.Enabled)
        {
            return IdentityOidcRetentionResult.Empty;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider
            .GetRequiredService<ICurrentTenantContextWriter>();
        currentTenant.SetHost();
        try
        {
            var result = await scope.ServiceProvider
                .GetRequiredService<IdentityOidcRetentionRunner>()
                .RunOnceAsync(currentOptions, cancellationToken)
                .ConfigureAwait(false);
            IdentityOidcRetentionHostedProcessorLog.IterationCompleted(
                logger,
                result.TokensDeleted,
                result.AuthorizationsDeleted);
            return result;
        }
        finally
        {
            currentTenant.Clear();
        }
    }
}

internal static partial class IdentityOidcRetentionHostedProcessorLog
{
    [LoggerMessage(
        EventId = 4511,
        Level = LogLevel.Information,
        Message = "OIDC retention deleted {TokensDeleted} tokens and {AuthorizationsDeleted} authorizations")]
    public static partial void IterationCompleted(
        ILogger logger,
        int tokensDeleted,
        int authorizationsDeleted);

    [LoggerMessage(
        EventId = 4512,
        Level = LogLevel.Error,
        Message = "OIDC retention iteration failed")]
    public static partial void IterationFailed(
        ILogger logger,
        Exception exception);
}