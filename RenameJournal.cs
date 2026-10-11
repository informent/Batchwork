using System.IO;
using System.Text.Json;

namespace Batchwork;

public sealed record RenameJournalEntry(DateTimeOffset CreatedUtc, IReadOnlyList<RenameItem> Items);

public sealed class RenameJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string path;

    public RenameJournal(string? storagePath = null) => path = storagePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenRename", "last-rename.json");

    public void Prepare(IReadOnlyList<RenameItem> plan)
    {
        var validated = RenameEngine.Validate(plan).Where(x => !Path.GetFullPath(x.OriginalPath).Equals(Path.GetFullPath(x.ProposedPath), StringComparison.Ordinal)).ToArray();
        if (validated.Length == 0) throw new InvalidOperationException("There are no filename changes to journal.");
        Write(new RenameJournalEntry(DateTimeOffset.UtcNow, validated));
    }

    public IReadOnlyList<RenameItem> LoadUndoPlan()
    {
        if (!File.Exists(path)) return Array.Empty<RenameItem>();
        RenameJournalEntry entry;
        try { entry = JsonSerializer.Deserialize<RenameJournalEntry>(File.ReadAllText(path)) ?? throw new InvalidDataException("Rename journal is empty."); }
        catch (JsonException ex) { throw new InvalidDataException("Rename journal is damaged.", ex); }
        if (entry.Items.Count == 0) throw new InvalidDataException("Rename journal contains no files.");
        var applied = entry.Items.Count(item => File.Exists(item.ProposedPath) && !File.Exists(item.OriginalPath));
        var untouched = entry.Items.Count(item => File.Exists(item.OriginalPath) && !File.Exists(item.ProposedPath));
        if (applied == entry.Items.Count) return RenameEngine.Validate(entry.Items.Select(x => new RenameItem(x.ProposedPath, x.OriginalPath)));
        if (untouched == entry.Items.Count) return Array.Empty<RenameItem>();
        throw new InvalidDataException("Rename journal does not match a complete filesystem state; automatic undo is disabled.");
    }

    public void Clear() { if (File.Exists(path)) File.Delete(path); }

    private void Write(RenameJournalEntry entry)
    {
        var directory = Path.GetDirectoryName(path)!; Directory.CreateDirectory(directory); var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(entry, JsonOptions)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
