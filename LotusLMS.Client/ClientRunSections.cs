namespace LotusLMS.Client;

public static class ClientRunSections
{
    public const string Exam = "exam";
    public const string Review = "review";
    public const string Theory = "theory";
    public const string Practical = "practical";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly<string>(
        [Exam, Review, Theory, Practical]);

    public static bool IsValid(IReadOnlyCollection<string>? sections) =>
        sections is { Count: > 0 } && sections.Count <= All.Count
        && sections.All(All.Contains) && sections.Distinct().Count() == sections.Count;
}
