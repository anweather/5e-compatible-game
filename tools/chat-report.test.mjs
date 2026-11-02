// node --test tools/chat-report.test.mjs. The log is real: chatbench's five records, then the
// recorder's output for each stream fixture (ChatRecordTests keeps those lines true).
import assert from "node:assert/strict";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import { kindOf, read, report, text } from "./chat-report.mjs";

const folder = fileURLToPath(new URL("../core/Game.Core.Tests/RulesMcp/chat-fixtures/log", import.meta.url));
const log = read(folder);
const r = report(log);

test("it counts prompts and outcomes", () => {
  assert.equal(r.prompts, 9);
  assert.equal(r.unreadableLines, 0);
  assert.deepEqual(r.outcomes, { applied: 6, refused: 2, failed: 1 });
});

test("each record has its kind", () => {
  const kinds = Object.fromEntries(log.records.slice(0, 5).map(x => [x.prompt, kindOf(x)]));
  assert.deepEqual(kinds, {
    "make all fire blue": "look",
    "make grass burn twice as fast": "rule",
    "add a cantrip that throws a small ball of frost": "rule",
    "build a small house near hollowmere": "building",
    "make the player twice as tall": "refused",
  });
});

test("prompt to world falls back to the room's answer, and says so", () => {
  assert.equal(r.world.measuredInTheGame, 0);
  assert.equal(r.world.upToTheRoomsAnswer, 6);
  assert.ok(r.world.medianMs > 0 && r.world.p90Ms >= r.world.medianMs);
});

test("the shares add up to the whole", () => {
  const { startingUp, model, tools, leftOver } = r.shares;
  assert.ok(Math.abs(startingUp + model + tools + leftOver - 1) < 1e-9);
});

test("a later line folds into its record", () => {
  assert.equal(r.reverted, 1);
  assert.equal(log.records.find(x => x.id === "fixture-0").afterwards.afterMs, 60000);
});

test("the dearest come first and what was refused is listed", () => {
  assert.equal(r.dearest[0].prompt, "add a cantrip that throws a small ball of frost");
  assert.ok(r.dearest.length <= 10);
  assert.ok(r.refused.some(x => x.prompt === "make the player twice as tall" && /Player size/.test(x.why)));
});

test("a retry is counted, and says what ran", () => {
  const records = [{ ...log.records[0], model: "the-default", retry: { from: "claude-haiku-5-5", why: "no", at: 900 } }];
  const once = report({ records, bad: 0 });
  assert.deepEqual(once.retried, [{ prompt: records[0].prompt, from: "claude-haiku-5-5", ran: "the-default" }]);
  assert.ok(text(once).includes("Retried on the account's model:** 1, from claude-haiku-5-5."));
  assert.deepEqual(r.retried, []);
  assert.ok(!text(r).includes("Retried"));
});

test("the text says it all", () => {
  const said = text(r);
  for (const s of ["9 prompts", "Prompt to world", "| building | 1 |", "Asked for, and refused", "make the player twice as tall"]) assert.ok(said.includes(s), s);
});
