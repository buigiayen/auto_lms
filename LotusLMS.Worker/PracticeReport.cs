namespace LotusLMS.Worker;

internal sealed record PracticeReport(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    IReadOnlyList<PracticeCourseReport> Courses)
{
    public int CoursesProcessed => Courses.Count;
    public int QuestionsSubmitted => Courses.Sum(course => course.Result.QuestionsSubmitted);
    public int BatchesSubmitted => Courses.Sum(course => course.Result.BatchesSubmitted);

    public double GainedKpiSeconds => Courses.Sum(course => course.Result.GainedKpiSeconds);
}

internal sealed record PracticeCourseReport(
    int CourseIid,
    string Name,
    PracticeResult Result);

internal sealed record PracticeResult(
    int? PracticeIid,
    int QuestionCount,
    int AlreadyCorrect,
    int QuestionsSubmitted,
    int BatchesSubmitted,
    string Outcome,
    double GainedKpiSeconds = 0,
    bool AlreadyOvertime = false,
    bool ServerFinished = false);
