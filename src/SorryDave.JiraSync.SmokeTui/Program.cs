using SorryDave.JiraSync.SmokeTui;
using SorryDave.JiraSync.SmokeTui.Ui;
using Terminal.Gui;

var targets = AppServices.Build(args);

Application.Init();
try
{
    Application.Run(new MainWindow(targets));
}
finally
{
    Application.Shutdown();
}
