using LotusLMS.Api;
using LotusLMS.Client;
using LotusLMS.Worker.V2.Accounts;
using LotusLMS.Worker.V2.LotusRunner.Theory;
namespace LotusLMS.Worker.V2.LotusRunner.CheckingTime;

internal sealed class CheckingTimeService(ILotusLmsApiClient client,
    ILogger<CheckingTimeService> logger,
    LearningTimeTrackingService learningTimeTrackingService,
    PracticeServices practiceServices,
    TheoryServices theoryServices,
    ProxyIpChecker proxyIpChecker)
{
    public async Task<AccountStudyResult> Runner(
        LotusLmsLoginRequest loginRequest,
        int IID_enrolment_plan,
        bool hasProxy,
        Func<string, CancellationToken, Task> sendClientLog,
        CancellationToken token,
        IReadOnlyCollection<string>? runSections = null,
        Action<ClientCurrentLesson?>? reportCurrentLesson = null)
    {
        reportCurrentLesson?.Invoke(null);
        var sections = runSections ?? ClientRunSections.All;
        if (!ClientRunSections.IsValid(sections))
            throw new InvalidDataException("Chọn ít nhất một phần cần chạy hợp lệ.");
        var lotusLmsSession = await CretificateAccountAsync(loginRequest, token);
        if (hasProxy)
        {
            var proxyIp = await proxyIpChecker.GetProxyIpAsync(token);
            var message = $"Đăng nhập thành công qua proxy. IP proxy: {proxyIp}.";
            logger.LogInformation("{Message}", message);
            await sendClientLog(message, token);
        }

        if (await LearningTimeTracking(lotusLmsSession, sendClientLog, token))
        {
            return AccountStudyResult.DailyKpiReached;
        }

        Task<StudyPlan> RefreshStudyPlan() => learningTimeTrackingService.IdeaAutoStudy(
            lotusLmsSession, IID_enrolment_plan, sendClientLog, token, sections);

        var studyPlan = await RefreshStudyPlan();
        if (studyPlan.CompletionResult != AccountStudyResult.Pending)
        {
            return studyPlan.CompletionResult;
        }

        if (sections.Contains(ClientRunSections.Review))
        {
            var practiceResult = await practiceServices.RunAsync(
                lotusLmsSession,
                studyPlan.Items,
                RefreshStudyPlan,
                sendClientLog,
                token,
                reportCurrentLesson);
            if (practiceResult != AccountStudyResult.Pending)
            {
                return practiceResult;
            }
        }

        var refreshedPlan = await RefreshStudyPlan();
        if (refreshedPlan.CompletionResult != AccountStudyResult.Pending)
        {
            return refreshedPlan.CompletionResult;
        }

        if (refreshedPlan.Items.Any(item => item.typeAutoStudy is
                CheckingTimeDTO.TypeAutoStudy.Exam or CheckingTimeDTO.TypeAutoStudy.PracticeSpecialOnly))
        {
            const string message = "Phần kiểm tra hoặc thực hành mô phỏng đã chọn còn bài chưa hoàn thành; worker chưa hỗ trợ thực thi các phần này.";
            logger.LogInformation("{Message}", message);
            await sendClientLog(message, token);
        }

        return sections.Contains(ClientRunSections.Theory)
            ? await theoryServices.RunAsync(
                lotusLmsSession,
                refreshedPlan.Items,
                RefreshStudyPlan,
                sendClientLog,
                token,
                reportCurrentLesson)
            : AccountStudyResult.Pending;
    }

    public async Task<LotusLmsSession> CretificateAccountAsync(LotusLmsLoginRequest lotusLmsLoginRequest, CancellationToken token)
    {
        logger.LogInformation("---- Đăng nhập vào lotus API. {Username}", lotusLmsLoginRequest.Username);
        var login = await client.LoginAsync(lotusLmsLoginRequest, token);
        if (login.Token is null) { throw new Exception("Lỗi không thể lấy token định danh"); }
        return login;
    }
    public async Task<bool> LearningTimeTracking(
        LotusLmsSession lotusLmsSession,
        Func<string, CancellationToken, Task> sendClientLog,
        CancellationToken token)
    {
        return await learningTimeTrackingService.TrackAsync(lotusLmsSession, token, sendClientLog);
    }


}
