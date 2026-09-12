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
// A skill reaches the transcript by two different routes, and reading only one
// of them produces a badly wrong answer. Reading only Skill tool calls reported
// /implement as never used when a developer had typed it 100 times.
//
//   Skill tool call        an assistant tool_use named "Skill". The model chose
//                          it, or another skill called it. The trigger cannot be
//                          recovered, so none is emitted.
//   Typed slash command    a user message carrying <command-name>/foo</command-name>.
//                          Here the trigger IS known, so the record says
//                          invocation_trigger=user-slash.
//
// The two routes do not overlap: a typed command produces no Skill tool_use.
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

// Claude Code's own commands are not skills. Without this, /clear alone would
// add 199 phantom activations.
const BUILT_IN = new Set([
  "add-dir", "agents", "artifacts", "bug", "clear", "compact", "config", "context",
  "cost", "doctor", "exit", "export", "fast", "feedback", "help", "hooks", "ide",
  "init", "install-github-app", "login", "logout", "mcp", "memory", "model",
  "output-style", "permissions", "plugin", "pr-comments", "privacy-settings",
  "release-notes", "reload-skills", "resume", "review", "rewind", "schedule",
  "security-review", "skill-doctor", "skills", "status", "statusline", "tasks",
  "terminal-setup", "todos", "usage", "vim", "workflows",
]);

function record(a) {
  const attrs = [
    ["event.name", a.event ?? "skill_activated"],
    ["skill.name", a.skill],
    ["origin", "backfill"],
    ["invocation_trigger", a.trigger],
    ["session.id", a.sessionId],
    ["repo", a.repo],
    ["git.branch", a.branch],
    ["app.version", a.version],
    ["model", a.model],
    ["input_tokens", a.input],
    ["output_tokens", a.output],
    ["cache_read_tokens", a.cacheRead],
    ["cache_creation_tokens", a.cacheCreate],
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
  const body = JSON.stringify(payload);

  // A month of transcripts is tens of thousands of records, which outruns
  // Loki's default ingestion rate. 429 is expected, not exceptional.
  for (let attempt = 0; attempt < 8; attempt += 1) {
    const res = await fetch(ENDPOINT, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body,
    });
    if (res.ok) return;
    const text = (await res.text()).slice(0, 300);
    const throttled = res.status === 429 || text.includes("rate limit") || text.includes("Ingestion rate");
    if (!throttled) throw new Error(`Loki ${res.status}: ${text}`);
    await new Promise((r) => setTimeout(r, 500 * 2 ** attempt));
  }
  throw new Error("Loki kept throttling after 8 attempts");
}

async function* transcripts(dir) {
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) yield* transcripts(path);
    else if (entry.name.endsWith(".jsonl")) yield path;
  }
}

// Text of a user message, whichever shape it arrived in.
function textOf(content) {
  if (typeof content === "string") return content;
  if (Array.isArray(content)) return content.map((b) => b?.text ?? "").join(" ");
  return "";
}

const counts = new Map();
const byRoute = { tool: 0, typed: 0 };
let files = 0;
let found = 0;
let skipped = 0;
let ignoredBuiltIn = 0;
let requests = 0;
let tokens = 0;
let batch = [];

const note = (skill, route) => {
  found += 1;
  byRoute[route] += 1;
  const c = counts.get(skill) ?? { tool: 0, typed: 0 };
  c[route] += 1;
  counts.set(skill, c);
};

for await (const path of transcripts(ROOT)) {
  const info = await stat(path);
  if (info.mtimeMs < cutoff) { skipped += 1; continue; }
  files += 1;

  const rl = createInterface({ input: createReadStream(path), crlfDelay: Infinity });
  for await (const line of rl) {
    // Cheap pre-filter. These files run to hundreds of megabytes.
    const maybeTool = line.includes('"Skill"');
    const maybeTyped = line.includes("<command-name>");
    const maybeSpend = line.includes('"attributionSkill"');
    if (!maybeTool && !maybeTyped && !maybeSpend) continue;

    let rec;
    try { rec = JSON.parse(line); } catch { continue; }

    const at = Date.parse(rec.timestamp ?? "");
    if (!Number.isFinite(at) || at < cutoff) continue;

    const common = {
      at,
      sessionId: rec.sessionId ?? rec.session_id,
      repo: rec.cwd ? basename(rec.cwd) : undefined,
      branch: rec.gitBranch,
      version: rec.version,
    };

    // Route one: the model, or another skill, called the Skill tool.
    if (maybeTool && Array.isArray(rec?.message?.content)) {
      for (const block of rec.message.content) {
        if (block?.type !== "tool_use" || block?.name !== "Skill") continue;
        const skill = block?.input?.skill;
        if (!skill) continue;
        note(skill, "tool");
        batch.push({ skill, ...common });
        if (batch.length >= BATCH) { await push(batch); batch = []; }
      }
    }

    // Spend. attributionSkill names the skill that was active for this request,
    // and the usage block is the real token count for it. Note what that does
    // and does not mean: it attributes every token of a request to whichever
    // skill was live, which is how Claude Code's own cost metric works too. It
    // is not a claim that the skill caused those tokens.
    //
    // No cost figure is emitted. The transcript carries tokens, which are
    // measured, and no price. Inventing a rate would put a made-up number
    // beside measured ones.
    if (maybeSpend && rec.attributionSkill && rec?.message?.usage) {
      const u = rec.message.usage;
      requests += 1;
      tokens += (u.input_tokens ?? 0) + (u.output_tokens ?? 0) +
        (u.cache_read_input_tokens ?? 0) + (u.cache_creation_input_tokens ?? 0);
      batch.push({
        event: "api_request",
        skill: rec.attributionSkill,
        model: rec.message.model,
        input: u.input_tokens ?? 0,
        output: u.output_tokens ?? 0,
        cacheRead: u.cache_read_input_tokens ?? 0,
        cacheCreate: u.cache_creation_input_tokens ?? 0,
        ...common,
      });
      if (batch.length >= BATCH) { await push(batch); batch = []; }
    }

    // Route two: a developer typed it. The trigger is known here.
    if (maybeTyped) {
      for (const m of textOf(rec?.message?.content).matchAll(/<command-name>\/?([^<]+)<\/command-name>/g)) {
        const skill = m[1].trim();
        if (!skill) continue;
        if (BUILT_IN.has(skill)) { ignoredBuiltIn += 1; continue; }
        note(skill, "typed");
        batch.push({ skill, trigger: "user-slash", ...common });
        if (batch.length >= BATCH) { await push(batch); batch = []; }
      }
    }
  }
}
await push(batch);

const total = (c) => c.tool + c.typed;
const ranked = [...counts].sort((a, b) => total(b[1]) - total(a[1]));

console.log(`${files} transcripts read, ${skipped} older than ${days} days skipped`);
console.log(`${found} activations across ${counts.size} skills`);
console.log(`  ${byRoute.tool} Skill tool calls, trigger unknown`);
console.log(`  ${byRoute.typed} typed slash commands, trigger user-slash`);
console.log(`  ${ignoredBuiltIn} built-in commands ignored, /clear and friends are not skills`);
console.log(`${requests} attributed requests carrying ${tokens.toLocaleString()} tokens`);
console.log(dryRun ? "\n--dry-run, nothing pushed\n" : `\npushed to ${ENDPOINT}\n`);
console.log(`  ${"TOOL".padStart(6)} ${"TYPED".padStart(6)}  SKILL`);
for (const [name, c] of ranked.slice(0, 20)) {
  console.log(`  ${String(c.tool).padStart(6)} ${String(c.typed).padStart(6)}  ${name}`);
}
if (ranked.length > 20) console.log(`  ... and ${ranked.length - 20} more`);
