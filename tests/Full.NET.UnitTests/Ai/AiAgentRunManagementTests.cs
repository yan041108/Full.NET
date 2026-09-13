using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageAgentRuns;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiAgentRunManagementTests
{
    [TestMethod]
    public void Request_hash_changes_when_prompt_changes()
    {
        var scope = "host";
        var baseRequest = new CreateAiAgentRunRequest(
            Guid.CreateVersion7(),
            AiAgentRunManagementService.SingleTextDefinitionKey,
            Guid.CreateVersion7(),
            "hello",
            100,
            100);
        var other = baseRequest with { Prompt = "world" };
        var hash1 = InvokeHash(baseRequest, scope);
        var hash2 = InvokeHash(other, scope);
        Assert.AreNotEqual(hash1, hash2);
    }

    private static string InvokeHash(CreateAiAgentRunRequest request, string scope)
    {
        var method = typeof(AiAgentRunManagementService).GetMethod(
            "ComputeRequestHash",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (string)method!.Invoke(null, [request, scope])!;
    }
}
