using LotusLMS.Api;

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
            $"{todayKpiTime.TodayGainedKpiTime.TotalGainedKpiTimeInSeconds} / " +
            $"{todayKpiTime.TodayGainedKpiTime.MaxGainedKpiTimeInSeconds}";
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
        CancellationToken token)
    {
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
            CheckingTimeDTO.AutoStudy autoStudy = new CheckingTimeDTO.AutoStudy();
            autoStudy.name = item.Name;
            autoStudy.iid = item.__Expand?.Syllabus?.children?.FirstOrDefault(c => c?.ntype == "practice")?.iid ?? 0;
            autoStudy.iidRoot = item.Iid;
            autoStudy.typeAutoStudy = item.Name?.Contains("Mô phòng các tình huống giao thông", StringComparison.CurrentCultureIgnoreCase) == true
                                    ? CheckingTimeDTO.TypeAutoStudy.PracticeSpecialOnly
                                    : CheckingTimeDTO.TypeAutoStudy.PracticeOnly;
            autoStudies.Add(autoStudy);
        }

        ///Lý thuyết
        var TheoryIsNotPass = FillterCourse(leafStudy, "Xem giáo trình");
        foreach (var item in TheoryIsNotPass)
        {
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
                autoStudy.iid = i.Iid;
                autoStudy.ntype = i.Ntype ?? "video";
                autoStudy.typeAutoStudy = CheckingTimeDTO.TypeAutoStudy.AutoStudy;
                autoStudy.iidRoot = item.Iid;
                autoStudies.Add(autoStudy);
            }
        }

        ///Kiểm tra
        var exam = FillterCourse(leafStudy, "Kiểm tra");
        foreach (var item in exam)
        {
            CheckingTimeDTO.AutoStudy autoStudy = new CheckingTimeDTO.AutoStudy();
            autoStudy.name = item.Name;
            autoStudy.iid = item.Iid;
            autoStudy.typeAutoStudy = CheckingTimeDTO.TypeAutoStudy.Exam;
            autoStudies.Add(autoStudy);
        }

        string NotiMessage = @$" {DateTime.Now} 
                                -- Theo phân tích số bài học cần phải thực hiện là 
                                -- trong đó có ôn luyện còn {practiceNotPass.Count()}/{listCourse.Count()}
                                -- trong đó có Lý thuyết tự học còn {TheoryIsNotPass.Count()}/{listCourse.Count() - 1}
                                -- Trong đó kiểm tra còn {exam.Count()}/{listCourse.Count()}";
        logger.LogInformation(NotiMessage);

        await sendClientLog(NotiMessage, token);
        return new StudyPlan(autoStudies, IsCompleted: false);
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
