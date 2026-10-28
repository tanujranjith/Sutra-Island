# Release review — v1.1.9

Reviewed on 2026-10-28. This records release preparation and verification. GitHub publication was authorized after the owner's review; the publication status and downloadable assets are recorded on the repository's Releases page.

## Scope

Reviewed the current source and pending changes, with attention to command parsing, assistant sessions and cancellation, media seek queuing, settings persistence, build assets, and release packaging. Reviewed the README, assistant setup notes, local file references, and files eligible for Git. This is a general correctness and release-readiness pass, not an exhaustive security audit.

## Resolved

- Moved the existing logo dependencies into the repository so a fresh source checkout can build without its neighboring workspace.
- Excluded generated WPF project files and workspace-specific agent instructions from Git. Build output and local QA captures remain ignored.
- Removed machine-specific paths from public design notes and clarified which older notes are historical.
- Refreshed assistant setup and screen-context documentation, including capture behavior for follow-up questions in a screen-help session.
- Removed whitespace errors reported by `git diff --check`.
- Added inspected screenshots from the native verification fixtures for the current README. These use test data and contain no desktop background or live conversation.

## Verification

- 479 unit tests passed.
- A Release solution build, with the normal analyzer settings, completed with zero warnings and errors.
- 120 full native assertions passed twice from the packaged app, including Settings, voice lifetime, notifications, and screen-context behavior.
- 221 native command and assistant assertions passed; the final check reported no WPF binding errors.
- 123 native graphite interface assertions passed.
- An isolated copy of the Git-visible source restored and built successfully with zero warnings and errors, using the local SDK and package cache. The isolated build disabled analyzers; it verified that source and assets were self-contained.
- Inspected the release workflow and project version, and checked the Git candidate list for generated output and files over 50 MB. No such files were found.
- A limited credential-pattern check found fixture strings and API-key UI handling, with no embedded credential identified by that check.
- All 18 local links checked across the main guides resolved, and `git diff --check` passed.

Local verification reports are kept under ignored `artifacts/` folders. They are not release assets.

The broader sweep also improved the verification harness: functional visibility checks wait for their expected state or use reduced motion, and fixture windows have separate identities. An unexpected native close message affected runs under the usual build executable name; the same packaged bytes passed twice under a unique process name, verified by matching SHA-256 hashes. The final packaged command and graphite checks used that same isolated copy.

## Review limits

Live provider sign-in and billing, physical AirPods reconnects, microphone permissions, mixed-DPI monitor transitions, and sleep/resume behavior need testing on the intended devices and accounts. Automated fixture checks do not establish frame-rate performance on every GPU or desktop. The release workflow's result is available in GitHub Actions.

The native application is published as an executable release asset rather than committed into Git. The source version, release tag, executable version, and checksum list should describe the same release.
