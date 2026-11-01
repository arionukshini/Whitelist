const sessionEndpoint = "http://127.0.0.1:47837/api/session";
const blockRuleId = 1;
const allowRuleIdBase = 100;
const syncAlarm = "desktop-session-sync";
const expiryAlarm = "focus-session-expiry";
let syncInFlight;

async function clearExpiredRules() {
  const { sessionEndTime } = await chrome.storage.local.get("sessionEndTime");
  if (!sessionEndTime) return;
  const endTime = Date.parse(sessionEndTime);
  if (!Number.isFinite(endTime)) {
    await chrome.storage.local.remove("sessionEndTime");
    await chrome.alarms.clear(expiryAlarm);
    return;
  }
  if (endTime <= Date.now()) {
    const current = await chrome.declarativeNetRequest.getDynamicRules();
    await chrome.declarativeNetRequest.updateDynamicRules({ removeRuleIds: current.map(rule => rule.id) });
    await chrome.storage.local.remove("sessionEndTime");
    await chrome.alarms.clear(expiryAlarm);
  } else {
    chrome.alarms.create(expiryAlarm, { when: endTime });
  }
}

async function syncRules() {
  try {
    const response = await fetch(sessionEndpoint, { cache: "no-store", signal: AbortSignal.timeout(4000) });
    if (!response.ok) throw new Error(`Desktop returned ${response.status}`);
    const session = await response.json();
    const rules = [];

    if (session.active && session.endTime && Date.parse(session.endTime) > Date.now()) {
      rules.push({
        id: blockRuleId,
        priority: 1,
        action: { type: "redirect", redirect: { extensionPath: "/blocked.html" } },
        condition: {
          regexFilter: "^https?://",
          resourceTypes: ["main_frame"]
        }
      });

      const websites = [...new Map((session.websites || []).map(site => [
        String(site.domain || "").toLowerCase().replace(/^www\./, "").replace(/\.$/, ""),
        site
      ])).values()].filter(site => /^[a-z0-9.-]+$/.test(site.domain) && !site.domain.includes(".."));

      if (websites.length > 4000) throw new Error("The allowlist is too large to synchronize.");
      websites.forEach((site, index) => rules.push({
        id: allowRuleIdBase + index,
        priority: 2,
        action: { type: "allow" },
        condition: {
          regexFilter: `^https?://${site.includeSubdomains ? "(?:[^/:?#]+\\.)?" : ""}${site.domain.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}([/:?#]|$)`,
          resourceTypes: ["main_frame"]
        }
      }));
    }

    const current = await chrome.declarativeNetRequest.getDynamicRules();
    await chrome.declarativeNetRequest.updateDynamicRules({
      removeRuleIds: current.map(rule => rule.id),
      addRules: rules
    });
    if (session.active && session.endTime && Date.parse(session.endTime) > Date.now()) {
      await chrome.storage.local.set({ sessionEndTime: session.endTime });
      chrome.alarms.create(expiryAlarm, { when: Date.parse(session.endTime) });
    } else {
      await chrome.storage.local.remove("sessionEndTime");
      await chrome.alarms.clear(expiryAlarm);
    }
    await chrome.storage.local.set({ connection: { connected: true, checkedAt: Date.now(), active: !!session.active } });
  } catch (error) {
    // Keep the last successfully installed rules when the desktop is offline.
    await clearExpiredRules();
    await chrome.storage.local.set({ connection: { connected: false, checkedAt: Date.now(), error: String(error) } });
  }
}

function requestSync() {
  if (!syncInFlight) syncInFlight = syncRules().finally(() => { syncInFlight = undefined; });
  return syncInFlight;
}

chrome.runtime.onInstalled.addListener(() => {
  chrome.alarms.create(syncAlarm, { periodInMinutes: 0.5 });
});
chrome.runtime.onStartup.addListener(requestSync);
chrome.alarms.onAlarm.addListener(alarm => {
  if (alarm.name === syncAlarm || alarm.name === expiryAlarm) requestSync();
});
requestSync();
