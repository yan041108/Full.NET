using System.Reflection;
using System.Reflection.Emit;

namespace Full.NET.ArchitectureTests;

internal sealed record GuidStorageSourceFile(string Path, string Content);

internal static class GuidStorageArchitectureScanner
{
    private static readonly OpCode[] OneByteOpCodes = new OpCode[0x100];
    private static readonly OpCode[] TwoByteOpCodes = new OpCode[0x100];
    private const string UuidToBinFunction = "UUID_TO_BIN";

    static GuidStorageArchitectureScanner()
    {
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode opCode)
            {
                continue;
            }

            var value = unchecked((ushort)opCode.Value);
            if (opCode.Size == 1)
            {
                OneByteOpCodes[value] = opCode;
            }
            else
            {
                TwoByteOpCodes[value & 0xff] = opCode;
            }
        }
    }

    public static string[] FindUnsafeSqlConversions(
        IEnumerable<GuidStorageSourceFile> files,
        IEnumerable<string> allowedFiles)
    {
        var allowed = new HashSet<string>(allowedFiles, StringComparer.OrdinalIgnoreCase);
        var timeSwapGuidFormat = "TimeSwap" + "Binary16";
        return files
            .Where(file => !allowed.Contains(file.Path))
            .Where(file => file.Content.Contains(timeSwapGuidFormat, StringComparison.Ordinal)
                || ContainsTimeSwapUuidToBin(file.Content))
            .Select(file => file.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool ContainsTimeSwapUuidToBin(string content)
    {
        var searchIndex = 0;
        while ((searchIndex = content.IndexOf(
                   UuidToBinFunction,
                   searchIndex,
                   StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var cursor = searchIndex + UuidToBinFunction.Length;
            while (cursor < content.Length && char.IsWhiteSpace(content[cursor]))
            {
                cursor++;
            }

            if (cursor >= content.Length || content[cursor] != '(')
            {
                searchIndex = cursor;
                continue;
            }

            var depth = 1;
            var secondArgumentStart = -1;
            for (cursor++; cursor < content.Length && depth > 0; cursor++)
            {
                switch (content[cursor])
                {
                    case '(':
                        depth++;
                        break;
                    case ')':
                        depth--;
                        if (depth == 0 && secondArgumentStart >= 0)
                        {
                            var argument = content[secondArgumentStart..cursor].Trim();
                            if (string.Equals(argument, "1", StringComparison.Ordinal))
                            {
                                return true;
                            }
                        }

                        break;
                    case ',' when depth == 1 && secondArgumentStart < 0:
                        secondArgumentStart = cursor + 1;
                        break;
                }
            }

            searchIndex += UuidToBinFunction.Length;
        }

        return false;
    }

    public static string[] FindGuidToByteArrayCalls(IEnumerable<Assembly> assemblies)
    {
        return assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(GetDeclaredMethods)
            .Where(CallsGuidToByteArray)
            .Select(method => $"{method.DeclaringType?.FullName}.{method.Name}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<MethodBase> GetDeclaredMethods(Type type)
    {
        const BindingFlags Flags = BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.DeclaredOnly;
        return type.GetMethods(Flags).Cast<MethodBase>().Concat(type.GetConstructors(Flags));
    }

    private static bool CallsGuidToByteArray(MethodBase method)
    {
        var body = method.GetMethodBody();
        var il = body?.GetILAsByteArray();
        if (il is null)
        {
            return false;
        }

        var offset = 0;
        while (offset < il.Length)
        {
            var opCode = ReadOpCode(il, ref offset);
            if (opCode.OperandType == OperandType.InlineMethod)
            {
                var metadataToken = BitConverter.ToInt32(il, offset);
                if (IsGuidToByteArray(method, metadataToken))
                {
                    return true;
                }
            }

            offset += GetOperandSize(opCode.OperandType, il, offset);
        }

        return false;
    }

    private static bool IsGuidToByteArray(MethodBase caller, int metadataToken)
    {
        try
        {
            var calledMethod = caller.Module.ResolveMethod(
                metadataToken,
                caller.DeclaringType?.GetGenericArguments(),
                caller is MethodInfo methodInfo ? methodInfo.GetGenericArguments() : null);
            return calledMethod?.DeclaringType == typeof(Guid)
                && string.Equals(calledMethod.Name, nameof(Guid.ToByteArray), StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static OpCode ReadOpCode(byte[] il, ref int offset)
    {
        var first = il[offset++];
        if (first != 0xfe)
        {
            return OneByteOpCodes[first];
        }

        return TwoByteOpCodes[il[offset++]];
    }

    private static int GetOperandSize(OperandType operandType, byte[] il, int offset) =>
        operandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI
                or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineBrTarget or OperandType.InlineField
                or OperandType.InlineI or OperandType.InlineMethod
                or OperandType.InlineSig or OperandType.InlineString
                or OperandType.InlineTok or OperandType.InlineType
                or OperandType.ShortInlineR => 4,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + (BitConverter.ToInt32(il, offset) * 4),
            _ => throw new InvalidDataException($"不支持的 IL 操作数类型: {operandType}。"),
        };
}

internal static class GuidToByteArrayNegativeFixture
{
    public static byte[] Convert(Guid id) => id.ToByteArray();
}
