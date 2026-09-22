using Allo.Shared.Models;

namespace Allo.Shared.Lists;

public static class CategoryTree
{
    // Parents in their sort order, each followed by its children. Uncategorized last,
    // wherever its sort order happens to sit.
    public static IReadOnlyList<Category> Ordered(IEnumerable<Category> categories)
    {
        var alive = categories.Where(c => !c.IsDeleted).ToList();
        var ordered = new List<Category>();

        void AddChildren(Guid? parentId)
        {
            foreach (var category in alive
                .Where(c => c.ParentId == parentId && c.Id != Category.UncategorizedId)
                .OrderBy(c => c.SortOrder).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                ordered.Add(category);
                AddChildren(category.Id);
            }
        }

        AddChildren(null);
        // A category whose parent was deleted still has to show up somewhere.
        ordered.AddRange(alive.Where(c => c.Id != Category.UncategorizedId && !ordered.Contains(c))
            .OrderBy(c => c.SortOrder));
        ordered.AddRange(alive.Where(c => c.Id == Category.UncategorizedId));
        return ordered;
    }
}
