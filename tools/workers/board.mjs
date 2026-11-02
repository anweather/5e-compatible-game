#!/usr/bin/env node
// board.mjs: small edits to Andrew's board page, so an update is one command plus a publish.
//   board.mjs who <chip> <text>                 replace a row in "Who's on what" (PC, Mac, u2, ...)
//   board.mjs landed <title> <text> [--video <blob> | --image <blob>]
//                                               add to the top of "Landed on dev", keeping the last 12
//   board.mjs sub <old> <new>                   replace exact text; must occur exactly once
//   board.mjs drop <fragment>                   remove the one <li> holding the fragment
// The page is .pm/board.html (git-ignored, kept between PM sessions). Publish it with the Artifact
// tool: file_path that page, url the board. If it's missing, Artifact read the board and save it there.
import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const page = join(dirname(fileURLToPath(import.meta.url)), "..", "..", ".pm", "board.html");
const keepLanded = 12;

function fail(message) {
    console.error(`STOP: ${message}`);
    process.exit(1);
}

function once(text, needle) {
    const first = text.indexOf(needle);
    if (first < 0) fail(`not on the board: ${needle}`);
    if (text.indexOf(needle, first + 1) >= 0) fail(`more than once on the board: ${needle}`);
    return first;
}

// The <ul> under a section's heading, as [start of its items, end of its items].
function list(text, heading) {
    const at = once(text, `<h2>${heading}</h2>`);
    const open = text.indexOf("<ul>", at);
    const close = text.indexOf("</ul>", open);
    if (open < 0 || close < 0) fail(`no list under ${heading}`);
    return [open + "<ul>".length, close];
}

// Top-level <li> blocks in the html, keeping nested ones whole.
function items(html) {
    const found = [];
    let depth = 0;
    let start = 0;
    for (const tag of html.matchAll(/<li\b|<\/li>/g)) {
        if (tag[0] === "</li>") {
            depth--;
            if (depth === 0) found.push(html.slice(start, tag.index + "</li>".length));
        } else {
            if (depth === 0) start = tag.index;
            depth++;
        }
    }
    return found;
}

function who(text, chip, row) {
    const [from, to] = list(text, "Who's on what");
    const mark = `<span class="chip run">${chip}</span>`;
    const hit = items(text.slice(from, to)).filter((li) => li.includes(mark));
    if (hit.length !== 1) fail(`${hit.length} rows for ${chip}`);
    const name = hit[0].match(/<b>.*?<\/b>/)?.[0] ?? "";
    return text.replace(hit[0], () => `<li>${mark}${name} ${row}</li>`);
}

function landed(text, title, body, flags) {
    const [from, to] = list(text, "Landed on dev");
    const video = flags.indexOf("--video");
    const image = flags.indexOf("--image");
    let media = "";
    if (video >= 0) {
        media = `\n      <video controls playsinline preload="metadata" src="/_blob/${flags[video + 1]}" style="display:block;max-width:100%;max-height:70vh;margin-top:8px"></video>`;
    } else if (image >= 0) {
        media = `\n      <img alt="" src="/_blob/${flags[image + 1]}" style="display:block;max-width:100%;margin-top:8px">`;
    }
    const added = `<li><b>${title}</b>\n      <span class="sub">${body}</span>${media}</li>`;
    const kept = [added, ...items(text.slice(from, to))].slice(0, keepLanded);
    return `${text.slice(0, from)}\n    ${kept.join("\n    ")}\n  ${text.slice(to)}`;
}

function drop(text, fragment) {
    const at = once(text, fragment);
    const start = text.lastIndexOf("<li", at);
    const end = text.indexOf("</li>", at);
    const block = text.slice(start, end);
    if (start < 0 || end < 0 || block.includes("<ul")) fail(`no simple <li> around: ${fragment}`);
    return text.slice(0, start) + text.slice(end + "</li>".length).replace(/^\s*\n/, "");
}

const [command, a, b, ...flags] = process.argv.slice(2);
if (!existsSync(page)) fail(`no ${page}: Artifact read the board and save it there`);
const text = readFileSync(page, "utf8");
const edits = {
    who: () => who(text, a, b),
    landed: () => landed(text, a, b, flags),
    sub: () => text.slice(0, once(text, a)) + b + text.slice(once(text, a) + a.length),
    drop: () => drop(text, a),
};
if (!edits[command] || a === undefined || (command !== "drop" && b === undefined)) {
    fail("usage: who <chip> <text> | landed <title> <text> [--video|--image <blob>] | sub <old> <new> | drop <fragment>");
}
writeFileSync(page, edits[command]());
console.log(`${command} done: publish .pm/board.html`);
