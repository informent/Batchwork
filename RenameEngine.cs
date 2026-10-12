using System.IO;

namespace Batchwork;

public sealed record RenameItem(string OriginalPath, string ProposedPath);

public static class RenameEngine
{
    public static bool TryPreview(IEnumerable<string> paths, string find, string replace, out IReadOnlyList<RenameItem> plan, out string? error)
    {
        try
        {
            plan = Validate(Preview(paths, find, replace));
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            plan = Array.Empty<RenameItem>();
            error = ex.Message;
            return false;
        }
    }

    public static IReadOnlyList<RenameItem> Preview(IEnumerable<string> paths, string find, string replace)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (string.IsNullOrEmpty(find)) throw new ArgumentException("Find text cannot be empty.", nameof(find));
        return paths.Select(path => Path.GetFullPath(path)).Select(path => new RenameItem(path, Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileName(path).Replace(find, replace, StringComparison.Ordinal)))).ToArray();
    }

    public static IReadOnlyList<RenameItem> Validate(IEnumerable<RenameItem> items)
    {
        var list = items.ToArray();
        if (list.Length == 0) return list;
        var sourceSet = new HashSet<string>(list.Select(x => Path.GetFullPath(x.OriginalPath)), StringComparer.OrdinalIgnoreCase);
        if (sourceSet.Count != list.Length) throw new InvalidOperationException("The same source file appears more than once.");
        var destinationSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in list)
        {
            var source = Path.GetFullPath(item.OriginalPath); var destination = Path.GetFullPath(item.ProposedPath);
            var name = Path.GetFileName(item.ProposedPath);
            if (!Path.GetDirectoryName(source)!.Equals(Path.GetDirectoryName(destination), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Renames must stay in the original folder.");
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name.EndsWith(' ') || name is "." or "..") throw new InvalidOperationException("A proposed filename is not valid on Windows.");
            var stem = name.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" || System.Text.RegularExpressions.Regex.IsMatch(stem, @"^(COM|LPT)[1-9¹²³]$")) throw new InvalidOperationException("A proposed filename is reserved by Windows.");
            if (Directory.Exists(destination)) throw new IOException("A folder already uses a proposed filename.");
            if (!File.Exists(source)) throw new FileNotFoundException("A source file no longer exists.", source);
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked files cannot be renamed in a batch.");
            if (string.IsNullOrWhiteSpace(Path.GetFileName(destination))) throw new InvalidOperationException("A proposed filename is empty.");
            if (!destinationSet.Add(destination)) throw new InvalidOperationException("Two or more files would receive the same name.");
            if (!source.Equals(destination, StringComparison.OrdinalIgnoreCase) && File.Exists(destination) && !sourceSet.Contains(destination)) throw new IOException($"A proposed filename already exists: {Path.GetFileName(destination)}");
        }
        return list;
    }

    public static void Execute(IEnumerable<RenameItem> items)
    {
        var changes = Validate(items).Where(x => !Path.GetFullPath(x.OriginalPath).Equals(Path.GetFullPath(x.ProposedPath), StringComparison.Ordinal)).ToArray();
        var staged = new List<(RenameItem Item, string Temporary)>();
        var completed = new List<(RenameItem Item, string Temporary)>();
        try
        {
            foreach (var item in changes)
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(item.OriginalPath))!;
                string temporary;
                do temporary = Path.Combine(directory, $".openrename-{Guid.NewGuid():N}.tmp"); while (File.Exists(temporary));
                File.Move(item.OriginalPath, temporary);
                staged.Add((item, temporary));
            }
            foreach (var entry in staged)
            {
                File.Move(entry.Temporary, entry.Item.ProposedPath);
                completed.Add(entry);
            }
        }
        catch (Exception failure)
        {
            var rollbackErrors = new List<Exception>();
            foreach (var entry in completed.AsEnumerable().Reverse())
                try { if (File.Exists(entry.Item.ProposedPath)) File.Move(entry.Item.ProposedPath, entry.Temporary); } catch (Exception ex) { rollbackErrors.Add(ex); }
            foreach (var entry in staged.AsEnumerable().Reverse())
                try { if (File.Exists(entry.Temporary) && !File.Exists(entry.Item.OriginalPath)) File.Move(entry.Temporary, entry.Item.OriginalPath); } catch (Exception ex) { rollbackErrors.Add(ex); }
            if (rollbackErrors.Count > 0) throw new AggregateException("Rename failed and rollback was incomplete.", new[] { failure }.Concat(rollbackErrors));
            throw;
        }
    }
}
