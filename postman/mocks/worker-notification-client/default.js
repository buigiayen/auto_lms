// Model only registration and polling needed to exercise fatal worker/lane notifications.
const http = require("http");
http.createServer(async (req, res) => {
  const url = new URL(req.url, "http://client.invalid");
  const respond = (status, body) => {
    res.writeHead(status, { "Content-Type": "application/json" });
    res.end(JSON.stringify(body));
  };
  if (url.pathname === "/_reset" && req.method === "POST") {
    await pm.state.set("worker-notification-client:registrations", []);
    return respond(200, { reset: true });
  }
  if (url.pathname === "/_stats" && req.method === "GET")
    return respond(200, { registrations: await pm.state.get("worker-notification-client:registrations") || [] });
  if (url.pathname === "/api/workers" && req.method === "POST") {
    const body = typeof req.body === "string" ? JSON.parse(req.body) : req.body;
    if (!body?.workerName) return respond(400, { error: { code: "MISSING_WORKER_NAME" } });
    const names = await pm.state.get("worker-notification-client:registrations") || [];
    names.push(body.workerName);
    await pm.state.set("worker-notification-client:registrations", names);
    return respond(201, { data: { workerName: body.workerName, apiKey: "fixture-key-" + body.workerName } });
  }
  if (url.pathname === "/api/worker/accounts" && req.method === "GET")
    return respond(403, { error: { code: "WORKER_FORBIDDEN", message: "Simulated permanent polling failure" } });
  return respond(501, { error: { code: "ENDPOINT_NOT_MODELED" } });
}).listen(process.env.PORT || 4501);
