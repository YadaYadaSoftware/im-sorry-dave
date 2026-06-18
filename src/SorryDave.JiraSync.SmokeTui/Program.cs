using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SorryDave.JiraSync.Core.Persistence;
using SorryDave.JiraSync.SmokeTui;
using SorryDave.JiraSync.SmokeTui.Ui;
using Terminal.Gui;

var provider = AppServices.Build(args);

// Ensure the database exists before the UI starts.
using (var scope = provider.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<JiraSyncDbContext>().Database.Migrate();
}

Application.Init();
try
{
    Application.Run(new MainWindow(provider));
}
finally
{
    Application.Shutdown();
}
