using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.AI;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>模型派发前冻结有界文本及工具历史；规模限制不代表供应商精确 Token 计量。</summary>
internal sealed class AiRunInputAdmission
{
    // 包括工具历史与说明的单次总字符预算；只约束接纳载荷，不认证网络字节数或外部收费。
    internal const int MaximumCharacters = 262144;
    internal const int MaximumMessages = 128;
    internal const int MaximumContents = 512;
    internal const int MaximumTools = 64;
    internal const int MaximumArguments = 128;
    internal const int MaximumJsonNodes = 4096;
    internal const int MaximumJsonDepth = 32;
    private int _characters;
    private int _contents;
    private int _jsonNodes;
    private readonly CancellationToken _token;

    private AiRunInputAdmission(CancellationToken token) => _token = token;

    /// <summary>只枚举有界前缀并复制已验证载荷；未知对象、扩展属性和原始请求工厂失败关闭。</summary>
    internal static (IReadOnlyList<ChatMessage> Messages, ChatOptions? Options) Freeze(
        IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var admission = new AiRunInputAdmission(token);
        var frozen = new List<ChatMessage>();
        foreach (var message in messages)
        {
            token.ThrowIfCancellationRequested();
            if (frozen.Count >= MaximumMessages || message is null
                || message.RawRepresentation is not null || message.AdditionalProperties is not null) Reject();
            var role = message.Role; var author = message.AuthorName; var id = message.MessageId;
            var created = message.CreatedAt;
            admission.Add(role.Value); admission.Add(author); admission.Add(id);
            var contents = new List<AIContent>();
            foreach (var content in message.Contents)
            {
                token.ThrowIfCancellationRequested();
                if (++admission._contents > MaximumContents || content is null
                    || content.RawRepresentation is not null || content.AdditionalProperties is not null) Reject();
                contents.Add(admission.CopyContent(content));
            }
            frozen.Add(new(role, contents) { AuthorName = author, MessageId = id, CreatedAt = created });
        }
        var cloned = admission.CopyOptions(options);
        token.ThrowIfCancellationRequested();
        return (frozen.AsReadOnly(), cloned);
    }

    private AIContent CopyContent(AIContent content)
    {
        switch (content)
        {
            case TextContent text:
                var value = text.Text;
                Add(value);
                return new TextContent(value);
            case FunctionCallContent call:
                var callId = call.CallId; var name = call.Name; var sourceArguments = call.Arguments;
                var informational = call.InformationalOnly;
                Add(callId); Add(name);
                var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
                if (sourceArguments is not null)
                    foreach (var (key, argumentValue) in sourceArguments)
                    {
                        _token.ThrowIfCancellationRequested();
                        if (arguments.Count >= MaximumArguments) Reject();
                        Add(key);
                        arguments.Add(key, CopyValue(argumentValue));
                    }
                return new FunctionCallContent(callId, name, sourceArguments is null ? null : arguments)
                    { InformationalOnly = informational };
            case FunctionResultContent result:
                var resultId = result.CallId; var resultValue = result.Result;
                Add(resultId);
                return new FunctionResultContent(resultId, CopyValue(resultValue));
            default:
                Reject();
                return null!;
        }
    }

    private object? CopyValue(object? value)
    {
        _token.ThrowIfCancellationRequested();
        switch (value)
        {
            case null: Add("null"); return null;
            case string text: Add(text); return text;
            case JsonElement element: return CopyJson(element);
            case bool boolean: Add("false"); return boolean;
            case int or long or double:
                // 固定原生数值的表示长度有上界；拒绝非有限浮点，不执行未知对象的 ToString 或反射序列化。
                if (value is double number && !double.IsFinite(number)) Reject();
                Add(new string('0', 32));
                return value;
            default: Reject(); return null;
        }
    }

    private ChatOptions? CopyOptions(ChatOptions? options)
    {
        if (options is null) return null;
        if (options.RawRepresentationFactory is not null || options.AdditionalProperties is not null
            || options.ResponseFormat is not null || options.Reasoning is not null || options.ContinuationToken is not null) Reject();
        // 不调用虚拟 Clone，也不提前复制未检查的列表；所有实际转发的标量只读取一次。
        var cloned = new ChatOptions
        {
            Instructions = options.Instructions, ModelId = options.ModelId, ConversationId = options.ConversationId,
            MaxOutputTokens = options.MaxOutputTokens, Temperature = options.Temperature, TopP = options.TopP,
            TopK = options.TopK, FrequencyPenalty = options.FrequencyPenalty, PresencePenalty = options.PresencePenalty,
            Seed = options.Seed, AllowMultipleToolCalls = options.AllowMultipleToolCalls,
            AllowBackgroundResponses = options.AllowBackgroundResponses
        };
        Add(cloned.Instructions); Add(cloned.ModelId); Add(cloned.ConversationId);
        cloned.ToolMode = CopyToolMode(options.ToolMode);
        var sourceStops = options.StopSequences; var sourceTools = options.Tools;
        if (sourceStops is not null)
        {
            var stops = new List<string>();
            foreach (var stop in sourceStops)
            {
                _token.ThrowIfCancellationRequested();
                if (stops.Count >= 32) Reject();
                Add(stop); stops.Add(stop);
            }
            cloned.StopSequences = stops;
        }
        if (sourceTools is not null)
        {
            var tools = new List<AITool>();
            foreach (var tool in sourceTools)
            {
                _token.ThrowIfCancellationRequested();
                if (tools.Count >= MaximumTools || tool is not AIFunctionDeclaration
                    || tool.AdditionalProperties is { Count: > 0 }) Reject();
                var function = (AIFunctionDeclaration)tool;
                var name = function.Name; var description = function.Description;
                var returnSchema = function.ReturnJsonSchema;
                Add(name); Add(description);
                var schema = CopyJson(function.JsonSchema);
                var resultSchema = returnSchema is { } result ? CopyJson(result) : (JsonElement?)null;
                // 外观只提供声明，工具执行仍由统一执行器负责；不把可执行委托或可变 Schema 透传给模型客户端。
                tools.Add(AIFunctionFactory.CreateDeclaration(name, description, schema, resultSchema));
            }
            cloned.Tools = tools;
        }
        return cloned;
    }

    private ChatToolMode? CopyToolMode(ChatToolMode? mode)
    {
        if (mode is null) return null;
        if (mode is AutoChatToolMode) return ChatToolMode.Auto;
        if (mode is NoneChatToolMode) return ChatToolMode.None;
        if (mode is RequiredChatToolMode required)
        {
            var name = required.RequiredFunctionName;
            Add(name);
            return name is null ? ChatToolMode.RequireAny : ChatToolMode.RequireSpecific(name);
        }
        Reject(); return null;
    }

    /// <summary>借用来源的原始字节仅用于立即有界复制；调用方须在准入期间保持来源 JsonDocument 存活。</summary>
    private JsonElement CopyJson(JsonElement element)
    {
        _token.ThrowIfCancellationRequested();
        if (element.ValueKind == JsonValueKind.Undefined) Reject();
        // .NET 10 的只读视图不物化字符串、不解码转义；先拒绝巨大 Token 和空白，再分配有界副本。
        var raw = JsonMarshal.GetRawUtf8Value(element);
        if (raw.Length > (MaximumCharacters - _characters) * 3) Reject();
        var bytes = raw.ToArray();
        AddCharacters(Encoding.UTF8.GetCharCount(bytes));
        _token.ThrowIfCancellationRequested();
        try
        {
            // 后续检查和冻结只使用副本，避免调用方修改原始内存后改变已验证的字符数或结构。
            using var document = JsonDocument.Parse(bytes, new() { MaxDepth = MaximumJsonDepth });
            Visit(document.RootElement, 1);
            return document.RootElement.Clone();
        }
        catch (JsonException) { Reject(); return default; }
    }

    private void Visit(JsonElement element, int depth)
    {
        _token.ThrowIfCancellationRequested();
        if (depth > MaximumJsonDepth || ++_jsonNodes > MaximumJsonNodes || element.ValueKind == JsonValueKind.Undefined) Reject();
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject()) Visit(property.Value, depth + 1);
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) Visit(item, depth + 1);
    }

    private void Add(string? value)
    {
        _token.ThrowIfCancellationRequested();
        if (value is null) return;
        AddCharacters(value.Length);
    }

    private void AddCharacters(int count)
    {
        if (count > MaximumCharacters - _characters) Reject();
        _characters += count;
    }

    [DoesNotReturn]
    private static void Reject() => throw new AiRunOutputBudgetException(false);
}
