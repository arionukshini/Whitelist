const sessionEndpoint = "http://127.0.0.1:47837/api/session";

async function getSession() {
  try {
    const response = await fetch(sessionEndpoint, { cache: "no-store" });
    if (!response.ok) {
      return { active: false, websites: [] };
    }

    return await response.json();
  } catch {
    return { active: false, websites: [] };
  }
}

function normalizeHost(hostname) {
  let host = hostname.toLowerCase().replace(/\.$/, "");
  if (host.startsWith("www.")) {
    host = host.slice(4);
  }
  return host;
}

function isAllowed(hostname, rule) {
  const host = normalizeHost(hostname);
  const domain = normalizeHost(rule.domain);
  if (host === domain) {
    return true;
  }

  return rule.includeSubdomains && host.endsWith("." + domain);
}

async function evaluateTab(tabId, url) {
  if (!url || url.startsWith("chrome:") || url.startsWith("edge:") || url.startsWith("brave:")) {
    return;
  }

  const parsed = new URL(url);
  if (parsed.hostname === "127.0.0.1" || parsed.hostname === "localhost") {
    return;
  }

  const session = await getSession();
  if (!session.active) {
    return;
  }

  const allowed = (session.websites || []).some(rule => isAllowed(parsed.hostname, rule));
  if (allowed) {
    return;
  }

  const blockedUrl = chrome.runtime.getURL("blocked.html") +
    `?domain=${encodeURIComponent(parsed.hostname)}` +
    `&session=${encodeURIComponent(session.name || "Focus Session")}` +
    `&end=${encodeURIComponent(session.endTime || "")}`;
  await chrome.tabs.update(tabId, { url: blockedUrl });
}

chrome.webNavigation.onCommitted.addListener(details => {
  if (details.frameId === 0) {
    evaluateTab(details.tabId, details.url);
  }
});

chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
  if (changeInfo.status === "complete" && tab.url) {
    evaluateTab(tabId, tab.url);
  }
});

