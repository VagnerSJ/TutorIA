// Shared helpers for the EnglishTutor Claude Code hooks.
import { spawnSync } from "node:child_process";
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { basename, dirname, join, relative, resolve, sep } from "node:path";

export function readHookInput() {
  try {
    const raw = readFileSync(0, "utf8");
    return raw.trim() ? JSON.parse(raw) : {};
  } catch {
    return {};
  }
}

export function projectRoot(input) {
  return resolve(process.env.CLAUDE_PROJECT_DIR || input.cwd || process.cwd());
}

export function runDotnet(args, cwd, timeoutMs = 240_000) {
  const result = spawnSync("dotnet", args, {
    cwd,
    encoding: "utf8",
    timeout: timeoutMs,
    env: { ...process.env, DOTNET_CLI_UI_LANGUAGE: "en", DOTNET_NOLOGO: "1" },
  });
  const output = `${result.stdout ?? ""}${result.stderr ?? ""}`;
  return { ok: result.status === 0, missing: result.error?.code === "ENOENT", timedOut: result.signal === "SIGTERM", output };
}

export function dotnetAvailable(cwd) {
  return !runDotnet(["--version"], cwd, 30_000).missing;
}

// Walks up from the edited file to the nearest .csproj, never leaving the repository root.
export function findProjectFile(filePath, root) {
  let dir = dirname(resolve(filePath));
  const rootKey = root.toLowerCase(); // Windows paths are case-insensitive (c:\ vs C:\)
  while (dir.toLowerCase().startsWith(rootKey)) {
    const csproj = readdirSync(dir).find((f) => f.endsWith(".csproj"));
    if (csproj) return join(dir, csproj);
    const parent = dirname(dir);
    if (parent === dir) break;
    dir = parent;
  }
  return null;
}

// src/X/X.csproj -> tests/X.Tests/X.Tests.csproj (if it exists); a test project maps to itself.
export function findTestProject(csproj, root) {
  const name = basename(csproj, ".csproj");
  if (name.endsWith(".Tests")) return csproj;
  const candidate = join(root, "tests", `${name}.Tests`, `${name}.Tests.csproj`);
  return existsSync(candidate) ? candidate : null;
}

export function findSolution(root) {
  return readdirSync(root).find((f) => f.endsWith(".slnx") || f.endsWith(".sln")) ?? null;
}

// Keeps only the lines an agent needs to fix the problem.
export function summarize(output, maxLines = 40) {
  const interesting = output
    .split(/\r?\n/)
    .filter((l) => /error|failed|\[FAIL\]|Assert|Expected|Exception|Total tests|Passed!|Failed!/i.test(l))
    .map((l) => l.trim());
  const unique = [...new Set(interesting)];
  const lines = unique.length > 0 ? unique : output.trim().split(/\r?\n/).slice(-maxLines);
  return lines.slice(0, maxLines).join("\n");
}

// Arguments for "dotnet test" whose output carries assertion messages (Expected/Actual).
export const testLoggerArgs = ["--logger", "console;verbosity=normal"];

// For each failed test: its name, the error message and the first stack frame (file:line).
export function summarizeTests(output, maxLines = 60) {
  const lines = output.split(/\r?\n/);
  const result = [];
  for (let i = 0; i < lines.length; i++) {
    if (!/^\s{2}Failed \S/.test(lines[i])) continue;
    result.push(lines[i].trim());
    let j = i + 1;
    while (j < lines.length && !/^\s*Stack Trace:/.test(lines[j])) {
      if (!/^\s*Error Message:/.test(lines[j]) && lines[j].trim()) result.push(`    ${lines[j].trim()}`);
      j++;
    }
    if (j + 1 < lines.length) result.push(`    ${lines[j + 1].trim()}`);
  }
  const totals = lines.filter((l) => /^\s*(Total tests|Passed|Failed):/.test(l)).map((l) => l.trim());
  const body = result.length > 0 ? result : [summarize(output, maxLines)];
  return [...body, ...totals].slice(0, maxLines).join("\n");
}

export function rel(root, path) {
  return relative(root, path).split(sep).join("/");
}
