const $ = (selector) => document.querySelector(selector);
const icon = (name) => `<svg aria-hidden="true"><use href="#i-${name}"/></svg>`;
const escape = (value) =>
  String(value ?? "").replace(
    /[&<>"']/g,
    (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        c
      ],
  );
const providerName = (provider) =>
  provider === "yenthuy" ? "Yên Thủy" : "Trường Sơn";
const date = (value) =>
  value
    ? new Intl.DateTimeFormat("vi-VN", {
        timeZone: "Asia/Ho_Chi_Minh",
        day: "2-digit",
        month: "2-digit",
        hour: "2-digit",
        minute: "2-digit",
      }).format(new Date(value))
    : "—";
const state = {
  view: "accounts",
  page: 1,
  logPage: 1,
  accounts: [],
  proxies: [],
  proxyEditorId: null,
  modal: null,
  modalEpoch: 0,
  accountRequest: 0,
  logRequest: 0,
};

async function api(path, { method = "GET", body, sessionId, signal } = {}) {
  const response = await fetch(path, {
    method,
    signal,
    headers: {
      ...(body !== undefined ? { "Content-Type": "application/json" } : {}),
      ...(sessionId ? { "X-Lms-Session-Id": sessionId } : {}),
    },
    ...(body !== undefined ? { body: JSON.stringify(body) } : {}),
  });
  if (response.status === 204) return {};
  const value = await response.json();
  if (!response.ok) {
    const message = value.error?.message ?? "Không thể hoàn thành yêu cầu.";
    const error = new Error(
      message +
        (value.error?.upstreamCode ? ` (${value.error.upstreamCode})` : ""),
    );
    error.fields = value.error?.fields;
    error.code = value.error?.code;
    throw error;
  }
  return value;
}
function toast(message, error = false) {
  const item = document.createElement("div");
  item.className = "toast" + (error ? " error" : "");
  item.textContent = message;
  $("#toasts").append(item);
  setTimeout(() => item.remove(), 4500);
}
function errorBox(selector, message) {
  const box = $(selector);
  if (!box) return;
  box.hidden = !message;
  box.textContent = message ?? "";
}
function empty(title, description, action = false) {
  return `<div class="empty-state"><span class="empty-icon">${icon("grid")}</span><h3>${escape(title)}</h3><p>${escape(description)}</p>${action ? `<button class="button primary" data-create>${icon("plus")}Thêm tài khoản đầu tiên</button>` : ""}</div>`;
}
const executionLabels = {
  queued: "Chờ Worker",
  accepted: "Đã tiếp nhận",
  running: "Đang xử lý",
  stopping: "Đang dừng",
  completed: "Hoàn thành",
  failed: "Lỗi",
  lost: "Mất kết nối",
  stopped: "Đã dừng",
};
const spinner = (message) =>
  `<div class="loading"><span class="spinner"></span>${escape(message)}</div>`;

async function loadAccounts() {
  const version = ++state.accountRequest;
  const params = new URLSearchParams({
    page: state.page,
    pageSize: 20,
    search: $("#account-search").value,
    provider: $("#provider-filter").value,
    status: $("#status-filter").value,
  });
  errorBox("#accounts-error", "");
  try {
    const result = await api(`/api/accounts?${params}`);
    if (version !== state.accountRequest) return;
    state.accounts = result.data;
    const summary = result.summary;
    for (const key of ["total", "running", "stopped", "workers"])
      $(`#stat-${key}`).textContent = summary[key];
    $("#nav-count").textContent = summary.total;
    $("#account-count").textContent = result.pagination.total;
    $("#connection").innerHTML =
      '<span class="status-dot"></span>Kết nối local';
    const rows = result.data
      .map(
        (account) => `<tr>
      <td><div class="account-cell"><span class="account-avatar">${icon("users")}</span><div><span class="account-name">${escape(account.identityNumber)}</span><span class="account-hint" title="${escape(account.enrolmentPlanName)}">${account.enrolmentPlanIid ? `${escape(account.enrolmentPlanName || "Chương trình học")} · IID ${account.enrolmentPlanIid}` : "Căn cước công dân"}</span></div></div></td>
      <td><span class="provider-tag"><span class="provider-symbol ${account.provider === "truongson" ? "blue" : ""}">${account.provider === "truongson" ? "T" : "Y"}</span>${providerName(account.provider)}</span></td>
      <td class="teacher-cell" title="${escape(account.managingTeacher)}"><span class="${account.managingTeacher ? "" : "muted"}">${escape(account.managingTeacher || "Chưa nhập")}</span></td>
      <td class="worker-activity"><span class="worker-label">${escape(account.execution?.workerName ?? (account.workerName === "unassigned" ? "Chưa tiếp nhận" : account.workerName))}</span>${account.execution?.lastLog ? `<small class="activity-preview" title="${escape(account.execution.lastLog.message)}">${escape(account.execution.lastLog.message)}</small>` : ""}</td>
      <td class="proxy-cell" title="${escape(account.proxy?.address ?? "Kết nối trực tiếp")}">${account.proxy ? `<span>${escape(account.proxy.name)}${account.proxy.enabled ? "" : " · Đang tắt"}</span><small>${escape(account.proxy.address)}</small>${proxyDeadline(account.proxy)}` : '<span class="muted">Trực tiếp</span>'}</td>
      <td><span class="badge ${account.status}">${account.status === "running" ? "Treo" : "Dừng treo"}</span>${account.execution ? `<small class="execution-state ${account.execution.state}">${executionLabels[account.execution.state] ?? escape(account.execution.state)}</small>` : ""}</td>
      <td class="date-cell">${date(account.updatedAt)}</td>
      <td><div class="row-actions"><button class="icon-button" data-action="toggle" data-id="${account.id}" aria-label="${account.status === "running" ? "Dừng treo" : "Treo"} ${escape(account.identityNumber)}" title="${account.status === "running" ? "Dừng treo" : "Treo"}">${icon(account.status === "running" ? "pause" : "play")}</button><button class="icon-button" data-action="edit" data-id="${account.id}" title="Sửa thông tin" aria-label="Sửa ${escape(account.identityNumber)}">${icon("edit")}</button><button class="icon-button" data-action="logs" data-id="${account.id}" title="Xem log" aria-label="Xem log ${escape(account.identityNumber)}">${icon("log")}</button><button class="button detail-button" data-action="details" data-id="${account.id}">Chi tiết${icon("arrow")}</button></div></td>
    </tr>`,
      )
      .join("");
    $("#account-rows").innerHTML =
      rows ||
      `<tr><td colspan="8">${summary.total ? empty("Không tìm thấy tài khoản", "Thử thay đổi từ khóa hoặc bộ lọc của bạn.") : empty("Bắt đầu với tài khoản đầu tiên", "Thêm tài khoản LMS để quản lý trạng thái và theo dõi hoạt động của worker.", true)}</td></tr>`;
    const total = result.pagination.total,
      start = total ? (state.page - 1) * 20 + 1 : 0;
    $("#accounts-range").textContent =
      `${start}–${Math.min(state.page * 20, total)} trong ${total} tài khoản`;
    $("#accounts-page-label").textContent = `Trang ${state.page}`;
    $("#accounts-prev").disabled = state.page <= 1;
    $("#accounts-next").disabled = state.page * 20 >= total;
    $("#updated-label").textContent =
      `Cập nhật lúc ${new Date().toLocaleTimeString("vi-VN")}`;
  } catch (error) {
    if (version !== state.accountRequest) return;
    errorBox("#accounts-error", error.message);
    $("#account-rows").innerHTML =
      `<tr><td colspan="8">${empty("Chưa kết nối được backend", "Kiểm tra Node.js server và bấm Làm mới để thử lại.")}</td></tr>`;
    $("#connection").textContent = "Mất kết nối local";
  }
}

function closeDialog() {
  const modal = state.modal;
  state.modal = null;
  state.modalEpoch++;
  if (modal?.timer) clearInterval(modal.timer);
  modal?.controller?.abort();
  modal?.kpiController?.abort();
  if (modal?.sessionId)
    api(`/api/accounts/${modal.account.id}/lms/session`, {
      method: "DELETE",
      sessionId: modal.sessionId,
    }).catch(() => {});
  if ($("#dialog").open) $("#dialog").close();
}
function openDialog(title, subtitle, body, { wide = false, footer = "" } = {}) {
  closeDialog();
  const dialog = $("#dialog");
  dialog.className = wide ? "wide" : "";
  dialog.innerHTML = `<div class="dialog-heading"><div><h2 id="dialog-title">${escape(title)}</h2><p>${escape(subtitle)}</p></div><button class="icon-button" data-close aria-label="Đóng">${icon("close")}</button></div>${body}${footer}`;
  dialog.showModal();
  return state.modalEpoch;
}

function proxyDeadline(proxy) {
  if (!proxy?.expiresOn)
    return '<span class="proxy-expiry unset">Chưa đặt ngày hết hạn</span>';
  const [year, month, day] = proxy.expiresOn.split("-");
  const remaining =
    Date.parse(`${proxy.expiresOn}T00:00:00+07:00`) + 86400000 - Date.now();
  const expired = remaining <= 0;
  const soon = remaining <= 3 * 86400000;
  return `<span class="proxy-expiry ${expired ? "expired" : soon ? "soon" : "valid"}">Hết hạn ${day}/${month}/${year} · ${expired ? "Đã hết hạn" : soon ? "Sắp hết hạn" : "Còn hạn"}</span>`;
}
function proxyPicker(proxies, selectedId, { running = false } = {}) {
  const choices = proxies.filter(
    (proxy) =>
      (proxy.enabled && !proxy.isExpired) ||
      (!running && proxy.id === selectedId),
  );
  return (
    `<label class="proxy-choice"><input type="radio" name="proxyId" value="" required ${selectedId == null ? "checked" : ""}><span class="proxy-choice-text"><strong>Kết nối trực tiếp</strong><small>Không sử dụng proxy</small></span></label>` +
    choices
      .map(
        (proxy) =>
          `<label class="proxy-choice"><input type="radio" name="proxyId" value="${proxy.id}" required ${proxy.id === selectedId ? "checked" : ""}><span class="proxy-choice-text"><strong>${escape(proxy.name)}${proxy.enabled ? "" : " · Đang tắt"}</strong><small>${escape(proxy.address)}</small>${proxyDeadline(proxy)}</span></label>`,
      )
      .join("") +
    (!choices.length
      ? '<p class="field-help">Không có proxy còn hạn đang bật. Thêm hoặc gia hạn trong Cấu hình proxy.</p>'
      : "")
  );
}
function runAccount(account) {
  const epoch = openDialog(
    "Chạy tài khoản",
    `${account.identityNumber} · ${providerName(account.provider)}`,
    `<form id="run-form"><div class="dialog-body"><p class="field-help">Chọn proxy, tải danh sách và chọn chương trình học muốn chạy.</p><fieldset class="proxy-picker"><legend>1. Kết nối</legend><div id="run-proxy">${spinner("Đang tải proxy còn hạn…")}</div></fieldset><section class="run-plan-panel"><div class="run-plan-heading"><div><h3>2. Chương trình học cần chạy</h3><p>Đăng nhập tài khoản bằng kết nối đã chọn để đọc chương trình học.</p></div><button type="button" class="button small" id="load-run-plans" disabled>${icon("refresh")}Tải chương trình</button></div><div id="run-plan-status" class="field-help" role="status">Chưa tải danh sách chương trình học.</div><div id="run-plan-fields" hidden><label>Chương trình học<select id="run-plan" required><option value="">Chọn chương trình học…</option></select></label><button type="button" class="button small run-more" id="run-more-plans" hidden>Tải thêm chương trình</button><p class="run-plan-summary" id="run-plan-summary" role="status"></p></div></section><div id="run-error" class="inline-error" role="alert" hidden></div></div><div class="dialog-footer"><button type="button" class="button" data-close>Hủy</button><button class="button primary" type="submit" disabled>${icon("play")}Chạy tài khoản</button></div></form>`,
  );
  const modal = {
    type: "run",
    epoch,
    account,
    sessionId: null,
    plans: [],
    planPage: 0,
    version: 0,
  };
  state.modal = modal;
  const current = () => state.modalEpoch === epoch;
  const button = $("#run-form button[type=submit]"),
    loadButton = $("#load-run-plans");
  const selectedProxy = () => $("#run-proxy input:checked")?.value || null;
  const release = (sessionId) =>
    sessionId &&
    api(`/api/accounts/${account.id}/lms/session`, {
      method: "DELETE",
      sessionId,
    }).catch(() => {});
  function resetPlans() {
    modal.version++;
    modal.controller?.abort();
    modal.controller = new AbortController();
    release(modal.sessionId);
    modal.sessionId = null;
    modal.plans = [];
    modal.planPage = 0;
    $("#run-plan-fields").hidden = true;
    $("#run-plan-status").textContent =
      "Bấm “Tải chương trình” để đọc bằng kết nối đã chọn.";
    $("#run-plan").innerHTML =
      '<option value="">Chọn chương trình học…</option>';
    $("#run-plan-summary").textContent = "";
    button.disabled = true;
    loadButton.disabled = false;
  }
  function selectionChanged() {
    const plan = modal.plans.find(
      (item) => item.iid === Number($("#run-plan").value),
    );
    button.disabled = !plan || !modal.sessionId;
    $("#run-plan-summary").textContent = plan
      ? `Sẽ chạy: ${plan.name} · IID ${plan.iid}`
      : "";
  }
  async function loadPlans() {
    const version = modal.version;
    $("#run-more-plans").disabled = true;
    try {
      const result = await api(
        `/api/accounts/${account.id}/enrolment-plans?page=${modal.planPage + 1}&pageSize=12`,
        { sessionId: modal.sessionId, signal: modal.controller.signal },
      );
      if (!current() || version !== modal.version) return;
      modal.planPage++;
      for (const plan of result.data)
        if (!modal.plans.some((item) => item.iid === plan.iid))
          modal.plans.push(plan);
      const selected =
        $("#run-plan").value || String(account.enrolmentPlanIid ?? "");
      $("#run-plan").innerHTML =
        '<option value="">Chọn chương trình học…</option>' +
        modal.plans
          .map(
            (plan) =>
              `<option value="${plan.iid}">${escape(plan.name)} · IID ${plan.iid}</option>`,
          )
          .join("");
      $("#run-more-plans").hidden = !result.pagination.hasMore;
      $("#run-plan").disabled = !modal.plans.length;
      $("#run-plan-fields").hidden = false;
      $("#run-plan-status").textContent = modal.plans.length
        ? "Chọn chương trình học trước khi xác nhận chạy."
        : "Tài khoản chưa có chương trình học.";
      if (modal.plans.some((plan) => String(plan.iid) === selected))
        $("#run-plan").value = selected;
      else if (modal.plans.length === 1)
        $("#run-plan").value = modal.plans[0].iid;
      selectionChanged();
    } finally {
      if (current() && version === modal.version)
        $("#run-more-plans").disabled = false;
    }
  }
  loadButton.onclick = async () => {
    if (!$("#run-proxy input:checked")) {
      errorBox("#run-error", "Chọn kết nối trước khi tải chương trình học.");
      return;
    }
    resetPlans();
    const version = modal.version;
    loadButton.disabled = true;
    errorBox("#run-error", "");
    $("#run-plan-status").innerHTML = spinner(
      "Đang đăng nhập và đọc chương trình học…",
    );
    try {
      const { data } = await api(`/api/accounts/${account.id}/run-session`, {
        method: "POST",
        body: { proxyId: selectedProxy() },
      });
      if (!current() || version !== modal.version) {
        release(data.sessionId);
        return;
      }
      modal.sessionId = data.sessionId;
      await loadPlans();
    } catch (error) {
      if (
        current() &&
        version === modal.version &&
        error.name !== "AbortError"
      ) {
        errorBox("#run-error", error.message);
        $("#run-plan-status").textContent =
          "Chưa tải được danh sách. Bạn có thể thử lại.";
      }
    } finally {
      if (current() && version === modal.version) loadButton.disabled = false;
    }
  };
  $("#run-proxy").onchange = () => {
    resetPlans();
    errorBox("#run-error", "");
  };
  $("#run-plan").onchange = selectionChanged;
  $("#run-more-plans").onclick = () =>
    loadPlans().catch((error) => {
      if (current() && error.name !== "AbortError")
        errorBox("#run-error", error.message);
    });
  api("/api/proxies?available=true")
    .then(({ data }) => {
      if (!current()) return;
      $("#run-proxy").innerHTML = proxyPicker(data, account.proxyId, {
        running: true,
      });
      loadButton.disabled = false;
    })
    .catch((error) => {
      if (current()) errorBox("#run-error", error.message);
    });
  $("#run-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    button.disabled = true;
    errorBox("#run-error", "");
    try {
      await api(`/api/accounts/${account.id}/run`, {
        method: "POST",
        sessionId: modal.sessionId,
        body: {
          proxyId: selectedProxy(),
          enrolmentPlanIid: Number($("#run-plan").value),
        },
      });
      if (current()) closeDialog();
      toast("Đã lưu chương trình học và đưa account vào hàng chờ Worker.");
      await loadAccounts();
    } catch (error) {
      if (current()) {
        errorBox("#run-error", error.message);
        if (["PROXY_EXPIRED", "PROXY_DISABLED"].includes(error.code)) {
          try {
            const { data } = await api("/api/proxies?available=true");
            if (current()) {
              const selected = selectedProxy();
              $("#run-proxy").innerHTML = proxyPicker(data, selected, {
                running: true,
              });
              resetPlans();
            }
          } catch {
            /* Preserve the original failure. */
          }
        } else selectionChanged();
      }
    }
  });
}

function accountForm(account) {
  const editing = Boolean(account);
  const busy = ["accepted", "running", "stopping"].includes(
    account?.execution?.state,
  );
  const epoch = openDialog(
    editing ? "Sửa tài khoản" : "Thêm tài khoản",
    editing
      ? `Cập nhật thông tin ${account.identityNumber}`
      : "Kết nối tài khoản LMS vào không gian của bạn.",
    `<form id="account-form"><div class="dialog-body"><div class="field-row"><label>Căn cước<input name="identityNumber" id="identityNumber" required inputmode="numeric" pattern="[0-9]{12}" maxlength="12" placeholder="12 chữ số" value="${escape(account?.identityNumber)}" autocomplete="off"></label><label>Nhà cung cấp<select name="provider"><option value="truongson" ${account?.provider === "truongson" ? "selected" : ""}>Trường Sơn</option><option value="yenthuy" ${account?.provider === "yenthuy" ? "selected" : ""}>Yên Thủy</option></select></label></div><div class="field-row"><label>Mật khẩu<input name="password" id="password" type="password" ${editing ? "" : "required"} maxlength="1024" placeholder="${editing ? "Giữ mật khẩu hiện tại" : "Mật khẩu đăng nhập LMS"}" autocomplete="new-password"></label><label>Tên worker (tùy chọn)<input name="workerName" id="workerName" maxlength="100" placeholder="Tự cập nhật khi Worker tiếp nhận" value="${escape(account?.workerName === "unassigned" ? "" : account?.workerName)}" autocomplete="off"></label></div><label class="teacher-field">Giáo viên quản lý<input name="managingTeacher" id="managingTeacher" maxlength="200" placeholder="Nhập tên giáo viên quản lý" value="${escape(account?.managingTeacher)}" autocomplete="off"></label><fieldset class="proxy-picker account-proxy-field"><legend>Proxy</legend><div id="account-proxy">Đang tải cấu hình proxy…</div></fieldset>${!editing ? '<label>Trạng thái ban đầu<select name="status"><option value="stopped">Dừng treo</option><option value="running">Treo</option></select></label>' : ""}<p class="field-help">${busy ? "Worker đang chạy. Bạn có thể cập nhật Giáo viên quản lý; dừng Worker trước khi sửa thông tin khác." : editing ? "Để trống mật khẩu để giữ nguyên. Thay đổi thông tin đăng nhập sẽ đóng phiên xem chi tiết cũ." : "Thông tin được lưu trên máy. GUID thiết bị được tạo tự động."}</p><div id="account-form-error" class="inline-error" role="alert" hidden></div></div><div class="dialog-footer"><button type="button" class="button" data-close>Hủy</button><button class="button primary" type="submit">${icon("check")}${editing ? "Lưu thay đổi" : "Tạo tài khoản"}</button></div></form>`,
  );
  state.modal = { type: "account", epoch };
  const saveButton = $("#account-form button[type=submit]");
  saveButton.disabled = true;
  api("/api/proxies")
    .then(({ data }) => {
      if (state.modalEpoch !== epoch) return;
      $("#account-proxy").innerHTML = proxyPicker(data, account?.proxyId);
      if (busy)
        $("#account-form")
          .querySelectorAll("input, select")
          .forEach((field) => {
            field.disabled = field.name !== "managingTeacher";
          });
      saveButton.disabled = false;
    })
    .catch((error) => {
      if (state.modalEpoch === epoch)
        errorBox("#account-form-error", error.message);
    });
  $("#account-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    const button = event.submitter;
    button.disabled = true;
    errorBox("#account-form-error", "");
    const body = busy
      ? { managingTeacher: $("#managingTeacher").value }
      : Object.fromEntries(new FormData(event.target));
    if (!busy) {
      body.proxyId = body.proxyId || null;
      if (editing && !body.password) delete body.password;
      if (!body.workerName) delete body.workerName;
    }
    try {
      await api(editing ? `/api/accounts/${account.id}` : "/api/accounts", {
        method: editing ? "PATCH" : "POST",
        body,
      });
      if (state.modalEpoch === epoch) closeDialog();
      toast(editing ? "Đã lưu thông tin tài khoản." : "Đã tạo tài khoản mới.");
      state.page = 1;
      await loadAccounts();
    } catch (error) {
      if (state.modalEpoch === epoch) {
        errorBox("#account-form-error", error.message);
        if (error.fields)
          Object.keys(error.fields).forEach((field) =>
            $(`#${field}`)?.classList.add("form-error"),
          );
      }
    } finally {
      button.disabled = false;
    }
  });
}

function renderLogs(rows) {
  if (!rows.length)
    return empty(
      "Chưa có sự kiện",
      "Log sẽ xuất hiện khi worker gửi hoạt động về client.",
    );
  return `<div class="log-list">${rows.map((row) => `<article class="log-entry"><time class="log-time">${date(row.occurredAt)}</time><span class="log-level ${row.level}">${row.level}</span><div><p class="log-message">${escape(row.message)}</p><div class="log-meta">${escape(row.workerName)} · ${escape(row.identityNumber)}${row.runId ? ` · Lượt ${escape(row.runId.slice(0, 8))}` : ""}</div>${Object.keys(row.context).length ? `<details><summary class="log-meta">Xem dữ liệu sự kiện</summary><pre class="log-context">${escape(JSON.stringify(row.context, null, 2))}</pre></details>` : ""}</div></article>`).join("")}</div>`;
}
async function loadLogs() {
  const version = ++state.logRequest;
  try {
    const params = new URLSearchParams({
      page: state.logPage,
      pageSize: 50,
      level: $("#log-level").value,
    });
    if ($("#log-from").value)
      params.set("from", new Date($("#log-from").value).toISOString());
    if ($("#log-to").value)
      params.set("to", new Date($("#log-to").value).toISOString());
    const result = await api(`/api/logs?${params}`);
    if (version !== state.logRequest) return;
    $("#logs-content").innerHTML = renderLogs(result.data);
    $("#logs-range").textContent = `${result.pagination.total} sự kiện`;
    $("#logs-page-label").textContent = `Trang ${state.logPage}`;
    $("#logs-prev").disabled = state.logPage <= 1;
    $("#logs-next").disabled = state.logPage * 50 >= result.pagination.total;
  } catch (error) {
    if (version === state.logRequest)
      $("#logs-content").innerHTML =
        `<div class="dialog-body"><div class="inline-error">${escape(error.message)}</div></div>`;
  }
}
function accountLogs(account) {
  const epoch = openDialog(
    "Nhật ký tài khoản",
    `${account.identityNumber} · ${providerName(account.provider)} · ${account.workerName}`,
    `<div class="dialog-body"><div class="filters"><select id="modal-log-level" aria-label="Cấp độ log"><option value="">Tất cả cấp độ</option><option value="info">Info</option><option value="warn">Warning</option><option value="error">Error</option><option value="debug">Debug</option></select><label class="date-filter">Từ thời điểm<input type="datetime-local" id="modal-log-from"></label><label class="date-filter">Đến thời điểm<input type="datetime-local" id="modal-log-to"></label></div><div id="modal-log-content">${spinner("Đang tải nhật ký…")}</div><div class="pagination modal-pagination"><span id="modal-log-range"></span><div><button class="button small" id="modal-log-prev">←</button><span id="modal-log-page">Trang 1</span><button class="button small" id="modal-log-next">→</button></div></div></div>`,
    { wide: true },
  );
  const modal = { type: "logs", epoch, account, page: 1 };
  state.modal = modal;
  let version = 0;
  async function load() {
    const current = ++version;
    try {
      const params = new URLSearchParams({
        page: modal.page,
        pageSize: 50,
        level: $("#modal-log-level").value,
      });
      if ($("#modal-log-from").value)
        params.set("from", new Date($("#modal-log-from").value).toISOString());
      if ($("#modal-log-to").value)
        params.set("to", new Date($("#modal-log-to").value).toISOString());
      const result = await api(`/api/accounts/${account.id}/logs?${params}`);
      if (state.modalEpoch !== epoch || version !== current) return;
      $("#modal-log-content").innerHTML = renderLogs(result.data);
      $("#modal-log-range").textContent = `${result.pagination.total} sự kiện`;
      $("#modal-log-page").textContent = `Trang ${modal.page}`;
      $("#modal-log-prev").disabled = modal.page === 1;
      $("#modal-log-next").disabled =
        modal.page * 50 >= result.pagination.total;
    } catch (error) {
      if (state.modalEpoch === epoch)
        $("#modal-log-content").innerHTML =
          `<div class="inline-error">${escape(error.message)}</div>`;
    }
  }
  $("#modal-log-level").onchange = () => {
    modal.page = 1;
    load();
  };
  for (const field of ["#modal-log-from", "#modal-log-to"])
    $(field).onchange = () => {
      modal.page = 1;
      load();
    };
  $("#modal-log-prev").onclick = () => {
    modal.page--;
    load();
  };
  $("#modal-log-next").onclick = () => {
    modal.page++;
    load();
  };
  modal.timer = setInterval(load, 5000);
  load();
}

async function details(account) {
  const epoch = openDialog(
    "Chi tiết tài khoản",
    `${account.identityNumber} · ${providerName(account.provider)}`,
    `<div class="dialog-body">${account.status === "running" ? '<div class="status-notice">Tài khoản đang ở trạng thái Treo. Đăng nhập xem chi tiết có thể ảnh hưởng phiên của worker.</div>' : ""}<div id="detail-status">${spinner("Đang đăng nhập vào LMS…")}</div><div id="detail-content" hidden><section class="run-plan-panel"><div class="run-plan-heading"><div><h3>KPI hôm nay (giây)</h3><p class="run-plan-summary" id="daily-kpi-result" role="status" aria-live="polite">Chưa kiểm tra.</p></div><button type="button" class="button small" id="check-daily-kpi">${icon("refresh")}Kiểm tra KPI 1 ngày</button></div></section><div class="plan-toolbar"><label>Chương trình học<select id="plan-select"><option value="">Chọn chương trình học…</option></select></label><button class="button" id="more-plans" hidden>Tải thêm chương trình</button></div><div class="json-heading"><strong>RESPONSE · JSON</strong><button class="button small" id="copy-json" disabled>${icon("copy")}Sao chép</button></div><div id="json-placeholder" class="json-placeholder">Chọn một chương trình để xem phản hồi danh sách khóa học.</div><pre id="raw-json" class="raw-json" hidden></pre><div class="pagination modal-pagination" id="course-pagination" hidden><span id="course-page">Trang 1</span><div><button class="button small" id="course-prev">← Trang trước</button><button class="button small" id="course-next">Trang sau →</button></div></div></div><div id="detail-error" class="inline-error" role="alert" hidden></div><button class="button" id="retry-login" hidden>Đăng nhập lại</button></div>`,
    { wide: true },
  );
  const modal = {
    type: "details",
    epoch,
    account,
    sessionId: null,
    plans: [],
    planPage: 0,
    coursePage: 1,
    request: 0,
    raw: null,
  };
  state.modal = modal;
  const current = () => state.modalEpoch === epoch;
  $("#retry-login").onclick = () => details(account);
  $("#check-daily-kpi").onclick = async () => {
    const button = $("#check-daily-kpi");
    const result = $("#daily-kpi-result");
    button.disabled = true;
    result.textContent = "Đang kiểm tra…";
    errorBox("#detail-error", "");
    modal.kpiController?.abort();
    modal.kpiController = new AbortController();
    try {
      const response = await api(
        `/api/accounts/${account.id}/today-gained-kpi-time`,
        { sessionId: modal.sessionId, signal: modal.kpiController.signal },
      );
      if (!current()) return;
      result.textContent =
        `${response.data.totalGainedKpiTimeInSeconds} / ${response.data.maxGainedKpiTimeInSeconds}`;
    } catch (error) {
      if (!current() || error.name === "AbortError") return;
      result.textContent = "Không thể kiểm tra KPI.";
      errorBox("#detail-error", error.message);
      $("#retry-login").hidden = false;
    } finally {
      if (current()) button.disabled = false;
    }
  };
  async function plans() {
    const nextPage = modal.planPage + 1;
    const result = await api(
      `/api/accounts/${account.id}/enrolment-plans?page=${nextPage}&pageSize=12`,
      { sessionId: modal.sessionId },
    );
    if (!current()) return;
    modal.planPage = nextPage;
    for (const plan of result.data)
      if (!modal.plans.some((p) => p.iid === plan.iid)) modal.plans.push(plan);
    const selected = $("#plan-select").value;
    $("#plan-select").innerHTML =
      '<option value="">Chọn chương trình học…</option>' +
      modal.plans
        .map(
          (p) =>
            `<option value="${p.iid}">${escape(p.name)} · #${p.iid}</option>`,
        )
        .join("");
    $("#plan-select").value = selected;
    $("#more-plans").hidden = !result.pagination.hasMore;
    $("#plan-select").disabled = !modal.plans.length;
    if (!modal.plans.length)
      $("#json-placeholder").textContent =
        "Tài khoản chưa có chương trình học.";
  }
  async function courses() {
    const id = $("#plan-select").value,
      request = ++modal.request;
    modal.controller?.abort();
    modal.controller = new AbortController();
    modal.raw = null;
    $("#raw-json").hidden = true;
    $("#copy-json").disabled = true;
    $("#course-pagination").hidden = true;
    errorBox("#detail-error", "");
    $("#json-placeholder").hidden = false;
    if (!id) {
      $("#json-placeholder").textContent =
        "Chọn một chương trình để xem phản hồi danh sách khóa học.";
      return;
    }
    $("#json-placeholder").innerHTML = spinner("Đang đọc danh sách khóa học…");
    try {
      const result = await api(
        `/api/accounts/${account.id}/enrolment-plans/${id}/courses?page=${modal.coursePage}&pageSize=20`,
        { sessionId: modal.sessionId, signal: modal.controller.signal },
      );
      if (!current() || modal.request !== request) return;
      modal.raw = JSON.stringify(result.data.rawResponse, null, 2);
      $("#raw-json").textContent = modal.raw;
      $("#raw-json").hidden = false;
      $("#json-placeholder").hidden = true;
      $("#copy-json").disabled = false;
      $("#course-pagination").hidden = false;
      $("#course-page").textContent = `Trang ${modal.coursePage}`;
      $("#course-prev").disabled = modal.coursePage <= 1;
      $("#course-next").disabled = !result.data.hasMore;
    } catch (error) {
      if (
        !current() ||
        modal.request !== request ||
        error.name === "AbortError"
      )
        return;
      errorBox("#detail-error", error.message);
      $("#json-placeholder").textContent =
        "Không tải được phản hồi. Chọn lại chương trình để thử lại.";
      $("#retry-login").hidden = false;
    }
  }
  $("#plan-select").onchange = () => {
    modal.coursePage = 1;
    courses();
  };
  $("#more-plans").onclick = async (event) => {
    event.target.disabled = true;
    try {
      await plans();
    } catch (error) {
      if (current()) errorBox("#detail-error", error.message);
    } finally {
      event.target.disabled = false;
    }
  };
  $("#course-prev").onclick = () => {
    modal.coursePage--;
    courses();
  };
  $("#course-next").onclick = () => {
    modal.coursePage++;
    courses();
  };
  $("#copy-json").onclick = async () => {
    try {
      await navigator.clipboard.writeText(modal.raw);
      toast("Đã sao chép JSON.");
    } catch {
      toast("Trình duyệt không cho phép sao chép.", true);
    }
  };
  try {
    const result = await api(`/api/accounts/${account.id}/lms/session`, {
      method: "POST",
    });
    if (!current()) {
      await api(`/api/accounts/${account.id}/lms/session`, {
        method: "DELETE",
        sessionId: result.data.sessionId,
      });
      return;
    }
    modal.sessionId = result.data.sessionId;
    $("#dialog .dialog-heading p").textContent =
      `${account.identityNumber} · ${providerName(account.provider)} · ${result.data.user.displayName}`;
    $("#detail-status").hidden = true;
    $("#detail-content").hidden = false;
    $("#json-placeholder").innerHTML = spinner("Đang đọc chương trình học…");
    try {
      await plans();
    } catch (error) {
      if (current()) {
        errorBox("#detail-error", error.message);
        $("#json-placeholder").textContent =
          "Không tải được danh sách chương trình học.";
        $("#retry-login").hidden = false;
      }
    }
  } catch (error) {
    if (current()) {
      $("#detail-status").hidden = true;
      errorBox("#detail-error", error.message);
      $("#retry-login").hidden = false;
    }
  }
}

function fillProxy(proxy) {
  state.proxyEditorId = proxy?.id ?? null;
  $("#proxy-editor-title").textContent = proxy
    ? "Sửa cấu hình proxy"
    : "Thêm cấu hình proxy";
  $("#proxy-name").value = proxy?.name ?? "";
  $("#proxy-expires-on").value = proxy?.expiresOn ?? "";
  $("#proxy-enabled").checked = proxy?.enabled ?? true;
  $("#proxy-username").value = proxy?.username ?? "";
  $("#proxy-password").value = "";
  $("#proxy-clear").checked = false;
  $("#proxy-password").placeholder = proxy?.hasPassword
    ? "Đã lưu · để trống để giữ nguyên"
    : "Mật khẩu proxy (nếu có)";
  $("#proxy-badge").className =
    `badge ${proxy?.enabled ? "running" : "stopped"}`;
  $("#proxy-badge").textContent = proxy
    ? proxy.enabled
      ? "Đang bật"
      : "Đang tắt"
    : "Cấu hình mới";
  $("#proxy-host").value = "";
  $("#proxy-port").value = "";
  $("#proxy-protocol").value = "http";
  if (proxy?.address) {
    const uri = new URL(proxy.address);
    $("#proxy-protocol").value = uri.protocol.replace(":", "");
    $("#proxy-host").value = uri.hostname;
    $("#proxy-port").value = uri.port || (uri.protocol === "https:" ? 443 : 80);
  }
  errorBox("#proxy-error", "");
  $("#proxy-result").hidden = true;
  renderProxies();
}
function renderProxies() {
  $("#proxy-list").innerHTML = state.proxies.length
    ? state.proxies
        .map(
          (proxy) =>
            `<div class="proxy-card ${proxy.id === state.proxyEditorId ? "selected" : ""}"><button type="button" class="proxy-select" data-proxy-select="${proxy.id}"><strong>${escape(proxy.name)}</strong><small>${escape(proxy.address)}</small><small>${proxy.enabled ? "Đang bật" : "Đang tắt"} · ${proxy.accountCount} tài khoản</small>${proxyDeadline(proxy)}</button><button type="button" class="icon-button" data-proxy-delete="${proxy.id}" ${proxy.accountCount ? "disabled" : ""} title="${proxy.accountCount ? "Gỡ proxy khỏi tài khoản trước khi xóa" : "Xóa cấu hình"}" aria-label="Xóa ${escape(proxy.name)}">${icon("close")}</button></div>`,
        )
        .join("")
    : empty(
        "Chưa có cấu hình proxy",
        "Tạo cấu hình rồi chọn proxy trong form tài khoản.",
      );
}
async function loadProxy(selectedId = state.proxyEditorId) {
  try {
    const { data } = await api("/api/proxies");
    state.proxies = data;
    fillProxy(data.find((proxy) => proxy.id === selectedId) ?? data[0] ?? null);
  } catch (error) {
    errorBox("#proxy-error", error.message);
  }
}
function proxyBody() {
  const host = $("#proxy-host").value.trim(),
    port = $("#proxy-port").value;
  if (
    !host ||
    !port ||
    Number(port) < 1 ||
    Number(port) > 65535 ||
    !Number.isInteger(Number(port))
  )
    throw new Error("Nhập host và port hợp lệ từ 1–65535.");
  const body = {
    name: $("#proxy-name").value.trim(),
    expiresOn: $("#proxy-expires-on").value || null,
    enabled: $("#proxy-enabled").checked,
    address: `${$("#proxy-protocol").value}://${host}:${port}`,
    username: $("#proxy-username").value.trim(),
  };
  if ($("#proxy-password").value) body.password = $("#proxy-password").value;
  if ($("#proxy-clear").checked) body.clearPassword = true;
  return body;
}
$("#proxy-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  const button = event.submitter;
  button.disabled = true;
  errorBox("#proxy-error", "");
  try {
    const { data } = await api(
      state.proxyEditorId
        ? `/api/proxies/${state.proxyEditorId}`
        : "/api/proxies",
      { method: state.proxyEditorId ? "PATCH" : "POST", body: proxyBody() },
    );
    await loadProxy(data.id);
    toast("Đã lưu cấu hình proxy.");
  } catch (error) {
    errorBox("#proxy-error", error.message);
  } finally {
    button.disabled = false;
  }
});
$("#test-proxy").onclick = async (event) => {
  const button = event.currentTarget;
  button.disabled = true;
  errorBox("#proxy-error", "");
  $("#proxy-result").hidden = true;
  try {
    const { data } = await api(
      state.proxyEditorId
        ? `/api/proxies/${state.proxyEditorId}/test`
        : "/api/proxies/test",
      { method: "POST", body: proxyBody() },
    );
    $("#proxy-result").hidden = false;
    $("#proxy-result").textContent =
      `Kết nối proxy thành công · IP đầu ra: ${data.ip}`;
  } catch (error) {
    errorBox("#proxy-error", error.message);
  } finally {
    button.disabled = false;
  }
};
$("#new-proxy").onclick = () => {
  fillProxy(null);
  $("#proxy-name").focus();
};
$("#proxy-list").addEventListener("click", async (event) => {
  const select = event.target.closest("[data-proxy-select]");
  if (select)
    fillProxy(
      state.proxies.find((proxy) => proxy.id === select.dataset.proxySelect),
    );
  const remove = event.target.closest("[data-proxy-delete]");
  if (remove && confirm("Xóa cấu hình proxy này?")) {
    try {
      await api(`/api/proxies/${remove.dataset.proxyDelete}`, {
        method: "DELETE",
      });
      await loadProxy();
      toast("Đã xóa cấu hình proxy.");
    } catch (error) {
      toast(error.message, true);
    }
  }
});

let logTimer;
function navigate() {
  const view = location.hash.slice(1);
  state.view = ["accounts", "logs", "proxy"].includes(view) ? view : "accounts";
  closeDialog();
  clearInterval(logTimer);
  document.querySelectorAll(".page").forEach((page) => {
    page.hidden = page.id !== `${state.view}-page`;
  });
  document.querySelectorAll("[data-nav]").forEach((link) => {
    link.classList.toggle("active", link.dataset.nav === state.view);
    link.setAttribute(
      "aria-current",
      link.dataset.nav === state.view ? "page" : "false",
    );
  });
  $("#breadcrumb").textContent = {
    accounts: "Tài khoản",
    logs: "Nhật ký worker",
    proxy: "Cấu hình proxy",
  }[state.view];
  document.title = `${$("#breadcrumb").textContent} · Lotus Client`;
  if (state.view === "accounts") {
    loadAccounts();
    logTimer = setInterval(() => {
      if (!state.modal) loadAccounts();
    }, 5000);
  }
  if (state.view === "logs") {
    loadLogs();
    logTimer = setInterval(loadLogs, 5000);
  }
  if (state.view === "proxy") loadProxy();
}
window.addEventListener("hashchange", navigate);
$("#dialog").addEventListener("cancel", (event) => {
  event.preventDefault();
  closeDialog();
});
document.addEventListener("click", async (event) => {
  if (event.target.closest("[data-close]")) closeDialog();
  if (
    event.target.closest("[data-create]") ||
    event.target.closest("#add-account")
  )
    accountForm();
  const button = event.target.closest("[data-action]");
  if (!button) return;
  const account = state.accounts.find((a) => a.id === button.dataset.id);
  if (!account) return;
  const action = button.dataset.action;
  if (action === "edit") accountForm(account);
  if (action === "details") details(account);
  if (action === "logs") accountLogs(account);
  if (action === "toggle") {
    if (account.status !== "running") {
      runAccount(account);
      return;
    }
    button.disabled = true;
    try {
      await api(`/api/accounts/${account.id}/status`, {
        method: "PATCH",
        body: { status: account.status === "running" ? "stopped" : "running" },
      });
      toast("Đã cập nhật trạng thái.");
      await loadAccounts();
    } catch (error) {
      toast(error.message, true);
      button.disabled = false;
    }
  }
});
let searchTimer;
$("#account-search").oninput = () => {
  clearTimeout(searchTimer);
  searchTimer = setTimeout(() => {
    state.page = 1;
    loadAccounts();
  }, 250);
};
for (const selector of ["#provider-filter", "#status-filter"])
  $(selector).onchange = () => {
    state.page = 1;
    loadAccounts();
  };
$("#refresh-accounts").onclick = loadAccounts;
$("#accounts-prev").onclick = () => {
  state.page--;
  loadAccounts();
};
$("#accounts-next").onclick = () => {
  state.page++;
  loadAccounts();
};
$("#refresh-logs").onclick = loadLogs;
$("#log-level").onchange = () => {
  state.logPage = 1;
  loadLogs();
};
for (const field of ["#log-from", "#log-to"])
  $(field).onchange = () => {
    state.logPage = 1;
    loadLogs();
  };
$("#logs-prev").onclick = () => {
  state.logPage--;
  loadLogs();
};
$("#logs-next").onclick = () => {
  state.logPage++;
  loadLogs();
};
navigate();
