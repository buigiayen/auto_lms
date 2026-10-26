using System.Net;
using LotusLMS.Api;
using LotusLMS.Worker.V2.LotusRunner.CheckingTime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

internal static class KpiResponseChecks
{
    public static async Task RunAsync(LotusLmsSession session, Action<bool, string> check)
    {
        foreach (var asStrings in new[] { false, true })
        {
            object Seconds(double value) => asStrings
                ? value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : value;
            using var handler = new Fixture((_, _) => Task.FromResult(Fixture.Json(new
            {
                success = true,
                result = new
                {
                    today_gained_kpi_time = new
                    {
                        total_gained_kpi_time_in_seconds = Seconds(1234.5),
                        total_gained_kpi_time_in_seconds_by_online_learning = Seconds(999.5),
                        max_gained_kpi_time_in_seconds = Seconds(3600.75),
                        max_online_learning_time_in_seconds = Seconds(2000.125)
                    }
                }
            })));
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://lms.invalid") };
            var api = new LotusLmsApiClient(http, Options.Create(new LotusLmsOptions { LogApiResponses = false }));
            var result = await api.GetTodayKpiTimeAsync(session, 110);
            var kpi = result.TodayGainedKpiTime;
            check(kpi.TotalGainedKpiTimeInSeconds == 1234.5
                && kpi.TotalGainedKpiTimeInSecondsByOnlineLearning == 999.5
                && kpi.MaxGainedKpiTimeInSeconds == 3600.75
                && kpi.MaxOnlineLearningTimeInSeconds == 2000.125,
                $"KPI duration fractions survive HTTP deserialization (string values: {asStrings})");
            check(!kpi.Full, "fractional KPI values retain the existing completion threshold");

            var logs = new List<string>();
            var service = new LearningTimeTrackingService(api, NullLogger<LearningTimeTrackingService>.Instance);
            check(!await service.TrackAsync(session, default, (message, _) =>
                { logs.Add(message); return Task.CompletedTask; }) && logs.Count == 2,
                "worker KPI check continues and logs progress after a fractional response");
        }

        foreach (var body in new[]
        {
            """{"success":true,"result":{"today_gained_kpi_time":{"total_gained_kpi_time_in_seconds":123,"max_gained_kpi_time_in_seconds":3600,"total_gained_kpi_time_in_seconds_by_online_learning":1001,"max_online_learning_time_in_seconds":2000}}}""",
            """{"success":true,"result":{"today_gained_kpi_time":{"total_gained_kpi_time_in_seconds":"123","max_gained_kpi_time_in_seconds":"3600","total_gained_kpi_time_in_seconds_by_online_learning":"1001","max_online_learning_time_in_seconds":"2000"}}}"""
        })
        {
            using var handler = new Fixture((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(body) }));
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://lms.invalid") };
            var api = new LotusLmsApiClient(http, Options.Create(new LotusLmsOptions { LogApiResponses = false }));
            var result = await api.GetTodayKpiTimeAsync(session, 110);
            check(result.TodayGainedKpiTime.TotalGainedKpiTimeInSeconds == 123 && result.TodayGainedKpiTime.Full,
                "integer KPI values remain compatible and retain the existing completion threshold");
        }
    }
}
