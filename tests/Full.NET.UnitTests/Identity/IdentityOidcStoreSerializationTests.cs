using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Full.NET.Modules.Identity.Oidc;
using OpenIddict.Abstractions;

namespace Full.NET.UnitTests.Identity;

/// <summary>静态序列化迁移后保持协议持久化格式、扩展属性和客户端分类语义。</summary>
[TestClass]
public sealed class IdentityOidcStoreSerializationTests
{
    [TestMethod]
    public void String_collections_round_trip_without_changing_keys()
    {
        var scopes = ImmutableArray.Create("openid", "profile", "orders.read");
        CollectionAssert.AreEqual(scopes.ToArray(),
            IdentityOidcStoreSupport.DeserializeStringArray(IdentityOidcStoreSupport.SerializeStringArray(scopes)).ToArray());
        Assert.AreEqual("[]", IdentityOidcStoreSupport.SerializeStringArray(default));
        var settings = ImmutableDictionary<string, string>.Empty.Add("MixedCase", "保留值");
        var restored = IdentityOidcStoreSupport.DeserializeSettings(IdentityOidcStoreSupport.SerializeSettings(settings));
        Assert.AreEqual("保留值", restored["MixedCase"]);
        var names = ImmutableDictionary<CultureInfo, string>.Empty.Add(CultureInfo.GetCultureInfo("zh-CN"), "管理系统");
        Assert.AreEqual("管理系统", IdentityOidcStoreSupport.DeserializeDisplayNames(
            IdentityOidcStoreSupport.SerializeDisplayNames(names))[CultureInfo.GetCultureInfo("zh-CN")]);
    }

    [TestMethod]
    public void Session_property_updates_preserve_nested_extension_values()
    {
        const string original = "{\"Custom\":{\"Number\":7,\"Enabled\":true}}";
        var updated = IdentityOidcStoreSupport.WriteSessionId(original, "session-id");
        Assert.AreEqual("session-id", IdentityOidcStoreSupport.ReadSessionId(updated));
        var cleared = IdentityOidcStoreSupport.WriteSessionId(updated, null);
        Assert.IsNull(IdentityOidcStoreSupport.ReadSessionId(cleared));
        using var document = JsonDocument.Parse(cleared);
        Assert.AreEqual(7, document.RootElement.GetProperty("Custom").GetProperty("Number").GetInt32());
        Assert.IsTrue(document.RootElement.GetProperty("Custom").GetProperty("Enabled").GetBoolean());
    }

    [TestMethod]
    public void Client_metadata_keeps_boolean_types_and_trimmed_audience()
    {
        var descriptor = new OpenIddictApplicationDescriptor();
        IdentityOidcClientMetadata.Write(descriptor, false, " api-a ", true);
        Assert.AreEqual(JsonValueKind.False, descriptor.Properties[IdentityOidcClientMetadata.IsFirstPartyKey].ValueKind);
        var restored = IdentityOidcClientMetadata.Read(descriptor.Properties.ToImmutableDictionary());
        Assert.IsFalse(restored.IsFirstParty);
        Assert.IsTrue(restored.IsDisabled);
        Assert.AreEqual("api-a", restored.ResourceAudience);
        IdentityOidcClientMetadata.Write(descriptor, true, null, false);
        Assert.IsFalse(descriptor.Properties.ContainsKey(IdentityOidcClientMetadata.ResourceAudienceKey));
    }
}
