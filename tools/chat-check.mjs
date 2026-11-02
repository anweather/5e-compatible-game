#!/usr/bin/env node
// chat-check.mjs: is a chat record true? Reads a chatprompts run's folder and compares each record
// (chat-log-*.jsonl, made by ChatRecorder) with what it was made from, by its own reading and none of
// the recorder's code: the raw stream the probe tapped (raw/), and the server's own log (mcp-log-*.jsonl).
// The probe's watch (prompts.jsonl) says what the frames showed beside the stamp.
//   node tools/chat-check.mjs <run folder> [--json]
// Raw lines are <utc>\t<clock ms>\t<stream-json line>; prompts.jsonl gives the send's clock.
import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

const lines = file => readFileSync(file, "utf8").split("\n").filter(Boolean);
const logs = (dir, prefix) => readdirSync(dir).filter(f => f.startsWith(prefix) && f.endsWith(".jsonl")).sort().flatMap(f => lines(join(dir, f)).map(l => JSON.parse(l)));

export function readRaw(file) {
  return lines(file).map(l => {
    const a = l.indexOf("\t"), b = l.indexOf("\t", a + 1);
    let o = null;
    try {
      o = JSON.parse(l.slice(b + 1));
    } catch {
      // the stream's own non-JSON lines are nobody's record
    }
    return { utc: Date.parse(l.slice(0, a)), at: Number(l.slice(a + 1, b)), o };
  }).filter(r => r.o);
}

// Each reply once, by message.id; its last line has the fullest count, and each line of one reply has the same input side.
export function perMessage(rows) {
  const seen = new Map();
  for (const { o } of rows) if (o.type === "assistant" && o.message?.id && o.message.usage) seen.set(o.message.id, o.message.usage);
  const sum = k => [...seen.values()].reduce((a, u) => a + (u[k] ?? 0), 0);
  return {
    replies: seen.size,
    input: sum("input_tokens"),
    cacheWrite: sum("cache_creation_input_tokens"),
    cacheRead: sum("cache_read_input_tokens"),
    outputFloor: sum("output_tokens"),
    write1h: [...seen.values()].reduce((a, u) => a + (u.cache_creation?.ephemeral_1h_input_tokens ?? 0), 0),
    write5m: [...seen.values()].reduce((a, u) => a + (u.cache_creation?.ephemeral_5m_input_tokens ?? 0), 0),
    firstRead: [...seen.values()][0]?.cache_read_input_tokens ?? 0,
    firstWrite: [...seen.values()][0]?.cache_creation_input_tokens ?? 0,
  };
}

// Each tool call from line arrivals: tool_use's line to tool_result's line, in the order they were asked.
export function toolTimes(rows, sent) {
  const calls = [];
  const open = new Map();
  for (const { at, utc, o } of rows) {
    const content = o.message?.content;
    if (!Array.isArray(content)) continue;
    for (const p of content) {
      if (o.type === "assistant" && p.type === "tool_use") {
        const call = { id: p.id, name: p.name.replace(/^mcp__spell-valley__/, ""), game: p.name.startsWith("mcp__"), from: at - sent };
        calls.push(call);
        open.set(p.id, call);
      }
      if (o.type === "user" && p.type === "tool_result" && open.has(p.tool_use_id)) {
        const call = open.get(p.tool_use_id);
        call.to = at - sent;
        call.utc = utc;
        call.ms = at - sent - call.from;
        call.error = p.is_error === true;
      }
    }
  }
  return calls;
}

const union = calls => {
  let total = 0, reached = -Infinity;
  for (const c of [...calls].filter(c => c.to != null).sort((a, b) => a.from - b.from)) {
    const from = Math.max(c.from, reached);
    if (c.to > from) {
      total += c.to - from;
      reached = c.to;
    }
  }
  return total;
};

// The server's lines for this prompt's game calls, in order: its log says when each ended.
function joinServer(calls, server) {
  const left = [...server];
  return calls.filter(c => c.game).map(c => {
    const at = left.findIndex(s => s.tool === c.name);
    if (at < 0) return { ...c, server: null };
    const [s] = left.splice(at, 1);
    return { ...c, server: s, serverEndedToLine: c.utc - Date.parse(s.at) };
  });
}

function checkOne(p, record, allRows, mcp) {
  // A retried prompt's record is its last process's: the first try's lines are only its `retry`.
  const lastStart = allRows.findLastIndex(r => r.o.type === "system" && r.o.subtype === "init");
  const rows = record.retry && lastStart > 0 ? allRows.slice(lastStart) : allRows;
  const init = rows.find(r => r.o.type === "system" && r.o.subtype === "init")?.o;
  const result = rows.find(r => r.o.type === "result")?.o;
  const rate = rows.find(r => r.o.type === "rate_limit_event")?.o.rate_limit_info;
  const sent = p.sentClock;
  const u = result?.usage ?? {};
  const fromResult = { input: u.input_tokens, cacheWrite: u.cache_creation_input_tokens, cacheRead: u.cache_read_input_tokens, output: u.output_tokens };
  const dedup = perMessage(rows);
  const t = record.tokens;
  const models = Object.entries(result?.modelUsage ?? {});
  const calls = toolTimes(rows, sent);
  const first = rows[0]?.utc, last = rows[rows.length - 1]?.utc;
  const server = mcp.filter(m => {
    const at = Date.parse(m.at);
    return at >= first - 100 && at <= last + 100;
  });
  const joined = joinServer(calls, server);
  const inRecord = record.tools ?? [];
  const toolDiffs = calls.map((c, i) => ({ name: c.name, stream: c.ms, record: inRecord[i]?.ms, same: inRecord[i]?.name === c.name && inRecord[i]?.ms === c.ms }));
  const gameMs = joined.filter(c => c.server);
  const sumStream = gameMs.reduce((a, c) => a + c.ms, 0), sumServer = gameMs.reduce((a, c) => a + c.server.ms, 0), sumWait = gameMs.reduce((a, c) => a + c.server.waitMs, 0);
  const split = record.split;
  const firstAssistant = rows.find(r => r.o.type === "assistant");
  const resultAt = rows.find(r => r.o.type === "result");
  return {
    name: p.name,
    prompt: p.prompt,
    id: record.id,
    outcome: record.outcome,
    expected: p.expected,
    model: { record: record.model, init: init?.model, inResult: models.map(([m]) => m), claude: init?.claude_code_version, recordClaude: record.claude, mode: init?.permissionMode, apiKeySource: init?.apiKeySource, costBasis: models.map(([, v]) => v.costBasis) },
    weekly: rate?.unifiedWindows?.seven_day?.utilization ?? null,
    fiveHour: rate?.unifiedWindows?.five_hour?.utilization ?? null,
    tokens: {
      record: { input: t.input, cacheWrite: t.cacheWrite, cacheRead: t.cacheRead, output: t.output },
      result: fromResult,
      perMessage: dedup,
      recordIsResult: ["input", "cacheWrite", "cacheRead", "output"].every(k => t[k] === fromResult[k]),
      inputSideIsPerMessage: t.input === dedup.input && t.cacheWrite === dedup.cacheWrite && t.cacheRead === dedup.cacheRead,
      turns: { record: t.turns, result: result?.num_turns, replies: dedup.replies },
      modelUsageIsResult: models.length === 1 && models[0][1].inputTokens === u.input_tokens && models[0][1].cacheReadInputTokens === u.cache_read_input_tokens && models[0][1].outputTokens === u.output_tokens && models[0][1].cacheCreationInputTokens === u.cache_creation_input_tokens,
      thinking: u.output_tokens_details?.thinking_tokens ?? null,
    },
    cost: {
      record: t.cost,
      total: result?.total_cost_usd,
      modelUsage: models.reduce((a, [, v]) => a + v.costUSD, 0),
      same: t.cost === result?.total_cost_usd,
    },
    tools: {
      calls: calls.length,
      unanswered: calls.filter(c => c.to == null).length,
      sameAsStream: toolDiffs.filter(d => d.same).length,
      different: toolDiffs.filter(d => !d.same),
      serverLines: server.length,
      gameCalls: calls.filter(c => c.game).length,
      sumStreamMs: sumStream,
      sumServerMs: sumServer,
      sumWaitMs: sumWait,
      clientMs: sumStream - sumServer,
      worstStream: Math.max(0, ...calls.map(c => c.ms ?? 0)),
      overlapUnion: union(calls),
      recordTools: split.tools,
      refused: inRecord.filter(c => c.ok === false).map(c => `${c.name}: ${(c.reason ?? "").slice(0, 60)}`),
      used: inRecord.map(c => c.name),
    },
    split: {
      ...split,
      adds: split.startingUp + split.model + split.tools,
      claudeSaysTotal: result?.duration_ms,
      claudeSaysApi: result?.duration_api_ms,
      startToResult: resultAt ? resultAt.at - sent : null,
      firstReplyAt: firstAssistant ? firstAssistant.at - sent : null,
    },
    notes: {
      read: inRecord.filter(c => c.name === "Read").map(c => c.ok),
      write: inRecord.filter(c => c.name === "Write" || c.name === "Edit").map(c => `${c.name}:${c.ok}`),
      denials: result?.permission_denials ?? [],
      bytes: p.notesBytes,
    },
    stamp: { at: record.at, watch: p.watch, gameCost: record.gameCost, notEarly: p.watch.inWorld == null || ((p.watch.rulesChanged ?? 0) <= p.watch.inWorld && (p.watch.rowsChanged ?? 0) <= p.watch.inWorld && (p.watch.lastRecolouring ?? 0) <= p.watch.inWorld) },
  };
}

export function check(dir) {
  const records = new Map(logs(dir, "chat-log-").filter(o => o.id).map(o => [o.id, o]));
  const mcp = logs(dir, "mcp-log-");
  return lines(join(dir, "prompts.jsonl")).map(l => JSON.parse(l)).map(p => checkOne(p, records.get(p.record), readRaw(join(dir, "raw", p.name + ".jsonl")), mcp));
}

function print(all) {
  for (const c of all) {
    console.log(`\n${c.name} (${c.outcome}, wanted ${c.expected}), ${c.model.init}, weekly ${c.weekly}`);
    console.log(`  tokens record=result ${c.tokens.recordIsResult}, input side=per message ${c.tokens.inputSideIsPerMessage}, modelUsage ${c.tokens.modelUsageIsResult}; output record ${c.tokens.record.output} vs floor ${c.tokens.perMessage.outputFloor}; thinking ${c.tokens.thinking}; turns ${c.tokens.turns.record}/${c.tokens.turns.result}, replies ${c.tokens.turns.replies}`);
    console.log(`  first reply: write ${c.tokens.perMessage.firstWrite}, read ${c.tokens.perMessage.firstRead}; all writes 1h ${c.tokens.perMessage.write1h}, 5m ${c.tokens.perMessage.write5m}`);
    console.log(`  cost record ${c.cost.record} = total ${c.cost.same}, modelUsage ${c.cost.modelUsage}, basis ${c.model.costBasis}`);
    console.log(`  tools ${c.tools.sameAsStream}/${c.tools.calls} same as lines, unanswered ${c.tools.unanswered}; server lines ${c.tools.serverLines} for ${c.tools.gameCalls} game calls; ms stream ${c.tools.sumStreamMs} = server ${c.tools.sumServerMs} (wait ${c.tools.sumWaitMs}) + client ${c.tools.clientMs}; union ${c.tools.overlapUnion}, record ${c.tools.recordTools}`);
    console.log(`  split up ${c.split.startingUp} + model ${c.split.model} + tools ${c.split.tools} = ${c.split.adds}, total ${c.split.total}, left ${c.split.leftOver}; claude says ${c.split.claudeSaysTotal}/${c.split.claudeSaysApi}; first reply at ${c.split.firstReplyAt}`);
    console.log(`  notes read ${c.notes.read}, write ${c.notes.write}, denials ${c.notes.denials.length}, ${c.notes.bytes} bytes; refused: ${c.tools.refused.join(" | ")}`);
    console.log(`  stamp ${c.stamp.at.inWorld} (applied ${c.stamp.at.applied}, done ${c.stamp.at.done}), not early ${c.stamp.notEarly}; watch ${JSON.stringify(c.stamp.watch)}`);
  }
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  const all = check(process.argv[2]);
  if (process.argv.includes("--json")) console.log(JSON.stringify(all, null, 1));
  else print(all);
}
