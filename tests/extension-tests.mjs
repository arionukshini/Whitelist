import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import vm from "node:vm";

const workerSource = await readFile(new URL("../src/FocusGuard.BrowserExtension/service-worker.js", import.meta.url), "utf8");

function createHarness(initialSession, { nativeEnabled = false } = {}) {
  const state = { rules: [], data: {}, session: initialSession, online: true, nativeEnabled, fetchCount: 0 };
  const event = () => ({ addListener() {} });
  const chrome = {
    runtime: {
      onInstalled: event(), onStartup: event(), onMessage: event(),
      connectNative: () => {
        if (!state.nativeEnabled) throw new Error("Specified native messaging host not found.");
        const messageListeners = [];
        const disconnectListeners = [];
        return {
          onMessage: { addListener: listener => messageListeners.push(listener) },
          onDisconnect: { addListener: listener => disconnectListeners.push(listener) },
          postMessage: message => {
            assert.equal(message.type, "get-session");
            queueMicrotask(() => messageListeners.forEach(listener => listener(state.session)));
          },
          disconnect: () => disconnectListeners.forEach(listener => listener())
        };
      }
    },
    alarms: { onAlarm: event(), create: async () => {}, clear: async () => {} },
    declarativeNetRequest: {
      getDynamicRules: async () => state.rules,
      updateDynamicRules: async ({ removeRuleIds, addRules = [] }) => {
        state.rules = state.rules.filter(rule => !removeRuleIds.includes(rule.id)).concat(addRules);
      }
    },
    storage: { local: {
      get: async key => ({ [key]: state.data[key] }),
      set: async values => Object.assign(state.data, values),
      remove: async key => { delete state.data[key]; }
    } }
  };
  const context = vm.createContext({
    chrome,
    AbortSignal,
    Date,
    setTimeout,
    clearTimeout,
    fetch: async () => {
      state.fetchCount++;
      if (!state.online) throw new Error("offline");
      return { ok: true, json: async () => state.session };
    }
  });
  const firstSync = vm.runInContext(workerSource, context);
  return { state, context, firstSync };
}

test("active session installs a catch-all block plus domain-boundary allow rules", async () => {
  const endTime = new Date(Date.now() + 60_000).toISOString();
  const { state, firstSync } = createHarness({
    active: true,
    endTime,
    websites: [
      { domain: "youtube.com", includeSubdomains: true },
      { domain: "reddit.com", includeSubdomains: false }
    ]
  });
  await firstSync;

  assert.equal(state.rules.length, 3);
  assert.equal(state.rules[0].action.type, "redirect");
  const youtube = state.rules.find(rule => rule.condition.regexFilter.includes("youtube"));
  const reddit = state.rules.find(rule => rule.condition.regexFilter.includes("reddit"));
  assert.match(youtube.condition.regexFilter, /\(\?:\[\^\/:\?#\]\+\\\.\)\?/);
  assert.doesNotMatch(reddit.condition.regexFilter, /\(\?:/);
  assert.equal(state.data.sessionEndTime, endTime);
  assert.equal(state.data.connection.connected, true);
});

test("installed native host synchronizes the session without loopback", async () => {
  const endTime = new Date(Date.now() + 60_000).toISOString();
  const { state, firstSync } = createHarness({
    active: true,
    endTime,
    websites: [{ domain: "wikipedia.org", includeSubdomains: true }]
  }, { nativeEnabled: true });
  await firstSync;

  assert.equal(state.rules.length, 2);
  assert.equal(state.data.connection.connected, true);
  assert.equal(state.data.connection.transport, "native");
  assert.equal(state.fetchCount, 0);
});

test("desktop disconnect keeps the last valid rules", async () => {
  const { state, context, firstSync } = createHarness({
    active: true,
    endTime: new Date(Date.now() + 60_000).toISOString(),
    websites: [{ domain: "github.com", includeSubdomains: true }]
  });
  await firstSync;
  const lastRules = structuredClone(state.rules);
  state.online = false;
  await vm.runInContext("requestSync()", context);

  assert.equal(JSON.stringify(state.rules), JSON.stringify(lastRules));
  assert.equal(state.data.connection.connected, false);
});

test("offline worker clears retained rules after the persisted session expires", async () => {
  const { state, context, firstSync } = createHarness({
    active: true,
    endTime: new Date(Date.now() + 60_000).toISOString(),
    websites: []
  });
  await firstSync;
  state.data.sessionEndTime = new Date(Date.now() - 1000).toISOString();
  state.online = false;
  await vm.runInContext("requestSync()", context);

  assert.equal(state.rules.length, 0);
  assert.equal("sessionEndTime" in state.data, false);
});

test("inactive session removes the extension's existing dynamic rules", async () => {
  const { state, context, firstSync } = createHarness({
    active: true,
    endTime: new Date(Date.now() + 60_000).toISOString(),
    websites: []
  });
  await firstSync;
  state.session = { active: false, endTime: null, websites: [] };
  await vm.runInContext("requestSync()", context);

  assert.equal(state.rules.length, 0);
  assert.equal("sessionEndTime" in state.data, false);
});
