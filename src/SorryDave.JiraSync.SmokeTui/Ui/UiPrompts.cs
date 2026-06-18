using Terminal.Gui;

namespace SorryDave.JiraSync.SmokeTui.Ui;

public static class UiPrompts
{
    /// <summary>
    /// In fake mode, returns true immediately. In real mode, asks the user to confirm an action
    /// that would mutate real Jira, returning true only if they choose to continue.
    /// </summary>
    public static bool ConfirmRealMutation(IServiceProvider provider, string action)
    {
        if (AppServices.IsFakeMode(provider)) return true;

        var choice = MessageBox.Query("Real Jira",
            $"{action}\n\nThis will modify REAL Jira. Continue?", "Cancel", "Continue");
        return choice == 1; // 0 = Cancel, 1 = Continue
    }
}
