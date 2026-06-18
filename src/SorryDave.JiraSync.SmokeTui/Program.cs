using SorryDave.JiraSync.SmokeTui;
using SorryDave.JiraSync.SmokeTui.Ui;
using Terminal.Gui;

var (client, baseUrl) = AppServices.Build(args);

Application.Init();
try
{
    Application.Run(new MainWindow(client, baseUrl));
}
finally
{
    Application.Shutdown();
}
