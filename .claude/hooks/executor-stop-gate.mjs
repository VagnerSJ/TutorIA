// SubagentStop gate for the executor: blocks the executor from finishing while the solution
// does not build or unit tests are red. Blocks only once (stop_hook_active) so the agent can
// still stop and explain a problem it cannot fix.
import {
  dotnetAvailable, findSolution, projectRoot, readHookInput, runDotnet, summarize, summarizeTests, testLoggerArgs,
} from "./lib.mjs";

const input = readHookInput();
if (input.stop_hook_active) process.exit(0);

const root = projectRoot(input);
const solution = findSolution(root);
if (!solution || !dotnetAvailable(root)) process.exit(0);

const build = runDotnet(["build", solution, "-nologo", "-v", "q", "-clp:NoSummary"], root);
if (!build.ok) {
  process.stderr.write(
    `Definition of Done not met: "dotnet build" fails. Do not declare the task done.\n${summarize(build.output)}\n`,
  );
  process.exit(2);
}

const test = runDotnet(
  ["test", solution, "--no-build", "-nologo", ...testLoggerArgs, "--filter", "Category!=LiveBrain&Category!=LiveVoice"],
  root,
);
if (!test.ok) {
  process.stderr.write(
    `Definition of Done not met: unit tests are red. Fix them or explain why they cannot pass.\n${summarizeTests(test.output)}\n`,
  );
  process.exit(2);
}
process.exit(0);
