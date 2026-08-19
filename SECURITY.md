# Security Policy

## Reporting a vulnerability

Please report security issues privately through
[GitHub's private vulnerability reporting](https://github.com/goldenproductions/chartPilot/security/advisories/new)
rather than opening a public issue.

Include what you were running, what you observed, and how to reproduce it. You can expect an
acknowledgement within a week. ChartPilot is a personal project, not a commercial product — there is
no paid support and no bounty, but reports are taken seriously and credited unless you prefer
otherwise.

## What ChartPilot's threat model actually is

Worth stating plainly, because it determines what counts as a vulnerability.

ChartPilot runs `helm template` on charts the user points it at. Helm templates are Go templates,
and rendering a chart **executes code from that chart**. ChartPilot is therefore designed as a local
tool that runs with the user's own privileges on charts the user already trusts enough to deploy —
it is not a sandbox, and it is not a service.

The deliberate protections are:

| Protection | What it prevents |
|---|---|
| **Loopback-only binding** | The API refuses to start on a non-loopback address, so the renderer is never exposed on a network interface |
| **No kubeconfig, ever** | No credentials are passed to Helm and no cluster is contacted, so a malicious chart cannot reach a cluster through ChartPilot |
| **Allowlist root** | Chart and values paths must resolve under a configured root; traversal is rejected before existence is checked, so probing cannot map the filesystem |
| **No argument injection** | User input is written to files in a per-workspace temp directory, never concatenated into process arguments |
| **Timeout and output cap** | A chart that renders forever, or renders gigabytes, cannot hang or exhaust the host |
| **`--dependency-update` off by default** | Rendering does not reach the network unless the user explicitly asks |
| **In-memory workspaces** | Nothing is written back to the user's chart directory except an explicit export |

### In scope

- Escaping the allowlist root, by any path form.
- Getting a kubeconfig, cluster credential or network call out of a render.
- Argument or command injection through a chart path, values file or API parameter.
- Binding to a non-loopback interface.
- A rendered chart writing outside its temp directory.
- Anything that makes ChartPilot report a chart as clean when a rule should have fired — a false
  negative in a security rule is a security issue, not merely a bug.

### Out of scope

- **A malicious chart executing template logic during render.** That is what `helm template` does;
  ChartPilot inherits it. Do not point it at charts you would not deploy.
- Denial of service against your own loopback API.
- The accuracy of the per-rule advice in `Checks/Guidance/`. It is machine-authored and has not been
  reviewed by a security expert — treat it as a starting point, not an authority. See the README.
- Findings you disagree with. Those are issues, and they are welcome as issues.

## Supported versions

`main` only. There are no released versions yet, so fixes land on `main`.
