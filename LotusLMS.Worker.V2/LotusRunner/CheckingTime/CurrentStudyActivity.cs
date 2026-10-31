using LotusLMS.Client;

namespace LotusLMS.Worker.V2.LotusRunner.CheckingTime;

internal sealed class CurrentStudyActivity : IDisposable
{
    private readonly Action<ClientCurrentLesson?>? _report;

    public CurrentStudyActivity(CheckingTimeDTO.AutoStudy item, string section,
        Action<ClientCurrentLesson?>? report)
    {
        _report = report;
        report?.Invoke(new ClientCurrentLesson
        {
            Section = section,
            CourseIid = item.iidRoot!.Value,
            CourseName = Name(item.courseName, $"Học phần {item.iidRoot}"),
            ItemIid = item.iid,
            ItemName = Name(item.name, $"Bài {item.iid}"),
            ItemType = section == ClientRunSections.Review ? "practice" : item.ntype
        });
    }

    private static string Name(string? value, string fallback)
    {
        var name = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return name.Length <= 1000 ? name : name[..1000];
    }

    public void Dispose() => _report?.Invoke(null);
}
