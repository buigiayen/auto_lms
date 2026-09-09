# LotusLMS.Worker

Linux-ready .NET worker host. The initial host only wires dependency injection and
waits for a future job source; it does not call any LotusLMS mutation automatically.

```bash
dotnet restore LotusLMS.Worker.sln
dotnet build LotusLMS.Worker.sln --no-restore
dotnet run --project LotusLMS.Worker/LotusLMS.Worker.csproj
```

## Configuration

Configuration can be supplied with `appsettings.json` or environment variables.
Use double underscores for nested keys, for example:

```bash
Http__Proxy__Enabled=true
Http__Proxy__Address=http://proxy.example:8080
Http__Proxy__Username=proxy-user
Http__Proxy__Password=proxy-password
LotusLms__BaseUrl=https://staging-api.lotuslms.com
LotusLms__WebOrigin=https://yenthuy.huelms.com
LotusLms__LogApiResponses=true
LotusLms__ApiResponseLogDirectory=logs/api-responses
```

Every HTTP response is appended as one JSON object per line to a file named
from its URI path, for example `course_api_save-item-learning-time.jsonl`.
Repeated calls to the same URI append to the same file. Query strings are not
included in file names. These files contain raw response bodies and headers,
including account/session data, so the default directory is excluded from Git
and files are owner-readable only on Unix-like systems. A response-log write
failure stops the current API operation instead of silently losing the record.

Keep proxy credentials and future LotusLMS account credentials in a secret store;
do not commit them to configuration files.

## Read-only account probe

The worker has an opt-in, bounded probe that logs in, enumerates enrolment plans
and courses, and validates that each course learning tree can be read and
deserialized. It never calls learning-time mutation, tracking, or practice-save
endpoints.

For local development, store credentials outside the repository with .NET user
secrets. On macOS/Linux this store is a local plaintext JSON file protected by the
current user's filesystem permissions; it is not copied to build output or committed.

```bash
dotnet user-secrets set "LotusLmsCredentials:Username" "account-name" \
  --project LotusLMS.Worker/LotusLMS.Worker.csproj
dotnet user-secrets set "LotusLmsCredentials:Password" "account-password" \
  --project LotusLMS.Worker/LotusLMS.Worker.csproj
dotnet user-secrets list --project LotusLMS.Worker/LotusLMS.Worker.csproj

Worker__Mode=ReadOnlyProbe \
  dotnet run --project LotusLMS.Worker/LotusLMS.Worker.csproj
```

If a tenant requires request context values, store `RequestId`, `RequestToken`,
`StudentRequestId`, `StudentRequestToken`, and `ClientSyncToken` under the same
section. `RequestId`/`RequestToken` are sent on login; `StudentRequestId`/
`StudentRequestToken` are sent on student and learning-time endpoints and fall
back to the login values when omitted. Practice endpoints instead use the
per-exam request context returned by `get-practice-detail` (`exam_sco.ri`/`rit`),
matching the web client. To bind the session to a fixed device/browser identity
(matching the web client), set `DeviceUuid` and `SessionId`; when omitted the API
library generates a new GUID per login. Never copy captured tokens into source.

Use an environment-specific secret provider for a deployed worker. The probe refuses
to send credentials unless both the API base URL and web origin use HTTPS.

## Auto-study job

The worker can also run a bounded job that mirrors the active web-client flow:
login, read the first enrolment plan, walk each course's learning tree in order,
and submit `save-item-learning-time` only for incomplete `video` nodes. `sco`
nodes are aggregate/grouping parents and never receive learning-time saves.

```bash
dotnet user-secrets set "LotusLmsCredentials:ClientSyncToken" "n:c1da89127987252489739834" \
  --project LotusLMS.Worker/LotusLMS.Worker.csproj

Worker__Mode=AutoStudy \
  dotnet run --project LotusLMS.Worker/LotusLMS.Worker.csproj
```

Behaviour is controlled through the `Worker` section (defaults shown):

- `SaveItemSeconds` (15): wait interval and seconds per
  `save-item-learning-time` call.
- `TrackTimeSeconds` (15): seconds sent to `track-time` when KPI stopping is enabled.
- `KpiCheckEverySaves` (3): when KPI stopping is enabled, check daily KPI and
  send `track-time` every N saves.
- `StopOnDailyKpi` (false): optionally stop the whole job as soon as KPI is reached.
  The default continues to the next course after all required videos are complete.
- `IncludePractice` (false): optionally run the practice auto-complete step.
- `PracticeBatchSize` (5): questions submitted per `save-take` batch.
- `MarkPracticeFinished` (false): mark the last batch as finished. The current
  web client always submits `finished=0`, so this stays disabled for parity.
- `OnlyFirstPlan` (true) / `OnlyFirstCoursePage` (true): match the web client,
  which only reads the first plan and page 1 of courses.

> Auto-study sends real mutations to the LotusLMS account. Use it only for the
> account you are authorised to operate. For a short first test, explicitly set
> `Worker__StopOnDailyKpi=true`.
