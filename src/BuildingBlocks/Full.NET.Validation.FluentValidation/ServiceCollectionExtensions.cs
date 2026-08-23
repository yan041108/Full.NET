using Full.NET.Abstractions.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Validation.FluentValidation;

/// <summary>
/// 注册 FluentValidation 校验管道行为到 CQRS Dispatcher，使所有 Command/Query 在 Handler 执行前自动校验。
/// </summary>
/// <remarks>
/// <para>注册方式为 <see cref="ServiceLifetime.Scoped"/> 与 <c>IDispatchBehavior&lt;,&gt;</c> 同生命周期，
/// 避免在请求作用域内捕获更长的依赖。Validator 实现本身由调用方按 FluentValidation 习惯注册
/// （自动扫描或显式注册），本扩展不重复扫描以避免覆盖宿主诊断与生命周期策略。</para>
/// <para>校验失败时由 <see cref="FluentValidationBehavior{TMessage,TResult}"/> 统一返回
/// <see cref="ValidationErrorCodes.Failed"/> 错误码，确保跨用例的校验错误结构稳定、可被前端按字段与机器码渲染。</para>
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 <see cref="FluentValidationBehavior{TMessage,TResult}"/> 作为 CQRS Dispatcher 的校验前置行为。
    /// </summary>
    /// <param name="services">服务集合，扩展方法基于此追加 Scoped 注册。</param>
    /// <returns>传入的服务集合，便于链式注册。</returns>
    /// <remarks>
    /// 该注册只确保校验行为在管道中存在；具体 <c>IValidator&lt;TMessage&gt;</c> 实现仍需由各业务模块按其需要注册，
    /// 缺少 Validator 时该类型将无校验规则，等价于校验通过。
    /// </remarks>
    public static IServiceCollection AddFullNetFluentValidation(
        this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped(
            typeof(IDispatchBehavior<,>),
            typeof(FluentValidationBehavior<,>)));
        return services;
    }
}
