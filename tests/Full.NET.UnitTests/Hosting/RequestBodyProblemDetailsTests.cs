using System.Resources;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Hosting.Serialization;
using Full.NET.Localization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Hosting;

/// <summary>请求绑定错误保留状态与本地化，在移除反射元数据后仍能输出标准错误。</summary>
[TestClass]
public sealed class RequestBodyProblemDetailsTests
{
    [TestMethod]
    [DataRow(400,"zh-CN")]
    [DataRow(400,"en-US")]
    [DataRow(413,"en-US")]
    [DataRow(415,"en-US")]
    public async Task Empty_request_errors_receive_localized_problem_details_with_generated_metadata(int status,string locale)
    {
        using var provider=Services(locale);using var body=new MemoryStream();var context=new DefaultHttpContext{RequestServices=provider,TraceIdentifier="safe-trace"};
        context.Request.Method="POST";context.Response.Body=body;
        var pipeline=new ApplicationBuilder(provider);pipeline.UseFullNetRequestBodyProblemDetails();pipeline.Run(http=>{http.Response.StatusCode=status;return Task.CompletedTask;});
        await pipeline.Build()(context);using var json=JsonDocument.Parse(body.ToArray());
        Assert.AreEqual(status,context.Response.StatusCode);Assert.AreEqual(status,json.RootElement.GetProperty("status").GetInt32());
        Assert.AreEqual(CommonErrorCodes.RequestInvalid,json.RootElement.GetProperty("code").GetString());Assert.AreEqual("safe-trace",json.RootElement.GetProperty("traceId").GetString());
        Assert.AreEqual(locale,context.Response.Headers.ContentLanguage.ToString());Assert.IsTrue(context.Response.Headers.Vary.ToString().Contains("Accept-Language",StringComparison.Ordinal));
        Assert.AreEqual(locale=="zh-CN"?"请求内容无效或不受支持。":"The request content is invalid or unsupported.",json.RootElement.GetProperty("title").GetString());
        Assert.IsFalse(json.RootElement.TryGetProperty("detail",out _));
    }
    [TestMethod]
    [DataRow(200,false)]
    [DataRow(204,false)]
    [DataRow(401,false)]
    [DataRow(404,false)]
    [DataRow(500,false)]
    [DataRow(400,true)]
    public async Task Other_statuses_and_existing_bodies_are_preserved(int status,bool hasBody)
    {
        using var provider=Services("en-US");using var body=new MemoryStream();var context=new DefaultHttpContext{RequestServices=provider};context.Response.Body=body;
        var pipeline=new ApplicationBuilder(provider);pipeline.UseFullNetRequestBodyProblemDetails();pipeline.Run(async http=>{
            http.Response.StatusCode=status;if(hasBody){http.Response.ContentType="application/problem+json";await http.Response.WriteAsync("{\"code\":\"existing\"}");}
        });
        await pipeline.Build()(context);Assert.AreEqual(status,context.Response.StatusCode);
        Assert.AreEqual(hasBody?"{\"code\":\"existing\"}":"",System.Text.Encoding.UTF8.GetString(body.ToArray()));
    }
    private static ServiceProvider Services(string locale)
    {
        var resources=new ResourceManager("Full.NET.Hosting.Resources.CommonErrors",typeof(StandardApiResultMapper).Assembly);
        var services=new ServiceCollection().AddLogging().AddProblemDetails().AddFullNetJson();
        services.AddSingleton<ILocaleContext>(new TestLocale(locale));services.AddSingleton<IErrorMessageLocalizer>(new ResourceErrorMessageLocalizer(
            [new ResourceManagerErrorResourceSource(CommonErrorCodes.Prefix,resources)],new NamedMessageFormatter()));
        services.ConfigureHttpJsonOptions(options=>{foreach(var resolver in options.SerializerOptions.TypeInfoResolverChain.OfType<DefaultJsonTypeInfoResolver>().ToArray())options.SerializerOptions.TypeInfoResolverChain.Remove(resolver);});
        return services.BuildServiceProvider();
    }
    private sealed class TestLocale(string locale):ILocaleContext{public string CurrentLocale=>locale;}
}
