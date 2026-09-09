using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using LotusLMS.Api;
using Microsoft.Extensions.Options;

namespace LotusLMS.Worker;

internal sealed class LotusLmsReadOnlyProbe(
    ILotusLmsApiClient apiClient,
    IOptions<LotusLmsOptions> apiOptions)
{
    public async Task<ReadOnlyProbeReport> RunAsync(
        LotusLmsCredentialsOptions credentials,
        WorkerOptions workerOptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(workerOptions);

        EnsureCredentials(credentials);
        EnsureHttps(apiOptions.Value.BaseUrl, $"{LotusLmsOptions.SectionName}:BaseUrl");
        EnsureHttps(apiOptions.Value.WebOrigin, $"{LotusLmsOptions.SectionName}:WebOrigin");

        var session = await apiClient.LoginAsync(
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

        var planIds = new List<int>();
        var seenPlanIds = new HashSet<int>();
        var planRowsReturned = 0;
        var planResultsTruncated = false;

        for (var page = 1; page <= workerOptions.MaxPages; page++)
        {
            var response = await apiClient.GetStudentEnrolmentPlansAsync(
                session,
                page,
                workerOptions.PlanPageSize,
                cancellationToken);
            var rows = RequireResultArray(response, "enrolment plans");
            planRowsReturned += rows.Count;
            var newIdsOnPage = 0;

            for (var index = 0; index < rows.Count; index++)
            {
                if (rows[index] is not JsonObject row
                    || !TryReadPositiveInt(row["iid"], out var planIid))
                {
                    throw new InvalidDataException(
                        $"The enrolment plan response contains an invalid iid at result[{index}].");
                }

                if (seenPlanIds.Add(planIid))
                {
                    planIds.Add(planIid);
                    newIdsOnPage++;
                }
            }

            if (rows.Count < workerOptions.PlanPageSize)
            {
                break;
            }

            if (newIdsOnPage == 0)
            {
                throw new InvalidDataException(
                    "Enrolment plan pagination repeated a full page without any new ids.");
            }

            if (page == workerOptions.MaxPages)
            {
                planResultsTruncated = true;
            }
        }

        var plans = new List<PlanProbeResult>(planIds.Count);
        foreach (var planIid in planIds)
        {
            plans.Add(await ReadPlanCoursesAsync(
                session,
                planIid,
                workerOptions,
                cancellationToken));
        }

        return new ReadOnlyProbeReport(
            DateTimeOffset.UtcNow,
            planRowsReturned,
            plans,
            planResultsTruncated);
    }

    private async Task<PlanProbeResult> ReadPlanCoursesAsync(
        LotusLmsSession session,
        int planIid,
        WorkerOptions workerOptions,
        CancellationToken cancellationToken)
    {
        var courses = new List<CourseProbeResult>();
        var warnings = new List<string>();
        var seenCourseIds = new HashSet<int>();
        var rowsReturned = 0;
        var resultsTruncated = false;

        for (var page = 1; page <= workerOptions.MaxPages; page++)
        {
            var response = await apiClient.GetStudentCoursesAsync(
                session,
                planIid,
                page,
                workerOptions.CoursePageSize,
                cancellationToken);
            var rows = response;
            rowsReturned += rows.Count;
            var newCoursesOnPage = 0;

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var row = rows[rowIndex];

                newCoursesOnPage += AddCourse(
                    row,
                    $"result[{rowIndex}].course",
                    seenCourseIds,
                    courses,
                    warnings);

                for (var childIndex = 0; childIndex < row.Children.Count; childIndex++)
                {
                    newCoursesOnPage += AddCourse(
                        row.Children[childIndex],
                        $"result[{rowIndex}].children[{childIndex}].course",
                        seenCourseIds,
                        courses,
                        warnings);
                }
            }

            if (rows.Count < workerOptions.CoursePageSize)
            {
                break;
            }

            if (newCoursesOnPage == 0)
            {
                throw new InvalidDataException(
                    $"Course pagination for plan {planIid} repeated a full page without new courses.");
            }

            if (page == workerOptions.MaxPages)
            {
                resultsTruncated = true;
            }
        }

        foreach (var course in courses)
        {
            try
            {
                var detail = await apiClient.GetDetailLearningTimeAsync(
                    session,
                    course.CourseIid,
                    "video",
                    cancellationToken);

                if (detail.Result is null || CountVideos(detail.Result.Children) == 0)
                {
                    warnings.Add($"course {course.CourseIid} learning tree contains no video nodes");
                }
            }
            catch (Exception exception)
            {
                warnings.Add(
                    $"course {course.CourseIid} learning tree could not be read: {exception.Message}");
            }
        }

        return new PlanProbeResult(
            planIid,
            rowsReturned,
            courses,
            warnings,
            resultsTruncated);
    }

    private static int CountVideos(IEnumerable<LearningTimeNode> nodes)
    {
        var count = 0;
        foreach (var node in nodes)
        {
            if (string.Equals(node.Ntype, "video", StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }

            count += CountVideos(node.Children);
        }

        return count;
    }

    private static int AddCourse(
        Student_course_in_enrolment_plan_view row,
        string path,
        ISet<int> seenCourseIds,
        ICollection<CourseProbeResult> courses,
        ICollection<string> warnings)
    {
        var course = row.Course;
        if (course is null)
        {
            return 0;
        }

        var courseIid = course.Iid;
        if (courseIid <= 0)
        {
            warnings.Add($"{path}.iid is invalid");
            return 0;
        }

        if (!seenCourseIds.Add(courseIid))
        {
            warnings.Add($"duplicate course iid {courseIid}");
            return 0;
        }

        var name = NormalizeName(course.Name
                                 ?? row.Name
                                 ?? "(unnamed course)");

        courses.Add(new CourseProbeResult(
            courseIid,
            name,
            course.Cp,
            course.P,
            course.Pf == 1));
        return 1;
    }

    private static JsonArray RequireResultArray(JsonObject response, string operation) =>
        response["result"] as JsonArray
        ?? throw new InvalidDataException(
            $"The {operation} response is missing a result array.");

    private static bool TryReadPositiveInt(JsonNode? node, out int value)
    {
        value = 0;
        if (node is not JsonValue jsonValue)
        {
            return false;
        }

        if (jsonValue.TryGetValue<int>(out value))
        {
            return value > 0;
        }

        return int.TryParse(
                   ReadString(node),
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out value)
               && value > 0;
    }

    private static double? ReadFiniteDouble(JsonNode? node)
    {
        if (node is not JsonValue jsonValue)
        {
            return null;
        }

        double value;
        if (!jsonValue.TryGetValue<double>(out value)
            && !double.TryParse(
                ReadString(node),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value))
        {
            return null;
        }

        return double.IsFinite(value) ? value : null;
    }

    private static string? ReadString(JsonNode? node)
    {
        if (node is not JsonValue value || !value.TryGetValue<string>(out var text))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static string NormalizeName(string value)
    {
        var normalized = string.Join(
            ' ',
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 256 ? normalized : normalized[..256];
    }

    private static void EnsureCredentials(LotusLmsCredentialsOptions credentials)
    {
        if (string.IsNullOrWhiteSpace(credentials.Username)
            || string.IsNullOrEmpty(credentials.Password))
        {
            throw new InvalidOperationException(
                "Read-only probe credentials are not configured in the local secret provider.");
        }
    }

    private static void EnsureHttps(string value, string settingName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"Read-only probe refuses to send credentials because {settingName} is not HTTPS.");
        }
    }
}
