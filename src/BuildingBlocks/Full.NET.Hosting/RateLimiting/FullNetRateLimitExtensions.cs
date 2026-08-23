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
/// 支持固定窗口、滑动窗口与令牌桶三类策略，拒绝响应统一走 <see cref="IApiResultMapper"/>
/// 输出结构化 ProblemDetails，避免默认 429 文本响应与宿主 API 信封不一致。
/// </summary>
public static class FullNetRateLimitExtensions
{
    /// <summary>
    /// 注册限流选项校验、<see cref="RateLimitPolicyErrorCodes"/>、
    /// 全局 <see cref="GlobalApiRateLimiterConfigurator"/> 以及 ASP.NET Core RateLimiter 中间件，
    /// 并配置 OnRejected 回调以统一映射 429 响应体。
    /// </summary>
    /// <param name="services">宿主服务集合。</param>
    /// <param name="configuration">应用配置根，读取 <c>RateLimiting</c> 节。</param>
    public static IServiceCollection AddFullNetRateLimiter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(configuration);

        services.AddOptions<RateLimitingOptions>()
            .BindConfiguration(RateLimitingOptions.SectionName)
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<RateLimitingOptions>,
            RateLimitingOptionsValidator>());
        services.AddOptions<RateLimitPolicyErrorCodes>();
        services.TryAddSingleton(static serviceProvider =>
            serviceProvider
                .GetRequiredService<IOptions<RateLimitPolicyErrorCodes>>()
                .Value);
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
