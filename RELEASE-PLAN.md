# Current release status

Public source publication to nsileo77/usage-tray is authorized. Account ownership verified. MIT copyright 2026 Nicholas Sileo. Binary release remains pending final desktop QA. Earlier planning notes below are historical.

# Proposed free GitHub release — approval pending

Approved product name: Usage Tray; proposed repository slug: `usage-tray`. Proposed visibility: public. Owner destination: **not yet available**; the user has no GitHub account. MIT LICENSE is included locally, copyright (c) 2026 Nicholas Sileo. The user confirmed the project was created only by him and the assistant. No account, repository, push or release has been created.

The user chose free and open source. Publish source plus a downloadable portable Windows ZIP after approval; executable-only distribution would not satisfy that choice. Start with the repository README and GitHub Releases. A separate static site can wait. GitHub Pages can later host a free public-repository landing page on its provided domain; the host logs visitor IPs for security even if the utility has no telemetry.

## Exact proposed repository contents
`source/` (nine C#/PowerShell/manifest files), `screenshots/` (three synthetic PNGs), README.md, ATTRIBUTION.md, RELEASE-PLAN.md, SCAN-REPORT.md, .gitignore. LICENSE and PROVENANCE-AND-TERMS.md are also included. No executable, logs, credentials, actual account data, personal absolute paths, browser artifacts or unrelated projects in the source archive.

Proposed release assets: compiled portable EXE plus README, approved LICENSE/attributions and SHA-256 checksums, matched to a reviewed source tag. This candidate is a source preparation, not the final signed release. Choose a prerelease version/tag after QA rather than implying stable production support.

## Gates / packaging checklist
- After the user creates a GitHub account, verify the owner destination and repository slug, final contents and concrete public-publish approval. Product name, copyright holder and project provenance are confirmed.
- Keep the documented free local/open-source integration scope, separate official client/login, no credential collection, and experimental support disclosure. Reassess commercial/hosted use. No mandatory OpenAI approval gate is established for this release.
- Complete visible taskbar/Explorer, narrow display, multiple-monitor/DPI, clean-machine launch/quit/uninstall, and keyboard/browser demo QA. Preserve original installation and review-only settings.
- Build from the final reviewed tag; rerun parser, account-isolation fixtures, error recovery, layout/persistence, dates/DST and pacing tests. Validate supported Windows and official client versions; do not claim all plans/windows are available.
- Scan the final source and binary package, exclude credentials/account logs/personal paths, include dependency notices and synthetic-only screenshots. Check checksums and source-to-binary match.
- Decide signing. Current EXE is unsigned; SmartScreen may warn, and signing does not guarantee immediate reputation. Do not ask users to disable security. A free app need not imply that signing services are free.
- Document portable extraction/run/Quit/removal, no auto-start, no automatic updater, rollback and issue-reporting privacy guidance. Never ask users to post auth files or raw account responses.
- Review a draft release and assets, then request final approval before publication. Reddit announcement is handled separately.

## Official references (checked 2026-10-24)
- [Codex protocol](https://learn.chatgpt.com/docs/app-server) and [experimental status](https://learn.chatgpt.com/docs/mcp-server).
- [OpenAI individual terms](https://openai.com/policies/terms-of-use/), [Services Agreement](https://openai.com/policies/services-agreement/), [brand guidelines](https://openai.com/brand/). Official auth guidance permits existing local/open-source applications; commercial/hosted uses are a separate scope.
- [GitHub licensing](https://docs.github.com/articles/licensing-a-repository), [releases](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases), [Pages availability/privacy](https://docs.github.com/en/pages/getting-started-with-github-pages/what-is-github-pages).
- [Microsoft SmartScreen reputation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation).

Provenance and exact outstanding terms question are narrowed in PROVENANCE-AND-TERMS.md, included in the proposed repository contents. No project Git remote exists; owner remains unverified.


