// Push the skill activations already sitting in your session transcripts into Loki.
//
// Live telemetry only starts counting the day you switch it on. The transcripts
// under ~/.claude/projects hold roughly 30 days of real usage already, so the
// dashboard can answer "is this worth building" tonight rather than next month.
//
//   node telemetry/backfill/backfill-transcripts.mjs            push
//   node telemetry/backfill/backfill-transcripts.mjs --dry-run  count only
//   node telemetry/backfill/backfill-transcripts.mjs --days 7   narrow the window
//
// Two honesty rules are baked in.
//
// A transcript records WHICH skill ran, never WHY. There is no way to tell a
// model-chosen activation from a typed slash command after the fact, so this
// emits no invocation_trigger at all rather than guessing one. The donut panel
// filters those rows out, so backfilled data never invents a trigger split.
//
// Every backfilled record carries origin="backfill" so it can be told apart
// from anything Claude Code sent live.

import { createReadStream } from "node:fs";
import { readdir, stat } from "node:fs/promises";
import { createInterface } from "node:readline";
import { homedir } from "node:os";
import { basename, join } from "node:path";

const ENDPOINT = process.env.LOKI_OTLP ?? "http://127.0.0.1:3100/otlp/v1/logs";
const ROOT = join(homedir(), ".claude", "projects");
const BATCH = 400;

const args = process.argv.slice(2);
const dryRun = args.includes("--dry-run");
const days = Number(args[args.indexOf("--days") + 1]) || 45;
const cutoff = Date.now() - days * 86400_000;

const str = (v) => ({ stringValue: String(v) });

function record(a) {
  const attrs = [
    ["event.name", "skill_activated"],
    ["skill.name", a.skill],
    ["origin", "backfill"],
    ["session.id", a.sessionId],
    ["repo", a.repo],
    ["git.branch", a.branch],
    ["app.version", a.version],
  ].filter(([, v]) => v != null && v !== "");

  return {
    timeUnixNano: String(a.at * 1e6),
    observedTimeUnixNano: String(a.at * 1e6),
    body: str("claude_code.skill_activated"),
    attributes: attrs.map(([key, value]) => ({ key, value: str(value) })),
  };
}

async function push(batch) {
  if (dryRun || batch.length === 0) return;
  const payload = {
    resourceLogs: [
      {
        resource: { attributes: [{ key: "service.name", value: str("claude-code") }] },
        scopeLogs: [
          { scope: { name: "com.anthropic.claude_code.events" }, logRecords: batch.map(record) },
        ],
      },
    ],
  };
  const res = await fetch(ENDPOINT, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(payload),
  });
  if (!res.ok) throw new Error(`Loki ${res.status}: ${(await res.text()).slice(0, 300)}`);
}

async function* transcripts(dir) {
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) yield* transcripts(path);
    else if (entry.name.endsWith(".jsonl")) yield path;
  }
}

const counts = new Map();
let files = 0;
let scanned = 0;
let found = 0;
let skipped = 0;
let batch = [];

for await (const path of transcripts(ROOT)) {
  const info = await stat(path);
  if (info.mtimeMs < cutoff) { skipped += 1; continue; }
  files += 1;

  const rl = createInterface({ input: createReadStream(path), crlfDelay: Infinity });
  for await (const line of rl) {
    if (!line.includes('"Skill"')) continue; // cheap pre-filter, the files are large
    scanned += 1;

    let rec;
    try { rec = JSON.parse(line); } catch { continue; }

    const content = rec?.message?.content;
    if (!Array.isArray(content)) continue;

    const at = Date.parse(rec.timestamp ?? "");
    if (!Number.isFinite(at) || at < cutoff) continue;

    for (const block of content) {
      if (block?.type !== "tool_use" || block?.name !== "Skill") continue;
      const skill = block?.input?.skill;
      if (!skill) continue;

      found += 1;
      counts.set(skill, (counts.get(skill) ?? 0) + 1);
      batch.push({
        skill,
        at,
        sessionId: rec.sessionId ?? rec.session_id,
        repo: rec.cwd ? basename(rec.cwd) : undefined,
        branch: rec.gitBranch,
        version: rec.version,
      });

      if (batch.length >= BATCH) { await push(batch); batch = []; }
    }
  }
}
await push(batch);

const ranked = [...counts].sort((a, b) => b[1] - a[1]);
console.log(`${files} transcripts read, ${skipped} older than ${days} days skipped`);
console.log(`${scanned} candidate lines, ${found} skill activations across ${counts.size} skills`);
console.log(dryRun ? "\n--dry-run, nothing pushed\n" : `\npushed to ${ENDPOINT}\n`);
for (const [name, n] of ranked.slice(0, 20)) console.log(`  ${String(n).padStart(5)}  ${name}`);
if (ranked.length > 20) console.log(`  ... and ${ranked.length - 20} more`);
