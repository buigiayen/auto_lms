// Shared by the mascot renderer and the account-list filter.
export function accountMood(account) {
  const execution = account?.execution;
  if (["failed", "lost"].includes(execution?.state)) return "angry";
  if (execution?.state === "completed") return "celebrating";
  // A queued run may still carry an error from its previous run.
  if (["accepted", "running", "stopping"].includes(execution?.state) &&
      execution.lastLog?.level === "error") return "angry";
  return !account || account.status !== "running" || execution?.state === "deferred"
    ? "sleeping" : "awake";
}
