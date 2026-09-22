using MudBlazor;

namespace Allo.Client.Lists;

public static class Prompts
{
    // A one-field rename dialog, used by stores, lists and categories.
    public static async Task<string?> RenameAsync(IDialogService dialogs, string title, string current)
    {
        var parameters = new DialogParameters<RenameDialog>
        {
            { x => x.Value, current },
        };
        var dialog = await dialogs.ShowAsync<RenameDialog>(title, parameters,
            new DialogOptions { FullWidth = true, MaxWidth = MaxWidth.ExtraSmall });
        var result = await dialog.Result;
        return result is { Canceled: false, Data: string name } && !string.IsNullOrWhiteSpace(name)
            ? name.Trim()
            : null;
    }
}
