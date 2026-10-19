using Full.NET.Modules.Ai.Domain;

namespace Full.NET.UnitTests.Ai;

/// <summary>单元测试 AI：聊天内容与敏感信息边界策略。</summary>
[TestClass]
public sealed class AiChatContentPolicyTests
{
    [TestMethod]
    public void ValidateUserMessage_rejects_likely_api_key()
    {
        var message = AiChatContentPolicy.ValidateUserMessage("my key is sk-abcdefghijklmnopqrstuvwxyz");

        Assert.IsNotNull(message);
    }

    [TestMethod]
    public void BuildTitleFromMessage_truncates_long_text()
    {
        var title = AiChatContentPolicy.BuildTitleFromMessage(
            new string('A', 80));

        Assert.IsTrue(title.EndsWith("...", StringComparison.Ordinal));
        Assert.IsLessThanOrEqualTo(title.Length, 51);
    }
}
