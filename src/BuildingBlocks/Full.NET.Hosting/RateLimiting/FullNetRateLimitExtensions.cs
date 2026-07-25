using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Full.NET.Hosting.RateLimiting;

/// <summary>
/// 统一注册 Host API 限流基础设施与全局限流策略。
/// </summary>
public static class FullNetRateLimitExtensions
{
    public static IServiceCollection AddFullNetRateLimiter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<RateLimitingOptions>,
            RateLimitingOptionsValidator>());
        services.TryAddSingleton<RateLimitPolicyErrorCodes>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IConfigureOptions<RateLimiterOptions>,
            GlobalApiRateLimiterConfigurator>());

        services.AddRateLimiter(rateLimiter =>
        {
            rateLimiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            rateLimiter.OnRejected = async (context, _) =>
            {
                var policyName = context.HttpContext.GetEndpoint()
                    ?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
                var registry = context.HttpContext.RequestServices
                    .GetRequiredService<RateLimitPolicyErrorCodes>();
                var mapper = context.HttpContext.RequestServices
                    .GetRequiredService<IApiResultMapper>();
                var problem = mapper.Map(
                    Result<object?>.Failure(new Error(
                        Code: registry.Resolve(policyName, CommonErrorCodes.RateLimited),
                        Message: "Too many requests.",
                        Type: ErrorType.RateLimited)),
                    context.HttpContext);
                await problem.ExecuteAsync(context.HttpContext).ConfigureAwait(false);
            };
        });

        return services;
    }
}
