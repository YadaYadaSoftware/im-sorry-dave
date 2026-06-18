using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SorryDave.JiraSync.Core.DependencyInjection;
using SorryDave.JiraSync.Core.Persistence;
using SorryDave.JiraSync.SmokeTui.Smoke;

namespace SorryDave.JiraSync.Tests;

public class JiraSyncSmokeRunnerTests
{
    [Fact]
    public async Task Guided_run_passes_every_step_against_fake_backend()
    {
        // File-backed SQLite so the runner's multiple scopes share one database.
        var dbPath = Path.Combine(Path.GetTempPath(), $"smoke-{Guid.NewGuid():N}.db");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:JiraSync"] = $"Data Source={dbPath}"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddJiraSyncCore(config); // no Jira credentials -> in-memory fake client
        await using var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
            scope.ServiceProvider.GetRequiredService<JiraSyncDbContext>().Database.Migrate();

        try
        {
            var steps = await new JiraSyncSmokeRunner(provider).RunAsync();

            Assert.NotEmpty(steps);
            Assert.True(JiraSyncSmokeRunner.AllPassed(steps),
                "Steps:\n" + string.Join("\n", steps.Select(s => $"[{(s.Passed ? "PASS" : "FAIL")}] {s.Name} — {s.Detail}")));
            Assert.Contains(steps, s => s.Name == "Verify delivery" && s.Passed);
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* best effort */ }
        }
    }
}
