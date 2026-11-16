# Assistant math rendering

AssistantReplyView renders completed inline \(…\) / $…$ and display \[…\] / $$…$$ regions locally with WPF-Math 2.1.0. The original response remains intact for Copy and provider history; voice/compact text keeps the existing readable text presentation. Both comparison responses use the same reply view.

MathReplyMarkup masks equations before response cleanup so fractions, subscripts, matrices and nested groups survive. Code examples, escaped prices and incomplete regions are not promoted to equation controls. Display math occupies its own line and contributes its actual height; wide expressions scale down to fit the reply. Cached controls follow theme foregrounds. Outer \boxed and \fbox frames are drawn natively around typeset contents. amsmath aligned/align* map to native align; gathered/gather map to a native matrix. Unsupported formulas remain as their original source, preserving mathematical content.

References: [WPF-Math package](https://www.nuget.org/packages/WpfMath/2.1.0), [official renderer source](https://github.com/ForNeVeR/xaml-math/tree/v2.1.0), [supported native environments](https://github.com/ForNeVeR/xaml-math/blob/v2.1.0/docs/environments.md). MIT and original font notices are embedded under Assets/ThirdParty and documented in THIRD_PARTY_NOTICES.md.

Validation: MathReplyMarkupTests covers delimiter styles, nested fractions/boxing, code, prices, incomplete streams and token collisions. Run --verify-math with ISLAND_PREVIEW=1 and an absolute ISLAND_PREVIEW_DATA directory for native layout, renderer, citation, streaming, fallback and theme checks. --verify-web-search checks shared reply/source behavior. Isolated full-chat and expanded-island captures are under ../artifacts/math-v137/staging in the workspace.
