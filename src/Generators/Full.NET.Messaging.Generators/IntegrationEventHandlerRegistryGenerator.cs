using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Full.NET.Messaging.Generators;

/// <summary>
/// 编译期扫描所有标注 <c>IntegrationEventSubscriptionAttribute</c> 的集成事件处理器，生成以 (MessageType, SchemaVersion, ConsumerName) 三元组为键的常量时间路由表，并在编译期拒绝重复路由与不符合稳定机器码约束的订阅元数据。
/// </summary>
/// <remarks>
/// 该生成器是 Kafka 订阅注册的编译期守门员：只有通过机器码校验且三元组唯一的订阅才会进入运行期路由表，避免运行期才发现重复订阅或大小写漂移导致消息路由错乱。
/// 生成的 <c>IntegrationEventHandlerRegistry</c> 实现 <c>IIntegrationEventHandlerRegistry</c>，运行期由 Kafka 消费者按三元组精确解析订阅，禁止自动适配遗留处理器。
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class IntegrationEventHandlerRegistryGenerator : IIncrementalGenerator
{
    private const string AttributeMetadataName =
        "Full.NET.Messaging.Abstractions.IntegrationEventSubscriptionAttribute";

    // 检测同一 (MessageType, SchemaVersion, ConsumerName) 路由被多个订阅声明；编译期失败，避免运行期随机选择处理器。
    private static readonly DiagnosticDescriptor DuplicateRoute = new(
        "FNMESSAGING001",
        "集成事件订阅路由重复",
        "路由 ({0}, {1}, {2}) 已由多个订阅声明",
        "Full.NET.Messaging",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // 检测 ConsumerName、MessageType 或 SchemaVersion 不符合稳定机器码约束；编译期失败，避免运行期因大小写或分隔符漂移导致路由 miss。
    private static readonly DiagnosticDescriptor InvalidMetadata = new(
        "FNMESSAGING002",
        "集成事件订阅元数据无效",
        "订阅 '{0}' 的 ConsumerName、MessageType 或 SchemaVersion 不符合稳定机器码约束",
        "Full.NET.Messaging",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// 注册增量生成管线：先按特性收集所有订阅元数据，再在聚合阶段校验、去重并输出 <c>IntegrationEventHandlerRegistry.g.cs</c>。
    /// </summary>
    /// <param name="context">增量生成器初始化上下文，用于注册语法提供器与源输出。</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var subscriptions = context.SyntaxProvider.ForAttributeWithMetadataName(
            AttributeMetadataName,
            static (node, _) => node is ClassDeclarationSyntax,
            static (attributeContext, _) => CreateSubscription(attributeContext));
        context.RegisterSourceOutput(
            subscriptions.Collect(),
            static (productionContext, items) => Generate(productionContext, items));
    }

    private static SubscriptionMetadata CreateSubscription(
        GeneratorAttributeSyntaxContext context)
    {
        var type = (INamedTypeSymbol)context.TargetSymbol;
        var attribute = context.Attributes[0];
        return new SubscriptionMetadata(
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            attribute.ConstructorArguments[0].Value as string ?? string.Empty,
            attribute.ConstructorArguments[1].Value as string ?? string.Empty,
            attribute.ConstructorArguments[2].Value is int version ? version : 0,
            type.Locations.FirstOrDefault());
    }

    private static void Generate(
        SourceProductionContext context,
        ImmutableArray<SubscriptionMetadata> items)
    {
        var valid = new List<SubscriptionMetadata>(items.Length);
        foreach (var item in items)
        {
            // 机器码校验失败的诊断报给声明位置，避免生成无法路由的条目；通过校验的才进入去重与排序阶段。
            if (!IsValidConsumerName(item.ConsumerName)
                || !IsValidMessageType(item.MessageType)
                || item.SchemaVersion < 1)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidMetadata,
                    item.Location,
                    item.TypeName));
                continue;
            }

            valid.Add(item);
        }

        // 排序保证生成代码确定性：同一编译输入跨机器、跨进程必须产生字节级相同的输出，便于 diff 与缓存命中。
        var ordered = valid
            .OrderBy(item => item.MessageType, StringComparer.Ordinal)
            .ThenBy(item => item.SchemaVersion)
            .ThenBy(item => item.ConsumerName, StringComparer.Ordinal)
            .ThenBy(item => item.TypeName, StringComparer.Ordinal)
            .ToArray();
        foreach (var group in ordered.GroupBy(
                     item => new RouteKey(
                         item.MessageType,
                         item.SchemaVersion,
                         item.ConsumerName)))
        {
            if (group.Skip(1).Any())
            {
                var first = group.First();
                context.ReportDiagnostic(Diagnostic.Create(
                    DuplicateRoute,
                    first.Location,
                    first.MessageType,
                    first.SchemaVersion,
                    first.ConsumerName));
            }
        }

        // 重复路由只报诊断不剔除，留给开发者显式决策；生成代码只保留每组第一条，保证运行期路由表无歧义。
        var unique = ordered
            .GroupBy(item => new RouteKey(
                item.MessageType,
                item.SchemaVersion,
                item.ConsumerName))
            .Select(group => group.First())
            .ToArray();
        context.AddSource(
            "IntegrationEventHandlerRegistry.g.cs",
            SourceText.From(Render(unique), Encoding.UTF8));
    }

    private static string Render(IReadOnlyList<SubscriptionMetadata> items)
    {
        var source = new StringBuilder(
            """
            // <auto-generated />
            #nullable enable
            namespace Full.NET.Generated;

            internal sealed class IntegrationEventHandlerRegistry
                : global::Full.NET.Messaging.Abstractions.IIntegrationEventHandlerRegistry
            {
                public bool TryResolve(
                    string messageType,
                    int schemaVersion,
                    string consumerName,
                    out global::Full.NET.Messaging.Abstractions.IntegrationEventHandlerDescriptor descriptor)
                {
                    switch ((messageType, schemaVersion, consumerName))
                    {
            """);
        foreach (var item in items)
        {
            source.Append("            case (")
                .Append(SymbolDisplay.FormatLiteral(item.MessageType, quote: true))
                .Append(", ")
                .Append(item.SchemaVersion)
                .Append(", ")
                .Append(SymbolDisplay.FormatLiteral(item.ConsumerName, quote: true))
                .AppendLine("):" )
                .Append("                descriptor = new(")
                .Append(SymbolDisplay.FormatLiteral(item.MessageType, quote: true))
                .Append(", ")
                .Append(item.SchemaVersion)
                .Append(", ")
                .Append(SymbolDisplay.FormatLiteral(item.ConsumerName, quote: true))
                .Append(", typeof(")
                .Append(item.TypeName)
                .AppendLine("));")
                .AppendLine("                return true;");
        }

        source.Append(
            """
                        default:
                            descriptor = default;
                            return false;
                    }
                }
            }
            """);
        return source.ToString();
    }

    // ConsumerName 允许下划线与连字符，用于模块内细分消费者；最长 128 字符，至少 1 段，避免空名或超长导致路由表体积膨胀。
    private static bool IsValidConsumerName(string value) =>
        value.Length is > 0 and <= 128
        && IsDotSeparatedMachineCode(
            value,
            minimumSegments: 1,
            allowUnderscore: true,
            allowHyphen: true);

    // MessageType 强制至少 4 段点分（如领域.模块.事件.版本），禁止连字符，避免与 Kafka Topic 命名约定冲突；最长 256 字符。
    private static bool IsValidMessageType(string value) =>
        value.Length is > 0 and <= 256
        && IsDotSeparatedMachineCode(
            value,
            minimumSegments: 4,
            allowUnderscore: true,
            allowHyphen: false);

    // 稳定机器码约束：每段必须小写字母开头，后续字符只能是小写字母、数字或显式允许的下划线/连字符；禁止 PascalCase、空格与非 ASCII，确保跨端一致匹配。
    private static bool IsDotSeparatedMachineCode(
        string value,
        int minimumSegments,
        bool allowUnderscore,
        bool allowHyphen)
    {
        var segments = value.Split('.');
        if (segments.Length < minimumSegments)
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment[0] is < 'a' or > 'z')
            {
                return false;
            }

            for (var index = 1; index < segment.Length; index++)
            {
                var character = segment[index];
                if (character is >= 'a' and <= 'z'
                    || character is >= '0' and <= '9'
                    || allowUnderscore && character == '_'
                    || allowHyphen && character == '-')
                {
                    continue;
                }

                return false;
            }
        }

        return true;
    }

    private sealed class SubscriptionMetadata(
        string typeName,
        string consumerName,
        string messageType,
        int schemaVersion,
        Location? location)
    {
        public string TypeName { get; } = typeName;

        public string ConsumerName { get; } = consumerName;

        public string MessageType { get; } = messageType;

        public int SchemaVersion { get; } = schemaVersion;

        public Location? Location { get; } = location;
    }

    private sealed class RouteKey(
        string messageType,
        int schemaVersion,
        string consumerName) : IEquatable<RouteKey>
    {
        private readonly string _messageType = messageType;
        private readonly int _schemaVersion = schemaVersion;
        private readonly string _consumerName = consumerName;

        public bool Equals(RouteKey? other) =>
            other is not null
            && _schemaVersion == other._schemaVersion
            && string.Equals(_messageType, other._messageType, StringComparison.Ordinal)
            && string.Equals(_consumerName, other._consumerName, StringComparison.Ordinal);

        public override bool Equals(object? obj) => Equals(obj as RouteKey);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = StringComparer.Ordinal.GetHashCode(_messageType);
                hash = (hash * 397) ^ _schemaVersion;
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(_consumerName);
                return hash;
            }
        }
    }
}
