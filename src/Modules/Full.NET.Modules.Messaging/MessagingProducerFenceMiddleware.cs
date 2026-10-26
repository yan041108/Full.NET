using System.Diagnostics;
using System.Globalization;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Localization;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Messaging.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modules.Messaging;

/// <summary>回退屏障是可预期的临时写入拒绝；只在 HTTP 边界转换，不截断事务异常传播。</summary>
internal static class MessagingProducerFenceMiddleware
{
    internal static async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try { await next(context).ConfigureAwait(false); }
        catch (EventDeliveryProducerFencedException) when (!context.Response.HasStarted)
        {
            // 业务/Outbox 事务已随异常回滚；不得回显事件流、代次或内部异常，也不自动重试业务写入。
            var locale = context.RequestServices.GetRequiredService<ILocaleContext>().CurrentLocale;
            var error = new Error(MessagingErrorCodes.ProducerFenced,
                "Event delivery is temporarily protected for rollback. Retry the operation later.", ErrorType.BusinessRule);
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Type = $"https://full.net/errors/{error.Code}",
                Title = context.RequestServices.GetRequiredService<IErrorMessageLocalizer>().Localize(error, CultureInfo.GetCultureInfo(locale)),
            };
            problem.Extensions["code"] = error.Code;
            problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
            context.Response.Headers.RetryAfter = "1";
            LocalizationHttpHeaders.Apply(context.Response, locale, varyByAcceptLanguage: true);
            await Results.Problem(problem).ExecuteAsync(context).ConfigureAwait(false);
        }
    }
}
