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

## File responsibilities

- `LotusLmsApiClient.cs`: endpoint paths, query/form payloads and business-level
  response handling only.
- `LotusLmsApiTransport.cs`: reusable GET/POST calls, common headers, HTTP errors,
  JSON validation and typed deserialization.
- `ApiResponseFileWriter.cs`: one JSONL response log per URI path.
- `dto/`: typed API response contracts.

When adding or changing an endpoint, keep its path and payload in
`LotusLmsApiClient`; edit shared HTTP behavior only in `LotusLmsApiTransport`.

The practice-save method accepts caller-supplied outcomes and timing. It does not
infer correct answers, generate scores, or create artificial elapsed time.

Mutation calls are deliberately not retried by the library. If a timeout occurs
after a request may have reached the server, the worker must read the server state
and reconcile before sending another increment.
