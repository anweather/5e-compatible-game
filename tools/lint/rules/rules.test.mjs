// The rules' tests (rules/): comments new against old, paths there and not, the user's own data named, and the module map's reaches and ratchet in each language.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { commentFindings } from './comments.mjs';
import { pathFindings, namedPaths } from './paths.mjs';
import { ownDataFindings } from './own-data.mjs';
import { readMap, moduleOf, references, reachFindings, knownText, cycles } from './modules.mjs';

test('comments: one on a new or changed line is an error, one already there a warning, the top one neither', () => {
  const text = '// The file.\npublic class A\n{\n    int a; // old\n    int b; // new\n}\n';
  const r = commentFindings({ path: 'A.cs', text, language: 'csharp', changed: new Set([5]) });
  assert.deepEqual(r.errors, ['A.cs:5: comment: int b; // new']);
  assert.deepEqual(r.warnings, ['A.cs:4: comment: int a; // old']);
  assert.equal(commentFindings({ path: 'A.cs', text, language: 'csharp', changed: 'all' }).errors.length, 2);
});

test('comments: with level warning, one on a new or changed line is a warning too', () => {
  const text = 'using System;\nnamespace N {\nclass A {\nint a; // old\nint b; // new\n}\n}\n';
  const r = commentFindings({ path: 'A.cs', text, language: 'csharp', changed: 'all', level: 'warning' });
  assert.deepEqual(r.errors, []);
  assert.deepEqual(r.warnings, ['A.cs:4: comment: int a; // old', 'A.cs:5: comment: int b; // new']);
});

test('comments: Python and JS by the same rule', () => {
  const py = '"""The tool."""\nimport os\n\ndef f():\n    """Says f."""\n    return 1\n';
  assert.deepEqual(commentFindings({ path: 't.py', text: py, language: 'python', changed: 'all' }).errors, ['t.py:5: comment: """Says f."""']);
  const js = '#!/usr/bin/env node\n// The tool.\nconst a = 1;\n/** a summary */\nfunction f() {}\n';
  assert.deepEqual(commentFindings({ path: 't.mjs', text: js, language: 'js', changed: 'all' }).errors, ['t.mjs:4: comment: /** a summary */']);
});

test('paths: a spoiled path fails; a glob, a placeholder, a made folder and the history do not', () => {
  const text = 'run tools/work/lint.mjs, then tools/work/lnt.mjs; tools/views/*/x.txt, tools/<name>.mjs, tools/out/world.json; docs/adr/0001-a.md.';
  const there = new Set(['tools/work/lint.mjs', 'docs/adr/0001-a.md']);
  const r = pathFindings({ files: ['CLAUDE.md', 'docs/log/old.md'], read: () => text, exists: (p) => there.has(p), config: { made: ['tools/out'], history: ['^docs/log/'] } });
  assert.deepEqual(r.errors, ['CLAUDE.md:1: path: tools/work/lnt.mjs is named and is not there']);
  assert.deepEqual(namedPaths('see tools/check/tour.txt. And ../tools/make/net.mjs.', ['tools/']).map((p) => p.path), ['tools/check/tour.txt', 'tools/make/net.mjs']);
});

test('paths: a path named from a folder above the file is there too', () => {
  const there = new Set(['unity/docs/editor.md']);
  const r = pathFindings({ files: ['unity/CLAUDE.md', 'CLAUDE.md'], read: () => 'docs/editor.md', exists: (p) => there.has(p), config: {} });
  assert.deepEqual(r.errors, ['CLAUDE.md:1: path: docs/editor.md is named and is not there']);
});

test('paths and own data: a test naming a missing path or the user folder on purpose is not read, unless skipTests is off', () => {
  const read = () => 'tools/gone.mjs persistentDataPath';
  assert.equal(pathFindings({ files: ['tools/a.test.mjs', 'tools/b_test.py', 'src/Tests/ATests.cs'], read, exists: () => false, config: {} }).errors.length, 0);
  assert.equal(pathFindings({ files: ['tools/a.test.mjs'], read, exists: () => false, config: { skipTests: false } }).errors.length, 1);
  assert.equal(ownDataFindings({ files: ['tools/a.test.mjs'], read, config: { patterns: ['persistentDataPath'] } }).errors.length, 0);
  assert.equal(ownDataFindings({ files: ['tools/a.mjs'], read, config: { patterns: ['persistentDataPath'] } }).errors.length, 1);
});

test('paths: with strict off, a missing path on an old line is a warning', () => {
  const r = pathFindings({ files: ['a.md'], read: () => 'tools/a.mjs\ntools/b.mjs', exists: () => false, config: { strict: false }, changedOf: () => new Set([2]) });
  assert.deepEqual([r.errors.length, r.warnings.length], [1, 1]);
});

test('own data: a tool that names the user folder fails, unless it is the one allowed', () => {
  const config = { in: ['tools/'], patterns: ['persistentDataPath', 'Application Support'], allowed: { 'tools/lib/machine.mjs': 'names it to refuse it' } };
  const read = (f) => (f.endsWith('.md') ? 'Application Support' : 'const saves = path.join(home, "Library/Application Support/Game");');
  const r = ownDataFindings({ files: ['tools/a.mjs', 'tools/lib/machine.mjs', 'tools/README.md', 'src/game.cs'], read, config });
  assert.equal(r.errors.length, 1);
  assert.match(r.errors[0], /^tools\/a\.mjs:1: own data/);
});

const mapText = `module base uses
module rules uses base
module app uses base rules
module tests uses *
base/ base
rules/ rules
app/ app
app/*Test.cs tests
web/ app
py/ rules
py/app/ app
shaders/ base`;

const files = () => new Map([
  ['src/base/Clock.cs', 'public static class Clock { public static float Now; }'],
  ['src/rules/Score.cs', 'public class Score { float t = Clock.Now; }'],
  ['src/app/Game.cs', 'public class Game { Score s = new Score(); }'],
  ['src/app/GameTest.cs', 'public class GameTest { Game g; }'],
  ['src/web/view.ts', "import { score } from '../rules/score';\n// import { x } from '../app/x';\n"],
  ['src/rules/score.ts', "import { game } from '../web/view';\nexport const score = 1;\n"],
  ['src/py/rules.py', 'from .app import main\nimport os\n'],
  ['src/py/app/__init__.py', ''],
  ['src/py/app/main.py', 'from .. import rules\n'],
  ['src/shaders/Sea.hlsl', '#include "Common.hlsl"\n'],
  ['src/shaders/Common.hlsl', 'float a;\n'],
]);

test('modules: a file is in the module of its longest row, a * for any run of a name', () => {
  const map = readMap(mapText);
  assert.equal(moduleOf('src/app/GameTest.cs', map, 'src/'), 'tests');
  assert.equal(moduleOf('src/app/Game.cs', map, 'src/'), 'app');
  assert.equal(moduleOf('src/other/x.cs', map, 'src/'), '');
  assert.equal(moduleOf('lib/x.cs', map, 'src/'), null);
  assert.throws(() => readMap('module a uses b'));
});

test('modules: references in C#, TS, Python and shaders, not one in a comment', () => {
  const { refs } = references(files(), readMap(mapText), { root: 'src/' });
  const seen = refs.map((r) => `${r.path.split('/').pop()} ${r.kind}:${r.target} ${r.from}>${r.to}`).sort();
  assert.deepEqual(seen, [
    'Game.cs type:Score app>rules',
    'GameTest.cs type:Game tests>app',
    'Score.cs type:Clock rules>base',
    'main.py import:..rules app>rules',
    'rules.py import:.app rules>app',
    'score.ts import:view.ts rules>app',
    'view.ts import:score.ts app>rules',
  ].sort());
});

test('modules: a new reach fails, a known one counts as old, one taken out is reported gone', () => {
  const f = files();
  const first = reachFindings({ files: f, mapText, root: 'src/' });
  assert.equal(first.errors.length, 2);
  const known = knownText(first.now);
  assert.match(known, /^py\/rules\.py import:\.app  1  rules>app\nrules\/score\.ts import:view\.ts  1  rules>app\n$/);
  const listed = reachFindings({ files: f, mapText, knownText: known, root: 'src/' });
  assert.deepEqual([listed.errors.length, listed.old], [0, 2]);
  f.set('src/rules/score.ts', 'export const score = 1;\n');
  const fewer = reachFindings({ files: f, mapText, knownText: known, root: 'src/' });
  assert.deepEqual([fewer.errors.length, fewer.old, fewer.gone], [0, 1, ['rules/score.ts import:view.ts']]);
  f.set('src/base/Clock.cs', 'public static class Clock { public static float Now; Game g; }');
  assert.match(reachFindings({ files: f, mapText, knownText: known, root: 'src/' }).errors[0], /src\/base\/Clock\.cs:1: module: base reaches app \(type Game\)/);
});

test('modules: a source file in no module fails when every file must be mapped', () => {
  const f = files();
  f.set('src/other/Loose.cs', 'public class Loose { }');
  const r = reachFindings({ files: f, mapText, root: 'src/', knownText: knownText(reachFindings({ files: files(), mapText, root: 'src/' }).now) });
  assert.deepEqual(r.errors, ['src/other/Loose.cs:1: module: in no module of modules.txt; give it a row']);
  assert.equal(reachFindings({ files: f, mapText, root: 'src/', everyFileMapped: false, knownText: knownText(r.now) }).errors.length, 0);
});

test('modules: the cycles are the knots of modules that reach each other', () => {
  const edges = new Map([['a', new Set(['b'])], ['b', new Set(['c'])], ['c', new Set(['a'])], ['d', new Set(['a'])]]);
  assert.deepEqual(cycles(edges, ['a', 'b', 'c', 'd']), [['a', 'b', 'c']]);
});
