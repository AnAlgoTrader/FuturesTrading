import { readFileSync, writeFileSync } from "node:fs";

const [, , csvPath, jsonPath] = process.argv;
if (!csvPath || !jsonPath) {
  console.error("usage: node tools/csv2json.mjs <input.csv> <output.json>");
  process.exit(1);
}

const lines = readFileSync(csvPath, "utf8").split(/\r?\n/);
const candles = [];
for (const line of lines) {
  if (!line.trim()) continue;
  const parts = line.split(",").map((p) => p.trim());
  if (parts.length < 6) continue;
  const [time, open, high, low, close, volume] = parts.map(Number);
  if ([time, open, high, low, close, volume].some(Number.isNaN)) continue;
  candles.push({ time, open, high, low, close, volume });
}

writeFileSync(jsonPath, JSON.stringify(candles));
console.log(`${candles.length} candles -> ${jsonPath}`);
