using Avalonia.Controls;
using Avalonia.Platform;

namespace WinNutCPlus.App.Services;

/// <summary>
/// AppIconIdx flag combinations, ported from Common_Enums.vb. Bitwise-ORed together and passed
/// to <see cref="IconProvider.GetIcon"/> to select one of the pre-rendered .ico assets carried
/// over from the original app (WinNUT_V2/images/Ico).
/// </summary>
[Flags]
public enum AppIconIdx
{
    IDX_BATT_0 = 1,
    IDX_BATT_25 = 2,
    IDX_BATT_50 = 4,
    IDX_BATT_75 = 8,
    IDX_BATT_100 = 16,
    IDX_OL = 32,
    WIN_DARK = 64,
    IDX_ICO_OFFLINE = 128,
    IDX_ICO_RETRY = 256,
    IDX_OFFSET = 1024,
}

/// <summary>
/// Resolves an <see cref="AppIconIdx"/> bit combination to one of the pre-rendered .ico assets.
/// Ported from WinNUT.vb's GetIcon — same index table, including the original's 1104→1096
/// duplicate mapping (kept for visual parity rather than "fixed", since it's unclear whether
/// that was intentional in the source assets).
/// </summary>
public static class IconProvider
{
    private static readonly int[] KnownIndices =
    {
        1025, 1026, 1028, 1032, 1040, 1057, 1058, 1060, 1064, 1072,
        1079, 1080, 1092, 1096, 1104, 1121, 1122, 1124, 1128, 1136,
        1152, 1216, 1280, 1344,
    };

    private static readonly Dictionary<int, WindowIcon> Cache = new();

    public static WindowIcon GetIcon(int iconIdx)
    {
        var resolvedIdx = KnownIndices.Contains(iconIdx) ? iconIdx : 1136; // default/Case Else
        if (resolvedIdx == 1104) resolvedIdx = 1096; // original's duplicate mapping, preserved

        if (Cache.TryGetValue(resolvedIdx, out var cached)) return cached;

        var uri = new Uri($"avares://WinNutCPlus/Assets/Icons/{resolvedIdx}.ico");
        using var stream = AssetLoader.Open(uri);
        var icon = new WindowIcon(stream);
        Cache[resolvedIdx] = icon;
        return icon;
    }

    public static WindowIcon BaseAppIcon
    {
        get
        {
            const int key = -1;
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var uri = new Uri("avares://WinNutCPlus/Assets/Icons/WinNut.ico");
            using var stream = AssetLoader.Open(uri);
            var icon = new WindowIcon(stream);
            Cache[key] = icon;
            return icon;
        }
    }
}
