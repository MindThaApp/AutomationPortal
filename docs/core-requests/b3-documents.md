# Core requests from B3 (Flowmate.Actions.Documents)

1. **AppSettings.AiModel default.** Core defaults to `Smart`; the brief wants `Best` (claude-opus-5-5) as the recommended default.
   Suggested: `public AiModelTier AiModel { get; set; } = AiModelTier.Best;`. B3 works with any value (it only applies to Claude).
2. **Summary tokens that are hidden by VisibleWhen.** B3 uses summaries such as `"Resize {images} {by} {width}{height}{longest}{percent}"` where only
   one of the trailing tokens is visible at a time. The editor should skip tokens whose parameter `IsVisible(...)` is false when rendering a Summary
   (adjacent tokens, no spaces between them). No Core change needed, just a rendering rule for App.
3. **ParamType.Shortcut / Color value format** (documentation only). B3 accepts a shortcut id (Guid) or name for `ParamType.Shortcut`, and any WPF colour text
   (`#RRGGBB`, `#AARRGGBB`, names) for `ParamType.Color`.
