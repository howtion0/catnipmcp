namespace Catnip.Shared.Configuration;

public sealed record ConfigurationValidationResult(
    bool IsValid,
    IReadOnlyList<ConfigurationValidationIssue> Issues)
{
    public IReadOnlyList<ConfigurationValidationIssue> Issues { get; init; } = Issues ?? [];
}

public sealed record ConfigurationValidationIssue(
    string Path,
    string Code,
    string Message,
    string Severity);
