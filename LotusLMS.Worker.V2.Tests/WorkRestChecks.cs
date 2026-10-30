using LotusLMS.Worker.V2;
using LotusLMS.Worker.V2.Accounts;

internal static class WorkRestChecks
{
    public static void Run(Action<bool, string> check)
    {
        var defaults = new WorkerOptions();
        check(defaults.WorkRestEnabled && defaults.ContinuousWorkMinutes == 180
            && defaults.MinRestMinutes == 15 && defaults.MaxRestMinutes == 30,
            "accounts default to three hours of work followed by a 15–30 minute break");
        check(Enumerable.Range(0, 30).Select(_ => defaults.PickRestSeconds()).All(seconds => seconds is >= 900 and <= 1800)
            && new WorkerOptions { MinRestMinutes = 30, MaxRestMinutes = 30 }.PickRestSeconds() == 1800,
            "break duration respects the inclusive configured bounds, including a fixed duration");
        var invalid = new WorkerOptions { ContinuousWorkMinutes = 0, MinRestMinutes = 20, MaxRestMinutes = 15 }.ValidationErrors().ToArray();
        check(invalid.Any(error => error.Contains("ContinuousWorkMinutes")) && invalid.Any(error => error.Contains("MaxRestMinutes")),
            "invalid cycle settings identify the relevant configuration fields");

        var clock = new ManualClock();
        AccountRunContext Create()
        {
            var id = Guid.NewGuid().ToString();
            var json = Fixture.Assignment(accountId: id);
            json["runId"] = Guid.NewGuid().ToString();
            var assignment = Fixture.Read(json);
            return new(assignment, null, id, Fixture.WorkerName, Fixture.InstanceId,
                clock.GetTimestamp(), clock, "000000000001");
        }
        void Advance(TimeSpan duration, params AccountRunContext[] contexts)
        {
            for (var elapsed = TimeSpan.Zero; elapsed < duration; elapsed += TimeSpan.FromSeconds(10))
            {
                foreach (var context in contexts)
                {
                    var control = Fixture.Assignment(accountId: context.AccountId);
                    control["runId"] = context.Ownership.RunId;
                    context.UpdateControl(Fixture.Read(control), clock.GetTimestamp());
                }
                clock.Advance(TimeSpan.FromSeconds(10));
            }
        }
        using var first = Create();
        first.ScheduleRest(TimeSpan.FromHours(3), 900);
        clock.JumpWallClock(TimeSpan.FromDays(1));
        check(!first.StoppingToken.IsCancellationRequested, "wall-clock changes do not trigger a premature break");
        Advance(TimeSpan.FromMinutes(30), first);
        using var second = Create();
        second.ScheduleRest(TimeSpan.FromHours(3), 1800);
        Advance(TimeSpan.FromMinutes(150) - TimeSpan.FromSeconds(10), first, second);
        check(!first.StoppingToken.IsCancellationRequested && !second.StoppingToken.IsCancellationRequested,
            "heartbeat renewals do not reset a work period and neither account rests early");
        Advance(TimeSpan.FromSeconds(10), first, second);
        check(first.StoppingToken.IsCancellationRequested && first.ScheduledRestSeconds == 900 && !first.LeaseLost
            && !second.StoppingToken.IsCancellationRequested,
            "the first account rests after exactly three hours while a later account continues");
        Advance(TimeSpan.FromMinutes(30), second);
        check(second.StoppingToken.IsCancellationRequested && second.ScheduledRestSeconds == 1800,
            "the second account has its own full work period and selected break");

        using var stopped = Create();
        stopped.ScheduleRest(TimeSpan.FromHours(3), 900);
        stopped.Stop("Backend yêu cầu dừng account.");
        clock.Advance(TimeSpan.FromHours(4));
        check(stopped.ScheduledRestSeconds is null, "a manual stop disables the work timer and never becomes an automatic resume");
        using var lost = Create();
        lost.ScheduleRest(TimeSpan.FromHours(3), 900);
        clock.Advance(TimeSpan.FromSeconds(31));
        clock.Advance(TimeSpan.FromHours(4));
        check(lost.LeaseLost && lost.ScheduledRestSeconds is null, "lease loss keeps its original stop reason rather than scheduling rest");
    }
}
