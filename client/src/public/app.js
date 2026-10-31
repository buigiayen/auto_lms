import { accountMood } from "./account-mood.js";
import { accountIdentityPolicy } from "./account-identity.js";
import { renderCourseProgress } from "./course-progress.js";

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
const runSectionNames = {
  exam: "Kiểm tra",
  review: "Ôn luyện",
  theory: "Lý thuyết",
  practical: "Thực hành (mô phỏng)",
};
const runSectionSummary = (account) =>
  (account.runSections ?? Object.keys(runSectionNames))
    .map((section) => runSectionNames[section]).filter(Boolean).join(", ");
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
  authenticated: false,
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
    if (response.status === 401 && value.error?.code === 'AUTH_REQUIRED') {
      state.authenticated = false;
      clearInterval(logTimer);
      location.replace('/login' + location.hash);
    }
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
function createRegistrationKey() {
  const bytes = new Uint8Array(32);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, (byte) => byte.toString(16).padStart(2, "0")).join("");
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
  deferred: "Chờ lịch chạy lại",
};
const spinner = (message) =>
  `<div class="loading"><span class="spinner"></span>${escape(message)}</div>`;

const accountWorker = (account) =>
  account.execution?.workerName ||
  (account.workerName === "unassigned" ? "Chưa tiếp nhận" : account.workerName) ||
  "Chưa tiếp nhận";
const mascotMoodLabels = {
  sleeping: "Đang nghỉ",
  awake: "Thức giấc",
  celebrating: "Ăn mừng!",
  angry: "Gặp lỗi",
};
const mascotMoodMessages = {
  sleeping: "Nghỉ một chút, sẵn sàng khi bạn cần.",
  awake: "Sẵn sàng nhận công việc mới.",
  celebrating: "Xong việc rồi, nhảy ăn mừng thôi!",
  angry: "Có lỗi rồi! Chọn tên worker để xem log.",
};

// Inline vector artwork keeps the character crisp at every size; CSS animates
// the head, eyes and hands according to the account's current state.
function mascotScene(account = null) {
  return `<svg class="mascot-scene is-${accountMood(account)}" viewBox="0 0 280 200" aria-hidden="true">
    <ellipse class="scene-shadow" cx="140" cy="182" rx="105" ry="9"/>
    <g class="desk-chair"><path class="chair-stem" d="M140 137v40m-23 6 23-6 23 6"/><rect class="chair-back" x="108" y="82" width="64" height="64" rx="23"/></g>
    <g class="mascot-body">
      <path class="mascot-foot" d="M121 140v26q9 7 17 0v-24m7 0v24q10 7 17 0v-26"/>
      <path class="mascot-shirt" d="M109 140v-25q0-22 31-22t31 22v25Z"/>
      <path class="shirt-seam" d="m134 106 6 7 6-7m-6 7v20"/>
      <g class="mascot-head">
        <path class="mascot-ear" d="M108 64Q89 34 113 41l14 18m27-1q24-32 27-12l-9 26"/>
        <path class="mascot-face" d="M103 73q0-27 37-27t37 27v17q0 25-37 25t-37-25Z"/>
        <path class="mascot-tuft" d="M135 48q-8-13-1-18 11 3 11 17 9-13 17-8-1 9-17 12"/>
        <ellipse class="mascot-cheek" cx="116" cy="87" rx="6" ry="4"/><ellipse class="mascot-cheek" cx="164" cy="87" rx="6" ry="4"/>
        <g class="eyes-open"><ellipse cx="125" cy="78" rx="3" ry="4.5"/><ellipse cx="155" cy="78" rx="3" ry="4.5"/><path d="M135 88q5 6 10 0"/></g>
        <g class="eyes-closed"><path d="m119 80 6 3 6-3m18 0 6 3 6-3"/><ellipse cx="140" cy="94" rx="3" ry="4"/></g>
        <g class="eyes-happy"><path d="M119 79q6-10 12 0m18 0q6-10 12 0"/><path class="happy-mouth" d="M132 87q8 16 16 0Z"/></g>
        <g class="eyes-angry"><path class="angry-brows" d="m118 68 14 6m16 0 14-6"/><ellipse cx="125" cy="79" rx="3" ry="3"/><ellipse cx="155" cy="79" rx="3" ry="3"/><path class="angry-mouth" d="M133 90q7-9 14 0"/></g>
      </g>
      <path class="mascot-hand hand-left" d="M111 114q-15 5-8 18l21 4"/><path class="mascot-hand hand-right" d="M170 114q15 5 8 18l-21 4"/>
      <g class="celebration-arms"><path d="M111 115Q88 101 91 73m79 42q23-14 20-42"/><circle cx="91" cy="72" r="6"/><circle cx="190" cy="72" r="6"/></g>
    </g>
    <g class="sleep-letters"><text x="190" y="65">z</text><text x="207" y="48">z</text><text x="222" y="28">Z</text></g>
    <g class="desk-furniture"><path class="desk-leg" d="M62 144v37m156-37v37"/><rect class="desk-front" x="46" y="139" width="188" height="12" rx="4"/><rect class="desk-top" x="40" y="133" width="200" height="9" rx="4"/></g>
    <g class="laptop"><path class="laptop-shell" d="m105 130-7-40q-1-4 4-4h76q5 0 4 4l-7 40Z"/><path class="laptop-logo" d="M133 105q7-12 14 0-7 13-14 0Z"/><path class="laptop-error" d="m132 98 16 15m0-15-16 15"/><rect class="laptop-base" x="94" y="129" width="92" height="5" rx="2"/><circle class="laptop-light" cx="169" cy="124" r="2"/></g>
    <g class="desk-plant"><path class="plant-stem" d="M65 116V97"/><path class="plant-leaf" d="M65 108q-18-1-12-15 13 2 12 15m0-4q0-17 14-14 0 13-14 14"/><path class="plant-pot" d="m55 113 3 20h15l3-20Z"/></g>
    <g class="desk-cup"><path d="M205 118h8q9 7-1 9"/><path class="cup-body" d="M195 114h15v17h-15Z"/><path class="cup-steam" d="M201 107q-5-5 0-10"/></g>
    <g class="celebration-confetti"><path class="confetti-gold" d="m68 47 5 8m130-28 7 5m-10 44 8-3"/><path class="confetti-green" d="m79 25 7 4m126 29 3-9M70 83l7-2"/><path class="confetti-pink" d="m99 39 3-7m80 5 5 7"/><path class="celebration-star" d="m213 19 2 6 6 2-6 2-2 6-2-6-6-2 6-2Z"/></g>
    <g class="tantrum-hands"><g class="tantrum-left"><path d="M110 113Q83 104 104 123"/><circle cx="111" cy="123" r="7"/></g><g class="tantrum-right"><path d="M170 113Q193 101 173 110"/><circle cx="166" cy="110" r="7"/></g></g>
    <g class="anger-symbol"><path d="M195 42v6h-6m15-6v6h6m-21 9h6v6m15-6h-6v6"/><path class="anger-steam" d="M86 65q-8-6-3-12m-5 16q-8-6-3-12"/></g>
    <g class="tantrum-impact impact-desk"><path d="m92 130-7-5m6 11-10 1m102-6 7-6m-5 12 10 1"/></g>
    <g class="tantrum-impact impact-chair"><path d="m102 94-8-5m7 13-11 2m84-11 8-5m-7 13 11 2"/></g>
    <g class="tantrum-impact impact-laptop"><path d="m126 87-5-7m20 5v-10m17 12 6-7"/></g>
  </svg>`;
}

function accountInformation(account) {
  const execution = account.execution;
  const fields = [
    ["Căn cước", account.identityNumber],
    ["Nhà cung cấp", providerName(account.provider)],
    ["Giáo viên quản lý", account.managingTeacher || "Chưa nhập"],
    ["Worker", accountWorker(account)],
    ["Trạng thái", account.status === "running" ? "Treo" : "Dừng treo"],
    ["Hoạt động", executionLabels[execution?.state] || "Đã dừng"],
    ["Chương trình", account.enrolmentPlanIid ? `${account.enrolmentPlanName || "Chương trình học"} · IID ${account.enrolmentPlanIid}` : "Chưa chọn"],
    ["Phần cần chạy", runSectionSummary(account)],
    ...(execution?.currentLesson ? [
      ["Phần đang chạy", runSectionNames[execution.currentLesson.section] || execution.currentLesson.section],
      ["Bài cha đang chạy", execution.currentLesson.courseName],
      ["Bài con đang chạy", execution.currentLesson.itemName],
    ] : []),
    ["Proxy", account.proxy ? `${account.proxy.name} · ${account.proxy.address}${account.proxy.enabled ? "" : " · Đang tắt"}` : "Kết nối trực tiếp"],
    ...(account.proxy ? [["Hạn proxy", account.proxy.expiresOn || "Chưa đặt"]] : []),
    ["Mật khẩu", account.hasPassword ? "Đã lưu, được mã hóa" : "Chưa lưu"],
    ["Ngày tạo", date(account.createdAt)],
    ["Cập nhật", date(account.updatedAt)],
    ...(execution?.lastHeartbeatAt ? [["Heartbeat", date(execution.lastHeartbeatAt)]] : []),
    ...(execution?.availableAfter ? [["Tiếp tục lúc", date(execution.availableAfter)]] : []),
    ["ID tài khoản", account.id],
    ...(execution?.runId ? [["ID lượt chạy", execution.runId]] : []),
  ];
  return `<dl class="mascot-information">${fields.map(([label, value]) => `<div><dt>${escape(label)}</dt><dd>${escape(value)}</dd></div>`).join("")}</dl>${execution?.lastLog ? `<div class="mascot-last-log"><span>LOG GẦN NHẤT · ${date(execution.lastLog.occurredAt)}</span><p>${escape(execution.lastLog.message)}</p></div>` : ""}`;
}

function accountStation(account) {
  const mood = accountMood(account);
  const execution = account.execution?.state || "stopped";
  return `<article class="account-station station-${mood}" data-station-id="${escape(account.id)}">
    <div class="station-heading"><span class="provider-tag"><span class="provider-symbol ${account.provider === "truongson" ? "blue" : ""}">${account.provider === "truongson" ? "T" : "Y"}</span>${providerName(account.provider)}</span><span class="station-status"><i></i>${mascotMoodLabels[mood]}</span></div>
    <div class="mascot-target">
      ${mascotSpeech(account.execution?.currentLesson)}
      <button class="mascot-button" data-action="account" data-id="${escape(account.id)}" aria-label="Mở thao tác tài khoản ${escape(account.identityNumber)}" aria-describedby="account-tip-${escape(account.id)}" aria-haspopup="dialog">${mascotScene(account)}<span class="mascot-invitation">Chọn tôi để thao tác ${icon("arrow")}</span></button>
      <div class="mascot-tooltip" id="account-tip-${escape(account.id)}" role="tooltip"><strong>Hồ sơ tài khoản</strong>${accountInformation(account)}</div>
    </div>
    <div class="station-account"><strong>${escape(account.identityNumber)}</strong><span class="execution-state ${escape(execution)}">${execution === "deferred" && account.execution?.availableAfter ? `Chạy lại ${date(account.execution.availableAfter)}` : executionLabels[execution] || escape(execution)}</span></div>
    <button class="worker-nameplate" data-action="logs" data-id="${escape(account.id)}" aria-label="Xem log của ${escape(accountWorker(account))}, tài khoản ${escape(account.identityNumber)}">${icon("server")}<span>${escape(accountWorker(account))}</span>${icon("log")}</button>
    <p class="station-activity" title="${escape(account.execution?.lastLog?.message || "")}">${escape(account.execution?.lastLog?.message || mascotMoodMessages[mood])}</p>
  </article>`;
}

function mascotSpeech(lesson) {
  return `<div class="mascot-speech" role="status" aria-live="polite" aria-atomic="true"${lesson ? "" : " hidden"}>${lesson ? `<span class="mascot-speech-intro">Tôi đang xử lý:</span> <p><strong>${escape(runSectionNames[lesson.section] || lesson.section)}:</strong> ${escape(lesson.courseName)} <span class="mascot-speech-plus">+</span> ${escape(lesson.itemName)}</p>` : ""}</div>`;
}

function currentLessonInformation(lesson) {
  if (!lesson) return "";
  return `<div class="station-current-lesson"><span class="current-lesson-section">ĐANG CHẠY · ${escape(runSectionNames[lesson.section] || lesson.section)}</span><div><span>Bài cha</span><strong>${escape(lesson.courseName)}</strong></div><div><span>Bài con</span><strong>${escape(lesson.itemName)}</strong></div></div>`;
}

function renderAccountStations(accounts, total) {
  const grid = $("#account-stations");
  // Reuse unchanged stations during polling so animation, hover and keyboard
  // focus survive the five-second refresh.
  const existing = new Map(Array.from(grid.querySelectorAll("[data-station-id]"), (node) => [node.dataset.stationId, node]));
  const retained = new Set(accounts.map((account) => account.id));
  for (const [id, node] of existing) if (!retained.has(id)) node.remove();
  grid.querySelectorAll(".office-placeholder").forEach((node) => node.remove());
  if (!accounts.length) {
    grid.insertAdjacentHTML("afterbegin", `<div class="office-placeholder">${total ? empty("Không tìm thấy người bạn nào", "Thử thay đổi từ khóa hoặc bộ lọc của bạn.") : empty("Văn phòng đang chờ người bạn đầu tiên", "Tạo tài khoản để một mascot mới có góc làm việc của riêng mình.")}</div>`);
  }
  let position = 0;
  for (const account of accounts) {
    const signature = JSON.stringify(account);
    let node = existing.get(account.id);
    if (!node || node.accountSignature !== signature) {
      const template = document.createElement("template");
      template.innerHTML = accountStation(account);
      const next = template.content.firstElementChild;
      if (node) {
        // Keep the SVG mounted so heartbeats and incoming logs cannot restart
        // a celebration or interrupt the desk/chair/laptop animation sequence.
        node.className = next.className;
        const scene = node.querySelector(".mascot-scene");
        const sceneClass = next.querySelector(".mascot-scene").getAttribute("class");
        if (scene.getAttribute("class") !== sceneClass) scene.setAttribute("class", sceneClass);
        for (const selector of [".station-heading", ".mascot-speech", ".mascot-tooltip", ".station-account", ".worker-nameplate", ".station-activity"]) {
          const current = node.querySelector(selector), replacement = next.querySelector(selector);
          if (current.innerHTML !== replacement.innerHTML) current.innerHTML = replacement.innerHTML;
        }
        node.querySelector(".mascot-speech").hidden = next.querySelector(".mascot-speech").hidden;
        for (const [selector, attribute] of [[".mascot-button", "aria-label"], [".worker-nameplate", "aria-label"], [".station-activity", "title"]])
          node.querySelector(selector).setAttribute(attribute, next.querySelector(selector).getAttribute(attribute));
      } else node = next;
      node.accountSignature = signature;
      if (grid.children[position] !== node) grid.insertBefore(node, grid.children[position] || null);
    } else if (grid.children[position] !== node) {
      grid.insertBefore(node, grid.children[position] || null);
    }
    position++;
  }
  if (!grid.querySelector(".station-create")) grid.insertAdjacentHTML("beforeend", `<button class="station-create" data-create aria-haspopup="dialog"><span class="create-mascot" aria-hidden="true">${icon("plus")}</span><strong>Một người bạn mới</strong><span>Thêm tài khoản vào văn phòng</span><span class="create-seat">Còn một chỗ dành cho bạn ${icon("arrow")}</span></button>`);
  grid.setAttribute("aria-busy", "false");
}

function accountActions(account) {
  const epoch = openDialog(
    `Tài khoản ${account.identityNumber}`,
    `${providerName(account.provider)} · ${accountWorker(account)}`,
    `<div class="dialog-body account-actions-body"><div class="account-action-preview ${account.provider === "yenthuy" ? "station-yenthuy" : ""}">${mascotScene(account)}<span class="badge ${account.status}">${account.status === "running" ? "Treo" : "Dừng treo"} · ${executionLabels[account.execution?.state] || "Đã dừng"}</span></div><div id="account-current-lesson">${currentLessonInformation(account.execution?.currentLesson)}</div><div class="mascot-actions"><button class="mascot-action" data-action="toggle" data-id="${escape(account.id)}">${icon(account.status === "running" ? "pause" : "play")}<span><strong>${account.status === "running" ? "Dừng treo" : "Run tài khoản"}</strong><small>${account.status === "running" ? "Cho mascot nghỉ và dừng công việc" : "Chọn chương trình học và bắt đầu"}</small></span>${icon("arrow")}</button><button class="mascot-action" data-action="edit" data-id="${escape(account.id)}">${icon("edit")}<span><strong>Sửa thông tin</strong><small>Cập nhật tài khoản, giáo viên và proxy</small></span>${icon("arrow")}</button><button class="mascot-action" data-action="details" data-id="${escape(account.id)}">${icon("grid")}<span><strong>Xem chi tiết</strong><small>Mở chương trình và thông tin học tập</small></span>${icon("arrow")}</button></div><details class="account-profile"><summary>Thông tin đầy đủ của tài khoản</summary><div id="account-profile-information">${accountInformation(account)}</div></details></div>`,
  );
  const modal = state.modal = { type: "account-actions", epoch, account, controller: new AbortController() };
  let loading = false;
  const refresh = async () => {
    if (loading) return;
    loading = true;
    try {
      const { data } = await api(`/api/accounts/${encodeURIComponent(account.id)}`, { signal: modal.controller.signal });
      if (state.modal !== modal) return;
      modal.account = data;
      $("#account-current-lesson").innerHTML = currentLessonInformation(data.execution?.currentLesson);
      $("#account-profile-information").innerHTML = accountInformation(data);
    } catch {
      // Keep the last snapshot while offline; closing the dialog aborts this request.
    } finally { loading = false; }
  };
  refresh();
  modal.timer = setInterval(refresh, 5000);
}

async function loadAccounts() {
  const version = ++state.accountRequest;
  $("#account-stations").setAttribute("aria-busy", "true");
  const params = new URLSearchParams({
    page: state.page,
    pageSize: 20,
    search: $("#account-search").value,
    provider: $("#provider-filter").value,
    status: $("#status-filter").value,
    mood: $("#mascot-filter").value,
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
    renderAccountStations(result.data, summary.total);
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
    const grid = $("#account-stations");
    grid.setAttribute("aria-busy", "false");
    if (!grid.querySelector("[data-station-id]"))
      grid.innerHTML = `<div class="office-placeholder">${empty("Chưa kết nối được backend", "Kiểm tra Node.js server và bấm Làm mới để thử lại.")}</div>`;
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
function openDialog(title, subtitle, body, { wide = false, viewport = false, footer = "" } = {}) {
  closeDialog();
  const dialog = $("#dialog");
  dialog.className = viewport ? "viewport" : wide ? "wide" : "";
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
  $("#run-error").insertAdjacentHTML("beforebegin",
    `<fieldset class="proxy-picker run-sections"><legend>3. Phần cần chạy</legend><p class="field-help">Chọn ít nhất một phần. Worker chỉ xử lý nội dung đã chọn.</p><div class="run-section-options">${Object.entries(runSectionNames).map(([value, label]) => `<label class="checkbox-label"><input type="checkbox" name="runSections" value="${value}"${(account.runSections ?? Object.keys(runSectionNames)).includes(value) ? " checked" : ""}>${label}</label>`).join("")}</div><p class="field-help">Kiểm tra và thực hành hiện chỉ được lọc trong kế hoạch; worker chưa có luồng thực thi hai phần này.</p></fieldset>`);
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
  const selectedSections = () =>
    [...document.querySelectorAll('#run-form input[name="runSections"]:checked')]
      .map((input) => input.value);
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
    button.disabled = !plan || !modal.sessionId || !selectedSections().length;
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
  $("#run-form .run-sections").onchange = selectionChanged;
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
    if (!selectedSections().length) {
      errorBox("#run-error", "Chọn ít nhất một phần cần chạy.");
      selectionChanged();
      return;
    }
    button.disabled = true;
    errorBox("#run-error", "");
    try {
      await api(`/api/accounts/${account.id}/run`, {
        method: "POST",
        sessionId: modal.sessionId,
        body: {
          proxyId: selectedProxy(),
          enrolmentPlanIid: Number($("#run-plan").value),
          runSections: selectedSections(),
        },
      });
      if (current()) closeDialog();
      toast("Đã lưu chương trình, phần cần chạy và đưa account vào hàng chờ Worker.");
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

async function accountForm(account) {
  let identityPolicy;
  try {
    const { data } = await api('/api/config');
    identityPolicy = accountIdentityPolicy(data.allowTextUsernames);
  } catch (error) {
    toast(error.message, true);
    return;
  }
  const editing = Boolean(account);
  const busy = ["accepted", "running", "stopping"].includes(
    account?.execution?.state,
  );
  const epoch = openDialog(
    editing ? "Sửa tài khoản" : "Thêm tài khoản",
    editing
      ? `Cập nhật thông tin ${account.identityNumber}`
      : "Kết nối tài khoản LMS vào không gian của bạn.",
    `<form id="account-form"><div class="dialog-body"><div class="field-row"><label>${escape(identityPolicy.label)}<input name="identityNumber" id="identityNumber" required inputmode="${identityPolicy.inputMode}" pattern="${escape(identityPolicy.pattern)}" maxlength="${identityPolicy.maxLength}" placeholder="${escape(identityPolicy.placeholder)}" value="${escape(account?.identityNumber)}" autocomplete="off"></label><label>Nhà cung cấp<select name="provider"><option value="truongson" ${account?.provider === "truongson" ? "selected" : ""}>Trường Sơn</option><option value="yenthuy" ${account?.provider === "yenthuy" ? "selected" : ""}>Yên Thủy</option></select></label></div><div class="field-row"><label>Mật khẩu<input name="password" id="password" type="password" ${editing ? "" : "required"} maxlength="1024" placeholder="${editing ? "Giữ mật khẩu hiện tại" : "Mật khẩu đăng nhập LMS"}" autocomplete="new-password"></label><label>Tên worker (tùy chọn)<input name="workerName" id="workerName" maxlength="100" placeholder="Tự cập nhật khi Worker tiếp nhận" value="${escape(account?.workerName === "unassigned" ? "" : account?.workerName)}" autocomplete="off"></label></div><label class="teacher-field">Giáo viên quản lý<input name="managingTeacher" id="managingTeacher" maxlength="200" placeholder="Nhập tên giáo viên quản lý" value="${escape(account?.managingTeacher)}" autocomplete="off"></label><fieldset class="proxy-picker account-proxy-field"><legend>Proxy</legend><div id="account-proxy">Đang tải cấu hình proxy…</div></fieldset>${!editing ? '<label>Trạng thái ban đầu<select name="status"><option value="stopped">Dừng treo</option><option value="running">Treo</option></select></label>' : ""}<p class="field-help">${busy ? "Worker đang chạy. Bạn có thể cập nhật Giáo viên quản lý; dừng Worker trước khi sửa thông tin khác." : editing ? "Để trống mật khẩu để giữ nguyên. Thay đổi thông tin đăng nhập sẽ đóng phiên xem chi tiết cũ." : "Thông tin được lưu trên máy. GUID thiết bị được tạo tự động."}</p><div id="account-form-error" class="inline-error" role="alert" hidden></div></div><div class="dialog-footer"><button type="button" class="button" data-close>Hủy</button><button class="button primary" type="submit">${icon("check")}${editing ? "Lưu thay đổi" : "Tạo tài khoản"}</button></div></form>`,
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
    `${account.identityNumber} · ${providerName(account.provider)} · ${accountWorker(account)}`,
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
    `<div class="dialog-body">${account.status === "running" ? '<div class="status-notice">Tài khoản đang ở trạng thái Treo. Đăng nhập xem chi tiết có thể ảnh hưởng phiên của worker.</div>' : ""}<div id="detail-status">${spinner("Đang đăng nhập vào LMS…")}</div><div id="detail-content" hidden><section class="run-plan-panel"><div class="run-plan-heading"><div><h3>KPI hôm nay (giây)</h3><p class="run-plan-summary" id="daily-kpi-result" role="status" aria-live="polite">Chưa kiểm tra.</p></div><button type="button" class="button small" id="check-daily-kpi">${icon("refresh")}Kiểm tra KPI 1 ngày</button></div></section><div class="plan-toolbar"><label>Chương trình học<select id="plan-select"><option value="">Chọn chương trình học…</option></select></label><button class="button" id="more-plans" hidden>Tải thêm chương trình</button></div><div class="course-progress-heading"><div><h3>Tiến độ học tập</h3><p>Điểm đạt / Điểm tối đa</p></div><div class="course-progress-legend"><span class="is-passed">Đạt</span><span class="is-failed">Chưa đạt</span></div></div><div id="course-progress-placeholder" class="course-progress-placeholder" role="status">Chọn một chương trình để xem tiến độ học tập.</div><div id="course-progress" hidden></div><div class="pagination modal-pagination" id="course-pagination" hidden><span id="course-page">Trang 1</span><div><button class="button small" id="course-prev">← Trang trước</button><button class="button small" id="course-next">Trang sau →</button></div></div></div><div id="detail-error" class="inline-error" role="alert" hidden></div><button class="button" id="retry-login" hidden>Đăng nhập lại</button></div>`,
    { viewport: true },
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
      $("#course-progress-placeholder").textContent =
        "Tài khoản chưa có chương trình học.";
    else if (!selected && !$("#course-progress-placeholder").hidden)
      $("#course-progress-placeholder").textContent =
        "Chọn một chương trình để xem tiến độ học tập.";
  }
  async function courses() {
    const id = $("#plan-select").value,
      request = ++modal.request;
    modal.controller?.abort();
    modal.controller = new AbortController();
    $("#course-progress").hidden = true;
    $("#course-progress").innerHTML = "";
    $("#course-pagination").hidden = true;
    $("#retry-login").hidden = true;
    errorBox("#detail-error", "");
    $("#course-progress-placeholder").hidden = false;
    if (!id) {
      $("#course-progress-placeholder").textContent =
        "Chọn một chương trình để xem tiến độ học tập.";
      return;
    }
    $("#course-progress-placeholder").innerHTML = spinner("Đang tính lại điểm và tải tiến độ học tập…");
    try {
      const result = await api(
        `/api/accounts/${account.id}/enrolment-plans/${id}/recalculate-courses?page=${modal.coursePage}&pageSize=20`,
        { method: "POST", sessionId: modal.sessionId, signal: modal.controller.signal },
      );
      if (!current() || modal.request !== request) return;
      $("#course-progress").innerHTML = renderCourseProgress(result.data.rawResponse);
      $("#course-progress").hidden = false;
      $("#course-progress-placeholder").hidden = true;
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
      $("#course-progress-placeholder").textContent =
        "Không tải được tiến độ học tập. Chọn lại chương trình để thử lại.";
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
    $("#course-progress-placeholder").innerHTML = spinner("Đang đọc chương trình học…");
    try {
      await plans();
    } catch (error) {
      if (current()) {
        errorBox("#detail-error", error.message);
        $("#course-progress-placeholder").textContent =
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
  $("#proxy-provider-value").value = "";
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
    $("#proxy-port").value = uri.port || (uri.protocol === "socks5:" ? 1080 : uri.protocol === "https:" ? 443 : 80);
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
function parseProviderProxyValue(value) {
  const trimmed = value.trim(),
    scheme = trimmed.match(/^(https?|socks5):\/\//i),
    raw = scheme ? trimmed.slice(scheme[0].length) : trimmed;
  if (scheme && /^(?:\[[^\]]+\]|[^:/]+):\d+\/?$/.test(raw)) {
    const uri = new URL(trimmed);
    const port = Number(raw.match(/:(\d+)\/?$/)[1]);
    if (uri.hostname && port > 0 && port <= 65535
      && ["", "/"].includes(uri.pathname) && !uri.search && !uri.hash)
      return { host: uri.hostname, port, protocol: scheme[1].toLowerCase(), username: "", password: "" };
  }
  const match = raw.startsWith("[")
    ? raw.match(/^(\[[^\]]+\]):(\d+):([^:]+):(.*)$/)
    : raw.match(/^([^:]+):(\d+):([^:]+):(.*)$/);
  if (!match || !match[1].trim() || !match[3].trim())
    throw new Error("Nhập host:port:username:password hoặc URI như socks5://host:port.");
  const port = Number(match[2]);
  if (!Number.isInteger(port) || port < 1 || port > 65535)
    throw new Error("Port proxy phải là số từ 1–65535.");
  return {
    host: match[1].trim(),
    port,
    protocol: scheme ? scheme[1].toLowerCase() : "http",
    username: match[3].trim(),
    password: match[4],
  };
}
$("#import-proxy-value").onclick = () => {
  try {
    const proxy = parseProviderProxyValue($("#proxy-provider-value").value);
    $("#proxy-host").value = proxy.host;
    $("#proxy-port").value = proxy.port;
    $("#proxy-protocol").value = proxy.protocol;
    $("#proxy-username").value = proxy.username;
    $("#proxy-password").value = proxy.password;
    $("#proxy-clear").checked = !proxy.username;
    $("#proxy-provider-value").value = "";
    errorBox("#proxy-error", "");
  } catch (error) {
    errorBox("#proxy-error", error.message);
  }
};
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
  if (!state.authenticated) return;
  const view = location.hash.slice(1);
  state.view = ["accounts", "logs", "proxy", "workers"].includes(view)
    ? view
    : "accounts";
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
    workers: "Worker & khóa",
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
for (const type of ["pointerover", "focusin"])
  $("#account-stations").addEventListener(type, (event) => {
    const target = event.target.closest(".mascot-target");
    if (!target || target.contains(event.relatedTarget)) return;
    const bounds = target.getBoundingClientRect();
    const above = bounds.top;
    const below = window.innerHeight - bounds.bottom;
    const showAbove = above > below;
    target.classList.toggle("tooltip-above", showAbove);
    target.querySelector(".mascot-tooltip").style.maxHeight =
      `${Math.max(80, Math.min(500, window.innerHeight * .65, showAbove ? above : below))}px`;
  });
// Escape dismisses hover information without moving keyboard focus.
document.addEventListener("keydown", (event) => {
  if (event.key !== "Escape" || $("#dialog").open) return;
  document.querySelectorAll(".mascot-target:hover, .mascot-target:focus-within")
    .forEach((target) => target.classList.add("tooltip-dismissed"));
});
for (const type of ["pointerout", "focusout"])
  $("#account-stations").addEventListener(type, (event) => {
    const target = event.target.closest(".mascot-target");
    if (target && !target.contains(event.relatedTarget))
      target.classList.remove("tooltip-dismissed");
  });
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
  if (action === "account") accountActions(account);
  if (action === "edit") accountForm(account);
  if (action === "details") details(account);
  if (action === "logs") accountLogs(account);
  if (action === "toggle") {
    if (account.status !== "running") {
      runAccount(account);
      return;
    }
    const actionEpoch = state.modalEpoch;
    button.disabled = true;
    try {
      await api(`/api/accounts/${account.id}/status`, {
        method: "PATCH",
        body: { status: account.status === "running" ? "stopped" : "running" },
      });
      if (state.modal?.type === "account-actions" && state.modalEpoch === actionEpoch)
        closeDialog();
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
for (const selector of ["#provider-filter", "#status-filter", "#mascot-filter"])
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
$("#generate-registration-key").onclick = () => {
  if (
    $("#registration-key-value").value &&
    !confirm("Tạo khóa mới sẽ thay thế khóa đang hiển thị. Tiếp tục?")
  )
    return;
  try {
    $("#registration-key-value").value = createRegistrationKey();
    $("#registration-key-result").hidden = false;
  } catch {
    toast("Trình duyệt không hỗ trợ tạo khóa an toàn. Hãy mở giao diện qua localhost hoặc HTTPS.", true);
  }
};
$("#copy-registration-key").onclick = async () => {
  const input = $("#registration-key-value");
  try {
    await navigator.clipboard.writeText(input.value);
    toast("Đã sao chép RegistrationApiKey.");
  } catch {
    input.focus();
    input.select();
    toast("Không thể truy cập clipboard. Khóa đã được chọn để bạn sao chép thủ công.", true);
  }
};
$('#logout').addEventListener('click', async event => {
  const button = event.currentTarget;
  button.disabled = true;
  closeDialog();
  try {
    await api('/api/auth/logout', { method: 'POST' });
    state.authenticated = false;
    clearInterval(logTimer);
    location.replace('/login');
  } catch (error) {
    toast(error.message, true);
    button.disabled = false;
  }
});
window.addEventListener('pageshow', event => { if (event.persisted) location.reload(); });
try {
  const { data } = await api('/api/auth/session');
  if (!data.authenticated) location.replace('/login' + location.hash);
  else {
    state.authenticated = true;
    $('#session-user').textContent = data.username || '';
    $('#session-avatar').textContent = Array.from(data.username || 'LC').slice(0, 2).join('').toUpperCase();
    navigate();
  }
} catch (error) {
  toast('Không thể kiểm tra phiên đăng nhập. Vui lòng tải lại trang.', true);
}
