# LotusLMS.Api

Typed, dependency-injected client for the LotusLMS endpoints used by the existing
WinForms application. API responses can be logged through the configured response
file writer.

## Registration

```csharp
services.AddWebProxySupport(
    configuration.GetSection(WebProxyOptions.SectionName));

services.AddLotusLmsApi(
    configuration.GetSection(LotusLmsOptions.SectionName));
```

Resolve `ILotusLmsApiClient`, call `LoginAsync`, and keep the returned
`LotusLmsSession` scoped to one account. All operations accept a
`CancellationToken`. General API responses use `JsonObject` until stable,
minimal DTOs can be confirmed from contract fixtures.

Every request is signed with a fresh `_sand_ri` / `_sand_rit` pair by
`LotusLMS.RequestSigning`. Login uses the guest seed; authenticated requests use
`session.UserIid`. Authenticated POSTs and GETs also receive a current
`_sand_client_sync_token` derived from the login token and Unix seconds. A missing
sync token was observed to produce `err_code=12`; no captured signing
values are needed. See the
[algorithm and verification notes](../LotusLMS.RequestSigning/README.md).
`LotusLms:GenerateRequestTokens` defaults to `true`; set it to `false` only to
replay legacy caller-supplied request pairs. Existing login/session fields and
practice-method overrides, including `ClientSyncToken`, are used only in that
legacy mode. GET requests include the session identity in the signed query.

Callers that receive provider details per account can set `LotusLmsLoginRequest.Provider`
with `BaseUrl`, `WebOrigin`, and `Tenant`. The API client uses those values for
login and retains them on `LotusLmsSession`, so authenticated requests use the
same API host, origin, and `_sand_domain` without changing shared worker options.
If `Provider` is omitted, the configured `LotusLmsOptions` values remain the
fallback for existing callers.

## File responsibilities

- `LotusLmsApiClient.cs`: endpoint paths, query/form payloads and business-level
  response handling only.
- `LotusLmsApiTransport.cs`: reusable GET/POST calls, common headers, HTTP errors,
  JSON validation and typed deserialization.
- `ApiResponseFileWriter.cs`: one JSONL response log per URI path.
- `LotusLmsDeviceIdentity.cs`: creates and persists the automatic device UUID.
- `dto/`: typed API response contracts.

When adding or changing an endpoint, keep its path and payload in
`LotusLmsApiClient`; edit shared HTTP behavior only in `LotusLmsApiTransport`.

The practice-save method accepts caller-supplied outcomes and timing. It does not
infer correct answers, generate scores, or create artificial elapsed time.
It sends multipart form data, including the nested `take_info` and per-question
timing fields, matching the HAR form contract. Signing is applied
immediately before creating the multipart content.

Mutation calls are deliberately not retried by the library. If a timeout occurs
after a request may have reached the server, the worker must read the server state
and reconcile before sending another increment.

`GetQuestionBankStatusAsync` uses POST with `bank_iids[]`,
`exam_template_iid`, `last_ts=0`, and `lang=vn` in the query, matching the
supplied HAR. Detail and bank requests use multipart bodies as well.
`RecalculateCoursePracticeProgressAsync`, `GetSyncPracticeProgressAsync`,
`StartPracticeAsync`, `TrackPracticeTimeAsync`, and `SavePracticeTakeAsync` use
multipart forms without a URL query. `StartPracticeAsync` returns the new take
in `result.id`; the similarly named sync result ID belongs to progress history.
Practice tracking uses `page_id=practice-exam`, the practice ID as `item_id`, and
`metadata[user_iid]`. See the [HAR analysis](../docs/practice-har-analysis.md).
