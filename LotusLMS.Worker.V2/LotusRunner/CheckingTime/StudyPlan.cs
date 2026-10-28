using LotusLMS.Worker2;

namespace LotusLMS.Worker.V2.LotusRunner.CheckingTime;

public sealed record StudyPlan(
    IReadOnlyList<CheckingTimeDTO.AutoStudy> Items,
    bool IsCompleted)
{
    public AccountStudyResult CompletionResult => IsCompleted
        ? AccountStudyResult.Completed
        : Items.Count == 0 ? AccountStudyResult.SelectedSectionsCompleted : AccountStudyResult.Pending;
}

public enum AccountStudyResult
{
    Pending,
    DailyKpiReached,
    Completed,
    SelectedSectionsCompleted
}
