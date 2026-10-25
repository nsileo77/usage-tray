# Provenance and integration review — 2026-10-24

## Evidence found locally

The inherited Usage Tray 2.0.1 package's ATTRIBUTION.md explicitly says the utility was independently created for the user. Its README describes the same taskbar implementation, quota protocol, rendering and tests continued here. This supports an origin in the user's earlier local project. It does not identify an upstream repository. No .git metadata exists in that project root, its application/source/work folders, or this review/candidate. Therefore no commit history or GitHub owner/remote can be verified from this project. No unrelated Git config or credential store was consulted.

The inherited package identifies one externally sourced asset: ChatGPTLogo.svg, obtained from OpenAI's brand page. That SVG is absent from this candidate. TaskbarStrip.LoadLogo now constructs only the original rectangular AI-chip mark; the inherited blossom path is not embedded. Consequently the old artwork attribution is historical provenance, not a license grant for artwork included in this release.

Source inspections found no third-party copyright headers, repository links, package manifests or vendored libraries. This is evidence of no identified copied upstream component, not proof of exhaustive originality. The user subsequently confirmed that the project was made only by him and the assistant. This closes the identified provenance confirmation; no third-party source license was discovered. MIT LICENSE is now included with Nicholas Sileo as copyright holder.

## What the included MIT license covers

The project's own C# code, scripts, manifest, original AI-chip drawing and synthetic preview assets, to the extent the owner holds rights in them. OpenAI's [Terms of Use, Content](https://openai.com/policies/terms-of-use/) assign its rights in generated output to the user subject to applicable law; they exclude third-party output and do not guarantee uniqueness. That supports licensing generated project contributions without needing an OpenAI copyright grant beyond the existing terms. It does not prove copyrightability of every generated fragment or clear undisclosed third-party material.

Keep ATTRIBUTION.md as the dependency/provenance record. Windows/.NET/WPF/WinForms/System.Web.Extensions and Segoe UI remain external installed platform dependencies: no runtime DLL, font or Microsoft license is being relicensed as MIT. The official Codex client is external, not bundled. Its public source is [Apache-2.0](https://github.com/openai/codex/blob/main/LICENSE); this project does not copy that implementation or distribute its binaries, so it should not label Codex itself MIT or claim that its software license grants hosted-service access. No third-party notice requiring inclusion for a bundled component was identified.

## Integration scope clarified

The official [auth endpoint guidance](https://learn.chatgpt.com/docs/app-server#auth-endpoints) explicitly permits existing local or open-source applications using app-server authentication to continue, and documents account/rateLimits/read. It distinguishes commercial/hosted services, which were not permitted. The free, local, open-source design here uses a separately installed official Codex client and its existing login; it collects no credentials and uses conservative sequential polling.

The experimental/unsupported-production statement is a reliability and support caveat, not an automatic prohibition on free local source distribution. There is no mandatory OpenAI approval gate for this proposed release established by the reviewed evidence. A new quota-only application's exact polling interval is not expressly addressed; clarification may be useful, but silence is not evidence of prohibition. Reassess terms before commercial or hosted use. MIT licenses this utility, not the hosted service or external client.

## Remaining release steps

1. Completed: authenticated nsileo77 account verified; public usage-tray source publication authorized.
2. Completed: MIT license included, copyright (c) 2026 Nicholas Sileo; user confirms project created only by him and assistant.
3. Complete deferred visible Windows QA and final package review; retain experimental dependency and supported-window disclosures.
4. Source publication authorized; binary release remains pending final desktop QA.

