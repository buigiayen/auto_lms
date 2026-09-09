using System.Text.Json.Nodes;

namespace LotusLMS.Api;

public interface ILotusLmsApiClient
{
    Task<LotusLmsSession> LoginAsync(
        LotusLmsLoginRequest request,
        CancellationToken cancellationToken = default);

    Task<JsonObject> GetStudentEnrolmentPlansAsync(
        LotusLmsSession session,
        int page = 1,
        int itemsPerPage = 12,
        CancellationToken cancellationToken = default);

    Task<List<Student_course_in_enrolment_plan_view>> GetStudentCoursesAsync(
        LotusLmsSession session,
        int enrolmentPlanIid,
        int page = 1,
        int itemsPerPage = 20,
        CancellationToken cancellationToken = default);

    Task<JsonObject> ReportLearningTimeAsync(
        LotusLmsSession session,
        int enrolmentPlanIid,
        int courseIid,
        CancellationToken cancellationToken = default);

    Task<BaseApi<ResultDetailLearningTime>> GetDetailLearningTimeAsync(
        LotusLmsSession session,
        int courseIid,
        string itemNtype = "video",
        CancellationToken cancellationToken = default);

    Task<JsonObject> SaveItemLearningTimeAsync(
        LotusLmsSession session,
        SaveItemLearningTimeRequest request,
        CancellationToken cancellationToken = default);

    Task<JsonObject> TrackTimeAsync(
        LotusLmsSession session,
        TrackTimeRequest request,
        CancellationToken cancellationToken = default);

    Task<JsonObject> GetTodayKpiTimeAsync(
        LotusLmsSession session,
        int courseIid,
        CancellationToken cancellationToken = default);

    Task<JsonObject> GetCourseAsync(
        LotusLmsSession session,
        int courseIid,
        CancellationToken cancellationToken = default);

    Task<JsonObject> GetPracticeDetailAsync(
        LotusLmsSession session,
        int practiceIid,
        int courseIid,
        CancellationToken cancellationToken = default);

    Task<JsonObject> GetQuestionBankStatusAsync(
        LotusLmsSession session,
        int examTemplateIid,
        int bankIid,
        int courseIid,
        CancellationToken cancellationToken = default);

    Task<JsonObject> GetSyncPracticeProgressAsync(
        LotusLmsSession session,
        int practiceIid,
        int examScoIid,
        int courseIid,
        string? requestId = null,
        string? requestToken = null,
        CancellationToken cancellationToken = default);

    Task<JsonObject> SavePracticeTakeAsync(
        LotusLmsSession session,
        SavePracticeTakeRequest request,
        string? requestId = null,
        string? requestToken = null,
        CancellationToken cancellationToken = default);
}
