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
The default launch profile sets `Worker:Mode=Idle`, so it can start before job
targets are configured. To choose another mode with `dotnet run`, pass it as an
application argument (for example `-- --Worker:Mode=PracticeOnly`), or use
`--no-launch-profile` when setting `Worker__Mode` through the environment.
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
dotnet user-secrets set "LotusLmsCredentials:Username" "001075018139" \
  --project LotusLMS.Worker/LotusLMS.Worker.csproj
dotnet user-secrets set "LotusLmsCredentials:Password" "123456" \
  --project LotusLMS.Worker/LotusLMS.Worker.csproj
dotnet user-secrets list --project LotusLMS.Worker/LotusLMS.Worker.csproj

Worker__Mode=ReadOnlyProbe \
  dotnet run --no-launch-profile --project LotusLMS.Worker/LotusLMS.Worker.csproj
```

The API transport automatically generates a fresh `_sand_ri` / `_sand_rit` pair
for login and every subsequent request, including practice requests. Do not copy
`ri`/`rit` from exam JSON or captured requests. The separate
`LotusLMS.RequestSigning` library derives the encryption key from the logged-in
user iid; see its [README](../LotusLMS.RequestSigning/README.md).
Legacy `RequestId`, `RequestToken`, `StudentRequestId`, and `StudentRequestToken`
configuration is ignored unless `LotusLms:GenerateRequestTokens=false`.
`_sand_client_sync_token` is also generated per authenticated request from the
login token and current Unix seconds; captured `ClientSyncToken` configuration
is used only in legacy mode. The practice step reads the learning tree with
`item_ntype=practice` so its item appears in `children`.
`DeviceUuid` and `SessionId` are optional. Leave them blank to generate them
automatically: the device UUID is created once in `.lotuslms/device-uuid.txt` and
reused across restarts, while the session ID is fresh for each login. The device
directory is relative to the current working directory and can be changed through
`LotusLms:DeviceIdentityDirectory`. Docker Compose mounts `.lotuslms` to preserve
the device UUID when recreating the container. Never copy captured tokens into source.

Check automatic UUID creation and persistence without contacting the LMS:

```bash
dotnet run --project LotusLMS.RequestSigning.Tests -- --device-identity-only
```

Use an environment-specific secret provider for a deployed worker. The probe refuses
to send credentials unless both the API base URL and web origin use HTTPS.

## Practice-only job

`Worker:Mode=PracticeOnly` runs the practice lifecycle independently of video
learning. For each configured target, it follows the captured Truong Son flow:

1. Recalculate course practice progress.
2. Read practice detail and resolve the current `exam_sco.iid` and question bank.
3. Read the question bank and sync history.
4. Call `start-practice` and use **its `result.id`** as the new take ID.
5. Save question batches and track real elapsed practice time.

Only course and practice IDs are required. The checked-in target below comes
from the supplied HAR; change it when working on a different course.

```json
{
  "Worker": {
    "Mode": "PracticeOnly",
    "PracticeBatchSize": 5,
    "PracticeIntervalSeconds": 5,
    "PracticeMaxIntervalSeconds": 10,
    "PracticeTargets": [
      {
        "CourseIid": 30877358,
        "PracticeIid": 26150095,
        "Name": "Ôn luyện"
      }
    ]
  }
}
```

Credentials use the existing `LotusLmsCredentials` secret configuration. Targets
can also be supplied through `Worker__PracticeTargets__0__CourseIid` and
`Worker__PracticeTargets__0__PracticeIid`. An empty list or non-positive IDs stop
before login. Legacy `ExamScoIid`, `BankIid`, and `TakeId` target settings are
accepted but ignored: each run reads current metadata and creates a fresh take.
The sync response ID identifies history and is never used as a take ID.

After configuring credentials and targets, run explicitly:

```bash
dotnet run --project LotusLMS.Worker/LotusLMS.Worker.csproj -- --Worker:Mode=PracticeOnly
```

The default launch profile stays `Idle`. Configuration errors print the failing
keys to stderr and exit with code 1.

All practice calls use UTF-8 multipart forms and fresh request signatures. Detail
and bank reads retain query parameters; recalculate, sync, start, tracking and
save send their parameters and flags in the form body. The bank query's
`exam_template_iid` is the expanded **exam SCO ID**, rather than the nested
exercise-template ID.

The worker submits only questions with a valid ID and an answer marked
`is_answer=1` in `mc_answers` or `answers2`; questions without a marked answer are
skipped. Prior `correct > 0` history does not
remove questions from this pass: the captured workflow uses KPI time per
question and repeats previously answered questions. `AlreadyCorrect` reports the
historical count and does not mean those questions were skipped.

Batch size defaults to five, with a smaller final batch. Each submission interval
is randomly selected from 5 through 10 seconds, including the initial wait.
These intervals are worker settings; the HAR contains only one save.
`time_start` remains the practice-entry timestamp throughout a run. Each batch's
elapsed time is measured, distributed across its questions, and recorded as
`doing`; `viewkey=0` because this worker does not open an answer-key panel.
Reported durations use measured elapsed time. Practice heartbeats use `page_id=practice-exam`,
`item_id=PracticeIid`, and `metadata[user_iid]`, at ten-second deadlines during
waits, plus a final flush. Calls stay sequential; slow network calls can delay
heartbeats. These durations measure worker execution, not human interaction.

The default is `finished=0`. A server auto-finish flag stops subsequent batches.
Overtime produces a warning. Reports distinguish submitted questions from KPI
seconds actually granted by `you_have_just_gained`; HTTP/application success alone
does not imply progress credit. A single pass through the bank does not promise
the configured learning-time KPI will be completed. Failed calls stop without
retry, including an uncertain `start-practice` response.

See the [call flow](../docs/practice-only-flow.md) and
[HAR findings](../docs/practice-har-analysis.md) for endpoint mapping, evidence,
and limits of the capture. Offline regression tests use a sanitized fixture:

```bash
dotnet run --project LotusLMS.RequestSigning.Tests/LotusLMS.RequestSigning.Tests.csproj
```

## Auto-study job

The worker can also run a bounded job that mirrors the active web-client flow:
login, read the first enrolment plan, walk each course's learning tree in order,
and submit `save-item-learning-time` only for incomplete `video` nodes. `sco`
nodes are aggregate/grouping parents and never receive learning-time saves.

```bash
Worker__Mode=AutoStudy \
  dotnet run --no-launch-profile --project LotusLMS.Worker/LotusLMS.Worker.csproj
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
- `PracticeBatchSize` (5): questions submitted per `save-take` batch. Only questions
  with a valid ID and an answer marked `is_answer=1` in `mc_answers` or `answers2` are submitted.
- `PracticeIntervalSeconds` (5) / `PracticeMaxIntervalSeconds` (10): inclusive
  bounds in seconds for a freshly randomized interval between submission starts;
  the first batch also waits a random interval.
- `MarkPracticeFinished` (false): mark the last batch as finished. The current
  web client always submits `finished=0`, so this stays disabled for parity.
- `OnlyFirstPlan` (true) / `OnlyFirstCoursePage` (true): match the web client,
  which only reads the first plan and page 1 of courses.

> Auto-study sends real mutations to the LotusLMS account. Use it only for the
> account you are authorised to operate. For a short first test, explicitly set
> `Worker__StopOnDailyKpi=true`.

## Proxy selected by the local client

With `Client:Enabled=true`, the worker obtains the proxy assigned to
`Client:AccountId` from the authenticated client control endpoint before LMS work
is permitted. A null selection means a direct connection. A disabled or expired profile
blocks startup; disabling or changing the selected proxy after work starts stops
the host at the next control poll. Expiry is checked against the client-provided
`expiresAt`; a profile remains valid through its configured Vietnam calendar day. Restart the worker to apply the new selection.
Local client control and log requests remain direct. With `Client:Enabled=false`,
the worker continues to use the standalone `Http:Proxy` configuration above.

Create proxy profiles and assign them in the account form in the
[Node.js client](../client/README.md#chọn-proxy-cho-từng-account).

## Student queue and exclusive ownership

With `Client:Enabled=true`, configure `Client:BaseUrl`, `WorkerName` and `ApiKey`.
`AccountId` is optional: leave it empty to select a queued student automatically.
The worker lists eligible accounts, atomically claims one, then uses the returned
student credentials, device UUID, provider and proxy. It resolves the LMS API
client after applying this configuration; no manual student credentials are
required in client mode.

Every process has a distinct instance ID; claim retries keep their request ID.
Heartbeat every five seconds renews a thirty-second lease. LMS requests require a
valid local lease, and losing ownership cancels outstanding requests and stops the
host. On shutdown, the worker drains logs and acknowledges its outcome after the
job has stopped. One process handles one student; restart it to take another.
Worker control/log HTTP traffic remains direct. Standalone mode is unchanged.

See [queue API and setup](../client/docs/WORKER_QUEUE.md) for registration,
request examples, account states and lifecycle behavior.

## Backend connection library

Client-backend HTTP and public DTOs live in
[`LotusLMS.Client`](../LotusLMS.Client/README.md). `ClientWorkerBridge` uses
`ILotusClient` for queue, claim, heartbeat, logs and finish; it retains worker
lifecycle, lease guards, credential/provider mapping and ILogger forwarding.
Existing `Client__*` settings remain valid. The transport is resolved after
logging is initialized so HttpClientFactory does not create a logging DI cycle.
