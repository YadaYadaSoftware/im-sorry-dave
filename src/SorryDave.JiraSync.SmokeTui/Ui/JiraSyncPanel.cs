using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SorryDave.JiraSync.Core.Domain;
using SorryDave.JiraSync.Core.Jira;
using SorryDave.JiraSync.Core.Persistence;
using SorryDave.JiraSync.Core.Sync;
using SorryDave.JiraSync.Core.WriteBack;
using Terminal.Gui;

namespace SorryDave.JiraSync.SmokeTui.Ui;

/// <summary>
/// Drives the jira-sync-core happy path: backfill, simulate an inbound webhook, submit a
/// write-back, and view work items, the outbox, and the Jira comments — refreshing after each
/// action. Service calls run off the UI thread and marshal results back via the main loop.
/// </summary>
public class JiraSyncPanel : View
{
    private readonly IServiceProvider _provider;
    private readonly ListView _workItems = new() { AllowsMarking = false };
    private readonly ListView _outbox = new() { AllowsMarking = false };
    private readonly ListView _comments = new() { AllowsMarking = false };

    public JiraSyncPanel(IServiceProvider provider)
    {
        _provider = provider;

        var backfill = new Button("_Backfill") { X = 0, Y = 0 };
        backfill.Clicked += () => RunInBackground(
            sp => sp.GetRequiredService<ReconciliationRunner>().BackfillAsync(), "Backfill");

        var webhook = new Button("Simulate _webhook") { X = Pos.Right(backfill) + 1, Y = 0 };
        webhook.Clicked += SimulateWebhook;

        var write = new Button("_Submit write-back") { X = Pos.Right(webhook) + 1, Y = 0 };
        write.Clicked += SubmitWriteBack;

        var refresh = new Button("_Refresh") { X = Pos.Right(write) + 1, Y = 0 };
        refresh.Clicked += Refresh;

        var wiLabel = new Label("Work items:") { X = 0, Y = 2 };
        _workItems.X = 0; _workItems.Y = 3; _workItems.Width = Dim.Fill(); _workItems.Height = Dim.Percent(34);

        var obLabel = new Label("Write-back outbox:") { X = 0, Y = Pos.Bottom(_workItems) };
        _outbox.X = 0; _outbox.Y = Pos.Bottom(obLabel); _outbox.Width = Dim.Fill(); _outbox.Height = Dim.Percent(33);

        var cmLabel = new Label("Jira comments:") { X = 0, Y = Pos.Bottom(_outbox) };
        _comments.X = 0; _comments.Y = Pos.Bottom(cmLabel); _comments.Width = Dim.Fill(); _comments.Height = Dim.Fill();

        Add(backfill, webhook, write, refresh, wiLabel, _workItems, obLabel, _outbox, cmLabel, _comments);
    }

    /// <summary>Reload all three lists from the local store (and the fake Jira, in fake mode).</summary>
    public void Refresh()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraSyncDbContext>();

        var items = db.WorkItems.AsNoTracking().OrderBy(w => w.Key).ToList()
            .Select(w => $"{w.Key}  [{w.Status}]  {w.Summary}  ({(w.IsDeleted ? "deleted" : w.AssigneeDisplayName ?? "unassigned")})")
            .ToList();
        _workItems.SetSource(items.Count > 0 ? items : new List<string> { "(none — click Backfill)" });

        var outbox = db.WriteBackRecords.AsNoTracking().ToList()
            .OrderByDescending(r => r.UpdatedUtc)
            .Select(r => $"{r.Status,-9} {r.WorkItemKey}  {r.RecordIdentity}  (attempts {r.Attempts})")
            .ToList();
        _outbox.SetSource(outbox.Count > 0 ? outbox : new List<string> { "(none)" });

        var jira = scope.ServiceProvider.GetRequiredService<IJiraClient>();
        List<string> comments = jira is FakeJiraClient fake
            ? fake.Comments.Select(kv => $"{kv.Value.IssueKey} #{kv.Key}: {FirstLine(kv.Value.Body)}").ToList()
            : new List<string> { "(real Jira — view comments in the browser)" };
        _comments.SetSource(comments.Count > 0 ? comments : new List<string> { "(none)" });
    }

    private void SimulateWebhook()
    {
        // Build a real-shaped Jira webhook envelope that updates DAVE-1's status.
        var json = $$"""
        {
          "webhookEvent": "jira:issue_updated",
          "issue": {
            "key": "DAVE-1",
            "fields": {
              "project": { "key": "DAVE" },
              "issuetype": { "name": "Story" },
              "status": { "name": "In Review" },
              "summary": "Open the pod bay doors",
              "assignee": { "displayName": "Dave Bowman" },
              "updated": "{{DateTimeOffset.UtcNow:o}}"
            }
          }
        }
        """;

        RunInBackground(async sp =>
        {
            var processor = sp.GetRequiredService<WebhookProcessor>();
            using var doc = JsonDocument.Parse(json);
            await processor.ProcessAsync(doc.RootElement);
        }, "Simulate webhook");
    }

    private void SubmitWriteBack()
    {
        var key = FirstWorkItemKey();
        if (key is null)
        {
            MessageBox.ErrorQuery("Write-back", "No work item yet — click Backfill first.", "OK");
            return;
        }

        var input = new TextField("") { X = 1, Y = 1, Width = Dim.Fill() - 2 };
        var submitted = false;
        var ok = new Button("Submit") { IsDefault = true };
        ok.Clicked += () => { submitted = true; Application.RequestStop(); };
        var cancel = new Button("Cancel");
        cancel.Clicked += () => Application.RequestStop();

        var dialog = new Dialog($"Write-back to {key}", 64, 8, ok, cancel);
        dialog.Add(new Label("Decision text:") { X = 1, Y = 0 }, input);
        Application.Run(dialog);

        if (!submitted) return;
        if (!UiPrompts.ConfirmRealMutation(_provider, $"Post a decision comment to {key}.")) return;

        var text = input.Text?.ToString();
        if (string.IsNullOrWhiteSpace(text)) text = "Smoke test decision.";

        RunInBackground(async sp =>
        {
            var writeBack = sp.GetRequiredService<IWriteBackService>();
            await writeBack.SubmitAsync(new WriteBackSubmission
            {
                WorkItemKey = key,
                RecordIdentity = $"tui-{Guid.NewGuid():N}",
                Kind = WriteBackKind.Decision,
                Content = text!,
                SourceUrl = "tui://smoke",
                Author = "SmokeTui"
            });
            // Drain immediately so the result is visible without waiting for the timer.
            await sp.GetRequiredService<WriteBackSender>().ProcessDueAsync();
        }, "Write-back");
    }

    /// <summary>Run service work on a background thread, then refresh on the UI thread.</summary>
    private void RunInBackground(Func<IServiceProvider, Task> work, string label)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _provider.CreateScope();
                await work(scope.ServiceProvider);
                Application.MainLoop.Invoke(Refresh);
            }
            catch (Exception ex)
            {
                Application.MainLoop.Invoke(() => MessageBox.ErrorQuery($"{label} failed", ex.Message, "OK"));
            }
        });
    }

    private string? FirstWorkItemKey()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraSyncDbContext>();
        return db.WorkItems.AsNoTracking().Where(w => !w.IsDeleted).OrderBy(w => w.Key)
            .Select(w => w.Key).FirstOrDefault();
    }

    private static string FirstLine(string text)
    {
        var line = text.Replace("\r", "").Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? text;
        return line.Length > 80 ? line[..80] : line;
    }
}
