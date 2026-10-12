using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Batchwork;

public sealed record RenameJournalEntry(DateTimeOffset CreatedUtc, IReadOnlyList<RenameItem> Items)
{
    public int Version { get; init; } = 1;
    public string[]? Fingerprints { get; init; }
    public bool Completed { get; init; }
}

public sealed class RenameJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string path;
    public RenameJournal(string? storagePath = null) => path = Path.GetFullPath(storagePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenRename", "last-rename.json"));

    public void Prepare(IReadOnlyList<RenameItem> plan)
    {
        var validated = RenameEngine.Validate(plan).Select(x => new RenameItem(Path.GetFullPath(x.OriginalPath), Path.GetFullPath(x.ProposedPath))).Where(x => x.OriginalPath != x.ProposedPath).ToArray();
        if (validated.Length == 0) throw new InvalidOperationException("There are no filename changes to journal.");
        Write(new RenameJournalEntry(DateTimeOffset.UtcNow, validated) { Version = 2, Fingerprints = validated.Select(x => Fingerprint(x.OriginalPath)).ToArray() });
    }

    public void Complete()
    {
        var entry = Read();
        if (entry.Version != 2 || !Matches(entry, false)) throw new InvalidDataException("Renamed files no longer match the prepared transaction.");
        Write(entry with { Completed = true });
    }

    public IReadOnlyList<RenameItem> LoadUndoPlan()
    {
        if (!File.Exists(path)) return Array.Empty<RenameItem>();
        var entry = Read();
        if (entry.Version == 2)
        {
            var original = Matches(entry, true);
            var proposed = Matches(entry, false);
            if (proposed && (entry.Completed || !original)) return RenameEngine.Validate(entry.Items.Select(x => new RenameItem(x.ProposedPath, x.OriginalPath)));
            if (original && !entry.Completed && !proposed) return Array.Empty<RenameItem>();
            throw new InvalidDataException("Files changed or recovery is ambiguous; automatic undo is disabled.");
        }
        // Legacy journals have no content evidence. Preserve ordinary rename recovery only.
        var applied = entry.Items.All(item => File.Exists(item.ProposedPath) && !File.Exists(item.OriginalPath));
        var untouched = entry.Items.All(item => File.Exists(item.OriginalPath) && !File.Exists(item.ProposedPath));
        if (applied) return RenameEngine.Validate(entry.Items.Select(x => new RenameItem(x.ProposedPath, x.OriginalPath)));
        if (untouched) return Array.Empty<RenameItem>();
        throw new InvalidDataException("Rename journal does not match a complete filesystem state; automatic undo is disabled.");
    }

    public void Clear() { if (File.Exists(path)) File.Delete(path); }

    private RenameJournalEntry Read()
    {
        RenameJournalEntry entry;
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Rename journal is too large.");
        try { entry = JsonSerializer.Deserialize<RenameJournalEntry>(File.ReadAllText(path)) ?? throw new InvalidDataException("Rename journal is empty."); }
        catch (JsonException ex) { throw new InvalidDataException("Rename journal is damaged.", ex); }
        if (entry.Version is not (1 or 2) || entry.Items is null || entry.Items.Count == 0 || entry.Items.Any(x => x is null || !Path.IsPathFullyQualified(x.OriginalPath) || !Path.IsPathFullyQualified(x.ProposedPath))) throw new InvalidDataException("Rename journal contains invalid files.");
        if (entry.Version == 2 && (entry.Fingerprints is null || entry.Fingerprints.Length != entry.Items.Count || entry.Fingerprints.Any(x => x is null || x.Length != 64 || !x.All(Uri.IsHexDigit)))) throw new InvalidDataException("Rename journal has invalid content evidence.");
        return entry;
    }

    private static bool Matches(RenameJournalEntry entry, bool original)
    {
        for (var index = 0; index < entry.Items.Count; index++)
        {
            var file = original ? entry.Items[index].OriginalPath : entry.Items[index].ProposedPath;
            if (!File.Exists(file) || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) return false;
            // File.Exists ignores case on Windows; the directory entry preserves the actual spelling.
            if (!Directory.EnumerateFiles(Path.GetDirectoryName(file)!).Any(x => Path.GetFileName(x).Equals(Path.GetFileName(file), StringComparison.Ordinal))) return false;
            if (Fingerprint(file) != entry.Fingerprints![index]) return false;
        }
        return true;
    }

    private static string Fingerprint(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private void Write(RenameJournalEntry entry)
    {
        var directory = Path.GetDirectoryName(path)!; Directory.CreateDirectory(directory); var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(stream, entry, JsonOptions); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
