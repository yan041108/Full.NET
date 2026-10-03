using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageAgentRuns;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiAgentRunManagementTests
{
    [TestMethod]
    public void Workflow_creation_and_hash_use_structured_validation_version()
    {
        var request = new CreateAiAgentRunRequest(Guid.CreateVersion7(),
            AiAgentRunManagementService.ChatRenameWorkflowDefinitionKey, Guid.CreateVersion7(), "", 100, 100);
        Assert.AreEqual(2, AiAgentRunManagementService.ResolveDefinitionVersion(request.DefinitionKey));
        Assert.AreEqual(1, AiAgentRunManagementService.ResolveDefinitionVersion(AiAgentRunManagementService.SingleTextDefinitionKey));
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            $"host|{request.ClientRequestId:N}|{request.DefinitionKey}|2|{request.ModelConfigId:N}|100|100|0:")));
        Assert.AreEqual(expected, InvokeHash(request, "host"));
    }

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
