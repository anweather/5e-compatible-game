// The scanner's tests (languages.mjs): for each language, which lines are comments, where the file's top comment ends, and that strings, docstrings, regexes and templates are read as they are.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { scan, languageOf } from './languages.mjs';

const lines = (set) => [...set].sort((a, b) => a - b);

test('a file is read as the language its extension names, among those switched on', () => {
  assert.equal(languageOf('src/Sea.cs'), 'csharp');
  assert.equal(languageOf('a/b.tsx'), 'js');
  assert.equal(languageOf('a/b.py'), 'python');
  assert.equal(languageOf('a/Fog.hlsl'), 'shader');
  assert.equal(languageOf('a/b.py', ['csharp']), null);
  assert.equal(languageOf('README.md'), null);
});

test('C#: line and block comments, the top comment ending at the first type, strings of every kind', () => {
  const text = [
    '// The file.',
    'using System;',
    '// Still above the first type.',
    'public class A',
    '{',
    '    string a = "// not a comment";',
    '    string b = @"a ""quoted"" // still a string";',
    '    string c = $"{1} /* no */";',
    '    string d = """',
    '        // raw, not a comment',
    '        """;',
    "    char e = '/';",
    '    int f = 1; // a comment',
    '    /* a block',
    '       of two lines */',
    '    /// <summary>a summary</summary>',
    '}',
  ].join('\n');
  const s = scan(text, 'csharp');
  assert.deepEqual(lines(s.comments), [1, 3, 13, 14, 15, 16]);
  assert.equal(s.headerEnds, 4);
  assert.match(s.blanked.split('\n')[5], /string a = " +";/);
});

test('C#: an interpolated string holding another string, its quotes and a URL inside the hole', () => {
  const text = [
    '// The file.',
    'class A',
    '{',
    '    string a = $"add {Url ?? $"http://127.0.0.1:{Port}/mcp"} now"; // a comment',
    '    string b = $@"{(x ? "a" : "b")} // still a string";',
    '    string c = $"{{not a hole}} {d:0.0} \\" {e}";',
    "    char f = '{';",
    '}',
  ].join('\n');
  const s = scan(text, 'csharp');
  assert.deepEqual(lines(s.comments), [1, 4]);
  assert.deepEqual(s.spans.map((p) => text.slice(p.from, p.to)), ['// The file.', '// a comment']);
});

test('shader: C-like comments, a shader name in quotes', () => {
  const s = scan('// The sea.\nShader "Game/Sea // x"\n{\n    float a; /* b */\n}\n', 'shader');
  assert.deepEqual(lines(s.comments), [1, 4]);
  assert.equal(s.headerEnds, 2);
});

test('JS and TS: a shebang, strings, templates with code inside, and regexes that hold slashes', () => {
  const text = [
    '#!/usr/bin/env node',
    '// The tool.',
    "import { a } from './a.mjs';",
    "const url = 'http://example.com';",
    'const t = `x ${ y ? "//" : `in ${z}` } // still template`;',
    'const r = /\\/\\/[/]/g; // a comment',
    'const half = a / 2 / 3;',
    'if (/^#/.test(s)) return; /* old */',
    'const o = { k: 1 }; // after an object',
  ].join('\n');
  const s = scan(text, 'js');
  assert.deepEqual(lines(s.comments), [2, 6, 8, 9]);
  assert.equal(s.headerEnds, 3);
});

test('Python: # comments, strings that hold a #, docstrings counted as comments, the module docstring a top comment', () => {
  const text = [
    '#!/usr/bin/env python3',
    '"""The tool."""',
    'import os',
    "s = '# not a comment'",
    'x = """',
    'a # string',
    '"""',
    'def f():',
    '    """A docstring."""',
    '    return 1  # a comment',
    'class A:',
    "    '''Two",
    "    lines.'''",
  ].join('\n');
  const s = scan(text, 'python');
  assert.deepEqual(lines(s.comments), [2, 9, 10, 12, 13]);
  assert.equal(s.headerEnds, 3);
});
