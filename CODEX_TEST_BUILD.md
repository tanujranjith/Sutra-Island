# Sutra Island Codex test build

This ZIP is the test distribution that includes a pinned official Codex runtime.
Extract the entire folder before launching the included app executable; do not run it
from inside the ZIP.

In **Settings > Q Assistant**, choose **ChatGPT / Codex** and select **Sign in with
ChatGPT**. The browser/device-code flow and OAuth token lifecycle are handled by
the official Codex app-server. Sutra Island never receives or stores the token.

Important behavior:

- Signing out in Sutra Island also signs out official Codex apps for this Windows user.
- Q sends prompts, OCR text, and an enabled screen image to OpenAI through Codex.
- Q requests read-only/no-approval operation, declines tool approvals, and deletes
  only the Codex threads it created after each request.
- ChatGPT subscription access applies only to the Codex service and its limits. It
  is not an OpenAI API key and does not provide general API credits.
- This test bundle pins Codex 0.151.0 and validates its files before launching.

For the complete setup, privacy, and troubleshooting notes, see the repository's
`Q.md` file.
