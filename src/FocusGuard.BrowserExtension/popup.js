const nativeHostName = "com.whitelistlockin.host";
const sessionEndpoint = "http://127.0.0.1:47837/api/session";
const status = document.querySelector("#status");
const dot = document.querySelector("#dot");
const sessionTitle = document.querySelector("#session");
const detail = document.querySelector("#detail");
const remaining = document.querySelector("#remaining");
const sites = document.querySelector("#sites");
const hint = document.querySelector("#hint");
const connectButton = document.querySelector("#connect");

function formatTime(endTime) {
  const ms = Date.parse(endTime) - Date.now();
  if (!Number.isFinite(ms) || ms <= 0) return "00:00:00";
  const seconds = Math.floor(ms / 1000);
  return [Math.floor(seconds / 3600), Math.floor(seconds % 3600 / 60), seconds % 60]
    .map(value => String(value).padStart(2, "0")).join(":");
}

function render(session, connection) {
  status.textContent = "Desktop connected";
  dot.classList.add("on");
  connectButton.hidden = true;
  hint.textContent = connection?.transport === "loopback"
    ? "Connected through the local browser fallback."
    : "Your desktop app manages website rules.";
  if (session?.active && Date.parse(session.endTime) > Date.now()) {
    sessionTitle.textContent = session.name || "Focus session active";
    detail.textContent = "Website rules are being enforced for this session.";
    remaining.textContent = formatTime(session.endTime);
    sites.textContent = String(session.websites?.length ?? 0);
  } else {
    sessionTitle.textContent = "No active session";
    detail.textContent = "Start focus mode from the desktop app.";
    remaining.textContent = "—";
    sites.textContent = "—";
  }
}

function showDisconnected(error = "") {
  status.textContent = "Desktop disconnected";
  dot.classList.remove("on");
  sessionTitle.textContent = "Connect to Whitelist Lock In";
  detail.textContent = error.includes("native messaging host not found")
    ? "Install or update Whitelist Lock In to register the browser bridge, then reload this extension."
    : "The desktop bridge could not be reached. Click Connect to retry.";
  remaining.textContent = "—";
  sites.textContent = "—";
  hint.textContent = error && !error.includes("native messaging host not found") ? error : "If Chrome asks, allow local network access.";
  connectButton.hidden = false;
}

function requestNativeSession() {
  return new Promise((resolve, reject) => {
    let port;
    try { port = chrome.runtime.connectNative(nativeHostName); }
    catch (error) { reject(error); return; }
    let settled = false;
    const timeout = setTimeout(() => finish(new Error("Native messaging host timed out.")), 7000);
    function finish(error, session) {
      if (settled) return;
      settled = true;
      clearTimeout(timeout);
      try { port.disconnect(); } catch { }
      if (error) reject(error); else resolve(session);
    }
    port.onMessage.addListener(message => message?.error
      ? finish(new Error(message.detail || message.error))
      : finish(null, message));
    port.onDisconnect.addListener(() => {
      if (!settled) finish(new Error(chrome.runtime.lastError?.message || "Native host disconnected."));
    });
    try { port.postMessage({ type: "get-session" }); }
    catch (error) { finish(error); }
  });
}

async function applySession(session, transport) {
  const state = await chrome.runtime.sendMessage({ type: "session-from-popup", session, transport });
  if (!state?.connection?.connected) throw new Error(state?.connection?.error || "The browser rules could not be updated.");
  render(session, state.connection);
}

async function connect() {
  connectButton.disabled = true;
  connectButton.textContent = "Connecting…";
  status.textContent = "Connecting to desktop…";
  try {
    const session = await requestNativeSession();
    await applySession(session, "native");
  } catch (nativeError) {
    try {
      const response = await fetch(sessionEndpoint, { cache: "no-store", signal: AbortSignal.timeout(20_000) });
      if (!response.ok) throw new Error(`Desktop returned ${response.status}`);
      const session = await response.json();
      await applySession(session, "loopback");
    } catch (loopbackError) {
      showDisconnected(`Native: ${String(nativeError)} · Local fallback: ${String(loopbackError)}`);
    }
  } finally {
    connectButton.disabled = false;
    connectButton.textContent = "Connect to desktop";
  }
}

async function showLastStatus() {
  try {
    const saved = await chrome.storage.local.get(["connection", "desktopSession"]);
    if (saved.connection?.connected && Date.now() - saved.connection.checkedAt < 90_000) {
      render(saved.desktopSession, saved.connection);
    } else {
      showDisconnected(saved.connection?.error || "");
    }
  } catch { showDisconnected("The extension could not read its connection status."); }
}

connectButton.addEventListener("click", connect);
showLastStatus();
