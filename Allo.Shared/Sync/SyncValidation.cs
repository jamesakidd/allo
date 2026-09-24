using Allo.Shared.Models;

namespace Allo.Shared.Sync;

// Field rules checked by the server on every push, and usable by the client before it
// saves, so a rejected push is the exception. References to other rows are checked by
// the server only, since it's the one that knows what exists.
public static class SyncValidation
{
    public const int NameMaxLength = 100;
    public const int ItemNameMaxLength = 200;
    public const int TitleMaxLength = 200;
    public const int NoteMaxLength = 500;
    public const int TagMaxLength = 50;

    public static string? Validate(Category category) => Name(category.Name, NameMaxLength);

    public static string? Validate(Store store) =>
        Name(store.Name, NameMaxLength) ?? Colour(store.Color);

    public static string? Validate(ShoppingList list) => Name(list.Name, NameMaxLength);

    public static string? Validate(StoreCategoryOrder order) => null;

    public static string? Validate(TaskList list) => Name(list.Name, NameMaxLength);

    public static string? Validate(TaskEntry task)
    {
        if (Name(task.Title, TitleMaxLength, "Title") is { } titleError)
        {
            return titleError;
        }
        if (!Enum.IsDefined(task.Priority))
        {
            return "Unknown priority.";
        }
        return task.Note is { Length: > NoteMaxLength }
            ? $"Note must be at most {NoteMaxLength} characters."
            : null;
    }

    public static string? Validate(Item item) =>
        Name(item.Name, ItemNameMaxLength) ?? UnitDefined(item.DefaultUnit);

    public static string? Validate(ListEntry entry)
    {
        if (UnitDefined(entry.Unit) is { } unitError)
        {
            return unitError;
        }
        if (entry.Quantity <= 0)
        {
            return "Quantity must be more than zero.";
        }
        if (entry.Unit.IsCountUnit() && entry.Quantity != decimal.Truncate(entry.Quantity))
        {
            return $"Quantity must be a whole number for {entry.Unit.ToCode()}.";
        }
        if (entry.Note is { Length: > NoteMaxLength })
        {
            return $"Note must be at most {NoteMaxLength} characters.";
        }
        return entry.Tags.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > TagMaxLength)
            ? $"Tags must be 1 to {TagMaxLength} characters."
            : null;
    }

    // Only a plain "#rrggbb": the value goes straight into a style attribute.
    private static string? Colour(string? colour) =>
        colour is null || (colour.Length == 7 && colour[0] == '#'
            && colour[1..].All(Uri.IsHexDigit))
            ? null
            : "Colour must look like #66bb6a.";

    private static string? Name(string? name, int maxLength, string label = "Name") =>
        string.IsNullOrWhiteSpace(name) ? $"{label} is required."
        : name.Trim().Length > maxLength ? $"{label} must be at most {maxLength} characters."
        : null;

    private static string? UnitDefined(Unit unit) =>
        Enum.IsDefined(unit) ? null : "Unknown unit.";
}
