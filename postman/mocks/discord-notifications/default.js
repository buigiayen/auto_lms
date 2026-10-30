// Local stand-in for the Discord Execute Webhook contract. No real webhook tokens.
// Contract: https://docs.discord.com/developers/resources/webhook#execute-webhook
const http = require("http");
const ids = ["111", "222", "333"];
http.createServer(async (req, res) => {
  const url = new URL(req.url, "http://discord.invalid");
  const respond = (status, body) => {
    res.writeHead(status, { "Content-Type": "application/json" });
    res.end(JSON.stringify(body));
  };
  if (url.pathname === "/_reset" && req.method === "POST") {
    for (const id of ids) await pm.state.set("discord-notifications:count:" + id, 0);
    return respond(200, { reset: true });
  }
  if (url.pathname === "/_stats" && req.method === "GET") {
    const counts = {};
    for (const id of ids) counts[id] = await pm.state.get("discord-notifications:count:" + id) || 0;
    return respond(200, { counts });
  }
  const match = url.pathname.match(/^\/api\/webhooks\/(111|222|333)\/fixture-hook$/);
  if (!match) return respond(501, { message: "Endpoint not modeled" });
  if (req.method !== "POST" || url.searchParams.get("wait") !== "true"
      || !req.headers["content-type"]?.startsWith("multipart/form-data"))
    return respond(400, { message: "Expected multipart Execute Webhook with wait=true" });
  const id = match[1];
  const key = "discord-notifications:count:" + id;
  const count = (await pm.state.get(key) || 0) + 1;
  await pm.state.set(key, count);
  if (id === "333") return respond(404, { message: "Unknown Webhook", code: 10015 });
  if (id === "111" && count === 1) return respond(502, { message: "Simulated transient gateway failure" });
  if (id === "222" && count === 1) {
    res.setHeader("Retry-After", "1");
    return respond(429, { message: "You are being rate limited.", retry_after: 1, global: false });
  }
  return respond(200, { id: "fixture-message-" + id + "-" + count, content: "accepted" });
}).listen(process.env.PORT || 4500);
