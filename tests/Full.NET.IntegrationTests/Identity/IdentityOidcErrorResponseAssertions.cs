namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcErrorResponseAssertions
{
    private static readonly string[] ForbiddenLeakMarkers =
    [
        "StackTrace",
        "InnerException",
        " at System.",
        " at Full.NET.",
        "PrivateKeyPem",
        "BEGIN RSA PRIVATE KEY",
        "SqlException",
        "ConnectionString",
    ];

    public static void AssertDoesNotLeakInternalDetails(string body, string scenario)
    {
        foreach (var marker in ForbiddenLeakMarkers)
        {
            Assert.IsFalse(
                body.Contains(marker, StringComparison.OrdinalIgnoreCase),
                $"{scenario} must not leak internal details ({marker}).");
        }
    }
}