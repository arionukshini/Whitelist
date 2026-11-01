const status = document.querySelector("#status");
const dot = document.querySelector("#dot");
const session = document.querySelector("#session");
const detail = document.querySelector("#detail");
const remaining = document.querySelector("#remaining");
const sites = document.querySelector("#sites");

function renderTime(endTime) {
  const ms = Date.parse(endTime) - Date.now();
  if (!Number.isFinite(ms) || ms <= 0) return "00:00:00";
  const seconds = Math.floor(ms / 1000);
  return [Math.floor(seconds / 3600), Math.floor(seconds % 3600 / 60), seconds % 60]
    .map(value => String(value).padStart(2, "0")).join(":");
}

async function refresh() {
  try {
    const response = await fetch("http://127.0.0.1:47837/api/session", { cache: "no-store", signal: AbortSignal.timeout(2500) });
    if (!response.ok) throw new Error("Desktop unavailable");
    const state = await response.json();
    status.textContent = "Desktop connected";
    dot.classList.add("on");
    if (state.active && Date.parse(state.endTime) > Date.now()) {
      session.textContent = state.name || "Focus session active";
      detail.textContent = "Website rules are being enforced for this session.";
      remaining.textContent = renderTime(state.endTime);
      sites.textContent = String(state.websites?.length ?? 0);
    } else {
      session.textContent = "No active session";
      detail.textContent = "Start focus mode from the desktop app.";
      remaining.textContent = "—";
      sites.textContent = "—";
    }
  } catch {
    status.textContent = "Desktop disconnected";
    dot.classList.remove("on");
    session.textContent = "Open the desktop app";
    detail.textContent = "Website status is unavailable until Whitelist Lock In is running.";
    remaining.textContent = "—";
    sites.textContent = "—";
  }
}

refresh();
