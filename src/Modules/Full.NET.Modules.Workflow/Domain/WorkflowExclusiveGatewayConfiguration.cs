using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>解析排他网关的闭合配置，并在发布期绑定表单字段协议。</summary>
internal static class WorkflowExclusiveGatewayConfiguration
{
    private const int MaximumBranchCount = 15;
    private const int MaximumKeyLength = 128;

    /// <summary>读取排他网关配置；未提供表单架构时仅执行结构校验。</summary>
    /// <param name="config">排他网关节点配置。</param>
    /// <param name="formSchema">可选的已发布表单架构。</param>
    /// <param name="definition">成功解析且可用于运行时求值的网关定义。</param>
    /// <returns>配置形状、条件类型和目标集合均有效时返回 <see langword="true"/>。</returns>
    public static bool TryRead(
        JsonElement config,
        WorkflowFormSchema? formSchema,
        out WorkflowExclusiveGatewayDefinition? definition)
    {
        definition = null;
        if (config.ValueKind != JsonValueKind.Object ||
            !HasOnlyProperties(config, "nodeName", "nextNodeKeys", "branches", "defaultNextNodeKey") ||
            !TryReadOptionalNodeName(config) ||
            !TryReadStableKey(config, "defaultNextNodeKey", out var defaultNextNodeKey) ||
            !TryReadKeyArray(config, "nextNodeKeys", out var nextNodeKeys) ||
            !config.TryGetProperty("branches", out var branchesElement) ||
            branchesElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var branchElements = branchesElement.EnumerateArray().ToArray();
        if (branchElements.Length is < 1 or > MaximumBranchCount)
        {
            return false;
        }

        var fields = formSchema?.Sections
            .SelectMany(section => section.Fields)
            .ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
        var branches = new List<WorkflowExclusiveGatewayBranch>(branchElements.Length);
        var branchKeys = new HashSet<string>(StringComparer.Ordinal);
        var targetKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var branchElement in branchElements)
        {
            if (!TryReadBranch(branchElement, fields, out var branch) ||
                !branchKeys.Add(branch!.BranchKey) ||
                !targetKeys.Add(branch.NextNodeKey))
            {
                return false;
            }

            branches.Add(branch);
        }

        // 默认目标也必须唯一，避免两个分支在配置层产生无法辨识的同一出口。
        if (!targetKeys.Add(defaultNextNodeKey))
        {
            return false;
        }

        var expectedTargets = branches.Select(branch => branch.NextNodeKey)
            .Append(defaultNextNodeKey)
            .ToArray();
        if (!nextNodeKeys.SequenceEqual(expectedTargets, StringComparer.Ordinal))
        {
            return false;
        }

        definition = new WorkflowExclusiveGatewayDefinition(branches, defaultNextNodeKey);
        return true;
    }

    /// <summary>读取单个有序分支。</summary>
    /// <param name="element">分支 JSON。</param>
    /// <param name="fields">可选的表单字段索引。</param>
    /// <param name="branch">解析后的分支。</param>
    /// <returns>分支键、目标和条件均有效时返回 <see langword="true"/>。</returns>
    private static bool TryReadBranch(
        JsonElement element,
        IReadOnlyDictionary<string, WorkflowFormField>? fields,
        out WorkflowExclusiveGatewayBranch? branch)
    {
        branch = null;
        if (element.ValueKind != JsonValueKind.Object ||
            !HasOnlyProperties(element, "branchKey", "nextNodeKey", "condition") ||
            !TryReadStableKey(element, "branchKey", out var branchKey) ||
            !TryReadStableKey(element, "nextNodeKey", out var nextNodeKey) ||
            !element.TryGetProperty("condition", out var conditionElement) ||
            !TryReadCondition(conditionElement, fields, out var condition))
        {
            return false;
        }

        branch = new WorkflowExclusiveGatewayBranch(branchKey, nextNodeKey, condition!);
        return true;
    }

    /// <summary>读取一个只允许单字段比较的闭合条件。</summary>
    /// <param name="element">条件 JSON。</param>
    /// <param name="fields">可选的表单字段索引。</param>
    /// <param name="condition">解析后的条件。</param>
    /// <returns>条件协议有效时返回 <see langword="true"/>。</returns>
    private static bool TryReadCondition(
        JsonElement element,
        IReadOnlyDictionary<string, WorkflowFormField>? fields,
        out WorkflowExclusiveGatewayCondition? condition)
    {
        condition = null;
        if (element.ValueKind != JsonValueKind.Object ||
            !TryReadStableKey(element, "fieldKey", out var fieldKey) ||
            !TryReadOperator(element, out var gatewayOperator))
        {
            return false;
        }

        var isEmptyOperator = gatewayOperator is WorkflowExclusiveGatewayOperator.IsEmpty or
            WorkflowExclusiveGatewayOperator.IsNotEmpty;
        var hasExpectedValue = element.TryGetProperty("value", out var expectedValue);
        if (!HasOnlyConditionProperties(element, isEmptyOperator) ||
            (isEmptyOperator ? hasExpectedValue : !hasExpectedValue))
        {
            return false;
        }

        WorkflowFormField? field = null;
        if (fields is not null &&
            (!fields.TryGetValue(fieldKey, out field) ||
             !IsOperatorCompatible(field, gatewayOperator) ||
             (!isEmptyOperator && !WorkflowFormValueValidator.IsFieldValueValid(field, expectedValue))))
        {
            return false;
        }

        condition = new WorkflowExclusiveGatewayCondition(
            fieldKey,
            gatewayOperator,
            isEmptyOperator ? null : expectedValue.Clone(),
            field);
        return true;
    }

    /// <summary>检查条件对象是否只包含当前操作符允许的属性。</summary>
    /// <param name="element">条件 JSON。</param>
    /// <param name="isEmptyOperator">是否为空值类操作符。</param>
    /// <returns>对象不存在扩展属性时返回 <see langword="true"/>。</returns>
    private static bool HasOnlyConditionProperties(JsonElement element, bool isEmptyOperator) =>
        isEmptyOperator
            ? HasOnlyProperties(element, "fieldKey", "operator")
            : HasOnlyProperties(element, "fieldKey", "operator", "value");

    /// <summary>读取稳定操作符机器码。</summary>
    /// <param name="element">条件 JSON。</param>
    /// <param name="gatewayOperator">解析后的操作符。</param>
    /// <returns>操作符属于闭合集合时返回 <see langword="true"/>。</returns>
    private static bool TryReadOperator(
        JsonElement element,
        out WorkflowExclusiveGatewayOperator gatewayOperator)
    {
        gatewayOperator = default;
        if (!element.TryGetProperty("operator", out var operatorElement) ||
            operatorElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        gatewayOperator = operatorElement.GetString() switch
        {
            "equals" => WorkflowExclusiveGatewayOperator.Equals,
            "notEquals" => WorkflowExclusiveGatewayOperator.NotEquals,
            "greaterThan" => WorkflowExclusiveGatewayOperator.GreaterThan,
            "greaterThanOrEqual" => WorkflowExclusiveGatewayOperator.GreaterThanOrEqual,
            "lessThan" => WorkflowExclusiveGatewayOperator.LessThan,
            "lessThanOrEqual" => WorkflowExclusiveGatewayOperator.LessThanOrEqual,
            "isEmpty" => WorkflowExclusiveGatewayOperator.IsEmpty,
            "isNotEmpty" => WorkflowExclusiveGatewayOperator.IsNotEmpty,
            _ => default,
        };
        return gatewayOperator != default;
    }

    /// <summary>检查操作符是否与表单字段类型相容。</summary>
    /// <param name="field">表单字段定义。</param>
    /// <param name="gatewayOperator">待检查的操作符。</param>
    /// <returns>运行时能够无隐式类型转换求值时返回 <see langword="true"/>。</returns>
    private static bool IsOperatorCompatible(
        WorkflowFormField field,
        WorkflowExclusiveGatewayOperator gatewayOperator)
    {
        if (gatewayOperator is WorkflowExclusiveGatewayOperator.IsEmpty or
            WorkflowExclusiveGatewayOperator.IsNotEmpty)
        {
            return true;
        }

        if (gatewayOperator is WorkflowExclusiveGatewayOperator.Equals or
            WorkflowExclusiveGatewayOperator.NotEquals)
        {
            return field.FieldTypeKey is "text" or "textarea" or "money" or "decimal" or
                "date" or "time" or "datetime" or "radio" or "select" or "integer" or "switch";
        }

        return field.FieldTypeKey is "money" or "decimal" or "date" or "time" or "datetime" or "integer";
    }

    /// <summary>读取非空、长度受限的稳定机器键。</summary>
    /// <param name="element">包含机器键的对象。</param>
    /// <param name="propertyName">属性名。</param>
    /// <param name="value">读取到的机器键。</param>
    /// <returns>机器键符合命名边界时返回 <see langword="true"/>。</returns>
    private static bool TryReadStableKey(
        JsonElement element,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               IsStableKey(property.GetString(), out value);
    }

    /// <summary>读取有序且不重复的出口键数组。</summary>
    /// <param name="element">网关配置对象。</param>
    /// <param name="propertyName">数组属性名。</param>
    /// <param name="keys">读取到的键数组。</param>
    /// <returns>数组内所有键均有效且唯一时返回 <see langword="true"/>。</returns>
    private static bool TryReadKeyArray(
        JsonElement element,
        string propertyName,
        out IReadOnlyList<string> keys)
    {
        keys = [];
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var parsed = new List<string>();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String ||
                !IsStableKey(item.GetString(), out var key) ||
                !unique.Add(key))
            {
                return false;
            }

            parsed.Add(key);
        }

        keys = parsed;
        return parsed.Count >= 2;
    }

    /// <summary>验证可选节点名称。</summary>
    /// <param name="config">网关配置对象。</param>
    /// <returns>名称缺失或属于非空短文本时返回 <see langword="true"/>。</returns>
    private static bool TryReadOptionalNodeName(JsonElement config)
    {
        if (!config.TryGetProperty("nodeName", out var nodeName))
        {
            return true;
        }

        return nodeName.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(nodeName.GetString()) &&
               nodeName.GetString()!.Length <= MaximumKeyLength;
    }

    /// <summary>拒绝闭合协议之外的任意扩展属性。</summary>
    /// <param name="element">待检查对象。</param>
    /// <param name="allowedProperties">允许出现的属性名。</param>
    /// <returns>对象全部属性均在白名单内时返回 <see langword="true"/>。</returns>
    private static bool HasOnlyProperties(JsonElement element, params string[] allowedProperties)
    {
        var allowed = new HashSet<string>(allowedProperties, StringComparer.Ordinal);
        return element.EnumerateObject().All(property => allowed.Contains(property.Name));
    }

    /// <summary>验证机器键只使用 ASCII 字母、数字、下划线、短横线和点。</summary>
    /// <param name="candidate">候选机器键。</param>
    /// <param name="value">规范化后的原始键值。</param>
    /// <returns>候选值符合稳定标识符边界时返回 <see langword="true"/>。</returns>
    private static bool IsStableKey(string? candidate, out string value)
    {
        value = candidate ?? string.Empty;
        if (string.IsNullOrWhiteSpace(candidate) ||
            candidate.Length > MaximumKeyLength ||
            !IsAsciiLetter(candidate[0]))
        {
            return false;
        }

        foreach (var character in candidate.AsSpan(1))
        {
            if (!IsAsciiLetter(character) &&
                !char.IsAsciiDigit(character) &&
                character is not ('_' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>判断字符是否为 ASCII 字母。</summary>
    /// <param name="character">待检查字符。</param>
    /// <returns>字符属于 ASCII 字母时返回 <see langword="true"/>。</returns>
    private static bool IsAsciiLetter(char character) =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
}

/// <summary>表示已验证且按声明顺序求值的排他网关。</summary>
internal sealed record WorkflowExclusiveGatewayDefinition(
    IReadOnlyList<WorkflowExclusiveGatewayBranch> Branches,
    string DefaultNextNodeKey)
{
    /// <summary>按配置顺序选择第一个成立分支，否则选择默认分支。</summary>
    /// <param name="values">实例绑定且已通过表单协议校验的字段值。</param>
    /// <param name="selection">唯一的分支选择结果。</param>
    /// <returns>所有条件都可安全求值时返回 <see langword="true"/>。</returns>
    public bool TrySelectBranch(
        IReadOnlyDictionary<string, JsonElement> values,
        out WorkflowExclusiveGatewaySelection selection)
    {
        foreach (var branch in Branches)
        {
            if (!branch.Condition.TryEvaluate(values, out var matched))
            {
                selection = default!;
                return false;
            }

            if (matched)
            {
                selection = new WorkflowExclusiveGatewaySelection(branch.BranchKey, branch.NextNodeKey);
                return true;
            }
        }

        selection = new WorkflowExclusiveGatewaySelection("default", DefaultNextNodeKey);
        return true;
    }
}

/// <summary>表示排他网关中的一个有序条件分支。</summary>
/// <param name="BranchKey">稳定分支机器键。</param>
/// <param name="NextNodeKey">分支目标节点键。</param>
/// <param name="Condition">单字段闭合条件。</param>
internal sealed record WorkflowExclusiveGatewayBranch(
    string BranchKey,
    string NextNodeKey,
    WorkflowExclusiveGatewayCondition Condition);

/// <summary>表示排他网关最终选择的唯一出口。</summary>
/// <param name="BranchKey">命中的分支键；默认出口固定为 <c>default</c>。</param>
/// <param name="NextNodeKey">命中的目标节点键。</param>
internal sealed record WorkflowExclusiveGatewaySelection(string BranchKey, string NextNodeKey);

/// <summary>表示已绑定可选表单字段定义的单字段条件。</summary>
/// <param name="FieldKey">表单字段机器键。</param>
/// <param name="Operator">闭合比较操作符。</param>
/// <param name="ExpectedValue">非空值操作符使用的规范比较值。</param>
/// <param name="Field">发布期绑定的字段定义；结构校验阶段允许为空。</param>
internal sealed record WorkflowExclusiveGatewayCondition(
    string FieldKey,
    WorkflowExclusiveGatewayOperator Operator,
    JsonElement? ExpectedValue,
    WorkflowFormField? Field)
{
    /// <summary>使用严格字段类型比较条件，禁止字符串与数字间隐式转换。</summary>
    /// <param name="values">实例绑定且已校验的字段值。</param>
    /// <param name="matched">条件是否成立。</param>
    /// <returns>条件已绑定字段协议且可安全求值时返回 <see langword="true"/>。</returns>
    public bool TryEvaluate(IReadOnlyDictionary<string, JsonElement> values, out bool matched)
    {
        matched = false;
        if (Field is null)
        {
            return false;
        }

        var hasValue = values.TryGetValue(FieldKey, out var actualValue) &&
            actualValue.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
        var isEmpty = !hasValue ||
            actualValue.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(actualValue.GetString()) ||
            actualValue.ValueKind == JsonValueKind.Array && actualValue.GetArrayLength() == 0;
        if (Operator == WorkflowExclusiveGatewayOperator.IsEmpty)
        {
            matched = isEmpty;
            return true;
        }

        if (Operator == WorkflowExclusiveGatewayOperator.IsNotEmpty)
        {
            matched = !isEmpty;
            return true;
        }

        if (!hasValue || !WorkflowFormValueValidator.IsFieldValueValid(Field, actualValue))
        {
            return true;
        }

        var comparison = Compare(actualValue, ExpectedValue!.Value, Field);
        if (comparison is null)
        {
            return false;
        }

        matched = Operator switch
        {
            WorkflowExclusiveGatewayOperator.Equals => comparison == 0,
            WorkflowExclusiveGatewayOperator.NotEquals => comparison != 0,
            WorkflowExclusiveGatewayOperator.GreaterThan => comparison > 0,
            WorkflowExclusiveGatewayOperator.GreaterThanOrEqual => comparison >= 0,
            WorkflowExclusiveGatewayOperator.LessThan => comparison < 0,
            WorkflowExclusiveGatewayOperator.LessThanOrEqual => comparison <= 0,
            _ => false,
        };
        return true;
    }

    /// <summary>按字段协议转换为可排序值并比较。</summary>
    /// <param name="actual">实例字段值。</param>
    /// <param name="expected">网关配置中的期望值。</param>
    /// <param name="field">已绑定字段定义。</param>
    /// <returns>比较结果；类型无法无损解析时返回空。</returns>
    private static int? Compare(JsonElement actual, JsonElement expected, WorkflowFormField field) =>
        field.FieldTypeKey switch
        {
            "integer" when actual.TryGetInt64(out var actualInteger) && expected.TryGetInt64(out var expectedInteger) =>
                actualInteger.CompareTo(expectedInteger),
            "money" or "decimal" => CompareDecimal(actual, expected, field),
            "date" or "time" or "datetime" => CompareTemporal(actual, expected, field),
            "switch" when actual.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                          expected.ValueKind is JsonValueKind.True or JsonValueKind.False =>
                actual.GetBoolean().CompareTo(expected.GetBoolean()),
            "text" or "textarea" or "radio" or "select"
                when actual.ValueKind == JsonValueKind.String && expected.ValueKind == JsonValueKind.String =>
                string.CompareOrdinal(actual.GetString(), expected.GetString()),
            _ => null,
        };

    /// <summary>比较规范十进制字符串。</summary>
    /// <param name="actual">实例字段值。</param>
    /// <param name="expected">条件期望值。</param>
    /// <param name="field">表单字段定义。</param>
    /// <returns>比较结果；约束或值无效时返回空。</returns>
    private static int? CompareDecimal(JsonElement actual, JsonElement expected, WorkflowFormField field)
    {
        if (actual.ValueKind != JsonValueKind.String ||
            expected.ValueKind != JsonValueKind.String ||
            !WorkflowFormFieldConstraints.TryReadDecimalScale(
                field,
                field.FieldTypeKey == "money" ? 4 : 28,
                out var scale) ||
            !WorkflowFormFieldConstraints.TryParseCanonicalDecimal(actual.GetString()!, scale, out var actualNumber) ||
            !WorkflowFormFieldConstraints.TryParseCanonicalDecimal(expected.GetString()!, scale, out var expectedNumber))
        {
            return null;
        }

        return actualNumber.CompareTo(expectedNumber);
    }

    /// <summary>比较规范日期、时间或带时区日期时间。</summary>
    /// <param name="actual">实例字段值。</param>
    /// <param name="expected">条件期望值。</param>
    /// <param name="field">表单字段定义。</param>
    /// <returns>比较结果；值无效时返回空。</returns>
    private static int? CompareTemporal(JsonElement actual, JsonElement expected, WorkflowFormField field)
    {
        if (actual.ValueKind != JsonValueKind.String ||
            expected.ValueKind != JsonValueKind.String ||
            !WorkflowFormFieldConstraints.TryParseCanonicalTemporal(
                field.FieldTypeKey,
                actual.GetString()!,
                out var actualValue) ||
            !WorkflowFormFieldConstraints.TryParseCanonicalTemporal(
                field.FieldTypeKey,
                expected.GetString()!,
                out var expectedValue))
        {
            return null;
        }

        return actualValue.CompareTo(expectedValue);
    }
}

/// <summary>定义排他网关允许的闭合比较操作符。</summary>
internal enum WorkflowExclusiveGatewayOperator
{
    /// <summary>未识别的操作符。</summary>
    Unknown = 0,

    /// <summary>严格相等。</summary>
    Equals = 1,

    /// <summary>严格不相等。</summary>
    NotEquals = 2,

    /// <summary>大于。</summary>
    GreaterThan = 3,

    /// <summary>大于或等于。</summary>
    GreaterThanOrEqual = 4,

    /// <summary>小于。</summary>
    LessThan = 5,

    /// <summary>小于或等于。</summary>
    LessThanOrEqual = 6,

    /// <summary>字段缺失、为空字符串或空数组。</summary>
    IsEmpty = 7,

    /// <summary>字段存在且不为空。</summary>
    IsNotEmpty = 8,
}
