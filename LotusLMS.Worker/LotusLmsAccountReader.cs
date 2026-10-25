using System.Text.Json.Nodes;
using LotusLMS.Api;
using Microsoft.Extensions.Options;
using static LotusLMS.Worker.LotusLmsJson;

namespace LotusLMS.Worker;

/// <summary>Account login and enrolled-course discovery shared by job runners.</summary>
internal sealed class LotusLmsAccountReader(
    ILotusLmsApiClient apiClient,
    IOptions<LotusLmsOptions> apiOptions,
    ILogger<LotusLmsAccountReader> logger)
{
    public async Task<LotusLmsSession> LoginAsync(
        LotusLmsCredentialsOptions credentials,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        EnsureCredentials(credentials);
        EnsureHttps(apiOptions.Value.BaseUrl, $"{LotusLmsOptions.SectionName}:BaseUrl");
        EnsureHttps(apiOptions.Value.WebOrigin, $"{LotusLmsOptions.SectionName}:WebOrigin");

        return await apiClient.LoginAsync(
            new LotusLmsLoginRequest
            {
                Username = credentials.Username,
                Password = credentials.Password,
                DeviceUuid = credentials.DeviceUuid,
                SessionId = credentials.SessionId,
                RequestId = credentials.RequestId,
                RequestToken = credentials.RequestToken,
                StudentRequestId = credentials.StudentRequestId,
                StudentRequestToken = credentials.StudentRequestToken,
                ClientSyncToken = credentials.ClientSyncToken
            },
            cancellationToken);
    }

    public async Task<List<int>> ReadPlanIdsAsync(
        LotusLmsSession session,
        WorkerOptions options,
        CancellationToken cancellationToken)
    {
        var planIds = new List<int>();
        var seen = new HashSet<int>();
        var pageLimit = options.OnlyFirstPlan ? 1 : options.MaxPages;

        for (var page = 1; page <= pageLimit; page++)
        {
            var response = await apiClient.GetStudentEnrolmentPlansAsync(
                session,
                page,
                options.PlanPageSize,
                cancellationToken);
            var rows = RequireResultArray(response, "enrolment plans");

            foreach (var row in rows)
            {
                if (row is not JsonObject obj)
                {
                    continue;
                }

                var iid = ReadPositiveInt(obj["iid"]);
                if (iid > 0 && seen.Add(iid))
                {
                    planIds.Add(iid);
                }
            }

            if (options.OnlyFirstPlan || rows.Count < options.PlanPageSize)
            {
                break;
            }

            if (page == options.MaxPages)
            {
                logger.LogWarning("Enrolment plan paging reached the configured page limit.");
            }
        }

        return planIds;
    }

    public async Task<List<CourseDescriptor>> ReadCoursesAsync(
        LotusLmsSession session,
        int planIid,
        WorkerOptions options,
        bool includePassed,
        CancellationToken cancellationToken)
    {
        var courses = new List<CourseDescriptor>();
        var seen = new HashSet<int>();
        var pageLimit = options.OnlyFirstCoursePage ? 1 : options.MaxPages;

        for (var page = 1; page <= pageLimit; page++)
        {
            var response = await apiClient.GetStudentCoursesAsync(
                session,
                planIid,
                page,
                options.CoursePageSize,
                cancellationToken);
            var rows = Flatten(response)
                .Where(p => p.Course is not null
                    && (includePassed || !p.Course.IsPass))
                .ToList();

            foreach (var row in rows)
            {
                AddCourse(row, courses, seen);
            }

            if (options.OnlyFirstCoursePage || response.Count < options.CoursePageSize)
            {
                break;
            }

            if (page == options.MaxPages)
            {
                logger.LogWarning(
                    "Course paging for plan {PlanIid} reached the configured page limit.",
                    planIid);
            }
        }

        return courses;
    }

    internal static IEnumerable<Student_course_in_enrolment_plan_view> Flatten(
        IEnumerable<Student_course_in_enrolment_plan_view>? nodes)
    {
        if (nodes is null)
        {
            yield break;
        }

        foreach (var node in nodes)
        {
            if (node is null)
            {
                continue;
            }

            yield return node;

            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    private static void AddCourse(
        Student_course_in_enrolment_plan_view row,
        List<CourseDescriptor> courses,
        ISet<int> seen)
    {
        var course = row.Course;
        if (course is null)
        {
            return;
        }

        var iid = course.Iid;
        if (iid <= 0 || !seen.Add(iid))
        {
            return;
        }

        var name = NormalizeCourseName(course.Name)
            ?? NormalizeCourseName(row.Name)
            ?? "(unnamed course)";

        courses.Add(new CourseDescriptor(iid, name,
            course.__Expand?.ScoreByRubric?.Rubric?.PassScore ?? 0,
            course.__Expand?.ScoreByRubric?.Rubric?.MaxScore ?? 0,
            course.IsPass));
    }

    private static string? NormalizeCourseName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void EnsureCredentials(LotusLmsCredentialsOptions credentials)
    {
        if (string.IsNullOrWhiteSpace(credentials.Username)
            || string.IsNullOrEmpty(credentials.Password))
        {
            throw new InvalidOperationException(
                "LotusLMS job credentials are not configured in the local secret provider.");
        }
    }

    private static void EnsureHttps(string value, string settingName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"LotusLMS job refuses to send credentials because {settingName} is not HTTPS.");
        }
    }
}

internal sealed record CourseDescriptor(int Iid, string Name, double Score = 0, double MaxScore = 0, bool IsPass = false);
