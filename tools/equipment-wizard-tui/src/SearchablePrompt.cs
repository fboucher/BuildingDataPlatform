using Spectre.Console;

namespace EquipmentWizardTui;

public static class SearchablePrompt
{
    public static T SelectWithSearch<T>(
        IReadOnlyList<T> items,
        string title,
        Func<T, string> displaySelector,
        int pageSize = 10) where T : notnull
    {
        if (items.Count == 0)
        {
            throw new InvalidOperationException("No items to select from.");
        }

        return AnsiConsole.Prompt(
            new SelectionPrompt<T>()
                .Title(title)
                .AddChoices(items)
                .EnableSearch()
                .SearchPlaceholderText("[grey](type to filter, case-insensitive)[/]")
                .MoreChoicesText("[grey](Use arrow keys and type to filter)[/]")
                .UseConverter(displaySelector)
                .PageSize(pageSize));
    }
}
