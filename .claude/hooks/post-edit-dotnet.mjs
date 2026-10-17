// PostToolUse hook (Edit|Write): formats the edited C# file, checks its .editorconfig style rules
// (e.g. IDE1006 naming, which the build does not enforce), builds the affected project and runs
// its unit tests. Exit code 2 sends the summary on stderr back to the agent.
// Disable the test step with ENGLISHTUTOR_HOOK_TESTS=0.
import {
  dotnetAvailable, findProjectFile, findTestProject, projectRoot, readHookInput, rel, runDotnet, summarize,
  summarizeTests, testLoggerArgs,
} from "./lib.mjs";

const input = readHookInput();
const filePath = input.tool_input?.file_path;
if (!filePath || !/\.(cs|csproj|props|targets)$/i.test(filePath)) process.exit(0);

const root = projectRoot(input);
const csproj = findProjectFile(filePath, root);
if (!csproj || !dotnetAvailable(root)) process.exit(0);

if (filePath.toLowerCase().endsWith(".cs")) {
  // --include must be relative to the working directory: an absolute path is silently ignored.
  const include = rel(root, filePath);
  runDotnet(["format", "whitespace", csproj, "--include", include], root, 60_000);

  const style = runDotnet(["format", "style", csproj, "--verify-no-changes", "--include", include], root, 120_000);
  if (!style.ok && !style.timedOut) {
    process.stderr.write(
      `Style rules from .editorconfig violated in ${rel(root, filePath)} (the build does not enforce them).\n` +
        `Fix these before continuing, or run "dotnet format style ${rel(root, csproj)} --include ${include}":\n` +
        `${summarize(style.output)}\n`,
    );
    process.exit(2);
  }
}

const testProject = findTestProject(csproj, root);
const target = testProject ?? csproj;

const build = runDotnet(["build", target, "-nologo", "-v", "q", "-clp:NoSummary"], root);
if (!build.ok) {
  process.stderr.write(
    `Build failed after editing ${rel(root, filePath)} (project ${rel(root, target)}).\n` +
      `Fix these errors before continuing:\n${summarize(build.output)}\n`,
  );
  process.exit(2);
}

if (!testProject || process.env.ENGLISHTUTOR_HOOK_TESTS === "0") process.exit(0);

const test = runDotnet(
  ["test", testProject, "--no-build", "-nologo", ...testLoggerArgs, "--filter", "Category!=LiveBrain"],
  root,
);
if (!test.ok) {
  process.stderr.write(
    `Tests failing in ${rel(root, testProject)} after editing ${rel(root, filePath)}.\n` +
      `If you are in the Red step, confirm this is the expected failure; otherwise fix it:\n` +
      `${summarizeTests(test.output)}\n`,
  );
  process.exit(2);
}
process.exit(0);
