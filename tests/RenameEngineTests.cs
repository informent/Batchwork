using Batchwork;
var root = Path.Combine(Path.GetTempPath(), "openrename-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); var a = Path.Combine(root, "draft-one.txt"); var b = Path.Combine(root, "draft-two.txt"); File.WriteAllText(a, "a"); File.WriteAllText(b, "b"); var plan = RenameEngine.Preview(new[] { a, b }, "draft", "final"); if (plan.Count != 2 || !Path.GetFileName(plan[0].ProposedPath).StartsWith("final")) throw new Exception("Preview failed.");
if (!RenameEngine.TryPreview(new[] { a, b }, "draft", "final", out var validPreview, out _) || validPreview.Count != 2) throw new Exception("Valid preview was not accepted.");
if (RenameEngine.TryPreview(new[] { a, b }, "", "invalid", out var invalidPreview, out var previewError) || invalidPreview.Count != 0 || string.IsNullOrWhiteSpace(previewError)) throw new Exception("Invalid preview retained executable rename operations.");
RenameEngine.Execute(plan); if (!File.Exists(Path.Combine(root, "final-one.txt"))) throw new Exception("Execution failed."); RenameEngine.Execute(plan.Select(x => new RenameItem(x.ProposedPath, x.OriginalPath))); if (!File.Exists(a)) throw new Exception("Undo failed."); Directory.Delete(root, true); Console.WriteLine("PASS: rename preview, invalid-preview clearing, execution, and undo");
var conflictRoot = Path.Combine(Path.GetTempPath(), "openrename-conflict-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(conflictRoot); var c1 = Path.Combine(conflictRoot, "one.txt"); var c2 = Path.Combine(conflictRoot, "two.txt"); File.WriteAllText(c1, "1"); File.WriteAllText(c2, "2"); var conflict = new[] { new RenameItem(c1, Path.Combine(conflictRoot, "same.txt")), new RenameItem(c2, Path.Combine(conflictRoot, "same.txt")) }; try { RenameEngine.Validate(conflict); throw new Exception("Duplicate destination was not rejected."); } catch (InvalidOperationException) { } if (!File.Exists(c1) || !File.Exists(c2)) throw new Exception("Conflict validation changed files."); Directory.Delete(conflictRoot, true); Console.WriteLine("PASS: duplicate destination protection");

var swapRoot = Path.Combine(Path.GetTempPath(), "openrename-swap-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(swapRoot); var left = Path.Combine(swapRoot, "left.txt"); var right = Path.Combine(swapRoot, "right.txt"); File.WriteAllText(left, "LEFT"); File.WriteAllText(right, "RIGHT");
RenameEngine.Execute(new[] { new RenameItem(left, right), new RenameItem(right, left) });
if (File.ReadAllText(left) != "RIGHT" || File.ReadAllText(right) != "LEFT") throw new Exception("Transactional swap failed.");
if (Directory.EnumerateFiles(swapRoot, ".openrename-*.tmp").Any()) throw new Exception("Temporary rename files were left behind.");
try { RenameEngine.Preview(new[] { left }, "", "x"); throw new Exception("Empty find text was accepted."); } catch (ArgumentException) { }
Directory.Delete(swapRoot, true); Console.WriteLine("PASS: transactional swaps and temporary-file cleanup");

var journalRoot = Path.Combine(Path.GetTempPath(), "openrename-journal-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(journalRoot); var oldPath = Path.Combine(journalRoot, "before.txt"); var newPath = Path.Combine(journalRoot, "after.txt"); var journalPath = Path.Combine(journalRoot, "journal.json"); File.WriteAllText(oldPath, "data"); var journalPlan = new[] { new RenameItem(oldPath, newPath) }; var journal = new RenameJournal(journalPath);
journal.Prepare(journalPlan); if (journal.LoadUndoPlan().Count != 0) throw new Exception("Prepared but unapplied journal offered unsafe undo."); RenameEngine.Execute(journalPlan);
var recoveredUndo = new RenameJournal(journalPath).LoadUndoPlan(); if (recoveredUndo.Count != 1 || recoveredUndo[0].OriginalPath != newPath) throw new Exception("Completed rename was not recovered after restart."); RenameEngine.Execute(recoveredUndo); journal.Clear();
if (!File.Exists(oldPath) || File.Exists(journalPath)) throw new Exception("Recovered undo or journal cleanup failed.");
File.WriteAllText(journalPath, "damaged"); try { journal.LoadUndoPlan(); throw new Exception("Damaged journal was accepted."); } catch (InvalidDataException) { }
Directory.Delete(journalRoot, true); Console.WriteLine("PASS: persistent undo recovery, unapplied-state safety, and damaged-journal rejection");

var verifiedRoot = Path.Combine(Path.GetTempPath(), "batchwork-verified-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(verifiedRoot);
try
{
    var one = Path.Combine(verifiedRoot, "one.txt"); var two = Path.Combine(verifiedRoot, "two.txt");
    var verifiedJournal = new RenameJournal(Path.Combine(verifiedRoot, "journal.json"));
    File.WriteAllText(one, "ONE"); File.WriteAllText(two, "TWO");
    var swap = new[] { new RenameItem(one, two), new RenameItem(two, one) };
    verifiedJournal.Prepare(swap);
    if (verifiedJournal.LoadUndoPlan().Count != 0) throw new Exception("Unapplied swap offered undo.");
    RenameEngine.Execute(swap); verifiedJournal.Complete();
    RenameEngine.Execute(new RenameJournal(Path.Combine(verifiedRoot, "journal.json")).LoadUndoPlan());
    if (File.ReadAllText(one) != "ONE" || File.ReadAllText(two) != "TWO") throw new Exception("Persistent swap undo changed content.");
    var casePlan = new[] { new RenameItem(one, Path.Combine(verifiedRoot, "ONE.txt")) };
    verifiedJournal.Prepare(casePlan); RenameEngine.Execute(casePlan); verifiedJournal.Complete();
    RenameEngine.Execute(verifiedJournal.LoadUndoPlan());
    if (!Directory.GetFiles(verifiedRoot).Any(x => Path.GetFileName(x) == "one.txt")) throw new Exception("Case-only undo failed.");
    var move = new[] { new RenameItem(one, Path.Combine(verifiedRoot, "renamed.txt")) };
    verifiedJournal.Prepare(move); RenameEngine.Execute(move); verifiedJournal.Complete();
    File.WriteAllText(move[0].ProposedPath, "replaced content");
    try { verifiedJournal.LoadUndoPlan(); throw new Exception("Modified content was offered for undo."); } catch (InvalidDataException) { }
    File.WriteAllText(one, "source");
    foreach (var name in new[] { "CON.txt", "LPT1.log", "trailing.", "trailing ", "bad:name.txt" })
    {
        try { RenameEngine.Validate(new[] { new RenameItem(one, Path.Combine(verifiedRoot, name)) }); throw new Exception("Unsafe filename was accepted: " + name); } catch (InvalidOperationException) { }
    }
    try { RenameEngine.Preview(new[] { one }, "one", "../outside").ToList().ForEach(x => RenameEngine.Validate(new[] { x })); throw new Exception("Folder escape was accepted."); } catch (InvalidOperationException) { }
    var folderCollision = Path.Combine(verifiedRoot, "folder"); Directory.CreateDirectory(folderCollision);
    try { RenameEngine.Validate(new[] { new RenameItem(one, folderCollision) }); throw new Exception("Directory collision was accepted."); } catch (IOException) { }
    File.WriteAllText(Path.Combine(verifiedRoot, "journal.json"), "{\"Items\":null}");
    try { verifiedJournal.LoadUndoPlan(); throw new Exception("Null journal items were accepted."); } catch (InvalidDataException) { }
    Console.WriteLine("PASS: verified persistent swaps, case-only undo, changed-file protection, Windows names, folder containment, and malformed journals");
}
finally { Directory.Delete(verifiedRoot, true); }
