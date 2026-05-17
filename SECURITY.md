# Security Policy

## Supported versions

Twinelike is currently pre-1.0; security fixes ship on the latest released version only. Once a 1.x line is established this policy will expand to cover at least one prior major.

| Version | Supported          |
| ------- | ------------------ |
| 0.x     | :white_check_mark: |

## Reporting a vulnerability

**Please report security issues privately rather than opening a public issue.**

Preferred:

- Use GitHub's [Privately report a vulnerability](https://github.com/RaheelYawar/twinelike/security/advisories/new) flow. This keeps the discussion scoped to the maintainers until a fix ships.

Alternative:

- Email **raheelyawar@gmail.com** with a clear subject (e.g. `[Twinelike security] <short description>`).

Helpful to include:

- A description of the issue and its impact
- A minimal repro — a Twee snippet, HTML export, or short C# fragment exercising the runtime
- Affected version(s)
- Any suggested fix you've considered

You'll get an acknowledgement within roughly a week. After triage we'll work on a fix, cut a release, and coordinate disclosure timing with you.

## Scope

Twinelike's core responsibility is parsing and evaluating untrusted author content (passage source written in Harlowe markup). Bugs in that boundary — where author content escapes the evaluation model and influences the host engine in unintended ways — are the most relevant class. Examples:

- Author markup that causes the runtime to throw rather than rendering an in-prose error (the runtime contract is "never throw on the render hot path"; an exception leak is a hard violation worth reporting).
- Malformed Twine 2 HTML or Twee 3 source that crashes the loader before any passage runs.
- Resource exhaustion from an author expression — unbounded recursion, exponential blowup — that doesn't terminate.

Out of scope:

- The behaviour of `(html:)`-emitted markup once it reaches the consumer's renderer. Sanitisation of inline HTML at the rendering boundary is the host engine's responsibility (see the engine-integration notes in [`README.md`](./README.md)).
- Vulnerabilities in transitive dependencies that are already disclosed upstream — please report those to the dep maintainers; Dependabot will surface fixes here.
