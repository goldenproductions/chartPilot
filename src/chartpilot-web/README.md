# chartpilot-web

The ChartPilot frontend: React 19 + TypeScript + Vite, with Monaco for the values editor and the
rendered manifest view.

## Running it

```bash
npm install

# Dev: Vite on http://127.0.0.1:5173, proxying /api to the API on 127.0.0.1:5080.
# Start the API first:  dotnet run --project ../ChartPilot.Api
npm run dev

# Production build: emits ./dist, which ChartPilot.Api serves as static files.
npm run build

# Types only
npm run typecheck
```

`npm run build` runs `tsc --noEmit` before `vite build`, so a type error fails the build.

Both the dev server and the API bind to loopback only. ChartPilot renders arbitrary Go templates
from charts the user points it at; it is not a service to expose on a network interface.

## How it is put together

| Path | Responsibility |
|---|---|
| `src/api/types.ts` | TypeScript mirrors of the API DTOs. Enums arrive as strings (`"Critical"`). |
| `src/api/client.ts` | `fetch` wrapper; parses `ProblemDetails` (including the `helmStderr` extension) into `ApiError`. |
| `src/api/endpoints.ts` | One typed function per route in the architecture doc's section 7 table. |
| `src/api/queries.ts` | TanStack Query hooks. **All server state lives here.** |
| `src/store/uiStore.ts` | Zustand. **UI state only** — selection, active environment/profile, editor toggle, panel sizes, findings filter. Server data is never mirrored into it. |
| `src/lib/monacoSetup.ts` | Loads Monaco from the local package (never a CDN), wires the workers, and feeds the chart's `values.schema.json` to `monaco-yaml`. |
| `src/lib/yamlPath.ts` | Resolves a Core `YamlPath` (`spec.template.spec.containers[0].image`) to a Monaco line — this is what makes findings clickable. |
| `src/lib/yaml.worker.ts` | Local re-export of `monaco-yaml/yaml.worker`. Referencing the dependency directly breaks the worker under Vite. |
| `src/components/` | The three-column shell: header, chart overview, resource explorer, centre pane, findings, score, diff, export dialogs. |

## Behaviour worth knowing

- **Live render** is debounced 400 ms after the last keystroke. The pipeline query PUTs the draft
  values and POSTs `review` in one round trip, carrying TanStack Query's `AbortSignal`, so a
  superseded render is cancelled rather than raced. The previous result stays on screen, greyed,
  while a render is in flight.
- **Findings navigate.** Clicking a finding selects its resource, flips the centre pane to the
  rendered manifest and scrolls Monaco to the finding's `yamlPath`. Chart-level findings reveal in
  the values editor instead.
- **Helm stderr is verbatim**, with `template.yaml:12:3` locations turned into links when
  ChartPilot can actually show that file (the values file, or a resource rendered from that
  template).
- **The pipeline uses `review`, not `render`.** `ReviewDto` already carries the rendered resources,
  so the editor never triggers two Helm executions per keystroke. The rendered-manifest view shows
  either the selected resource or the whole `---`-separated stream.
- The bundle includes the full `monaco-editor` package (~3.7 MB, ~1 MB gzipped). That is deliberate:
  it is served from loopback, and trimming Monaco's language contributions has historically been a
  source of subtle breakage.
