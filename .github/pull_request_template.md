## Summary

(What changed, in one or two sentences.)

## Motivation

(Why — bug, spec compliance, engine-integration ask, new macro family, etc. Link the issue if there is one.)

## Spec / reference notes

(If this touches author-facing behaviour, link the relevant Harlowe manual page and call out any places this implementation deliberately diverges from the reference. See `CONTRIBUTING.md` for the reference-impl link.)

## Tests

- [ ] Existing tests pass (`dotnet test harlowe-parser.sln`)
- [ ] Added tests covering the new behaviour and at least one edge case
- [ ] `IRenderOutput` event ordering / pairing invariants preserved (if touched)

## Checklist

- [ ] Code follows the conventions in `CONTRIBUTING.md` (2-space, Allman, no LINQ, public fields for DTOs)
- [ ] Errors use the in-prose `HarloweValue.Error` path, not exceptions
- [ ] If a new macro: registered in `StandardMacros.RegisterAll`
- [ ] `CHANGELOG.md` updated under `## [Unreleased]`
