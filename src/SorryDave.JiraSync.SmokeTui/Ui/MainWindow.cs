using SorryDave.JiraSync.SmokeTui.Api;
using Terminal.Gui;

namespace SorryDave.JiraSync.SmokeTui.Ui;

/// <summary>
/// Top-level shell: a menu, the jira-sync-core panel, and a status bar that shows the API the
/// console is connected to and the key actions.
/// </summary>
public class MainWindow : Toplevel
{
    public MainWindow(IApiClient client, string apiBaseUrl)
    {
        var panel = new JiraSyncPanel(client)
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
        content.Add(panel);

        var menu = new MenuBar(new MenuBarItem[]
        {
            new MenuBarItem("_Run", new MenuItem[]
            {
                new MenuItem("_Guided smoke run", "Ctrl-R", () => SmokeRunView.Show(client)),
                new MenuItem("_Refresh", "F5", () => panel.Refresh()),
                new MenuItem("_Quit", "Ctrl-Q", () => Application.RequestStop()),
            }),
            new MenuBarItem("_Help", new MenuItem[]
            {
                new MenuItem("_About", "", () => MessageBox.Query("About",
                    "jira-sync-core smoke test\nDrives the running API over HTTP.", "OK")),
            }),
        });

        var status = new StatusBar(new StatusItem[]
        {
            new StatusItem(Key.F5, "~F5~ Refresh", () => panel.Refresh()),
            new StatusItem(Key.CtrlMask | Key.R, "~^R~ Smoke run", () => SmokeRunView.Show(client)),
            new StatusItem(Key.CtrlMask | Key.Q, "~^Q~ Quit", () => Application.RequestStop()),
            new StatusItem(Key.Null, $"API: {apiBaseUrl}", null),
        });

        Add(menu, content, status);

        panel.Refresh();
    }
}
