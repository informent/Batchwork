using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Batchwork;

public partial class MainWindow : Window
{
    private string? folder;
    private IReadOnlyList<RenameItem> plan = Array.Empty<RenameItem>();
    private IReadOnlyList<RenameItem> previous = Array.Empty<RenameItem>();
    private readonly RenameJournal journal = new();

    public MainWindow()
    {
        InitializeComponent();
        try
        {
            previous = journal.LoadUndoPlan();
            if (previous.Count > 0) StatusText.Text = $"Recovered undo for {previous.Count:N0} file(s)";
        }
        catch (Exception ex) { StatusText.Text = $"Undo history needs review: {ex.Message}"; }
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog { Description = "Choose a folder whose files you want to rename" };
        if (dialog.ShowDialog() == Forms.DialogResult.OK) { folder = dialog.SelectedPath; RebuildPreview(); }
    }
    private void RuleChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => RebuildPreview();
    private void RebuildPreview()
    {
        if (folder is null) return;
        try
        {
            if (!RenameEngine.TryPreview(Directory.EnumerateFiles(folder), FindBox.Text, ReplaceBox.Text, out plan, out var error))
            {
                PreviewList.ItemsSource = Array.Empty<string>();
                CountText.Text = "No valid preview";
                StatusText.Text = error ?? "Preview could not be created.";
                return;
            }
            PreviewList.ItemsSource = plan.Select(x => $"{Path.GetFileName(x.OriginalPath)}  →  {Path.GetFileName(x.ProposedPath)}").ToArray();
            CountText.Text = $"{plan.Count:N0} files"; StatusText.Text = "Preview ready";
        }
        catch (Exception ex) { plan = Array.Empty<RenameItem>(); PreviewList.ItemsSource = Array.Empty<string>(); CountText.Text = "No valid preview"; StatusText.Text = ex.Message; }
    }
    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (plan.Count == 0) return;
        try
        {
            journal.Prepare(plan); RenameEngine.Execute(plan); journal.Complete(); previous = journal.LoadUndoPlan();
            StatusText.Text = $"Renamed {plan.Count:N0} files · undo survives restart"; RebuildPreview();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            previous = journal.LoadUndoPlan();
            if (previous.Count == 0) { StatusText.Text = "No completed rename to undo"; return; }
            RenameEngine.Execute(previous); journal.Clear(); previous = Array.Empty<RenameItem>(); StatusText.Text = "Last rename undone"; RebuildPreview();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
}
