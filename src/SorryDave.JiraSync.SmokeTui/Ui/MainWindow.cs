using SorryDave.JiraSync.SmokeTui.Api;
using Terminal.Gui;

namespace SorryDave.JiraSync.SmokeTui.Ui;

/// <summary>
/// Top-level shell: a menu (including a Target menu to switch between configured APIs), the
/// jira-sync-core panel, and a status bar showing the active target and key actions.
/// </summary>
public class MainWindow : Toplevel
{
    private readonly ResolvedTargets _targets;
    private readonly JiraSyncPanel _panel;
    private readonly StatusBar _status;
    private string _activeName;
    private bool _dryRun;

    public MainWindow(ResolvedTargets targets)
    {
        _targets = targets;
        _activeName = targets.ActiveName;

        _panel = new JiraSyncPanel(AppServices.CreateClient(targets.Active))
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        var content = new FrameView("jira-sync-core smoke panel")
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill() - 1
        };
        content.Add(_panel);

        // One menu entry per configured target; selecting it switches the active API at runtime.
        var targetItems = _targets.Targets
            .OrderBy(kv => kv.Key)
            .Select(kv => new MenuItem(kv.Key, kv.Value.BaseUrl, () => SwitchTarget(kv.Key)))
            .ToArray();

        var menu = new MenuBar(new MenuBarItem[]
        {
            new MenuBarItem("_Run", new MenuItem[]
            {
                new MenuItem("_Guided smoke run", "Ctrl-R", () => SmokeRunView.Show(_panel.Client)),
                new MenuItem("_Refresh", "F5", () => _panel.Refresh()),
                new MenuItem("_Quit", "Ctrl-Q", () => Application.RequestStop()),
            }),
            new MenuBarItem("_Target", targetItems),
            new MenuBarItem("_Slack", BuildSlackMenu()),
            new MenuBarItem("S_ummarize", new[]
            {
                new MenuItem("_Summarize conversation", "", () => _panel.SummarizeSmoke()),
            }),
            new MenuBarItem("_Help", new MenuItem[]
            {
                new MenuItem("_About", "", () => MessageBox.Query("About",
                    "jira-sync-core smoke test\nDrives the running API over HTTP.", "OK")),
            }),
        });

        _status = new StatusBar(BuildStatusItems());

        Add(menu, content, _status);

        _panel.Refresh();
    }

    private StatusItem[] BuildStatusItems() => new StatusItem[]
    {
        new StatusItem(Key.F5, "~F5~ Refresh", () => _panel.Refresh()),
        new StatusItem(Key.CtrlMask | Key.R, "~^R~ Smoke run", () => SmokeRunView.Show(_panel.Client)),
        new StatusItem(Key.CtrlMask | Key.Q, "~^Q~ Quit", () => Application.RequestStop()),
        new StatusItem(Key.Null, $"Target: {_activeName} ({_targets.Targets[_activeName].BaseUrl})", null),
    };

    private MenuItem[] BuildSlackMenu()
    {
        var dryRun = new MenuItem("Toggle _dry-run", "", null)
        {
            CheckType = MenuItemCheckStyle.Checked,
            Checked = _dryRun,
        };
        dryRun.Action = () => { _dryRun = !_dryRun; dryRun.Checked = _dryRun; };

        return new[]
        {
            new MenuItem("_Provision channel", "", () => _panel.ProvisionSlack(_dryRun)),
            new MenuItem("_Archive channel", "", () => _panel.ArchiveSlack(_dryRun)),
            new MenuItem("_Unarchive channel", "", () => _panel.UnarchiveSlack(_dryRun)),
            new MenuItem("_Link existing channel", "", () => _panel.LinkSlack()),
            new MenuItem("_Show linked channel", "", () => _panel.ShowSlackChannel()),
            dryRun,
        };
    }

    private void SwitchTarget(string name)
    {
        if (!_targets.Targets.TryGetValue(name, out var target)) return;
        _activeName = name;
        _panel.UpdateClient(AppServices.CreateClient(target));
        _status.Items = BuildStatusItems();
        _status.SetNeedsDisplay();
        _panel.Refresh();
    }
}
