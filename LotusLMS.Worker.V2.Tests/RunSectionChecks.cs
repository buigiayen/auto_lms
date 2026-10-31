using System.Reflection;
using System.Text.Json.Nodes;
using LotusLMS.Api;
using LotusLMS.Client;
using LotusLMS.Worker.V2;
using LotusLMS.Worker.V2.Accounts;
using LotusLMS.Worker.V2.LotusRunner.CheckingTime;
using LotusLMS.Worker.V2.LotusRunner.Theory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

internal static class RunSectionChecks
{
    public static async Task RunAsync(LotusLmsSession session, Action<bool, string> check)
    {
        check(Fixture.Read(Fixture.Assignment()).RunSections.SequenceEqual(ClientRunSections.All),
            "older assignment responses default to all run sections");
        check(!ClientRunSections.IsValid([]) && !ClientRunSections.IsValid(["unknown"])
            && !ClientRunSections.IsValid(["theory", "theory"]), "invalid section selections are rejected");

        var assignment = Fixture.Assignment();
        assignment["runSections"] = new JsonArray("theory", "review");
        using (var context = Fixture.Context(assignment))
        {
            var control = (JsonObject)assignment.DeepClone();
            control["runSections"] = new JsonArray("review", "theory");
            context.UpdateControl(Fixture.Read(control), TimeProvider.System.GetTimestamp());
            check(!context.StoppingToken.IsCancellationRequested, "section order does not stop an owned run");
            control["runSections"] = new JsonArray("review");
            context.UpdateControl(Fixture.Read(control), TimeProvider.System.GetTimestamp());
            check(context.StoppingToken.IsCancellationRequested && context.StopReason.Contains("Phần cần chạy"),
                "changing selected sections stops the old account context");
        }

        var expectedTypes = new Dictionary<string, CheckingTimeDTO.TypeAutoStudy>
        {
            [ClientRunSections.Exam] = CheckingTimeDTO.TypeAutoStudy.Exam,
            [ClientRunSections.Review] = CheckingTimeDTO.TypeAutoStudy.PracticeOnly,
            [ClientRunSections.Theory] = CheckingTimeDTO.TypeAutoStudy.AutoStudy,
            [ClientRunSections.Practical] = CheckingTimeDTO.TypeAutoStudy.PracticeSpecialOnly
        };
        foreach (var (section, expectedType) in expectedTypes)
        {
            var api = DispatchProxy.Create<ILotusLmsApiClient, RunSectionApi>();
            var fake = (RunSectionApi)(object)api;
            var tracking = new LearningTimeTrackingService(api, NullLogger<LearningTimeTrackingService>.Instance);
            var plan = await tracking.IdeaAutoStudy(session, 99, (_, _) => Task.CompletedTask, default, [section]);
            check(plan.Items.Count == 1 && plan.Items[0].typeAutoStudy == expectedType,
                $"the {section} selection includes only its matching study type");
            check((fake.TheoryReads > 0) == (section == ClientRunSections.Theory),
                $"the {section} selection does not load excluded theory details");
            if (section is ClientRunSections.Theory or ClientRunSections.Review)
                check(plan.Items[0].courseName == "Theory and review"
                    && plan.Items[0].name == (section == ClientRunSections.Theory ? "Theory video" : "Review"),
                    "study planning keeps the parent course and child lesson names separately");
        }

        foreach (var sections in new IReadOnlyList<string>[]
                 { ["theory"], ["review"], ["review", "theory"], ["exam"], ["practical"] })
        {
            var api = DispatchProxy.Create<ILotusLmsApiClient, RunSectionApi>();
            var fake = (RunSectionApi)(object)api;
            var tracking = new LearningTimeTrackingService(api, NullLogger<LearningTimeTrackingService>.Instance);
            var options = Options.Create(new WorkerOptions
            {
                SaveItemSeconds = 1, PracticeBatchSize = 1, PracticeIntervalSeconds = 1, PracticeMaxIntervalSeconds = 1
            });
            var runner = new CheckingTimeService(api, NullLogger<CheckingTimeService>.Instance, tracking,
                new PracticeServices(api, TimeProvider.System, tracking, NullLogger<PracticeServices>.Instance, options),
                new TheoryServices(api, TimeProvider.System, options, tracking, NullLogger<TheoryServices>.Instance),
                new ProxyIpChecker(new AccountWebProxyProvider()));
            fake.Session = session;
            var logs = new List<string>();
            var lessons = new List<ClientCurrentLesson?>();
            var result = await runner.Runner(new LotusLmsLoginRequest { Username = "fixture", Password = "fixture" },
                99, false, (message, _) => { logs.Add(message); return Task.CompletedTask; }, default, sections, lessons.Add);
            check(fake.TheorySaves == (sections.Contains("theory") ? 1 : 0)
                && fake.ReviewSaves == (sections.Contains("review") ? 1 : 0),
                $"Runner preserves the {string.Join(',', sections)} filter through all progress refreshes");
            var executable = sections.Contains("theory") || sections.Contains("review");
            var selectedLessons = lessons.OfType<ClientCurrentLesson>().ToList();
            check(selectedLessons.Count == sections.Count(section => section is "theory" or "review")
                && selectedLessons.All(lesson => lesson.CourseIid == 99 && lesson.CourseName == "Theory and review"),
                "runner reports the correct parent for each selected lesson");
            check(selectedLessons.All(lesson => lesson.Section == "theory"
                    ? lesson.ItemIid == 44 && lesson.ItemName == "Theory video" && lesson.ItemType == "video"
                    : lesson.ItemIid == 100 && lesson.ItemName == "Review" && lesson.ItemType == "practice")
                && lessons.Last() is null,
                "theory and review report their child IDs, names and types, then clear the current lesson");
            check(result == (executable ? AccountStudyResult.SelectedSectionsCompleted : AccountStudyResult.Pending),
                "finishing selected parts does not report the entire enrolment plan completed");
            if (!executable)
                check(logs.Any(message => message.Contains("chưa hỗ trợ")),
                    "pending exam and simulation tasks report the missing execution support");
        }

        var completed = new StudyPlan([], IsCompleted: true);
        check(completed.CompletionResult == AccountStudyResult.Completed,
            "full LMS completion remains distinct from selected-section completion");
    }
}

internal class RunSectionApi : DispatchProxy
{
    public LotusLmsSession Session { get; set; } = null!;
    public int TheoryReads { get; private set; }
    public int TheorySaves { get; private set; }
    public int ReviewSaves { get; private set; }

    protected override object Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
    {
        nameof(ILotusLmsApiClient.LoginAsync) => Task.FromResult(Session),
        nameof(ILotusLmsApiClient.GetStudentCoursesAsync) => Task.FromResult(new List<Student_course_in_enrolment_plan_view>
        {
            Course(99, "Theory and review", TheorySaves > 0, ReviewSaves > 0),
            Course(100, "Mô phỏng các tình huống giao thông", true, false)
        }),
        nameof(ILotusLmsApiClient.GetDetailLearningTimeAsync) => TheoryDetails(),
        nameof(ILotusLmsApiClient.SaveItemLearningTimeAsync) => SaveTheory(),
        nameof(ILotusLmsApiClient.GetTodayKpiTimeAsync) => Task.FromResult(new GetToDayKpiTime
            { TodayGainedKpiTime = new TodayGainedKpiTime { MaxOnlineLearningTimeInSeconds = 43200 } }),
        nameof(ILotusLmsApiClient.GetPracticeDetailAsync) => Task.FromResult(new BaseApi<GetPracticeDetail>
            { Result = new GetPracticeDetail { _expand = new _expand { ExamSco = new ExamSco { Iid = 33, QuestionBanks = [44] } } } }),
        nameof(ILotusLmsApiClient.GetQuestionBankStatusAsync) => Task.FromResult(new BaseApi<GetQuestionBankStatus>
        {
            Result = new GetQuestionBankStatus
            {
                Questions = new Questions
                {
                    Result = [new ResultQuestions { Iid = "question-1", McAnswers = [new McAnswers { IsAnswer = 1 }] }]
                }
            }
        }),
        nameof(ILotusLmsApiClient.StartPracticeAsync) => Task.FromResult(new JsonObject
            { ["result"] = new JsonObject { ["id"] = "fixture-take" } }),
        nameof(ILotusLmsApiClient.SavePracticeTakeAsync) => SaveReview(),
        nameof(ILotusLmsApiClient.TrackPracticeTimeAsync) => Task.FromResult(new JsonObject()),
        _ => throw new NotSupportedException($"Excluded LMS API call: {method?.Name}")
    };

    private Task<BaseApi<ResultDetailLearningTime>> TheoryDetails()
    {
        TheoryReads++;
        return Task.FromResult(new BaseApi<ResultDetailLearningTime>
        {
            Result = new ResultDetailLearningTime
            {
                Iid = 99,
                Children = [new LearningTimeNode
                {
                    Iid = 44, Name = "Theory video", Ntype = "video", MinLearningTime = 1,
                    ActualLearningTime = TheorySaves
                }]
            }
        });
    }

    private Task<JsonObject> SaveTheory()
    {
        TheorySaves++;
        return Task.FromResult(new JsonObject());
    }

    private Task<JsonObject> SaveReview()
    {
        ReviewSaves++;
        return Task.FromResult(new JsonObject());
    }

    private static Student_course_in_enrolment_plan_view Course(int iid, string name, bool theoryPassed, bool reviewPassed) => new()
    {
        Course = new Course
        {
            Iid = iid, Name = name,
            __Expand = new LotusLMS.Api._Expand
            {
                ScoreByRubric = new ScoreByRubric
                {
                    Passed = false,
                    score_by_rubrik =
                    [
                        new ScoreByRubik { name = "Ôn luyện", Passed = reviewPassed },
                        new ScoreByRubik { name = "Xem giáo trình", Passed = theoryPassed },
                        new ScoreByRubik { name = "Kiểm tra", Passed = iid == 100 }
                    ]
                },
                Syllabus = new syllabus { children = [new children { iid = iid + 1, name = "Review", ntype = "practice" }] }
            }
        }
    };
}
