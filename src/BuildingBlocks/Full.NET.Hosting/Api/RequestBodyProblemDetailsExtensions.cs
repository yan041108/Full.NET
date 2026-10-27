using System.Diagnostics;
using System.Globalization;
using Full.NET.Abstractions.Results;
using Full.NET.Localization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Hosting.Api;

/// <summary>为框架请求绑定产生的空错误响应补齐标准、本地化且不含输入内容的ProblemDetails。</summary>
public static class RequestBodyProblemDetailsExtensions
{
    /// <summary>补齐空400、413和415及业务 API 的其他空错误响应，保留协议端点和已有响应体。</summary>
    public static IApplicationBuilder UseFullNetRequestBodyProblemDetails(this IApplicationBuilder application)=>
        application.UseStatusCodePages(async context=>{
            var http=context.HttpContext;var status=http.Response.StatusCode;
            if(status is not(400 or 413 or 415))
            {
                // BFF 与普通 API 的框架拒绝同样使用 ProblemDetails；OIDC 等协议端点不接管。
                if(http.Request.Path.StartsWithSegments("/api"))
                    await Results.Problem(statusCode:status).ExecuteAsync(http).ConfigureAwait(false);
                return;
            }
            var locale=http.RequestServices.GetRequiredService<ILocaleContext>().CurrentLocale;
            var localizer=http.RequestServices.GetRequiredService<IErrorMessageLocalizer>();
            var error=new Error(CommonErrorCodes.RequestInvalid,"The request content is invalid or unsupported.",ErrorType.Validation);
            var problem=new ProblemDetails{Status=status,Type=$"https://full.net/errors/{error.Code}",Title=localizer.Localize(error,CultureInfo.GetCultureInfo(locale))};
            problem.Extensions["code"]=error.Code;
            problem.Extensions["traceId"]=Activity.Current?.TraceId.ToString()??http.TraceIdentifier;
            LocalizationHttpHeaders.Apply(http.Response,locale,varyByAcceptLanguage:true);
            await Results.Problem(problem).ExecuteAsync(http).ConfigureAwait(false);
        });
}
