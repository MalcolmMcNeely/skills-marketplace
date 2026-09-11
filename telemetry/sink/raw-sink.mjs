// Step 1. Dump every OTLP body Claude Code sends, unprocessed, to a file.
//
// The point is to read the JSON with your own eyes before trusting a dashboard.
// Run it, run a Claude session against it, then look at capture/*.json.
//
//   node telemetry/sink/raw-sink.mjs
//
// Needs OTEL_EXPORTER_OTLP_PROTOCOL=http/json so the bodies arrive readable.
// Under http/protobuf they arrive as binary and this prints nothing useful.

import { createServer } from "node:http";
import { appendFileSync, mkdirSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const PORT = Number(process.env.SINK_PORT ?? 4318);
const OUT = join(dirname(fileURLToPath(import.meta.url)), "capture");

mkdirSync(OUT, { recursive: true });

const stamp = new Date().toISOString().replace(/[:.]/g, "-");
const files = {
  logs: join(OUT, `${stamp}-logs.jsonl`),
  metrics: join(OUT, `${stamp}-metrics.jsonl`),
  traces: join(OUT, `${stamp}-traces.jsonl`),
};

const counts = { logs: 0, metrics: 0, traces: 0 };
const skills = new Map();

// An OTLP log body nests resource -> scope -> logRecord. Flatten the attributes
// of one record into a plain object so a human can read it.
function attrsOf(record) {
  const out = {};
  for (const a of record.attributes ?? []) {
    const v = a.value ?? {};
    out[a.key] =
      v.stringValue ?? v.intValue ?? v.boolValue ?? v.doubleValue ?? JSON.stringify(v);
  }
  return out;
}

function reportSkillEvents(body) {
  for (const res of body.resourceLogs ?? []) {
    for (const scope of res.scopeLogs ?? []) {
      for (const rec of scope.logRecords ?? []) {
        const a = attrsOf(rec);
        if (a["event.name"] !== "skill_activated") continue;
        const name = a["skill.name"] ?? "(absent)";
        skills.set(name, (skills.get(name) ?? 0) + 1);
        console.log(
          `  skill_activated  name=${name}  trigger=${a["invocation_trigger"] ?? "-"}  source=${a["skill.source"] ?? "-"}`,
        );
      }
    }
  }
}

createServer((req, res) => {
  const signal = req.url?.includes("/v1/logs")
    ? "logs"
    : req.url?.includes("/v1/metrics")
      ? "metrics"
      : req.url?.includes("/v1/traces")
        ? "traces"
        : null;

  if (req.method !== "POST" || !signal) {
    res.writeHead(404).end();
    return;
  }

  const chunks = [];
  req.on("data", (c) => chunks.push(c));
  req.on("end", () => {
    const raw = Buffer.concat(chunks).toString("utf8");
    appendFileSync(files[signal], raw + "\n");
    counts[signal] += 1;

    if (signal === "logs") {
      try {
        reportSkillEvents(JSON.parse(raw));
      } catch {
        console.log("  (body was not JSON. Set OTEL_EXPORTER_OTLP_PROTOCOL=http/json)");
      }
    }

    // OTLP wants an ExportServiceResponse. An empty object is a valid success.
    res.writeHead(200, { "content-type": "application/json" }).end("{}");
  });
}).listen(PORT, "127.0.0.1", () => {
  console.log(`raw OTLP sink listening on http://127.0.0.1:${PORT}`);
  console.log(`writing to ${OUT}`);
  console.log("stop with ctrl-c for a summary\n");
});

process.on("SIGINT", () => {
  const summary = {
    capturedAt: new Date().toISOString(),
    requests: counts,
    skillActivations: Object.fromEntries([...skills].sort((a, b) => b[1] - a[1])),
  };
  writeFileSync(join(OUT, `${stamp}-summary.json`), JSON.stringify(summary, null, 2));
  console.log("\n" + JSON.stringify(summary, null, 2));
  process.exit(0);
});
