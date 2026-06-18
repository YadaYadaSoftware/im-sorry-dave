using SorryDave.JiraSync.SmokeTui.Api;
using SorryDave.JiraSync.SmokeTui.Smoke;
using Terminal.Gui;

namespace SorryDave.JiraSync.SmokeTui.Ui;

/// <summary>
/// Modal view for the guided smoke run. Runs <see cref="JiraSyncSmokeRunner"/> (over the API) on
/// a background thread and renders each step's pass/fail plus an overall result.
/// </summary>
public static class SmokeRunView
{
    public static void Show(IApiClient client)
    {
        var lines = new List<string> { "Running guided smoke run..." };
        var list = new ListView(lines)
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill() - 1,
            AllowsMarking = false
        };

        var close = new Button("Close") { IsDefault = true };
        close.Clicked += () => Application.RequestStop();

        var dialog = new Dialog("Guided smoke run — jira-sync-core", 76, 18, close);
        dialog.Add(list);

        _ = Task.Run(async () =>
        {
            IReadOnlyList<SmokeStep> steps;
            try
            {
                steps = await new JiraSyncSmokeRunner(client).RunAsync();
            }
            catch (Exception ex)
            {
                steps = new[] { new SmokeStep("Run failed", false, ex.Message) };
            }

            var rendered = steps
                .Select(s => $"[{(s.Passed ? "PASS" : "FAIL")}] {s.Name} — {s.Detail}")
                .ToList();
            rendered.Add(string.Empty);
            rendered.Add(JiraSyncSmokeRunner.AllPassed(steps) ? "OVERALL: PASS ✓" : "OVERALL: FAIL ✗");

            Application.MainLoop.Invoke(() => list.SetSource(rendered));
        });

        Application.Run(dialog);
    }
}
