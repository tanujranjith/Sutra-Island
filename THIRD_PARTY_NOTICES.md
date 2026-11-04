# Third-party notices

The optional Codex test ZIP for Sutra Island redistributes unmodified Windows
binaries from the official [OpenAI Codex release](https://github.com/openai/codex/releases/tag/rust-v0.151.0).

Codex is Copyright OpenAI and contributors and is distributed under the Apache
License 2.0. Its source and license are available at
https://github.com/openai/codex.

Sutra Island and Codex are separate works. Inclusion of Codex in the test ZIP
does not change Sutra Island's license and does not grant access beyond the
signed-in user's eligible ChatGPT/Codex subscription limits.

## System.Speech 10.0.10

Sutra Island uses Microsoft's System.Speech package for Windows speech recognition
and synthesis. Copyright .NET Foundation and Contributors. Licensed under the MIT
License. Source: https://github.com/dotnet/runtime. Package:
https://www.nuget.org/packages/System.Speech/10.0.10.

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.


---

## Sutra Flow components

- Whisper.net and Whisper.net.Runtime 1.9.1, MIT License, Copyright (c) Sandro Hanea. https://github.com/sandrohanea/whisper.net
  The runtime package ships whisper.cpp and ggml, MIT License, Copyright (c) Georgi Gerganov. https://github.com/ggml-org/whisper.cpp
- NAudio.WinMM and NAudio.Core 2.2.1, MIT License, Copyright (c) Mark Heath and contributors. https://github.com/naudio/NAudio
- The Whisper base.en model (ggml-base.en.bin) is downloaded at runtime from https://huggingface.co/ggerganov/whisper.cpp; OpenAI Whisper weights are MIT licensed.
- Sutra Flow's cleanup instructions and examples are adapted from WhimprFlow, MIT License, Copyright (c) 2026 WhimprFlow contributors. https://github.com/Blueturboguy07/WhimprFlow

Each is provided under the MIT License text reproduced above, with its own copyright notice.
