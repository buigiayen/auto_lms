using LotusLMS.Worker2;

namespace LotusLMS.Worker.V2.LotusRunner.CheckingTime;

public sealed record StudyPlan(
    IReadOnlyList<CheckingTimeDTO.AutoStudy> Items,
    bool IsCompleted);

public enum AccountStudyResult
{
    Pending,
    DailyKpiReached,
    Completed
}
