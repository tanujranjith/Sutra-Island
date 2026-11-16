# Math rendering and Bluetooth removal QA

final result: passed

Requested changes: source screenshots codex-clipboard-rXdMSi.png (raw boxed/fraction markup) and codex-clipboard-fXvkZg.png (Bluetooth status tile). The user's original mascot, right-side user messages and compact typography are retained.

Evidence:
- ../artifacts/math-v137/staging/chat-derivative.png: actual native chat viewport 1000 × 625 DIP, 1500 × 938 pixels at 1.5×; matching derivative example with inline math, boxed result and stacked fractions.
- ../artifacts/math-v137/staging/chat-derivative-narrow.png: 800 × 625 DIP, 1200 × 938 pixels; equations and persistent composer controls visible.
- ../artifacts/math-v137/staging/island-no-bluetooth.png: actual Graphite expanded island 990 × 342 DIP; Wi-Fi and weather remain, Bluetooth tile and its column removed.
- ../artifacts/math-v137/staging/current-chat-search-complete.png: shared reply rendering still retains search sources and links.
- ../artifacts/math-v137/staging/bindings.log: empty.

Visual review: math uses actual Computer Modern fonts and native vector rendering rather than flattened command strings. Fraction bars, superscripts, log notation and boxed result are visible without clipping. Reply text remains 15 DIP; existing theme tokens are used for math and box foregrounds, including theme updates on cached equations. Assistant layout now provides the full available response width so equation-only answers can measure correctly. User bubble stays right aligned. No actionable P0/P1/P2 findings in the requested states.

Bluetooth removal: a user-requested GPT-6.1 Terra subagent at high reasoning removed the Graphite tile and hidden duplicate row, closed their grid gaps, and renamed relevant connectivity preference labels to Wi-Fi. The parent reviewed the changes and owns the combined build/deployment.

Validation: 836 unit tests passed. All 14 native math checks passed for inline/display formatting, boxes/fractions, roots, sums, integrals, matrices, aligned equations, unsupported-source fallback, incomplete/complete streams, caret preservation, citations, narrow widths, theme updates and ordinary replies. The Release self-contained single-file Windows x64 build is verified separately under ../artifacts/math-v137. Full interactive desktop automation remains unavailable in this environment; actual WPF captures and root executable/window checks provide validation.

Native rendering references and supported syntax: docs/math-rendering.md. Previous density report archived at ../artifacts/chat-layout-v136/design-qa.md.

Packaged verification: the published single-file executable passed the same 14 native math checks and all 10 web-search checks. Deployment version 1.3.7, SHA-256 FD5F60613713305113C735699F0EA43490ACF3BEA83503680F8885E7B05987C0; root installation hash matches. See ../artifacts/math-v137/deployment.json for process/window evidence.
