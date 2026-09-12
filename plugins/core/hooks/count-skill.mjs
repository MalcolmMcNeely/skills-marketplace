#!/usr/bin/env node
// Counts one skill activation, from the PostToolUse payload Claude Code puts on stdin.
//
// This exists because the OpenTelemetry cost and token metrics replace a private plugin's skill
// name with the literal "third-party", and that redaction has no escape hatch. A hook sees the real
// name. Measured: the same invocation read "probekit:probe-plugin" here and "third-party" on
// claude_code.cost.usage, in one session.
//
// Every failure path is silent and exits 0. PostToolUse cannot block, so a broken counter must not
// be the reason anybody's session looks wrong.

import { appendFileSync, mkdirSync } from "node:fs";
import { homedir } from "node:os";
import { dirname, join } from "node:path";

const LOG = process.env.ACME_SKILL_LOG ?? join(homedir(), ".claude", "skill-usage.jsonl");

async function read(stream) {
  const chunks = [];
  for await (const chunk of stream) chunks.push(chunk);
  return Buffer.concat(chunks).toString("utf8");
}

try {
  const payload = JSON.parse(await read(process.stdin));

  // Two fields carry the name. tool_input.skill is the one measured on both Pre and Post; the
  // response field is a second copy, kept as a fallback rather than trusted first.
  const skill = payload.tool_input?.skill ?? payload.tool_response?.commandName;
  if (!skill) process.exit(0);

  const row = {
    at: new Date().toISOString(),
    skill,
    plugin: skill.includes(":") ? skill.split(":")[0] : null,
    session: payload.session_id ?? null,
    cwd: payload.cwd ?? null,
    ok: payload.tool_response?.success ?? null,
    ms: payload.duration_ms ?? null,
  };

  mkdirSync(dirname(LOG), { recursive: true });
  appendFileSync(LOG, JSON.stringify(row) + "\n", "utf8");
} catch {
  // Unparseable payload, read-only home, a schema change: none of them are worth a red session.
}

process.exit(0);
