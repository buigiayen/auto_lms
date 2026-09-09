namespace LotusLMS.Api;

public sealed record LotusLmsLoginRequest
{
    public required string Username { get; init; }

    public required string Password { get; init; }

    public string? DeviceUuid { get; init; }

    public string? SessionId { get; init; }

    public string? RequestId { get; init; }

    public string? RequestToken { get; init; }

    /// <summary>
    /// Post-login request context (_sand_ri) sent on student, learning-time,
    /// and practice endpoints. Falls back to <see cref="RequestId"/> when unset.
    /// </summary>
    public string? StudentRequestId { get; init; }

    /// <summary>
    /// Post-login request token (_sand_rit) sent on student, learning-time,
    /// and practice endpoints. Falls back to <see cref="RequestToken"/> when unset.
    /// </summary>
    public string? StudentRequestToken { get; init; }

    public string? ClientSyncToken { get; init; }
}

/// <summary>
/// Authentication and browser-session data for one LotusLMS account.
/// Do not share one instance between different accounts.
/// </summary>
public sealed record LotusLmsSession
{
    public required string Token { get; init; }

    public required int UserIid { get; init; }

    public required string UserId { get; init; }

    public required string LoginName { get; init; }

    public required string DisplayName { get; init; }

    public required string DeviceUuid { get; init; }

    public required string SessionId { get; init; }

    public string? RequestId { get; init; }

    public string? RequestToken { get; init; }

    public string? StudentRequestId { get; init; }

    public string? StudentRequestToken { get; init; }

    public string? ClientSyncToken { get; init; }
}

public sealed record SaveItemLearningTimeRequest
{
    public required int ItemIid { get; init; }

    public required string ItemNtype { get; init; }

    public required int CourseIid { get; init; }

    public required int TimeSpentSeconds { get; init; }
}

public sealed record TrackTimeRequest
{
    public required int CourseIid { get; init; }

    public required double TimeSpentSeconds { get; init; }

    public string PageId { get; init; } = "learn";
}

public sealed record PracticeTakeItem
{
    public required string QuestionId { get; init; }

    public bool Touched { get; init; } = true;

    public required bool Correct { get; init; }

    public required double SpentSeconds { get; init; }

    public required decimal Score { get; init; }

    public required decimal Percent { get; init; }

    public int Weight { get; init; } = 1;

    public required int DoingMilliseconds { get; init; }

    public required int ViewKeyMilliseconds { get; init; }
}

/// <summary>
/// Caller-supplied result for one practice batch. The API library does not infer answers,
/// scores, timestamps, or artificial elapsed time.
/// </summary>
public sealed record SavePracticeTakeRequest
{
    public required int PracticeIid { get; init; }

    public required int ExamScoIid { get; init; }

    public required int CourseIid { get; init; }

    public string TakeId { get; init; } = string.Empty;

    public required bool Finished { get; init; }

    public required long TimeStartUnixSeconds { get; init; }

    public required double TimeUsedSeconds { get; init; }

    public required decimal Score { get; init; }

    public required decimal PaperScore { get; init; }

    public string QuestionCategory { get; init; } = string.Empty;

    public bool SaveProgressForCourse { get; init; } = true;

    public required IReadOnlyList<PracticeTakeItem> Items { get; init; }
}
