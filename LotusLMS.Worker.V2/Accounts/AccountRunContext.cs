using System.Net;
using LotusLMS.Api;
using LotusLMS.Client;

namespace LotusLMS.Worker.V2.Accounts;

// One instance owns one account's data, cancellation and monotonic lease deadline.
// Default ToString() deliberately does not print credentials or lease tokens.
internal sealed class AccountRunContext : IDisposable
{
    private readonly ClientAssignment _assignment;
    private readonly ClientProxy? _proxy;
    private readonly TimeProvider _clock;
    private readonly CancellationTokenSource _stop = new();
    private readonly ITimer _timer;
    private ITimer? _restTimer;
    private readonly long _receivedAt;
    private int? _scheduledRestSeconds;
    private readonly object _gate = new();
    private bool _disposed;
    private long _deadline;
    private string _identityNumber;
    public AccountRunContext(ClientAssignment assignment, ClientProxy? proxy, string accountId, string workerName,
        string instanceId, long requestStarted, TimeProvider clock, string identityNumber)
    {
        if (assignment.AccountId != accountId || assignment.WorkerName != workerName
            || assignment.InstanceId != instanceId || !Guid.TryParse(assignment.RunId, out _)
            || string.IsNullOrWhiteSpace(assignment.LeaseToken) || string.IsNullOrWhiteSpace(identityNumber))
            throw new InvalidDataException("Claim response không khớp quyền sở hữu đã yêu cầu.");
        _assignment = assignment;
        _proxy = proxy;
        _clock = clock;
        _receivedAt = requestStarted;
        Ownership = new ClientRunOwnership { RunId = assignment.RunId, InstanceId = instanceId, LeaseToken = assignment.LeaseToken };
        _timer = clock.CreateTimer(_ => CheckDeadline(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _identityNumber = identityNumber;
        try { UpdateControl(assignment, requestStarted); }
        catch { _timer.Dispose(); _stop.Dispose(); throw; }
    }
    public string IdentityNumber => _identityNumber;
    public string AccountId => _assignment.AccountId;
    public string WorkerName => _assignment.WorkerName;
    public int? EnrolmentPlanIid => _assignment.EnrolmentPlanIid;
    public string EnrolmentPlanName => _assignment.EnrolmentPlanName;
    public IReadOnlyList<string> RunSections => _assignment.RunSections;
    public string Provider => _assignment.Provider;
    public bool HasProxy => _proxy is not null;
    public ClientRunOwnership Ownership { get; }
    public CancellationToken StoppingToken => _stop.Token;
    public string StopReason { get; private set; } = "Đã yêu cầu dừng Worker.";
    public bool LeaseLost { get; private set; }
    public int? ScheduledRestSeconds { get { lock (_gate) return _scheduledRestSeconds; } }
    public TimeSpan RemainingLease => TimeSpan.FromSeconds(
        (Interlocked.Read(ref _deadline) - _clock.GetTimestamp()) / (double)_clock.TimestampFrequency);

    public string? ValidateConfiguration()
    {
        if (EnrolmentPlanIid is not > 0) return "Account chưa chọn chương trình học. Chọn chương trình rồi nhấn Run lại.";
        if (!ClientRunSections.IsValid(RunSections)) return "Account chưa chọn phần cần chạy hợp lệ. Chọn ít nhất một phần rồi nhấn Run lại.";
        if (_assignment.Credentials is not { Username.Length: > 0, Password.Length: > 0 } credentials
            || !Guid.TryParse(credentials.DeviceUuid, out _)) return "Account thiếu thông tin đăng nhập hoặc GUID thiết bị hợp lệ.";
        if (Provider is not ("yenthuy" or "truongson") || _assignment.Lms is null
            || _assignment.Lms.Tenant != Provider || !ValidUrl(_assignment.Lms.Base)
            || !ValidUrl(_assignment.Lms.Origin)) return "Cấu hình nhà cung cấp LMS của account không hợp lệ.";
        if (!ValidProxy(_proxy, _assignment.ServerTime)) return "Proxy của account đã tắt, hết hạn hoặc không hợp lệ.";
        return null;
    }

    public void ScheduleRest(TimeSpan workDuration, int restSeconds)
    {
        if (workDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(workDuration));
        if (restSeconds is < 60 or > 86400) throw new ArgumentOutOfRangeException(nameof(restSeconds));
        lock (_gate)
        {
            if (_disposed || _stop.IsCancellationRequested) return;
            if (_restTimer is not null) throw new InvalidOperationException("Work period is already scheduled.");
            _restTimer = _clock.CreateTimer(_ =>
            {
                lock (_gate)
                {
                    if (_disposed || _stop.IsCancellationRequested) return;
                    // The first stop reason wins; a manual stop/lease loss is never turned into an auto-resume.
                    _scheduledRestSeconds = restSeconds;
                    Stop($"Đã chạy đủ {workDuration.TotalMinutes:0.##} phút; nghỉ {restSeconds / 60d:0.##} phút rồi tự chạy tiếp.");
                }
            }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            var remaining = workDuration - _clock.GetElapsedTime(_receivedAt);
            _restTimer.Change(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }
    }

    public void UpdateControl(ClientRunControl control, long requestStarted)
    {
        lock (_gate)
        {
            if (_disposed || StoppingToken.IsCancellationRequested) return;
            if (Interlocked.Read(ref _deadline) != 0 && RemainingLease <= TimeSpan.Zero)
            { Stop("Lease đã hết hạn trước khi nhận control mới.", true); return; }
            ApplyControl(control, requestStarted);
        }
    }

    private void ApplyControl(ClientRunControl control, long requestStarted)
    {
        if (control.AccountId != AccountId || control.WorkerName != WorkerName || control.RunId != Ownership.RunId)
            throw new InvalidDataException("Control response không khớp lượt chạy.");
        var duration = control.LeaseExpiresAt - control.ServerTime;
        if (duration <= TimeSpan.FromSeconds(1) || duration > TimeSpan.FromMinutes(1))
            throw new InvalidDataException("Thời hạn lease từ backend không hợp lệ.");
        if (StoppingToken.IsCancellationRequested) return;
        if (control.Status != "running" || control.State == "stopping") { Stop("Backend yêu cầu dừng account."); return; }
        if (control.State is not ("accepted" or "running")) { Stop("Lượt chạy không còn hoạt động.", true); return; }
        if (control.EnrolmentPlanIid != EnrolmentPlanIid || !SameProxy(control.Proxy, _proxy))
        { Stop("Chương trình hoặc cấu hình proxy đã thay đổi; cần nhận lại account."); return; }
        if (!ClientRunSections.IsValid(control.RunSections)
            || !control.RunSections.Order().SequenceEqual(RunSections.Order()))
        { Stop("Phần cần chạy đã thay đổi; cần nhận lại account."); return; }
        // Count conservatively from request start, excluding response latency and a small margin.
        var lifetime = duration - TimeSpan.FromMilliseconds(250);
        if (_proxy?.ExpiresAt is { } expires && expires - control.ServerTime < lifetime)
            lifetime = expires - control.ServerTime;
        Interlocked.Exchange(ref _deadline, requestStarted + (long)(lifetime.TotalSeconds * _clock.TimestampFrequency));
        CheckDeadline();
    }

    public void Stop(string reason, bool leaseLost = false)
    {
        lock (_gate)
        {
            if (_disposed || _stop.IsCancellationRequested) return;
            StopReason = reason;
            LeaseLost = leaseLost;
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _restTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _stop.Cancel();
        }
    }

    public void EnsureCanUse()
    {
        if (RemainingLease <= TimeSpan.Zero) Stop("Lease đã hết hạn; dừng sử dụng account.", true);
        StoppingToken.ThrowIfCancellationRequested();
        if (ValidateConfiguration() is { } error) throw new InvalidDataException(error);
    }

    // Return a fresh account-specific copy; never mutate shared IOptions.Value.
    public LotusLmsOptions CreateLmsOptions()
    {
        EnsureCanUse();
        return new LotusLmsOptions { BaseUrl = _assignment.Lms!.Base, Tenant = _assignment.Lms.Tenant,
            WebOrigin = _assignment.Lms.Origin, LogApiResponses = false };
    }

    public LotusLmsLoginRequest CreateLoginRequest()
    {
        EnsureCanUse();
        return new LotusLmsLoginRequest { Username = _assignment.Credentials!.Username,
            Password = _assignment.Credentials.Password,
            DeviceUuid = _assignment.Credentials.DeviceUuid,
            Provider = new LotusLmsProvider
            {
                BaseUrl = _assignment.Lms!.Base,
                WebOrigin = _assignment.Lms.Origin,
                Tenant = _assignment.Lms.Tenant
            } };
    }

    public IWebProxy? CreateWebProxy()
    {
        EnsureCanUse();
        if (!WorkerProxyConfiguration.TryCreateWebProxy(_proxy, _assignment.ServerTime, out var proxy))
            throw new InvalidDataException("Proxy của account đã tắt, hết hạn hoặc không hợp lệ.");
        return proxy;
    }

    private void CheckDeadline()
    {
        lock (_gate)
        {
            if (_disposed || _stop.IsCancellationRequested) return;
            var remaining = RemainingLease;
            if (remaining <= TimeSpan.Zero) Stop("Lease hoặc thời hạn proxy đã hết; dừng sử dụng account.", true);
            else _timer.Change(remaining, Timeout.InfiniteTimeSpan);
        }
    }

    private static bool ValidUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 && uri.Query.Length == 0;
    private static bool ValidProxy(ClientProxy? proxy, DateTimeOffset serverTime) =>
        WorkerProxyConfiguration.TryCreateWebProxy(proxy, serverTime, out _);
    private static bool SameProxy(ClientProxy? left, ClientProxy? right) => left is null ? right is null
        : right is not null && left.Id == right.Id && left.Enabled == right.Enabled && left.Address == right.Address
            && left.Username == right.Username && left.Password == right.Password && left.ExpiresAt == right.ExpiresAt;

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            Stop("Lượt chạy đã được giải phóng.");
            _disposed = true;
            _timer.Dispose();
            _restTimer?.Dispose();
            _stop.Dispose();
        }
    }
}
