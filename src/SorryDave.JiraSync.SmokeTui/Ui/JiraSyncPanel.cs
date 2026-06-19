using SorryDave.JiraSync.SmokeTui.Api;
using Terminal.Gui;

namespace SorryDave.JiraSync.SmokeTui.Ui;

/// <summary>
/// Drives the jira-sync-core happy path through the API: backfill, simulate an inbound webhook,
/// submit a write-back, and view work items, the outbox, and Jira comments. Write-back and the
/// webhook simulation target the work item currently selected in the list. All calls go over
/// HTTP off the UI thread, with results marshalled back via the main loop.
/// </summary>
public class JiraSyncPanel : View
{
    private readonly IApiClient _api;
    private readonly ListView _workItems = new() { AllowsMarking = false };
    private readonly ListView _outbox = new() { AllowsMarking = false };
    private readonly ListView _comments = new() { AllowsMarking = false };

    // The work items currently shown, index-aligned with the _workItems list view, so the
    // selected row maps back to a real work item.
    private List<WorkItemDto> _items = new();

    public JiraSyncPanel(IApiClient api)
    {
        _api = api;

        var backfill = new Button("_Backfill") { X = 0, Y = 0 };
        backfill.Clicked += () => RunInBackground(ct => _api.BackfillAsync(ct), "Backfill");

        var webhook = new Button("Simulate _webhook") { X = Pos.Right(backfill) + 1, Y = 0 };
        webhook.Clicked += () =>
        {
            var target = SelectedItem();
            if (target is null) { NoSelection("Simulate webhook"); return; }
            RunInBackground(ct => _api.SimulateWebhookAsync(target, ct), "Simulate webhook");
        };

        var write = new Button("_Submit write-back") { X = Pos.Right(webhook) + 1, Y = 0 };
        write.Clicked += SubmitWriteBack;

        var refresh = new Button("_Refresh") { X = Pos.Right(write) + 1, Y = 0 };
        refresh.Clicked += Refresh;

        var wiLabel = new Label("Work items (select one, then Submit write-back / Simulate webhook):") { X = 0, Y = 2 };
        _workItems.X = 0; _workItems.Y = 3; _workItems.Width = Dim.Fill(); _workItems.Height = Dim.Percent(34);

        var obLabel = new Label("Write-back outbox:") { X = 0, Y = Pos.Bottom(_workItems) };
        _outbox.X = 0; _outbox.Y = Pos.Bottom(obLabel); _outbox.Width = Dim.Fill(); _outbox.Height = Dim.Percent(33);

        var cmLabel = new Label("Jira comments:") { X = 0, Y = Pos.Bottom(_outbox) };
        _comments.X = 0; _comments.Y = Pos.Bottom(cmLabel); _comments.Width = Dim.Fill(); _comments.Height = Dim.Fill();

        Add(backfill, webhook, write, refresh, wiLabel, _workItems, obLabel, _outbox, cmLabel, _comments);
    }

    /// <summary>The work item highlighted in the list, or null if there are none.</summary>
    private WorkItemDto? SelectedItem()
    {
        var index = _workItems.SelectedItem;
        return index >= 0 && index < _items.Count ? _items[index] : null;
    }

    /// <summary>Reload all three lists from the API.</summary>
    public void Refresh()
    {
        _ = Task.Run(async () =>
        {
            try { await LoadAsync(); }
            catch (Exception ex) { ShowError("Refresh", ex); }
        });
    }

    private async Task LoadAsync()
    {
        var items = (await _api.GetWorkItemsAsync()).ToList();
        var itemLines = items
            .Select(w => $"{w.Key}  [{w.Status}]  {w.Summary}  ({w.AssigneeDisplayName ?? "unassigned"})")
            .ToList();

        var outbox = (await _api.GetWriteBacksAsync())
            .Select(r => $"{r.Status,-9} {r.WorkItemKey}  {r.RecordIdentity}  (attempts {r.Attempts})")
            .ToList();

        var fakeComments = await _api.GetFakeCommentsAsync();
        var comments = fakeComments is null
            ? new List<string> { "(real Jira backend — view comments in the browser)" }
            : fakeComments.Select(c => $"{c.IssueKey} #{c.CommentId}: {FirstLine(c.Body)}").ToList();

        Application.MainLoop.Invoke(() =>
        {
            _items = items;
            _workItems.SetSource(itemLines.Count > 0 ? itemLines : new List<string> { "(none — click Backfill)" });
            _outbox.SetSource(outbox.Count > 0 ? outbox : new List<string> { "(none)" });
            _comments.SetSource(comments.Count > 0 ? comments : new List<string> { "(none)" });
        });
    }

    private void SubmitWriteBack()
    {
        var target = SelectedItem();
        if (target is null) { NoSelection("Submit write-back"); return; }

        var input = new TextField("") { X = 1, Y = 1, Width = Dim.Fill() - 2 };
        var submitted = false;
        var ok = new Button("Submit") { IsDefault = true };
        ok.Clicked += () => { submitted = true; Application.RequestStop(); };
        var cancel = new Button("Cancel");
        cancel.Clicked += () => Application.RequestStop();

        var dialog = new Dialog($"Write-back to {target.Key} — {target.Summary}", 70, 8, ok, cancel);
        dialog.Add(new Label("Decision text:") { X = 1, Y = 0 }, input);
        Application.Run(dialog);

        if (!submitted) return;
        var text = input.Text?.ToString();
        if (string.IsNullOrWhiteSpace(text)) text = "Smoke test decision.";

        RunInBackground(async ct =>
        {
            await _api.SubmitWriteBackAsync(target.Key,
                new WriteBackRequest($"tui-{Guid.NewGuid():N}", "Decision", text!, "tui://smoke", "SmokeTui"), ct);
            await _api.DrainWriteBackAsync(ct); // deliver now so the result is visible
        }, "Write-back");
    }

    /// <summary>Run an API call off the UI thread, then refresh; report failures in the UI.</summary>
    private void RunInBackground(Func<CancellationToken, Task> work, string label)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await work(default);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                ShowError(label, ex);
            }
        });
    }

    private static void NoSelection(string label)
        => MessageBox.ErrorQuery(label, "Select a work item in the list first (click it or use the arrow keys). Click Backfill if the list is empty.", "OK");

    private void ShowError(string label, Exception ex)
    {
        var message = ex is HttpRequestException
            ? $"Could not reach the API ({_api.BaseAddress}).\n{ex.Message}"
            : ex.Message;
        Application.MainLoop.Invoke(() => MessageBox.ErrorQuery($"{label} failed", message, "OK"));
    }

    private static string FirstLine(string text)
    {
        var line = text.Replace("\r", "").Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? text;
        return line.Length > 80 ? line[..80] : line;
    }
}
