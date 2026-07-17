using System.Globalization;
using Full.NET.Localization;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Localization;

[TestClass]
public sealed class CultureScopeTests
{
    [TestMethod]
    public void Push_sets_both_cultures_and_restores_the_callers_cultures()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        using (CultureScope.Push("en-US"))
        {
            Assert.AreEqual("en-US", CultureInfo.CurrentCulture.Name);
            Assert.AreEqual("en-US", CultureInfo.CurrentUICulture.Name);
        }

        Assert.AreEqual(originalCulture, CultureInfo.CurrentCulture);
        Assert.AreEqual(originalUiCulture, CultureInfo.CurrentUICulture);
    }

    [TestMethod]
    public async Task Push_isolated_parallel_async_flows_and_restores_each_caller()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");

        try
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var ready = new CountdownEvent(2);

            var chinese = Task.Run(() => ObserveScopeAsync("zh-CN", ready, release.Task));
            var english = Task.Run(() => ObserveScopeAsync("en-US", ready, release.Task));

            Assert.IsTrue(ready.Wait(TimeSpan.FromSeconds(5)));
            release.SetResult();

            var results = await Task.WhenAll(chinese, english);

            Assert.AreEqual(("zh-CN", "de-DE"), results[0]);
            Assert.AreEqual(("en-US", "de-DE"), results[1]);
            Assert.AreEqual("de-DE", CultureInfo.CurrentCulture.Name);
            Assert.AreEqual("de-DE", CultureInfo.CurrentUICulture.Name);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [TestMethod]
    public void Locale_context_exposes_the_normalized_current_ui_culture()
    {
        var normalizer = new LocaleNormalizer(
            Options.Create(new FullNetLocalizationOptions()));
        var context = new LocaleContext(normalizer);

        using (CultureScope.Push("en-GB"))
        {
            Assert.AreEqual("en-US", context.CurrentLocale);
        }
    }

    private static async Task<(string During, string After)> ObserveScopeAsync(
        string locale,
        CountdownEvent ready,
        Task release)
    {
        string during;
        using (CultureScope.Push(locale))
        {
            ready.Signal();
            await release;
            await Task.Yield();
            during = CultureInfo.CurrentUICulture.Name;
        }

        return (during, CultureInfo.CurrentUICulture.Name);
    }
}
