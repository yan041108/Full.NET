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
    /// <summary>仅补齐空400、413和415，保留已有错误体及原HTTP状态。</summary>
    public static IApplicationBuilder UseFullNetRequestBodyProblemDetails(this IApplicationBuilder application)=>
        application.UseStatusCodePages(async context=>{
            var http=context.HttpContext;var status=http.Response.StatusCode;
            if(status is not(400 or 413 or 415))return;
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
