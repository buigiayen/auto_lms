using LotusLMS.Api;
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
        CancellationToken token)
    {
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

        var studyPlan = await learningTimeTrackingService.IdeaAutoStudy(
            lotusLmsSession,
            IID_enrolment_plan,
            sendClientLog,
            token);
        if (studyPlan.IsCompleted)
        {
            return AccountStudyResult.Completed;
        }

        var practiceResult = await practiceServices.RunAsync(
            lotusLmsSession,
            studyPlan.Items,
            async () => await learningTimeTrackingService.IdeaAutoStudy(lotusLmsSession, IID_enrolment_plan, sendClientLog, token),
            sendClientLog,
            token);
        if (practiceResult == AccountStudyResult.DailyKpiReached)
        {
            return practiceResult;
        }

        var refreshedPlan = await learningTimeTrackingService.IdeaAutoStudy(
            lotusLmsSession,
            IID_enrolment_plan,
            sendClientLog,
            token);
        if (refreshedPlan.IsCompleted)
        {
            return AccountStudyResult.Completed;
        }

        return await theoryServices.RunAsync(
            lotusLmsSession,
            refreshedPlan.Items,
            () => learningTimeTrackingService.IdeaAutoStudy(
                lotusLmsSession,
                IID_enrolment_plan,
                sendClientLog,
                token),
            sendClientLog,
            token);
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