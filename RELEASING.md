# Releasing Twinelike

The release pipeline is split between this repo's `.github/workflows/release.yml` (build + pack + draft a GitHub Release) and the local prep below (decide the version, prepare `CHANGELOG.md` / `Twinelike.csproj` / `README.md`, commit, tag). **The tag push is what triggers the workflow.**

## Versioning

Semantic versioning. Tags are `vX.Y.Z`; the version *number* (no `v`) is what NuGet wants. Pre-`1.0.0` releases may make breaking changes between minor versions while the engine-integration surface stabilises — see the `CHANGELOG.md` preamble.

## 1. Pre-flight

- [ ] **CI is green on `main`.** `gh run list --limit 5 --workflow ci.yml`, or check the Actions tab.
- [ ] **Working tree is clean** and rebased on `origin/main`.
- [ ] **Skim the commits since the last release.** `git log v<last>..HEAD --oneline` — this drives the new CHANGELOG entry.
- [ ] **Decide the version.** Patch (`0.1.1`) for fixes + non-breaking additions; minor (`0.2.0`) for new authoring features or visible API additions; major (`1.0.0`) for breaking changes (only after engine-integration surface settles).

## 2. Update the changelog

Edit `CHANGELOG.md`:

- Add a new `## [X.Y.Z] — YYYY-MM-DD` section directly above the previous one.
- Group entries under **Added** / **Changed** / **Fixed** / **Security** / **Deprecated** / **Removed** (Keep a Changelog convention; omit empty sections).
- Update the footer links:
  - `[Unreleased]: …/compare/vX.Y.Z...HEAD` (bumped to the new version)
  - Add `[X.Y.Z]: …/compare/vPREV...vX.Y.Z` (or `releases/tag/vX.Y.Z` for the very first release).

The release workflow's `awk` extractor reads the matching `## [X.Y.Z]` section verbatim to seed the release notes, so what you write there is what reviewers see on GitHub.

## 3. Bump the version

- [ ] `Twinelike.csproj` — `<Version>X.Y.Z</Version>`.
- [ ] `README.md` — the `dist/Twinelike.X.Y.Z.nupkg` example filename in the build/pack section.

The release workflow passes `-p:Version=X.Y.Z` to `dotnet build` / `dotnet pack`, so the csproj `<Version>` is overridden at release time. But CI's pack smoke test uses the csproj value, so keep them in sync.

## 4. Commit and push

```sh
git add CHANGELOG.md Twinelike.csproj README.md
git commit -m "Bump version to X.Y.Z. <one-line summary of what's in this release.>"
git push origin main
```

## 5. Tag and push the tag

```sh
git tag vX.Y.Z
git push origin vX.Y.Z
```

The tag push triggers `release.yml`. Watch it:

```sh
gh run watch --exit-status            # blocks on the most recent run
# or
gh run list --limit 3
```

The job builds, runs tests as a release gate, packs the `.nupkg`, stages four artifacts (bare DLL, zipped bundle, `.nupkg`, `.snupkg`), extracts release notes from the changelog, and creates a **draft** GitHub Release.

## 6. Review the draft and publish

Open the draft from the [Releases page](https://github.com/RaheelYawar/twinelike/releases). Verify:

- Title is `Twinelike X.Y.Z`.
- Body matches your `## [X.Y.Z]` CHANGELOG section.
- All four assets are attached: `Twinelike-X.Y.Z.dll`, `Twinelike-X.Y.Z.zip`, `Twinelike.X.Y.Z.nupkg`, `Twinelike.X.Y.Z.snupkg`.

Click **Publish release** to make it public. The tag is already in place from step 5.

## Manual dispatch (without a tag)

The `workflow_dispatch` trigger on `release.yml` lets you draft a release without pushing a tag first — useful for retries or for cutting a release entirely from the GitHub UI. Provide the version (e.g. `0.1.1`) as the input; the tag is then created on Publish.

## If something goes wrong

- **Bad release notes / wrong assets.** Discard the draft from the GitHub UI (it's not public until you publish). Fix `CHANGELOG.md` / csproj, commit, then either re-trigger via workflow_dispatch — or delete the tag locally and on the remote (`git push --delete origin vX.Y.Z`) and re-tag.
- **Workflow run failed.** Re-run from the Actions tab once the underlying issue is fixed. If the build/test step regressed, fix on `main` first, then re-trigger.
- **Already published a bad release.** GitHub releases can be edited or deleted post-publish, but deleting does not yank consumers who already downloaded. Prefer cutting an `X.Y.Z+1` patch over deleting.

## Future: NuGet.org auto-publish

The release currently produces a `.nupkg` but does not push it to nuget.org — consumers who want the package can download it from the GitHub Release. To automate publishing, add a `dotnet nuget push` step to `release.yml` and store a `NUGET_API_KEY` secret on the repo.
