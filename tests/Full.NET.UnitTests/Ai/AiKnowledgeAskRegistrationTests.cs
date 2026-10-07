using Full.NET.Modules.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Ai;

/// <summary>公开 Ask 必须具备真实 API 依赖闭包。</summary>
[TestClass]
public sealed class AiKnowledgeAskRegistrationTests
{
    [TestMethod]
    public void Api_registers_public_ask_and_paid_execution_services()
    {
        var services = new ServiceCollection();
        new AiModule().AddServices(services, new ConfigurationBuilder().Build());
        foreach (var name in new[] { "AiKnowledgeAskService", "AiKnowledgePaidAnswerService", "AiKnowledgeAnswerStore", "AiKnowledgeRetrievalService", "AiKnowledgeAnswerPipeline" })
            Assert.IsTrue(services.Any(service => service.ServiceType.Name == name), $"缺少公开 Ask 依赖：{name}");
    }
}
