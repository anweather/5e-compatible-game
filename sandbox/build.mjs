// Bundles src/main.ts and inlines it into index.html -> dist/index.html,
// one self-contained file you can open locally or publish.
import * as esbuild from "esbuild";
import { mkdir, readFile, writeFile } from "node:fs/promises";

const watch = process.argv.includes("--watch");

const inline = {
  name: "inline-html",
  setup(build) {
    build.onEnd(async (result) => {
      if (result.errors.length) return;
      const js = result.outputFiles[0].text.replaceAll("</script", "<\\/script");
      const template = await readFile("index.html", "utf8");
      const fake = await readFile("dev/fake-claude.js", "utf8");
      await mkdir("dist", { recursive: true });
      await writeFile("dist/index.html", template.replace("<!--APP-->", () => `<script>\n${js}</script>`));
      // dev.html: the same page plus a fake platform, so two local tabs can share a room.
      await writeFile("dist/dev.html", `<meta charset="utf-8">\n` + template.replace("<!--APP-->", () => `<script>\n${fake}</script>\n<script>\n${js}</script>`));
      console.log(`dist/index.html + dist/dev.html (${(js.length / 1024).toFixed(0)} KB of script)`);
    });
  },
};

const options = {
  entryPoints: ["src/main.ts"],
  bundle: true,
  format: "iife",
  target: "es2020",
  minify: !watch,
  write: false,
  logLevel: "warning",
  plugins: [inline],
};

if (watch) {
  const ctx = await esbuild.context(options);
  await ctx.watch();
  console.log("watching src/ and ../rules/ ...");
} else {
  await esbuild.build(options);
}
