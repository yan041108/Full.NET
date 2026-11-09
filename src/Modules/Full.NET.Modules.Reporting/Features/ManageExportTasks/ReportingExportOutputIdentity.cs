using System.Globalization;
using Full.NET.Modules.Reporting.Domain;

namespace Full.NET.Modules.Reporting.Features.ManageExportTasks;

/// <summary>将行数与单次上传文件绑定，同时保留对外下载名。</summary>
internal static class ReportingExportOutputIdentity
{
    private const string Prefix = "reporting-rows-";
    private const int MaxFileNameLength = 255;

    internal static string BuildStorageFileName(string fileName, int rowCount)
    {
        if (rowCount is < 0 or > ReportingExportPolicy.MaxExportRows || !IsValidFileName(fileName))
            throw new InvalidDataException("Invalid reporting output identity.");
        var storageName = Prefix + rowCount.ToString(CultureInfo.InvariantCulture) + "-" + fileName;
        if (storageName.Length > MaxFileNameLength)
            throw new InvalidDataException("Reporting output identity exceeds the file name limit.");
        return storageName;
    }

    internal static bool TryRead(string storageName, out string fileName, out int rowCount)
    {
        fileName = string.Empty;
        rowCount = 0;
        if (storageName.Length > MaxFileNameLength || !storageName.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        var separator = storageName.IndexOf('-', Prefix.Length);
        if (separator <= Prefix.Length
            || !int.TryParse(storageName.AsSpan(Prefix.Length, separator - Prefix.Length),
                NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed is < 0 or > ReportingExportPolicy.MaxExportRows)
            return false;
        var name = storageName[(separator + 1)..];
        if (!IsValidFileName(name)) return false;
        fileName = name;
        rowCount = parsed;
        return true;
    }

    private static bool IsValidFileName(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value == value.Trim()
        && value.IndexOfAny(['/', '\\']) < 0
        && !value.Any(char.IsControl);
}
