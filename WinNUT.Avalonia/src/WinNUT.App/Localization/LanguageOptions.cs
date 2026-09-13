namespace WinNUT.App.Localization;

/// <summary>
/// Ordered list of languages selectable from Preferences &gt; Miscellaneous. Index 0 means "follow
/// the Windows UI language" (the original app's only behavior). Order must match the Language
/// ComboBox items in PreferencesWindow.axaml — both are index-driven, same convention as the
/// other combo-backed settings (StopType, Branch, etc.).
/// </summary>
public static class LanguageOptions
{
    public static readonly (string Code, string DisplayName)[] All =
    {
        ("", ""), // display name resolved at runtime via the LanguageSystemDefault resx key
        ("en", "English"),
        ("de-DE", "Deutsch"),
        ("fr-FR", "Français"),
        ("pl-PL", "Polski"),
        ("ru-RU", "Русский"),
        ("uk-UA", "Українська"),
        ("zh-CN", "简体中文"),
        ("zh-TW", "繁體中文"),
    };

    public static string CodeAt(int index) => index >= 0 && index < All.Length ? All[index].Code : string.Empty;
}
