var builder = DistributedApplication.CreateBuilder(args);

// The API is the primary service; its /health endpoint gates dependents.
var api = builder.AddProject<Projects.SorryDave_JiraSync_Api>("api")
    .WithHttpHealthCheck("/health");

// The smoke-test console is an interactive Terminal.Gui app, which cannot run inside Aspire's
// process host: DCP redirects stdio, so Terminal.Gui fails with "Unable to initialize the
// console." Instead we register it as an executable that opens a NEW terminal window running the
// TUI (a real console). It still waits for the API and receives the API endpoint via service
// discovery — the spawned process inherits those environment variables. Started explicitly from
// the dashboard's run button since it is interactive.
var smokeTuiDir = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "SorryDave.JiraSync.SmokeTui"));

builder.AddExecutable(
        "console",
        "powershell",
        smokeTuiDir,
        "-NoProfile", "-Command", "Start-Process cmd -ArgumentList '/k','dotnet run --no-build'")
    .WithReference(api)
    .WaitFor(api)
    .WithExplicitStart();

builder.Build().Run();
