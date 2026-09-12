// One row per skill, for whoever maintains the catalogue.
//
// A dashboard of totals answers "is anyone using skills". This answers the
// question a maintainer actually has, which is "what do I do about skill number
// seven". It joins the skills on disk against what Loki saw, so a skill that
// never fired gets a row too. Those rows are the whole point, and no query
// against telemetry alone can produce them.
//
//   node telemetry/report/skill-report.mjs
//   node telemetry/report/skill-report.mjs --days 7
//   node telemetry/report/skill-report.mjs --sort uses
//   node telemetry/report/skill-report.mjs --json > report.json
//
// Loki caps a query at 30 days, so --days is clamped to 29.

import { readdir, readFile } from "node:fs/promises";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const REPO = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");
const LOKI = process.env.LOKI_URL ?? "http://127.0.0.1:3100";

const args = process.argv.slice(2);
const flag = (name, fallback) => {
  const i = args.indexOf(`--${name}`);
  return i === -1 ? fallback : args[i + 1];
};
const days = Math.min(Number(flag("days", 29)) || 29, 29);
const asJson = args.includes("--json");
const sortBy = flag("sort", "decision");

// ---------------------------------------------------------------- the catalogue

// Enough of a YAML reader for a skill's frontmatter, which is flat key: value
// with occasional wrapping. A real parser would be a dependency for no gain.
function frontmatter(text) {
  const m = text.match(/^---\r?\n([\s\S]*?)\r?\n---/);
  if (!m) return {};
  const out = {};
  let key = null;
  for (const line of m[1].split(/\r?\n/)) {
    const kv = line.match(/^([A-Za-z0-9_-]+):\s*(.*)$/);
    if (kv) {
      key = kv[1];
      out[key] = kv[2].trim();
    } else if (key && line.trim()) {
      out[key] += " " + line.trim();
    }
  }
  return out;
}

async function skillsUnder(dir, origin, prefix) {
  const found = [];
  let entries;
  try {
    entries = await readdir(dir, { withFileTypes: true });
  } catch {
    return found;
  }
  for (const e of entries) {
    if (!e.isDirectory()) continue;
    const path = join(dir, e.name, "SKILL.md");
    let text;
    try {
      text = await readFile(path, "utf8");
    } catch {
      continue;
    }
    const fm = frontmatter(text);
    const name = fm.name ?? e.name;
    const description = fm.description ?? "";
    found.push({
      key: prefix ? `${prefix}:${name}` : name,
      name,
      origin,
      description,
      // What the skill costs every turn: its name and description sit in the
      // listing whether or not it ever runs. Four characters per token is the
      // usual rough conversion.
      listingTokens: Math.round((name.length + description.length) / 4),
      modelInvocable: fm["disable-model-invocation"] !== "true",
    });
  }
  return found;
}

async function catalogue() {
  const all = [...(await skillsUnder(join(REPO, ".claude", "skills"), "dev", null))];
  let plugins = [];
  try {
    plugins = await readdir(join(REPO, "plugins"), { withFileTypes: true });
  } catch {}
  for (const p of plugins) {
    if (!p.isDirectory()) continue;
    // A plugin skill reports as <plugin>:<skill> in telemetry, so key it that way.
    all.push(...(await skillsUnder(join(REPO, "plugins", p.name, "skills"), "catalogue", p.name)));
  }
  return all;
}

// ---------------------------------------------------------------------- the data

async function activations() {
  const end = Date.now();
  const start = end - days * 86400_000;
  const rows = [];
  let cursor = end;

  // Loki pages backwards from `end`. Keep asking until a page comes back short.
  for (let page = 0; page < 40; page += 1) {
    const u = new URL(`${LOKI}/loki/api/v1/query_range`);
    u.searchParams.set("query", '{service_name="claude-code"} | event_name="skill_activated"');
    u.searchParams.set("limit", "5000");
    u.searchParams.set("direction", "backward");
    u.searchParams.set("start", String(start * 1e6));
    u.searchParams.set("end", String(cursor * 1e6));

    const res = await fetch(u);
    if (!res.ok) throw new Error(`Loki ${res.status}: ${(await res.text()).slice(0, 200)}`);
    const body = await res.json();

    let count = 0;
    let oldest = cursor;
    for (const stream of body.data.result ?? []) {
      for (const [ns] of stream.values) {
        const at = Number(ns) / 1e6;
        rows.push({ at, ...stream.stream });
        oldest = Math.min(oldest, at);
        count += 1;
      }
    }
    if (count < 5000) break;
    cursor = oldest - 1;
  }
  return rows;
}

// Spend is 36,000 records where activations are 1,000, so it is aggregated in
// Loki rather than pulled down. One query per token kind.
async function spend() {
  const kinds = ["output_tokens", "input_tokens", "cache_read_tokens", "cache_creation_tokens"];
  const totals = new Map();

  for (const kind of kinds) {
    const u = new URL(`${LOKI}/loki/api/v1/query`);
    u.searchParams.set(
      "query",
      `sum by (skill_name) (sum_over_time({service_name="claude-code"} | event_name="api_request" | unwrap ${kind} [${days}d]))`,
    );
    const res = await fetch(u);
    if (!res.ok) continue;
    const body = await res.json();
    for (const row of body.data?.result ?? []) {
      const name = row.metric.skill_name;
      if (!name) continue;
      const n = Number(row.value[1]) || 0;
      const t = totals.get(name) ?? { output: 0, total: 0 };
      if (kind === "output_tokens") t.output += n;
      t.total += n;
      totals.set(name, t);
    }
  }
  return totals;
}

// -------------------------------------------------------------------- the report

const [disk, rows, tokens] = await Promise.all([catalogue(), activations(), spend()]);

const seen = new Map();
for (const r of rows) {
  const key = r.skill_name;
  if (!key) continue;
  let s = seen.get(key);
  if (!s) {
    s = { uses: 0, tool: 0, typed: 0, proactive: 0, sessions: new Set(), repos: new Set(), sources: new Set(), last: 0 };
    seen.set(key, s);
  }
  s.uses += 1;
  s.last = Math.max(s.last, r.at);
  if (r.session_id) s.sessions.add(r.session_id);
  if (r.repo) s.repos.add(r.repo);
  if (r.skill_source) s.sources.add(r.skill_source);

  // user-slash is the only trigger a transcript can establish. Everything else
  // reached the skill through the Skill tool, which the model or another skill
  // called. Live telemetry narrows that further with claude-proactive.
  if (r.invocation_trigger === "user-slash") s.typed += 1;
  else s.tool += 1;
  if (r.invocation_trigger === "claude-proactive") s.proactive += 1;
}

const ago = (ms) => {
  if (!ms) return "never";
  const d = Math.floor((Date.now() - ms) / 86400_000);
  if (d === 0) return "today";
  if (d === 1) return "yesterday";
  return `${d}d ago`;
};

// A note is a prompt to look, never an instruction to delete. Zero activations
// can mean a dead skill or a month with no merge conflicts in it, and this data
// cannot tell those apart.
function verdict(skill, s) {
  if (!s || s.uses === 0) return "no activation in window";
  if (skill.modelInvocable && s.typed > 0 && s.tool === 0) {
    return "only ever typed, so the description is not selecting it";
  }
  if (!skill.modelInvocable && s.tool > 0) {
    // disable-model-invocation keeps a skill out of the listing. Whether it also
    // refuses a Skill tool call naming it directly is a separate question, and
    // this row is the evidence that it may not. Another skill's prose naming the
    // entry point is the likely route.
    return `reached the Skill tool ${s.tool}x despite disable-model-invocation, worth checking`;
  }
  return "";
}

const report = disk.map((skill) => {
  const s = seen.get(skill.key);
  const t = tokens.get(skill.key);
  return {
    outputTokens: t?.output ?? 0,
    totalTokens: t?.total ?? 0,
    skill: skill.key,
    origin: skill.origin,
    kind: skill.modelInvocable ? "engine" : "entry point",
    uses: s?.uses ?? 0,
    tool: s?.tool ?? 0,
    typed: s?.typed ?? 0,
    proactive: s?.proactive ?? 0,
    sessions: s?.sessions.size ?? 0,
    repos: s?.repos.size ?? 0,
    lastUsed: s?.last ?? 0,
    listingTokens: skill.listingTokens,
    // Listing cost is paid every turn. Usage is what it bought.
    tokensPerUse: s?.uses ? Math.round(skill.listingTokens / s.uses) : null,
    verdict: verdict(skill, s),
  };
});

const offDisk = [...seen.entries()]
  .filter(([key]) => !disk.some((d) => d.key === key))
  .map(([key, s]) => ({ skill: key, uses: s.uses, lastUsed: s.last }))
  .sort((a, b) => b.uses - a.uses);

const sorters = {
  uses: (a, b) => b.uses - a.uses,
  name: (a, b) => a.skill.localeCompare(b.skill),
  cost: (a, b) => b.listingTokens - a.listingTokens,
  // Decisions first: never fired, then never self-selected, then by usage.
  decision: (a, b) =>
    (a.uses === 0 ? 0 : 1) - (b.uses === 0 ? 0 : 1) ||
    (a.verdict ? 0 : 1) - (b.verdict ? 0 : 1) ||
    a.uses - b.uses,
};
report.sort(sorters[sortBy] ?? sorters.decision);

if (asJson) {
  console.log(JSON.stringify({ days, generatedAt: new Date().toISOString(), report, offDisk }, null, 2));
  process.exit(0);
}

const pad = (v, w, right = false) =>
  right ? String(v).padStart(w) : String(v).padEnd(w);
const nameWidth = Math.max(24, ...report.map((r) => r.skill.length));

const short = (n) => {
  if (!n) return "-";
  if (n >= 1e9) return `${(n / 1e9).toFixed(1)}G`;
  if (n >= 1e6) return `${(n / 1e6).toFixed(1)}M`;
  if (n >= 1e3) return `${Math.round(n / 1e3)}k`;
  return String(n);
};

console.log(`\nSkills on disk, against ${rows.length} activations in the last ${days} days\n`);
console.log(
  [pad("SKILL", nameWidth), pad("KIND", 11), pad("USES", 5, true), pad("TOOL", 5, true), pad("TYPED", 5, true), pad("SESS", 5, true), pad("OUT", 6, true), pad("TOTAL", 6, true), pad("LAST", 9), pad("CTX", 4, true), "NOTE"].join("  "),
);
console.log("-".repeat(nameWidth + 78));

for (const r of report) {
  console.log(
    [
      pad(r.skill, nameWidth),
      pad(r.kind, 11),
      pad(r.uses, 5, true),
      pad(r.tool, 5, true),
      pad(r.typed, 5, true),
      pad(r.sessions, 5, true),
      pad(short(r.outputTokens), 6, true),
      pad(short(r.totalTokens), 6, true),
      pad(ago(r.lastUsed), 9),
      pad(r.listingTokens, 4, true),
      r.verdict,
    ].join("  "),
  );
}

const dead = report.filter((r) => r.uses === 0);
const deadCost = dead.reduce((n, r) => n + r.listingTokens, 0);
const totalCost = report.reduce((n, r) => n + r.listingTokens, 0);
const engines = report.filter((r) => r.kind === "engine");

const grandOutput = report.reduce((n, r) => n + r.outputTokens, 0);
const grandTotal = report.reduce((n, r) => n + r.totalTokens, 0);

console.log(`
TOOL   the model called it, or another skill did. A transcript cannot say which.
TYPED  a developer typed the slash command. The only trigger a transcript proves.
OUT    output tokens generated while this skill was attributed. The expensive kind.
TOTAL  every token of those requests, cache reads included. Cache reads dominate
       the figure and bill at a fraction, so read TOTAL as volume, not as spend.
CTX    approximate tokens this skill's listing costs every turn, used or not.

OUT and TOTAL attribute a whole request to whichever skill was active, which is
how Claude Code's own cost metric works. It is not a claim the skill caused them.
No money figure is shown: the transcripts carry token counts and no prices.

A zero row is a prompt to look, not a delete order. A merge conflict skill with
no activations may only mean a quiet month.`);

console.log(`
${report.length} skills on disk, ${report.length - dead.length} fired, ${dead.length} did not.
${engines.length} are engines and spend listing context; the rest are typed entry points.
Listing cost about ${totalCost} tokens per turn, of which ${deadCost} went to skills that never fired.
These skills account for ${short(grandOutput)} output tokens and ${short(grandTotal)} in total.`);

if (offDisk.length) {
  console.log(`\n${offDisk.length} skills fired that are not in this repo (other projects, other plugins):`);
  for (const r of offDisk.slice(0, 12)) console.log(`  ${pad(r.uses, 5, true)}  ${r.skill}  (${ago(r.lastUsed)})`);
  if (offDisk.length > 12) console.log(`  ... and ${offDisk.length - 12} more`);
}
console.log('');
