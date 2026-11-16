# Sutra web search

Researched against official provider documentation on November 15, 2026.

The setting is **Settings → Sutra Assistant → Enable web search**. It is saved as QWebSearchEnabled and defaults to off. It applies to typed chat, voice, and both sides of comparison mode. Unsupported model selections retain normal chat and show a specific search-availability note.

| Provider | Integration | Availability / setup |
| --- | --- | --- |
| OpenAI | Responses API hosted web_search, tool_choice auto | Supported GPT-6/GPT-5/GPT-4.1/GPT-4o/o3 families; nano excluded. Minimal reasoning is raised to low for search. |
| Anthropic | Messages API web_search_20250305, max_uses 5 | Claude models; organization must enable web search. Basic search avoids requiring dynamic-filtering code execution. |
| Gemini | generateContent Google Search grounding (googleSearch) | Current Gemini models; reads grounding chunks/supports and renders the supplied Google Search suggestions HTML. |
| Groq | GPT-OSS browser_search; Compound enabled_tools web_search + visit_website | openai/gpt-oss-120b, openai/gpt-oss-20b, groq/compound, groq/compound-mini. Other Groq models show a model-selection note. |
| xAI / Grok | Responses API hosted web_search | Grok-4 family; plain chat retains the existing Chat Completions path. |
| DeepSeek | deepseek-flash server tools; deepseek-v4-pro Responses web_search | deepseek-flash supports the recently released deepseek_web_search/deepseek_read_url tools. Older v4-flash is not silently substituted. |
| OpenRouter | Stable web plugin | All model families; searches each enabled question, with native search or Exa fallback. Existing reasoning and model selection are retained. |
| Ollama | Ollama cloud web_search → reference context → selected local model | Save an Ollama API key in the existing encrypted provider-key store. Each enabled question is searched. The key goes to ollama.com, not the local inference server. No screenshot/OCR context is included in the search query. |
| Codex | Per-thread config web_search live/disabled | Uses the signed-in account and respects managed restrictions. No global Codex config is edited. |

Search motion uses one globe per activity row, without a mascot search badge. Native providers that expose tool events start the animation on those events. Gemini/Groq may expose only final search metadata, so their pending status says “Checking web sources” until results arrive. OpenRouter’s plugin and Ollama’s explicit lookup begin searching immediately. Source cards are deduplicated by URL, citation links accept only HTTP(S), and reduced-motion settings stop the globe/shimmer loops.

## Official sources

- OpenAI: https://developers.openai.com/api/docs/guides/tools-web-search
- OpenAI model support: https://developers.openai.com/api/docs/models/gpt-6.1-terra
- Anthropic: https://platform.claude.com/docs/en/agents-and-tools/tool-use/web-search-tool
- Gemini: https://ai.google.dev/gemini-api/docs/google-search
- Gemini generateContent schema: https://ai.google.dev/api/generate-content
- Groq: https://console.groq.com/docs/tool-use/built-in-tools
- Groq GPT-OSS search: https://console.groq.com/docs/tool-use/built-in-tools/browser-search
- Grok: https://docs.x.ai/developers/tools/web-search
- DeepSeek: https://api-docs.deepseek.com/guides/web_tools/
- DeepSeek Responses: https://api-docs.deepseek.com/guides/responses_api/
- OpenRouter: https://openrouter.ai/docs/guides/features/plugins/web-search
- Ollama: https://docs.ollama.com/capabilities/web-search
- Codex thread config schema: https://github.com/openai/codex/blob/main/codex-rs/app-server-protocol/schema/json/v2/ThreadStartParams.json

## Verification

Provider request/stream fixtures cover all integrations, annotations and Unicode offsets, Gemini metadata-only chunks, DeepSeek tool events, disabled search, Ollama credential routing, cancellation/reset, and per-thread Codex isolation. The actual WPF verification runs with --verify-web-search and ISLAND_PREVIEW=1; its report and captures are written to the isolated ISLAND_PREVIEW_DATA directory. Provider fixtures verify protocol handling, not live account access.

Final validation: 818 unit/integration tests passed. The final published executable passed 10 isolated WPF checks. Live OpenAI (gpt-6.1-terra) and Gemini (gemini-3.7-flash) requests both returned answers and source URLs. Other HTTP providers had no saved keys and were covered by protocol fixtures. Desktop input automation was unavailable; the visible Sutra Island Settings window was verified on the root island.exe process.
