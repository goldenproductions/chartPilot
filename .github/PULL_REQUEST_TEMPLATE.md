## What this changes

<!-- One or two sentences. If it changes behaviour a user can see, say what they will notice. -->

## Why

<!-- The problem being solved, not a restatement of the diff. -->

## What you verified

<!-- Not just "tests pass". What did you actually run, and what did you see?
     e.g. "ran the CLI against samples/charts/legacy-importer: CP-SEC-015 fires once, on the
     importer container, and does not fire on samples/charts/member-api." -->

- [ ] `dotnet build ChartPilot.sln` and `dotnet test ChartPilot.sln` are green
- [ ] `npm run build` in `src/chartpilot-web` is green (if the frontend changed)
- [ ] `docs/` updated (if behaviour or architecture changed)
- [ ] New rule has guidance with a trade-off on every option (if a check was added)
- [ ] Snapshot diffs were read, not just regenerated (if a snapshot changed)
