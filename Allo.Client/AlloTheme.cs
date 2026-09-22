using MudBlazor;

namespace Allo.Client;

public static class AlloTheme
{
    // System fonts only: nothing to download, and it works offline.
    private static readonly string[] SystemFonts =
        ["system-ui", "-apple-system", "Segoe UI", "Roboto", "Helvetica Neue", "Arial", "sans-serif"];

    public static readonly MudTheme Theme = new()
    {
        PaletteDark = new PaletteDark
        {
            Primary = "#66bb6a",
            Secondary = "#ffb74d",
            AppbarBackground = "#1b1d21",
            Background = "#121316",
            Surface = "#1b1d21",
            DrawerBackground = "#1b1d21",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = SystemFonts },
        },
    };
}
