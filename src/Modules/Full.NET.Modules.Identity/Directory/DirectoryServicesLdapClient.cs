using System.DirectoryServices.Protocols;
using System.Text;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Directory;

/// <summary>基于 <see cref="System.DirectoryServices.Protocols"/> 的 LDAP 目录客户端。</summary>
internal sealed class DirectoryServicesLdapClient : ILdapDirectoryClient
{
    /// <inheritdoc />
    public Task<LdapConnectionTestOutcome> TestConnectionAsync(
        LdapConnectionRuntimeSettings settings,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var connection = CreateBoundServiceConnection(settings);
            return Task.FromResult(new LdapConnectionTestOutcome(
                true,
                "Connection and service account bind succeeded."));
        }
        catch (Exception exception)
        {
            return Task.FromResult(new LdapConnectionTestOutcome(
                false,
                SanitizeMessage(exception)));
        }
    }

    /// <inheritdoc />
    public Task<LdapAuthenticationTestOutcome> TestAuthenticationAsync(
        LdapConnectionRuntimeSettings settings,
        string account,
        string password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var serviceConnection = CreateBoundServiceConnection(settings);
            var userDn = FindUserDn(serviceConnection, settings, account);
            if (userDn is null)
            {
                return Task.FromResult(new LdapAuthenticationTestOutcome(
                    false,
                    null,
                    "No directory user matched the supplied account."));
            }

            using var userConnection = CreateConnection(settings);
            userConnection.Bind(new System.Net.NetworkCredential(userDn, password));
            return Task.FromResult(new LdapAuthenticationTestOutcome(
                true,
                userDn,
                "User bind succeeded."));
        }
        catch (Exception exception)
        {
            return Task.FromResult(new LdapAuthenticationTestOutcome(
                false,
                null,
                SanitizeMessage(exception)));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<LdapDirectoryEntrySnapshot>> PreviewEntriesAsync(
        LdapConnectionRuntimeSettings settings,
        string searchBaseDn,
        int maxEntries,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = CreateBoundServiceConnection(settings);
        var request = new SearchRequest(
            searchBaseDn,
            "(|(objectClass=user)(objectClass=person)(objectClass=inetOrgPerson)(objectClass=organizationalUnit))",
            SearchScope.OneLevel,
            BuildPreviewAttributes(settings));
        var response = (SearchResponse)connection.SendRequest(request);
        var entries = new List<LdapDirectoryEntrySnapshot>(Math.Min(maxEntries, response.Entries.Count));
        foreach (SearchResultEntry entry in response.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entries.Count >= maxEntries)
            {
                break;
            }

            entries.Add(MapPreviewEntry(entry, settings));
        }

        return Task.FromResult<IReadOnlyList<LdapDirectoryEntrySnapshot>>(entries);
    }

    private static LdapConnection CreateBoundServiceConnection(LdapConnectionRuntimeSettings settings)
    {
        var connection = CreateConnection(settings);
        connection.Bind(new System.Net.NetworkCredential(settings.BindDn, settings.BindPassword));
        return connection;
    }

    private static LdapConnection CreateConnection(LdapConnectionRuntimeSettings settings)
    {
        var identifier = new LdapDirectoryIdentifier(
            settings.Host,
            settings.Port,
            fullyQualifiedDnsHostName: false,
            connectionless: false);
        var connection = new LdapConnection(identifier)
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        connection.SessionOptions.ProtocolVersion = 3;
        if (settings.UseTls)
        {
            connection.SessionOptions.SecureSocketLayer = true;
        }

        return connection;
    }

    private static string? FindUserDn(
        LdapConnection connection,
        LdapConnectionRuntimeSettings settings,
        string account)
    {
        var filter = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            settings.UserSearchFilter,
            EscapeFilterValue(account));
        var request = new SearchRequest(
            settings.BaseDn,
            filter,
            SearchScope.Subtree,
            "distinguishedName");
        var response = (SearchResponse)connection.SendRequest(request);
        if (response.Entries.Count == 0)
        {
            return null;
        }

        return response.Entries[0].DistinguishedName;
    }

    private static string[] BuildPreviewAttributes(LdapConnectionRuntimeSettings settings)
    {
        var attributes = new List<string>
        {
            "distinguishedName",
            "objectClass",
            "displayName",
            "mail",
            settings.UserAccountAttribute,
        };
        if (!string.IsNullOrWhiteSpace(settings.EmployeeIdAttribute))
        {
            attributes.Add(settings.EmployeeIdAttribute);
        }

        if (!string.IsNullOrWhiteSpace(settings.DepartmentCodeAttribute))
        {
            attributes.Add(settings.DepartmentCodeAttribute);
        }

        return attributes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static LdapDirectoryEntrySnapshot MapPreviewEntry(
        SearchResultEntry entry,
        LdapConnectionRuntimeSettings settings)
    {
        var objectClasses = ReadAttributeValues(entry, "objectClass");
        var entryKind = objectClasses.Any(value =>
                value.Contains("organizationalUnit", StringComparison.OrdinalIgnoreCase))
            ? LdapSyncPreviewEntryKinds.OrganizationalUnit
            : LdapSyncPreviewEntryKinds.User;
        var account = ReadFirstAttribute(entry, settings.UserAccountAttribute);
        var departmentCode = string.IsNullOrWhiteSpace(settings.DepartmentCodeAttribute)
            ? null
            : ReadFirstAttribute(entry, settings.DepartmentCodeAttribute);
        return new LdapDirectoryEntrySnapshot(
            entry.DistinguishedName,
            entryKind,
            entryKind == LdapSyncPreviewEntryKinds.User ? account : null,
            ReadFirstAttribute(entry, "displayName"),
            ReadFirstAttribute(entry, "mail"),
            departmentCode);
    }

    private static string? ReadFirstAttribute(SearchResultEntry entry, string attributeName)
    {
        if (!entry.Attributes.Contains(attributeName))
        {
            return null;
        }

        var value = entry.Attributes[attributeName][0];
        return value switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string text => text,
            _ => value?.ToString(),
        };
    }

    private static IReadOnlyList<string> ReadAttributeValues(SearchResultEntry entry, string attributeName)
    {
        if (!entry.Attributes.Contains(attributeName))
        {
            return [];
        }

        var values = new List<string>(entry.Attributes[attributeName].Count);
        foreach (var value in entry.Attributes[attributeName])
        {
            values.Add(value switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                string text => text,
                _ => value?.ToString() ?? string.Empty,
            });
        }

        return values;
    }

    private static string EscapeFilterValue(string value) =>
        value
            .Replace("\\", "\\5c", StringComparison.Ordinal)
            .Replace("*", "\\2a", StringComparison.Ordinal)
            .Replace("(", "\\28", StringComparison.Ordinal)
            .Replace(")", "\\29", StringComparison.Ordinal)
            .Replace("\0", "\\00", StringComparison.Ordinal);

    private static string SanitizeMessage(Exception exception) =>
        exception.InnerException?.Message ?? exception.Message;
}
