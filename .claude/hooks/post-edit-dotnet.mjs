// PostToolUse hook (Edit|Write): formats the edited C# file, builds the affected project and runs
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
  runDotnet(["format", "whitespace", csproj, "--include", filePath], root, 60_000);
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
