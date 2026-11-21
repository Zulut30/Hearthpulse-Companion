using System.Reflection;
using Hearthpulse.Core.Localization;
using Hearthpulse.UI.Main;
using Hearthpulse.UI.Overlay;
using Xunit.Sdk;

[assembly: Hearthpulse.Tests.LanguageListeners]

namespace Hearthpulse.Tests;

/// <summary>
/// After each test of the <see cref="GlobalLanguage"/> collection, <see cref="Loc.LanguageChanged"/> keeps no listener of
/// a finished test: what the test added goes, and so does every main and overlay view model's, those the parallel
/// collections left included (the Windows suite's LanguageListeners takes off what each test added). The app keeps one
/// main view model for its lifetime; a run makes hundreds, and without this each language switch in the collection makes
/// every one of them rebuild its pages on the switching thread, seconds per test late in a run.
/// Only that collection: it runs alone, after the parallel ones, so no listener left belongs to a running test.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class LanguageListenersAttribute : BeforeAfterTestAttribute
{
    // The compiler's backing field of the field-like event; a rename fails here, not silently.
    private static readonly FieldInfo Field = typeof(Loc).GetField(nameof(Loc.LanguageChanged), BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Loc.LanguageChanged has no backing field to read");

    private static Delegate[] _before = [];

    public override void Before(MethodInfo methodUnderTest)
    {
        if (InGlobalLanguage(methodUnderTest))
        {
            _before = Listeners();
        }
    }

    public override void After(MethodInfo methodUnderTest)
    {
        if (!InGlobalLanguage(methodUnderTest))
        {
            return;
        }

        foreach (var listener in Listeners().Where(l => !_before.Contains(l) || l.Target is MainViewModel or OverlayViewModel))
        {
            Loc.Instance.LanguageChanged -= (Action)listener;
        }

        _before = [];
    }

    private static Delegate[] Listeners() => (Field.GetValue(Loc.Instance) as Delegate)?.GetInvocationList() ?? [];

    private static bool InGlobalLanguage(MethodInfo method) => method.ReflectedType?.GetCustomAttributesData().Any(a =>
        a.AttributeType == typeof(CollectionAttribute) && a.ConstructorArguments is [{ Value: nameof(GlobalLanguage) }]) == true;
}
