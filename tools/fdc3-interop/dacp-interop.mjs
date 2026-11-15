#!/usr/bin/env node
/**
 * AW-024 runtime interoperability harness (test-only DACP adapter).
 *
 * Drives the declared AetherWorkspace FDC3 subset through the FINOS Sail headless
 * Desktop Agent implementation (an independent DACP implementation, Apache-2.0) using
 * official FDC3 3.0 DACP wire messages. The harness simulates our bounded application
 * side (a shell raiser and the sample.watchlist target app) with the exact payloads and
 * intent declarations shipped in the repository fixtures, and checks that Sail:
 *   - delivers fdc3.instrument and fdc3.organization broadcasts with source/timestamp/traceId,
 *   - resolves the ViewInstrument intent to the app that declares it,
 *   - returns the intent result to the raising app,
 *   - routes context metadata supplied by the raiser (our traceId).
 *
 * This is interoperability evidence, NOT an FDC3 conformance claim.
 */
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import * as sail from '@finos/sail-headless-agent';
const {
  AbstractFDC3ServerInstance,
  BroadcastHandlerV3,
  IntentHandlerV3,
  OpenHandlerV3,
  State,
  ChannelType,
} = sail;

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(here, '..', '..');
const sailVersion = '3.0.0-beta.1'; // pinned in package.json; updated together
const reportPath = process.argv.includes('--report')
  ? process.argv[process.argv.indexOf('--report') + 1]
  : path.join(repoRoot, '.aether', 'dacp-interop-report.json');

const fixture = JSON.parse(readFileSync(path.join(repoRoot, 'tests', 'fixtures', 'fdc3', 'contexts.json'), 'utf8'));
const catalog = JSON.parse(readFileSync(path.join(repoRoot, 'samples', 'contexts', 'contexts.json'), 'utf8'));
const catalogContext = ref => {
  const found = catalog.contexts.find(c => c.contextRef === ref);
  if (!found) throw new Error(`catalog context not found: ${ref}`);
  return found.payload;
};

let uuidCounter = 0;
const uuid = () => `aether-interop-${++uuidCounter}`;
const now = () => new Date();
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const results = [];
const exchanges = [];
const fail = (name, detail) => {
  results.push({ name, ok: false, detail });
  throw new Error(`${name}: ${detail}`);
};
const pass = (name, detail) => results.push({ name, ok: true, detail });

/** In-memory app directory exposing our declared intents/context types to Sail. */
class InteropDirectory {
  constructor(apps) {
    this.apps = apps;
  }
  retrieveAllApps() { return this.apps; }
  retrieveAllIntents() {
    return this.apps.flatMap(a => (a.intents ?? []).map(intentName => ({
      intentName,
      appId: a.appId,
      displayName: intentName,
    })));
  }
  retrieveIntents(contextType, intentName) {
    return this.retrieveAllIntents().filter(i => {
      const app = this.apps.find(a => a.appId === i.appId);
      const matchesIntent = !intentName || i.intentName === intentName;
      const matchesContext = !contextType || (app.contextTypes ?? []).includes(contextType);
      return matchesIntent && matchesContext;
    });
  }
  retrieveApps(contextType, intentName) {
    const intents = this.retrieveIntents(contextType, intentName);
    return this.apps.filter(a => intents.some(i => i.appId === a.appId));
  }
  retrieveAppsById(appId) { return this.apps.filter(a => a.appId === appId); }
}

/** Minimal DACP server instance exposing in-memory mailboxes instead of real app processes. */
class InteropServer extends AbstractFDC3ServerInstance {
  constructor(handlers, channels, directory) {
    super(handlers, channels);
    this.directory = directory;
    this.instances = new Map();
    this.mailboxes = new Map();
    this.posted = [];
  }
  getDirectory() { return this.directory; }
  narrowIntents(_raiser, appIntents) { return Promise.resolve(appIntents); }
  getInstanceDetails(id) { return this.instances.get(id); }
  setInstanceDetails(id, details) { this.instances.set(id, { ...details, fdc3Version: details.fdc3Version ?? '3.0' }); }
  async open(appId) {
    const id = `open-${appId}-${++uuidCounter}`;
    this.setInstanceDetails(id, { appId, instanceId: id, state: State.Connected });
    return id;
  }
  async close(id) { this.instances.delete(id); }
  async setAppState(id, state) {
    const details = this.instances.get(id);
    if (details) details.state = state;
  }
  async getConnectedApps() { return [...this.instances.values()].filter(d => d.state === State.Connected); }
  async getAllApps() { return [...this.instances.values()]; }
  async isAppConnected(id) {
    const details = this.instances.get(id);
    return details?.state === State.Connected;
  }
  provider() { return 'aetherworkspace-dacp-interop-harness'; }
  providerVersion() { return '0.3.2'; }
  fdc3Version() { return '3.0'; }
  createUUID() { return uuid(); }
  async post(message, to) {
    if (process.env.AETHER_DACP_DEBUG) console.error('[post]', to, JSON.stringify(message).slice(0, 300));
    this.posted.push({ to, message });
    const box = this.mailboxes.get(to);
    if (box) box.push(message);
  }
  log() {}
}

function connect(server, appId) {
  const instanceId = `instance-${appId}-${++uuidCounter}`;
  server.setInstanceDetails(instanceId, { appId, instanceId, state: State.Connected });
  server.mailboxes.set(instanceId, []);
  return instanceId;
}
async function send(server, instanceId, message) {
  exchanges.push({ direction: 'app->agent', from: instanceId, type: message.type });
  if (process.env.AETHER_DACP_DEBUG) console.error('[send]', instanceId, message.type);
  await server.receive(message, instanceId);
  if (process.env.AETHER_DACP_DEBUG) console.error('[posted after send]', server.posted.length);
}
async function waitFor(server, instanceId, predicate, timeoutMs = 5000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const box = server.mailboxes.get(instanceId);
    const index = box.findIndex(predicate);
    if (index >= 0) return box.splice(index, 1)[0];
    await sleep(25);
  }
  throw new Error(`timeout waiting for message on ${instanceId}`);
}

const directory = new InteropDirectory([
  { appId: 'aether.shell', title: 'AetherWorkspace Shell', contextTypes: [], intents: [] },
  { appId: 'sample.watchlist', title: 'Synthetic Watchlist', contextTypes: ['fdc3.instrument', 'fdc3.organization'], intents: ['ViewInstrument'] },
  { appId: 'sample.risk', title: 'Synthetic Risk', contextTypes: ['fdc3.instrument', 'fdc3.organization'], intents: ['ViewAnalysis'] },
  { appId: 'sample.chart', title: 'Synthetic Chart', contextTypes: ['fdc3.instrument', 'fdc3.organization'], intents: ['ViewChart'] },
]);
const handlers = {
  '2.2': [new BroadcastHandlerV3(), new IntentHandlerV3(200), new OpenHandlerV3(2000)],
  '3.0': [new BroadcastHandlerV3(), new IntentHandlerV3(200), new OpenHandlerV3(2000)],
};
const server = new InteropServer(handlers, [{ id: 'default', type: ChannelType.app, context: [], displayMetadata: {} }], directory);

const shell = connect(server, 'aether.shell');
const watchlist = connect(server, 'sample.watchlist');

// The sample app registers context and intent listeners exactly as our declared subset does.
await send(server, watchlist, {
  type: 'addContextListenerRequest',
  payload: { channelId: 'default', contextType: null },
  meta: { requestUuid: uuid(), timestamp: now() },
});
await waitFor(server, watchlist, m => m.type === 'addContextListenerResponse');
await send(server, watchlist, {
  type: 'addIntentListenerRequest',
  payload: { intent: 'ViewInstrument', contextTypes: ['fdc3.instrument'] },
  meta: { requestUuid: uuid(), timestamp: now() },
});
const listenerResponse = await waitFor(server, watchlist, m => m.type === 'addIntentListenerResponse');
pass('intent-listener-registered', { listenerUUID: listenerResponse.payload.listenerUUID });

// 1) Broadcast fdc3.instrument with our trace metadata; Sail enriches source/timestamp.
const traceId = '0123456789abcdef0123456789abcdef';
const instrument = fixture.instrument;
const organizationContext = catalogContext('synthetic.organization.northstar');
await send(server, shell, {
  type: 'broadcastRequest',
  payload: { channelId: 'default', context: instrument, metadata: { traceId } },
  meta: { requestUuid: uuid(), timestamp: now() },
});
const broadcast = await waitFor(server, watchlist, m => m.type === 'broadcastEvent');
const bmeta = broadcast.payload.metadata ?? {};
if (broadcast.payload.context.type !== 'fdc3.instrument') fail('instrument-broadcast', 'wrong context type');
if (bmeta.source?.appId !== 'aether.shell') fail('instrument-broadcast', `wrong source ${JSON.stringify(bmeta.source)}`);
if (bmeta.traceId !== traceId) fail('instrument-broadcast', `wrong traceId ${bmeta.traceId}`);
if (!bmeta.timestamp) fail('instrument-broadcast', 'missing timestamp');
pass('instrument-broadcast-via-dacp', { channelId: broadcast.payload.channelId, source: bmeta.source, traceId: bmeta.traceId });

// 2) Broadcast the fdc3.organization context (our second declared context type).
await send(server, shell, {
  type: 'broadcastRequest',
  payload: { channelId: 'default', context: organizationContext, metadata: { traceId: uuid() } },
  meta: { requestUuid: uuid(), timestamp: now() },
});
const orgBroadcast = await waitFor(server, watchlist, m => m.type === 'broadcastEvent' && m.payload.context?.type === 'fdc3.organization');
pass('organization-broadcast-via-dacp', { source: orgBroadcast.payload.metadata?.source });

// 3) Raise our bounded ViewInstrument intent; Sail resolves it to sample.watchlist (declared intent).
const raiseRequestUuid = uuid();
await send(server, shell, {
  type: 'raiseIntentRequest',
  payload: { intent: 'ViewInstrument', context: instrument, metadata: { traceId } },
  meta: { requestUuid: raiseRequestUuid, timestamp: now() },
});
const intentEvent = await waitFor(server, watchlist, m => m.type === 'intentEvent');
if (intentEvent.payload.intent !== 'ViewInstrument') fail('intent-resolution', 'wrong intent');
if (intentEvent.payload.context.type !== 'fdc3.instrument') fail('intent-resolution', 'wrong context type');
if (intentEvent.payload.metadata?.traceId !== traceId) fail('intent-resolution', 'traceId not propagated');
pass('intent-resolved-to-declared-app', { intent: intentEvent.payload.intent, target: 'sample.watchlist' });

// 4) The target returns a result; Sail routes it back to the raising app.
await send(server, watchlist, {
  type: 'intentResultRequest',
  payload: { raiseIntentRequestUuid: raiseRequestUuid, intentResult: { type: 'fdc3.instrument', name: 'viewed' } },
  meta: { requestUuid: uuid(), timestamp: now() },
});
const result = await waitFor(server, shell, m => m.type === 'raiseIntentResultResponse');
if (result.payload.intentResult?.type !== 'fdc3.instrument') fail('intent-result', 'wrong result payload');
pass('intent-result-returned-to-raiser', { traceId: result.payload.resultMetadata?.traceId });

const report = {
  standard: 'FDC3 3.0 DACP (official JSON wire protocol)',
  independentImplementation: '@finos/sail-headless-agent',
  independentImplementationVersion: sailVersion,
  conformanceClaim: false,
  aetherworkspaceSubset: {
    contextTypes: ['fdc3.instrument', 'fdc3.organization'],
    intents: ['ViewInstrument'],
    channel: 'default',
  },
  results,
  exchanges,
  generatedAt: new Date().toISOString(),
};
mkdirSync(path.dirname(reportPath), { recursive: true });
writeFileSync(reportPath, JSON.stringify(report, null, 2) + '\n');
console.log(`DACP interop with ${report.independentImplementation}@${sailVersion}: ${results.length} checks passed`);
for (const r of results) console.log(`  [ok] ${r.name}`);
console.log(`report: ${path.relative(repoRoot, reportPath)} (${exchanges.length} DACP exchanges)`);
