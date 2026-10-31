using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace LotusLMS.Api;

public sealed class LotusLmsApiClient(
    HttpClient httpClient,
    IOptions<LotusLmsOptions> options) : ILotusLmsApiClient
{
    private const string Empty = "";
    private readonly LotusLmsOptions _options = options.Value;
    private readonly LotusLmsApiTransport _transport = new(httpClient, options.Value);

    public async Task<LotusLmsSession> LoginAsync(
        LotusLmsLoginRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var username = RequireText(request.Username, nameof(request.Username));
        if (string.IsNullOrEmpty(request.Password))
        {
            throw new ArgumentException("Password is required.", nameof(request));
        }

        var provider = ValidateProvider(request.Provider);
        var deviceUuid = string.IsNullOrWhiteSpace(request.DeviceUuid)
            ? LotusLmsDeviceIdentity.GetOrCreate(_options.DeviceIdentityDirectory)
            : request.DeviceUuid.Trim();
        var sessionId = string.IsNullOrWhiteSpace(request.SessionId)
            ? Guid.NewGuid().ToString()
            : request.SessionId.Trim();
        var loginUrl = BuildWebUrl("user/login/?logout=1", provider);

        var form = new List<KeyValuePair<string, string>>
        {
            Pair("lname", username),
            Pair("pass", request.Password),
            Pair("submit", "1"),
            Pair("_sand_ajax", "1"),
            Pair("_sand_platform", "3"),
            Pair("_sand_readmin", "1"),
            Pair("_sand_is_wan", "false"),
            Pair("_sand_ga_sessionToken", Empty),
            Pair("_sand_ga_browserToken", Empty),
            Pair("_sand_domain", provider?.Tenant ?? _options.Tenant),
            Pair("_sand_masked", Empty),
            Pair("_sand_web_url", loginUrl),
            Pair("_sand_device_uuid", deviceUuid),
            Pair("_sand_session_id", sessionId),
            Pair("_sand_use_internal_network", "0"),
            Pair("allow_cache_api_cdn", "1"),
            Pair("_sand_user_agent", _options.UserAgent)
        };

        AddIfPresent(form, "_sand_ri", request.RequestId);
        AddIfPresent(form, "_sand_rit", request.RequestToken);
        AddIfPresent(form, "_sand_client_sync_token", request.ClientSyncToken);

        var root = await _transport.PostAsync(
            "user/login",
            form,
            loginUrl,
            session: null,
            cancellationToken,
            provider);

        var result = root["result"] as JsonObject
            ?? throw BusinessError("user/login", "Login response does not contain a result object.", root);

        if (ReadInt(result["pass"]) != 1)
        {
            throw BusinessError("user/login", "LotusLMS rejected the login credentials.", root);
        }

        var token = ReadRequiredString(result, "token", "user/login");
        var userIid = ReadInt(result["iid"]);
        if (userIid <= 0)
        {
            throw BusinessError("user/login", "Login response contains an invalid user iid.", root);
        }

        var loginName = ReadString(result["lname"]) ?? username;
        var userId = ReadString(result["id"])
            ?? userIid.ToString(CultureInfo.InvariantCulture);
        var displayName = ReadString(result["name"]) ?? loginName;

        return new LotusLmsSession
        {
            Token = token,
            UserIid = userIid,
            UserId = userId,
            LoginName = loginName,
            DisplayName = displayName,
            DeviceUuid = deviceUuid,
            SessionId = sessionId,
            Provider = provider,
            RequestId = request.RequestId,
            RequestToken = request.RequestToken,
            StudentRequestId = request.StudentRequestId,
            StudentRequestToken = request.StudentRequestToken,
            ClientSyncToken = request.ClientSyncToken
        };
    }

    public Task<JsonObject> GetStudentEnrolmentPlansAsync(
        LotusLmsSession session,
        int page = 1,
        int itemsPerPage = 12,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        ValidatePage(page, itemsPerPage);

        var query = new List<KeyValuePair<string, string>>
        {
            Pair("_sand_get_total", "0"),
            Pair("items_per_page", Format(itemsPerPage)),
            Pair("page", Format(page))
        };
        query.AddRange(BuildBrowserQuery(session));

        var endpoint = BuildRelativeUri("student/api/get-student-enrolment-plans", query);
        return _transport.PostAsync(
            endpoint,
            BuildIdentityForm(session),
            BuildWebUrl("student", session.Provider),
            session,
            cancellationToken);
    }

    public async Task<List<Student_course_in_enrolment_plan_view>> GetStudentCoursesAsync(
        LotusLmsSession session,
        int enrolmentPlanIid,
        int page = 1,
        int itemsPerPage = 20,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        EnsurePositive(enrolmentPlanIid, nameof(enrolmentPlanIid));
        ValidatePage(page, itemsPerPage);

        var query = new List<KeyValuePair<string, string>>
        {
            Pair("_sand_get_total", "0"),
            Pair("items_per_page", Format(itemsPerPage)),
            Pair("enrolment_plan_iid", Format(enrolmentPlanIid)),
            Pair("expand[]", "syllabus"),
            Pair("expand[]", "score_by_rubric"),
            Pair("page", Format(page))
        };
        query.AddRange(BuildBrowserQuery(session));

        var endpoint = BuildRelativeUri(
            "student/api/get-student-courses-in-enrolment-plan-table-view",
            query);
        var webUrl = BuildWebUrl($"student/ep/{enrolmentPlanIid}", session.Provider);

        var data = await _transport.PostAsync<BaseApi<List<Student_course_in_enrolment_plan_view>>>(
            endpoint,
            BuildAuthenticatedForm(session, webUrl),
            webUrl,
            session,
            cancellationToken);

        return data.Result
            ?? throw new LotusLmsApiException(
                "Student course response does not contain a result list.",
                endpoint,
                System.Net.HttpStatusCode.OK);
    }

    public Task<JsonObject> ReportLearningTimeAsync(
        LotusLmsSession session,
        int enrolmentPlanIid,
        int courseIid,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        EnsurePositive(enrolmentPlanIid, nameof(enrolmentPlanIid));
        EnsurePositive(courseIid, nameof(courseIid));

        var query = new List<KeyValuePair<string, string>>
        {
            Pair("enrolment_plan_iid", Format(enrolmentPlanIid)),
            Pair("course_iid", Format(courseIid)),
            Pair("expand[]", "current_unit"),
            Pair("expand[]", "current_unit_group")
        };
        query.AddRange(BuildBrowserQuery(session));

        var endpoint = BuildRelativeUri("student/api/get-detail-learning-time", query);
        var webUrl = BuildWebUrl($"student/courses/{courseIid}", session.Provider);

        return _transport.PostAsync(
            endpoint,
            BuildAuthenticatedForm(session, webUrl),
            webUrl,
            session,
            cancellationToken);
    }
    public async Task<get_user_score_by_rubric> getUserScoreRubric(
       LotusLmsSession session,
       int courseIid,
       CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        EnsurePositive(courseIid, nameof(courseIid));

        var query = new List<KeyValuePair<string, string>>
        {
            Pair("iid", Format(courseIid)),
            Pair("recalculate", Format(1)),
            Pair("ntype", "course"),
        };
        query.AddRange(BuildBrowserQuery(session));

        var endpoint = BuildRelativeUri("rubrik/api/get-user-score-by-rubric", query);
        var webUrl = BuildWebUrl($"student/courses/{courseIid}", session.Provider);

        var data = await _transport.PostAsync<BaseApi<get_user_score_by_rubric>>(
            endpoint,
            BuildAuthenticatedForm(session, webUrl),
            webUrl,
            session,
            cancellationToken);
        return data.Result;
    }
    public async Task<BaseApi<ResultDetailLearningTime>> GetDetailLearningTimeAsync(
        LotusLmsSession session,
        int courseIid,
        string itemNtype = "video",
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        EnsurePositive(courseIid, nameof(courseIid));
        itemNtype = RequireText(itemNtype, nameof(itemNtype));

        var query = new List<KeyValuePair<string, string>>
        {
            Pair("user_iid", Format(session.UserIid)),
            Pair("course_iid", Format(courseIid)),
            Pair("item_ntype", itemNtype)
        };
        query.AddRange(BuildBrowserQuery(session));

        var endpoint = BuildRelativeUri(
            "time-tracking/course-time-tracking/get-detail-learning-time",
            query);
        var webUrl = BuildWebUrl($"student/course/{courseIid}", session.Provider);

        var data = await _transport.PostAsync<BaseApi<ResultDetailLearningTime>>(
            endpoint,
            BuildAuthenticatedForm(session, webUrl),
            webUrl,
            session,
            cancellationToken);

        return data.Result is not null
            ? data
            : throw new LotusLmsApiException(
                "Learning-time detail response does not contain a result object.",
                endpoint,
                System.Net.HttpStatusCode.OK);
    }

    public Task<JsonObject> SaveItemLearningTimeAsync(
        LotusLmsSession session,
        SaveItemLearningTimeRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        ArgumentNullException.ThrowIfNull(request);
        EnsurePositive(request.ItemIid, nameof(request.ItemIid));
        EnsurePositive(request.CourseIid, nameof(request.CourseIid));
        var itemNtype = RequireText(request.ItemNtype, nameof(request.ItemNtype));

        if (request.TimeSpentSeconds is < 1 or > 15)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.TimeSpentSeconds),
                "Learning-time increments must be between 1 and 15 seconds.");
        }

        var webUrl = BuildWebUrl(
            $"learn/course/learn/{request.CourseIid}/{request.ItemIid}",
            session.Provider);
        var form = BuildAuthenticatedForm(session, webUrl);
        form.InsertRange(0,
        [
            Pair("item_iid", Format(request.ItemIid)),
            Pair("item_ntype", itemNtype),
            Pair("course_iid", Format(request.CourseIid)),
            Pair("time_spent", Format(request.TimeSpentSeconds))
        ]);

        return _transport.PostAsync(
            "course/api/save-item-learning-time",
            form,
            webUrl,
            session,
            cancellationToken);
    }

    public Task<JsonObject> TrackTimeAsync(
        LotusLmsSession session,
        TrackTimeRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        ArgumentNullException.ThrowIfNull(request);
        EnsurePositive(request.CourseIid, nameof(request.CourseIid));

        if (!double.IsFinite(request.TimeSpentSeconds) || request.TimeSpentSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.TimeSpentSeconds),
                "Track time must be a positive finite number.");
        }

        var form = BuildAuthenticatedForm(session, BuildWebUrl("student", session.Provider));
        form.InsertRange(0,
        [
            Pair("page_id", RequireText(request.PageId, nameof(request.PageId))),
            Pair("item_id", Format(request.CourseIid)),
            Pair("time_spent", request.TimeSpentSeconds.ToString("F3", CultureInfo.InvariantCulture))
        ]);

        return _transport.PostAsync(
            "time-tracking/api/track-time",
            form,
            BuildWebUrl("student", session.Provider),
            session,
            cancellationToken);
    }

    public async Task<GetToDayKpiTime> GetTodayKpiTimeAsync(
        LotusLmsSession session,
        int courseIid,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        EnsurePositive(courseIid, nameof(courseIid));

        var query = new List<KeyValuePair<string, string>>
        {
            Pair("user_iid", Format(session.UserIid))
        };
        query.AddRange(BuildBrowserQuery(session));

        var endpoint = BuildRelativeUri(
            "time-tracking/learning-time-tracking/today-gained-kpi-time",
            query);
        var webUrl = BuildWebUrl($"learn/course/learn/{courseIid}", session.Provider);

        var data = await _transport.PostAsync<BaseApi<GetToDayKpiTime>>(
            endpoint,
            BuildAuthenticatedForm(session, webUrl),
            webUrl,
            session,
            cancellationToken);
        return data.Result;
    }

    public Task<JsonObject> GetCourseAsync(
        LotusLmsSession session,
        int courseIid,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        EnsurePositive(courseIid, nameof(courseIid));

        var query = new List<KeyValuePair<string, string>>
        {
            Pair("ntype", "course"),
            Pair("depth", "1"),
            Pair("editing_syllabus", "1"),
            Pair("ciid", Format(courseIid)),
            Pair("_sand_expand[]", "score_by_rubric"),
            Pair("iid", Format(courseIid))
        };
        query.AddRange(BuildBrowserQuery(session));

        var endpoint = BuildRelativeUri("course/api/get-course", query);
        var webUrl = BuildWebUrl($"student/course/{courseIid}/dashboard", session.Provider);

        return _transport.PostAsync(
            endpoint,
            BuildAuthenticatedForm(session, webUrl),
            webUrl,
            session,
            cancellationToken);
    }

    public Task<BaseApi<GetPracticeDetail>> GetPracticeDetailAsync(
        LotusLmsSession session,
        int practiceIid,
        int courseIid,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        EnsurePositive(practiceIid, nameof(practiceIid));
        EnsurePositive(courseIid, nameof(courseIid));

        var query = new List<KeyValuePair<string, string>>
        {
            Pair("practice_iid", Format(practiceIid)),
            Pair("course_iid", Format(courseIid)),
            Pair("_sand_expand[]", "exam_sco"),
            Pair("lang", "vn")
        };
        query.AddRange(BuildBrowserQuery(session));

        var endpoint = BuildRelativeUri("practice/api/get-practice-detail", query);
        var webUrl = BuildWebUrl($"learn/course/learn/{courseIid}", session.Provider);
        /// Debug đoạn này
        return _transport.PostAsync<BaseApi<GetPracticeDetail>>(
            endpoint,
            BuildAuthenticatedForm(session, webUrl),
            webUrl,
            session,
            cancellationToken);
    }

    public Task<BaseApi<GetQuestionBankStatus>> GetQuestionBankStatusAsync(
        LotusLmsSession session,
        int examTemplateIid,
        int bankIid,
        int courseIid,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        EnsurePositive(examTemplateIid, nameof(examTemplateIid));
        EnsurePositive(bankIid, nameof(bankIid));
        EnsurePositive(courseIid, nameof(courseIid));

        var query = new List<KeyValuePair<string, string>>
        {
            Pair("bank_iids[]", Format(bankIid)),
            Pair("exam_template_iid", Format(examTemplateIid)),
            Pair("last_ts", "0"),
            Pair("lang", "vn")
        };
        query.AddRange(BuildBrowserQuery(session));

        var endpoint = BuildRelativeUri("practice/api/get-question-bank-status", query);
        var webUrl = BuildWebUrl($"learn/course/learn/{courseIid}", session.Provider);
        return _transport.PostAsync<BaseApi<GetQuestionBankStatus>>(
            endpoint,
            BuildAuthenticatedForm(session, webUrl),
            webUrl,
            session,
            cancellationToken);
    }

    public Task<JsonObject> GetSyncPracticeProgressAsync(
        LotusLmsSession session,
        int practiceIid,
        int examScoIid,
        int courseIid,
        string? requestId = null,
        string? requestToken = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        EnsurePositive(practiceIid, nameof(practiceIid));
        EnsurePositive(examScoIid, nameof(examScoIid));
        EnsurePositive(courseIid, nameof(courseIid));

        const string endpoint = "practice/api/get-sync-practice-progress";
        var webUrl = BuildWebUrl($"learn/course/learn/{courseIid}", session.Provider);
        var form = BuildAuthenticatedForm(session, webUrl, requestId, requestToken);
        AddRequestFlags(form);
        form.InsertRange(0,
        [
            Pair("practice_iid", Format(practiceIid)),
            Pair("exam_sco_iid", Format(examScoIid)),
            Pair("lang", "vn")
        ]);

        return _transport.PostMultipartAsync(endpoint, form, webUrl, session, cancellationToken);
    }

    public Task<JsonObject> RecalculateCoursePracticeProgressAsync(
        LotusLmsSession session,
        int practiceIid,
        int courseIid,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        EnsurePositive(practiceIid, nameof(practiceIid));
        EnsurePositive(courseIid, nameof(courseIid));
        var webUrl = BuildWebUrl($"learn/course/learn/{courseIid}", session.Provider);
        var form = BuildAuthenticatedForm(session, webUrl);
        AddRequestFlags(form);
        form.AddRange([
            Pair("course_iid", Format(courseIid)),
            Pair("practice_iid", Format(practiceIid)),
            Pair("lang", "vn")
        ]);
        return _transport.PostMultipartAsync("practice/api/recalculate-course-practice-progress",
            form, webUrl, session, cancellationToken);
    }

    public Task<JsonObject> StartPracticeAsync(
        LotusLmsSession session,
        int practiceIid,
        int examScoIid,
        int courseIid,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        EnsurePositive(practiceIid, nameof(practiceIid));
        EnsurePositive(examScoIid, nameof(examScoIid));
        EnsurePositive(courseIid, nameof(courseIid));
        var webUrl = BuildWebUrl($"learn/course/learn/{courseIid}", session.Provider);
        var form = BuildAuthenticatedForm(session, webUrl);
        AddRequestFlags(form);
        form.AddRange([
            Pair("practice_iid", Format(practiceIid)),
            Pair("exam_sco_iid", Format(examScoIid)),
            Pair("course_iid", Format(courseIid)),
            Pair("lang", "vn")
        ]);
        return _transport.PostMultipartAsync("practice/api/start-practice",
            form, webUrl, session, cancellationToken);
    }

    public Task<JsonObject> TrackPracticeTimeAsync(
        LotusLmsSession session,
        int practiceIid,
        int courseIid,
        double timeSpentSeconds,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        EnsurePositive(practiceIid, nameof(practiceIid));
        EnsurePositive(courseIid, nameof(courseIid));
        if (!double.IsFinite(timeSpentSeconds) || timeSpentSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(timeSpentSeconds));

        var webUrl = BuildWebUrl($"learn/course/learn/{courseIid}", session.Provider);
        var form = BuildAuthenticatedForm(session, webUrl);
        AddRequestFlags(form);
        form.AddRange([
            Pair("page_id", "practice-exam"),
            Pair("item_id", Format(practiceIid)),
            Pair("time_spent", timeSpentSeconds.ToString("F3", CultureInfo.InvariantCulture)),
            Pair("metadata[user_iid]", Format(session.UserIid)),
            Pair("lang", "vn")
        ]);
        return _transport.PostMultipartAsync("time-tracking/api/track-time",
            form, webUrl, session, cancellationToken);
    }

    public Task<JsonObject> SavePracticeTakeAsync(
        LotusLmsSession session,
        SavePracticeTakeRequest request,
        string? requestId = null,
        string? requestToken = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        ValidatePracticeTake(request);

        const string endpoint = "practice/api/save-take";
        var webUrl = BuildWebUrl($"learn/course/learn/{request.CourseIid}", session.Provider);
        var form = BuildAuthenticatedForm(session, webUrl, requestId, requestToken);
        AddRequestFlags(form);

        form.InsertRange(0,
        [
            Pair("take_id", request.TakeId ?? Empty),
            Pair("finished", request.Finished ? "1" : "0"),
            Pair("time_start", request.TimeStartUnixSeconds.ToString(CultureInfo.InvariantCulture)),
            Pair("time_used", request.TimeUsedSeconds.ToString("F3", CultureInfo.InvariantCulture)),
            Pair("score", request.Score.ToString(CultureInfo.InvariantCulture)),
            Pair("paper_score", request.PaperScore.ToString(CultureInfo.InvariantCulture)),
            Pair("question_category", request.QuestionCategory ?? Empty),
            Pair("exam_sco_iid", Format(request.ExamScoIid)),
            Pair("practice_iid", Format(request.PracticeIid)),
            Pair("course_iid", Format(request.CourseIid)),
            Pair("should_save_progress_for_course", request.SaveProgressForCourse ? "true" : "false"),
            Pair("lang", "vn")
        ]);

        for (var index = 0; index < request.Items.Count; index++)
        {
            var item = request.Items[index];
            var prefix = $"take_info[{index}]";
            form.Add(Pair($"{prefix}[iid]", item.QuestionId));
            form.Add(Pair($"{prefix}[touched]", item.Touched ? "1" : "0"));
            form.Add(Pair($"{prefix}[correct]", item.Correct ? "1" : "0"));
            form.Add(Pair($"{prefix}[incorrect]", item.Correct ? "0" : "1"));
            form.Add(Pair($"{prefix}[last]", item.Correct ? "correct" : "incorrect"));
            form.Add(Pair(
                $"{prefix}[spentTime]",
                item.SpentSeconds.ToString("F3", CultureInfo.InvariantCulture)));
            form.Add(Pair($"{prefix}[score]", item.Score.ToString(CultureInfo.InvariantCulture)));
            form.Add(Pair($"{prefix}[percent]", item.Percent.ToString(CultureInfo.InvariantCulture)));
            form.Add(Pair($"{prefix}[weighted]", Format(item.Weight)));
            form.Add(Pair(
                $"spent_time_each_question[{item.QuestionId}][doing]",
                Format(item.DoingMilliseconds)));
            form.Add(Pair(
                $"spent_time_each_question[{item.QuestionId}][viewkey]",
                Format(item.ViewKeyMilliseconds)));
        }

        return _transport.PostMultipartAsync(endpoint, form, webUrl, session, cancellationToken);
    }

    private List<KeyValuePair<string, string>> BuildAuthenticatedForm(
        LotusLmsSession session,
        string webUrl,
        string? requestIdOverride = null,
        string? requestTokenOverride = null)
    {
        var form = BuildIdentityForm(session);
        form.Add(Pair("_sand_domain", session.Provider?.Tenant ?? _options.Tenant));
        form.Add(Pair("_sand_web_url", webUrl));
        form.Add(Pair("_sand_device_uuid", session.DeviceUuid));
        form.Add(Pair("_sand_session_id", session.SessionId));
        form.Add(Pair("_sand_user_agent", _options.UserAgent));

        var requestId = !string.IsNullOrWhiteSpace(requestIdOverride)
            ? requestIdOverride
            : session.StudentRequestId ?? session.RequestId;
        var requestToken = !string.IsNullOrWhiteSpace(requestTokenOverride)
            ? requestTokenOverride
            : session.StudentRequestToken ?? session.RequestToken;

        AddIfPresent(form, "_sand_ri", requestId);
        AddIfPresent(form, "_sand_rit", requestToken);
        return form;
    }

    private static List<KeyValuePair<string, string>> BuildIdentityForm(LotusLmsSession session)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            Pair("_sand_token", session.Token),
            Pair("_sand_uiid", Format(session.UserIid)),
            Pair("_sand_uid", session.UserId)
        };
        AddIfPresent(form, "_sand_client_sync_token", session.ClientSyncToken);
        return form;
    }

    private List<KeyValuePair<string, string>> BuildBrowserQuery(LotusLmsSession session) =>
    [
        Pair("submit", "1"),
        Pair("_sand_ajax", "1"),
        Pair("_sand_platform", "3"),
        Pair("_sand_readmin", "1"),
        Pair("_sand_is_wan", "false"),
        Pair("_sand_ga_sessionToken", Empty),
        Pair("_sand_ga_browserToken", Empty),
        Pair("_sand_domain", session.Provider?.Tenant ?? _options.Tenant),
        Pair("_sand_masked", Empty),
        Pair("_sand_session_id", session.SessionId),
        Pair("_sand_use_internal_network", "0"),
        Pair("allow_cache_api_cdn", "1"),
        Pair("_sand_user_agent", _options.UserAgent)
    ];

    private void AddRequestFlags(ICollection<KeyValuePair<string, string>> form)
    {
        form.Add(Pair("submit", "1"));
        form.Add(Pair("_sand_ajax", "1"));
        form.Add(Pair("_sand_platform", "3"));
        form.Add(Pair("_sand_readmin", "1"));
        form.Add(Pair("_sand_is_wan", "false"));
        form.Add(Pair("_sand_ga_sessionToken", Empty));
        form.Add(Pair("_sand_ga_browserToken", Empty));
        form.Add(Pair("_sand_masked", Empty));
        form.Add(Pair("_sand_use_internal_network", "0"));
        form.Add(Pair("allow_cache_api_cdn", "1"));
    }

    private static void ValidateSession(LotusLmsSession? session)
    {
        ArgumentNullException.ThrowIfNull(session);
        RequireText(session.Token, nameof(session.Token));
        RequireText(session.UserId, nameof(session.UserId));
        RequireText(session.DeviceUuid, nameof(session.DeviceUuid));
        RequireText(session.SessionId, nameof(session.SessionId));
        _ = ValidateProvider(session.Provider);
        EnsurePositive(session.UserIid, nameof(session.UserIid));
    }

    private static void ValidatePage(int page, int itemsPerPage)
    {
        EnsurePositive(page, nameof(page));
        if (itemsPerPage is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(
                nameof(itemsPerPage),
                "Page size must be between 1 and 200.");
        }
    }

    private static void ValidatePracticeTake(SavePracticeTakeRequest? request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsurePositive(request.PracticeIid, nameof(request.PracticeIid));
        EnsurePositive(request.ExamScoIid, nameof(request.ExamScoIid));
        EnsurePositive(request.CourseIid, nameof(request.CourseIid));

        if (request.TimeStartUnixSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.TimeStartUnixSeconds));
        }

        if (!double.IsFinite(request.TimeUsedSeconds) || request.TimeUsedSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.TimeUsedSeconds));
        }

        if (request.Score < 0 || request.PaperScore < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Scores cannot be negative.");
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            throw new ArgumentException("At least one practice result is required.", nameof(request));
        }

        if (request.Items.Count > 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "A practice batch cannot contain more than 500 results.");
        }

        var questionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in request.Items)
        {
            ArgumentNullException.ThrowIfNull(item);
            var questionId = RequireText(item.QuestionId, nameof(item.QuestionId));
            if (questionId.Length > 128
                || questionId.Any(character =>
                    !char.IsLetterOrDigit(character) && character is not '_' and not '-' and not '.'))
            {
                throw new ArgumentException(
                    "Practice question ids may only contain letters, digits, '_', '-' or '.'.",
                    nameof(request));
            }

            if (!questionIds.Add(questionId))
            {
                throw new ArgumentException(
                    $"Duplicate practice question id '{questionId}'.",
                    nameof(request));
            }

            if (!double.IsFinite(item.SpentSeconds) || item.SpentSeconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(item.SpentSeconds));
            }

            if (item.Score < 0 || item.Percent < 0 || item.Weight < 0
                || item.DoingMilliseconds < 0 || item.ViewKeyMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    "Practice result values cannot be negative.");
            }
        }
    }

    private string BuildWebUrl(string relativePath, LotusLmsProvider? provider = null) =>
        new Uri(
            new Uri((provider?.WebOrigin ?? _options.WebOrigin).TrimEnd('/') + "/", UriKind.Absolute),
            relativePath.TrimStart('/'))
        .ToString();

    private static LotusLmsProvider? ValidateProvider(LotusLmsProvider? provider)
    {
        if (provider is null)
        {
            return null;
        }

        var baseUrl = RequireProviderUrl(provider.BaseUrl, nameof(provider.BaseUrl), requireOrigin: false);
        var webOrigin = RequireProviderUrl(provider.WebOrigin, nameof(provider.WebOrigin), requireOrigin: true);
        var tenant = RequireText(provider.Tenant, nameof(provider.Tenant));
        return new LotusLmsProvider
        {
            BaseUrl = baseUrl.TrimEnd('/'),
            WebOrigin = webOrigin.TrimEnd('/'),
            Tenant = tenant
        };
    }

    private static string RequireProviderUrl(string? value, string parameterName, bool requireOrigin)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || uri.UserInfo.Length != 0
            || uri.Query.Length != 0
            || uri.Fragment.Length != 0
            || (requireOrigin && uri.AbsolutePath != "/"))
        {
            throw new ArgumentException("Provider URLs must be absolute HTTP(S) URLs without credentials, query, or fragment.", parameterName);
        }

        return uri.ToString();
    }

    private static string BuildRelativeUri(
        string path,
        IEnumerable<KeyValuePair<string, string>> query)
    {
        var encoded = string.Join(
            "&",
            query.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        return string.IsNullOrEmpty(encoded) ? path.TrimStart('/') : $"{path.TrimStart('/')}?{encoded}";
    }

    private static LotusLmsApiException BusinessError(
        string endpoint,
        string message,
        JsonObject response) =>
        new(message, endpoint, HttpStatusCode.OK, ReadServerMessage(response));

    private static string ReadRequiredString(JsonObject source, string name, string endpoint) =>
        ReadString(source[name])
        ?? throw new LotusLmsApiException(
            $"LotusLMS response is missing required field '{name}'.",
            endpoint,
            HttpStatusCode.OK);

    private static string? ReadServerMessage(JsonObject? root)
    {
        if (root is null)
        {
            return null;
        }

        var message = ReadString(root["message"])
            ?? ReadString(root["error"])
            ?? ReadString(root["error_message"]);

        return message is { Length: > 512 } ? message[..512] : message;
    }

    private static string? ReadString(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<string>(out var text))
        {
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        var scalar = value.ToString();
        return string.IsNullOrWhiteSpace(scalar) ? null : scalar;
    }

    private static int ReadInt(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<int>(out var number)
            ? number
            : int.TryParse(ReadString(node), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                ? number
                : 0;

    private static void EnsurePositive(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Value must be greater than zero.");
        }
    }

    private static string RequireText(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        return value.Trim();
    }

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static KeyValuePair<string, string> Pair(string key, string value) => new(key, value);

    private static void AddIfPresent(
        ICollection<KeyValuePair<string, string>> target,
        string key,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target.Add(Pair(key, value));
        }
    }
}
