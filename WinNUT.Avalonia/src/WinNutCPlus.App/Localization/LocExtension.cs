using Avalonia.Markup.Xaml;

namespace WinNutCPlus.App.Localization;

/// <summary>XAML markup extension: {loc:Loc SomeKey} resolves to Localize.Get("SomeKey") at load time.</summary>
public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; }

    public LocExtension() { Key = string.Empty; }

    public LocExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider) => Localize.Get(Key);
}
