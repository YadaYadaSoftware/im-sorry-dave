namespace SorryDave.JiraSync.SmokeTui.Smoke;

/// <summary>One step of a guided smoke run, with its pass/fail result and detail.</summary>
public record SmokeStep(string Name, bool Passed, string Detail);
