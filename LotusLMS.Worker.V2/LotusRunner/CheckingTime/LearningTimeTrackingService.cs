using LotusLMS.Api;
using LotusLMS.Client;

namespace LotusLMS.Worker.V2.LotusRunner.CheckingTime;

public sealed class LearningTimeTrackingService(
    ILotusLmsApiClient client,
    ILogger<LearningTimeTrackingService> logger)
{
    public async Task<bool> TrackAsync(
        LotusLmsSession lotusLmsSession,
        CancellationToken token,
        Func<string, CancellationToken, Task> sendClientLog)
    {
        const string checkingMessage = "---- Kiểm tra tiến độ KPI 1 ngày.";
        logger.LogInformation("{Message}", checkingMessage);
        await sendClientLog(checkingMessage, token);

        var todayKpiTime = await client.GetTodayKpiTimeAsync(
            lotusLmsSession,
            110,
            cancellationToken: token);
        var kpiMessage = $"[KPI]: {DateTime.Now} =======> " +
            $"{todayKpiTime.TodayGainedKpiTime.TotalGainedKpiTimeInSeconds / 3600} / " +
            $"{todayKpiTime.TodayGainedKpiTime.MaxGainedKpiTimeInSeconds / 3600}";
        logger.LogInformation("{Message}", kpiMessage);
        await sendClientLog(kpiMessage, token);

        if (todayKpiTime.TodayGainedKpiTime.Full)
        {
            const string fullKpiMessage = "Đã đủ KPI trong ngày; tạm dừng xử lý đến ngày mai.";
            logger.LogInformation("{Message}", fullKpiMessage);
            await sendClientLog(fullKpiMessage, token);
        }
        return todayKpiTime.TodayGainedKpiTime.Full;
    }
    public async Task<StudyPlan> IdeaAutoStudy(
        LotusLmsSession lotusLmsSession,
        int IID_enrolment_plan,
        Func<string, CancellationToken, Task> sendClientLog,
        CancellationToken token,
        IReadOnlyCollection<string>? runSections = null)
    {
        var sections = runSections ?? ClientRunSections.All;
        if (!ClientRunSections.IsValid(sections))
            throw new InvalidDataException("Chọn ít nhất một phần cần chạy hợp lệ.");
        logger.LogInformation("---- Lấy báo cáo tiến độ cần học.");
        var ideaAutoStudy = await client.GetStudentCoursesAsync(lotusLmsSession, IID_enrolment_plan, cancellationToken: token);
        var listCourse = GetLeafStudyItems(ideaAutoStudy);
        if (!listCourse.Any())
        {
            throw new InvalidDataException("LotusLMS returned no course items for the selected enrolment plan.");
        }

        var leafStudy = listCourse.Where(p => p.Course?.IsPass == false).ToList();
        if (leafStudy.Count == 0)
        {
            var message = $"Bạn {lotusLmsSession.DisplayName} - {lotusLmsSession.LoginName} đã học đủ, không cần xử lý.";
            logger.LogInformation("{Message}", message);
            await sendClientLog(message, token);
            return new StudyPlan([], IsCompleted: true);
        }

        List<CheckingTimeDTO.AutoStudy> autoStudies = [];

        ///Ôn luyện
        var practiceNotPass = FillterCourse(leafStudy, "Ôn luyện");
        foreach (var item in practiceNotPass)
        {
            if (await ReCalculateCourse(lotusLmsSession, item.Iid, token))
            {
                var message = $"Loại bỏ bài ${item.Name} do đã hoàn thành!";
                logger.LogInformation("{Message}", message);
                await sendClientLog(message, token);
                continue;
            }
            var practice = item.__Expand?.Syllabus?.children?.FirstOrDefault(c => c?.ntype == "practice");
            CheckingTimeDTO.AutoStudy autoStudy = new CheckingTimeDTO.AutoStudy();
            autoStudy.name = practice?.name ?? item.Name;
            autoStudy.courseName = item.Name;
            autoStudy.iid = practice?.iid ?? 0;
            autoStudy.iidRoot = item.Iid;
            autoStudy.typeAutoStudy = item.Name?.Contains("Mô phỏng các tình huống giao thông", StringComparison.CurrentCultureIgnoreCase) == true
                                    ? CheckingTimeDTO.TypeAutoStudy.PracticeSpecialOnly
                                    : CheckingTimeDTO.TypeAutoStudy.PracticeOnly;
            var section = autoStudy.typeAutoStudy == CheckingTimeDTO.TypeAutoStudy.PracticeSpecialOnly
                ? ClientRunSections.Practical : ClientRunSections.Review;
            if (!sections.Contains(section)) continue;
            autoStudies.Add(autoStudy);
        }

        ///Lý thuyết
        var TheoryIsNotPass = sections.Contains(ClientRunSections.Theory)
            ? FillterCourse(leafStudy, "Xem giáo trình") : [];
        foreach (var item in TheoryIsNotPass)
        {
            if (await ReCalculateCourse(lotusLmsSession, item.Iid, token))
            {
                var message = $"Loại bỏ bài ${item.Name} do đã hoàn thành!";
                logger.LogInformation("{Message}", message);
                await sendClientLog(message, token);
                continue;
            }
            var theoryItems = await client.GetDetailLearningTimeAsync(
                lotusLmsSession,
                item.Iid,
                cancellationToken: token);
            var filter = GetVideoAndTextNodes(theoryItems.Result!.Children)
                        .Where(p => string.Equals(p.Ntype, "video", StringComparison.OrdinalIgnoreCase)
                            && !p.IsSuccessLearningTime);


            foreach (var i in filter)
            {
                CheckingTimeDTO.AutoStudy autoStudy = new CheckingTimeDTO.AutoStudy();
                autoStudy.name = i.Name;
                autoStudy.courseName = item.Name;
                autoStudy.iid = i.Iid;
                autoStudy.ntype = i.Ntype ?? "video";
                autoStudy.typeAutoStudy = CheckingTimeDTO.TypeAutoStudy.AutoStudy;
                autoStudy.iidRoot = item.Iid;
                autoStudies.Add(autoStudy);
            }
        }

        ///Kiểm tra
        var exam = sections.Contains(ClientRunSections.Exam)
            ? FillterCourse(leafStudy, "Kiểm tra") : [];
        foreach (var item in exam)
        {
            if (await ReCalculateCourse(lotusLmsSession, item.Iid, token))
            {
                var message = $"Loại bỏ bài ${item.Name} do đã hoàn thành!";
                logger.LogInformation("{Message}", message);
                await sendClientLog(message, token);
                continue;
            }
            CheckingTimeDTO.AutoStudy autoStudy = new CheckingTimeDTO.AutoStudy();
            autoStudy.name = item.Name;
            autoStudy.iid = item.Iid;
            autoStudy.typeAutoStudy = CheckingTimeDTO.TypeAutoStudy.Exam;
            autoStudies.Add(autoStudy);
        }

        string NotiMessage = @$" {DateTime.Now}
                                -- Phần cần chạy: {string.Join(", ", sections)}
                                -- Theo phân tích số bài học cần phải thực hiện là 
                                -- trong đó có ôn luyện còn {autoStudies.Count(item => item.typeAutoStudy == CheckingTimeDTO.TypeAutoStudy.PracticeOnly)}/{listCourse.Count()}
                                -- trong đó có thực hành mô phỏng còn {autoStudies.Count(item => item.typeAutoStudy == CheckingTimeDTO.TypeAutoStudy.PracticeSpecialOnly)}/{listCourse.Count()}
                                -- trong đó có Lý thuyết tự học còn {TheoryIsNotPass.Count()}/{listCourse.Count() - 1}
                                -- Trong đó kiểm tra còn {exam.Count()}/{listCourse.Count()}";
        logger.LogInformation(NotiMessage);

        var groupedItems = autoStudies.GroupBy(item =>
            (Course: item.iidRoot, Item: item.iid, Type: item.typeAutoStudy, Ntype: item.ntype)).ToList();
        logger.LogInformation("PLAN_READ plan={PlanIid} entries={Entries} unique={UniqueEntries}",
            IID_enrolment_plan, autoStudies.Count, groupedItems.Count);
        foreach (var duplicate in groupedItems.Where(group => group.Count() > 1))
            logger.LogWarning(
                "PLAN_DUPLICATE plan={PlanIid} course={CourseIid} item={ItemIid} type={StudyType} rows={Rows}",
                IID_enrolment_plan, duplicate.Key.Course, duplicate.Key.Item, duplicate.Key.Type, duplicate.Count());

        await sendClientLog(NotiMessage, token);
        return new StudyPlan(autoStudies, IsCompleted: false);
    }

    public async Task<bool> ReCalculateCourse(LotusLmsSession lotusLmsSession, int courseID, CancellationToken token)
    {
        var data = await client.getUserScoreRubric(lotusLmsSession, courseID, token);
        var message = $"Fx1: {data.Passed}!";
        logger.LogInformation("{Message}", message);
        return data.Passed == 1;
    }



    private static IEnumerable<Student_course_in_enrolment_plan_view> GetLeafStudyItems(
        IEnumerable<Student_course_in_enrolment_plan_view> items)
    {
        foreach (var item in items)
        {
            if (item.Children is { Count: > 0 })
            {
                foreach (var child in GetLeafStudyItems(item.Children))
                {
                    yield return child;
                }
            }
            else
            {
                yield return item;
            }
        }
    }

    private static IEnumerable<Course> FillterCourse(
        IEnumerable<Student_course_in_enrolment_plan_view> items,
        string fillerString = "Ôn luyện|Xem giáo trình|Kiểm tra")
    {
        var filterTerms = fillerString.Split(
            '|',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return items
            .Where(result => result.Course?.__Expand?.ScoreByRubric?.score_by_rubrik?
                .Any(rubric =>
                    rubric?.Passed == false
                    && filterTerms.Any(term => rubric.name?.Contains(
                        term,
                        StringComparison.CurrentCultureIgnoreCase) == true)) == true)
            .Select(result => result.Course!);
    }

    private static List<LearningTimeNode> GetVideoAndTextNodes(
        IEnumerable<LearningTimeNode> nodes)
    {
        var filteredNodes = new List<LearningTimeNode>();

        foreach (var node in nodes)
        {
            if (string.Equals(node.Ntype, "video", StringComparison.OrdinalIgnoreCase)
                || string.Equals(node.Ntype, "ntext", StringComparison.OrdinalIgnoreCase))
            {
                filteredNodes.Add(node);
            }

            filteredNodes.AddRange(GetVideoAndTextNodes(node.Children));
        }

        return filteredNodes;
    }

}
