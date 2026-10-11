using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Full.NET.Agents.Runtime;

/// <summary>先冻结整批模型输出再派发工具；调用身份唯一、参数无损且复制和枚举有界。</summary>
internal sealed class AgentToolCallAdmission(CancellationToken token)
{
    // 与派发输入的规模边界一致；这是载荷准入，不是供应商精确 Token 或费用证明。
    private const int MaximumCharacters = 262144;
    private const int MaximumMessages = 128;
    private const int MaximumContents = 512;
    private const int MaximumArguments = 128;
    private const int MaximumJsonNodes = 4096;
    private const int MaximumJsonDepth = 32;
    private static readonly JsonElement NullValue = JsonDocument.Parse("null").RootElement.Clone();
    private int _characters;
    private int _serializedArguments;
    private int _jsonNodes;

    /// <summary>拒绝将转发的消息/内容扩展载荷；全部调用准入成功后才登记本轮身份。</summary>
    internal static (IReadOnlyList<ChatMessage> Messages, IReadOnlyList<AdmittedToolCall> Calls) Freeze(
        ChatResponse response, int remainingSteps, HashSet<string> previousCallIds, CancellationToken token)
    {
        var admission = new AgentToolCallAdmission(token);
        var messages = new List<ChatMessage>();
        var calls = new List<AdmittedToolCall>();
        var batchIds = new HashSet<string>(StringComparer.Ordinal);
        var contentCount = 0;
        // 顶层响应的 SDK 原对象和诊断属性从不转发或保存；不读取、转换它们，保留适配器兼容性。
        foreach (var message in response.Messages)
        {
            token.ThrowIfCancellationRequested();
            if (messages.Count >= MaximumMessages || message is null
                || message.RawRepresentation is not null || message.AdditionalProperties is not null) Reject();
            var role = message.Role;
            // 模型只能补充 assistant 历史，不能伪装 system/user/tool 提升后续提示的信任等级。
            if (role != ChatRole.Assistant) Reject();
            var author = message.AuthorName;
            var id = message.MessageId;
            admission.Add(role.Value); admission.Add(author); admission.Add(id);
            var contents = new List<AIContent>();
            foreach (var content in message.Contents)
            {
                token.ThrowIfCancellationRequested();
                if (++contentCount > MaximumContents || content is null
                    || content.RawRepresentation is not null || content.AdditionalProperties is not null) Reject();
                switch (content)
                {
                    case TextContent text:
                        var value = text.Text;
                        admission.Add(value);
                        contents.Add(new TextContent(value));
                        break;
                    case FunctionCallContent call:
                        // 模型步数已消耗；整批工具必须都能装入剩余额度，不能先执行前缀再发现超限。
                        if (calls.Count >= remainingSteps) throw new AgentToolLoopBudgetExceededException();
                        var callId = call.CallId; var name = call.Name;
                        if (!ValidIdentity(callId) || !ValidIdentity(name) || call.InformationalOnly || call.Exception is not null
                            || previousCallIds.Contains(callId) || !batchIds.Add(callId)) Reject();
                        admission.Add(callId); admission.Add(name);
                        var arguments = admission.CopyArguments(call.Arguments);
                        calls.Add(new(callId, name, arguments));
                        var chatArguments = new Dictionary<string, object?>(StringComparer.Ordinal);
                        foreach (var property in arguments.EnumerateObject()) chatArguments.Add(property.Name, property.Value);
                        contents.Add(new FunctionCallContent(callId, name, chatArguments));
                        break;
                    default:
                        Reject();
                        break;
                }
            }
            messages.Add(new(role, contents) { AuthorName = author, MessageId = id, CreatedAt = message.CreatedAt });
        }
        token.ThrowIfCancellationRequested();
        previousCallIds.UnionWith(batchIds);
        return (messages.AsReadOnly(), calls.AsReadOnly());
    }

    private JsonElement CopyArguments(IDictionary<string, object?>? source)
    {
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (source is not null)
            foreach (var (key, value) in source)
            {
                token.ThrowIfCancellationRequested();
                if (arguments.Count >= MaximumArguments) Reject();
                Add(key);
                arguments.Add(key, CopyValue(value));
            }
        var json = JsonSerializer.Serialize(arguments, AgentToolLoopJsonSerializerContext.Default.DictionaryStringJsonElement);
        // 序列化后另核对包含键转义和分隔符的累计规模；上面的字符检查限制本次序列化分配。
        if (json.Length > MaximumCharacters - _serializedArguments) Reject();
        _serializedArguments += json.Length;
        token.ThrowIfCancellationRequested();
        try
        {
            using var document = JsonDocument.Parse(json, new() { MaxDepth = MaximumJsonDepth });
            // 统一从最终参数树计数，包含根对象及原生标量；不因来源是 CLR 值或 JsonElement 而漏算/重算。
            Visit(document.RootElement, 1);
            return document.RootElement.Clone();
        }
        catch (JsonException) { Reject(); return default; }
    }

    private JsonElement CopyValue(object? value)
    {
        token.ThrowIfCancellationRequested();
        switch (value)
        {
            case null: Add("null"); return NullValue;
            case JsonElement element: return CopyJson(element);
            case string text:
                Add(text);
                return JsonSerializer.SerializeToElement(text, AgentToolLoopJsonSerializerContext.Default.String);
            case bool boolean:
                Add("false");
                return JsonSerializer.SerializeToElement(boolean, AgentToolLoopJsonSerializerContext.Default.Boolean);
            case int number:
                Add("00000000000");
                return JsonSerializer.SerializeToElement(number, AgentToolLoopJsonSerializerContext.Default.Int32);
            case long number:
                Add("00000000000000000000");
                return JsonSerializer.SerializeToElement(number, AgentToolLoopJsonSerializerContext.Default.Int64);
            case double number when double.IsFinite(number):
                Add("00000000000000000000000000000000");
                return JsonSerializer.SerializeToElement(number, AgentToolLoopJsonSerializerContext.Default.Double);
            default:
                // 不执行未知对象的 ToString、虚拟转换或反射序列化，避免参数含义在恢复时变化。
                Reject();
                return default;
        }
    }

    private JsonElement CopyJson(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Undefined) Reject();
        var raw = JsonMarshal.GetRawUtf8Value(element);
        if (raw.Length > (MaximumCharacters - _characters) * 3) Reject();
        var bytes = raw.ToArray();
        AddCharacters(Encoding.UTF8.GetCharCount(bytes));
        token.ThrowIfCancellationRequested();
        try
        {
            // 只对有界副本检查和克隆；后续工具等待期间不依赖原 JsonDocument 的寿命或内存。
            using var document = JsonDocument.Parse(bytes, new() { MaxDepth = MaximumJsonDepth });
            return document.RootElement.Clone();
        }
        catch (JsonException) { Reject(); return default; }
    }

    private void Visit(JsonElement element, int depth)
    {
        token.ThrowIfCancellationRequested();
        if (++_jsonNodes > MaximumJsonNodes || depth > MaximumJsonDepth) Reject();
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) Reject();
                Visit(property.Value, depth + 1);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) Visit(child, depth + 1);
    }

    private static bool ValidIdentity(string value) => value.Length is > 0 and <= 256
        && !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);
    private void Add(string? value) { token.ThrowIfCancellationRequested(); if (value is not null) AddCharacters(value.Length); }
    private void AddCharacters(int count) { if (count > MaximumCharacters - _characters) Reject(); _characters += count; }
    [DoesNotReturn]
    private static void Reject() => throw new InvalidOperationException("Model tool batch is invalid or exceeds admission limits.");
}

/// <summary>已接纳工具调用；参数为独立 JSON 副本，不能在工具等待后重新读取模型的可变对象。</summary>
internal sealed record AdmittedToolCall(string CallId, string Name, JsonElement Arguments)
{
    public override string ToString() => nameof(AdmittedToolCall);
}
