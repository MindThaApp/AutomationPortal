# Core requests from B1 (Flowmate.Actions.Content)

## 1. DictionaryItem needs an optional display text

**What:** `DictionaryItem` is a sealed record whose `DisplayText` is always indented JSON. Add an optional
display override, for example `DictionaryItem(IReadOnlyList<KeyValuePair<string, ContentValue>> Entries, string? Display = null)`
with `DisplayText => Display ?? <json>`, and keep it out of `WriteJson` / equality-sensitive persistence (or persist it, your call).

**Why:** `weather.current` returns a Dictionary whose text should read like "17° and light rain in London" (SPEC B1), so that
"Show Result", "Combine Text" and notifications give a sentence instead of a JSON blob. The same would help `web.rss` items
("Title") and `sheet.read` rows.

**Workaround in B1:** the weather dictionary has a `Summary` key holding exactly that sentence, so shortcuts can use
the Summary value. Once Core supports it, `WeatherService.BuildCurrent` should pass the sentence as the display text.

## Status (E1)
1. DONE. `DictionaryItem(Entries, Display = null)` plus `ContentValue.Dict(entries, display)`. Weather (current, daily, hourly), RSS items and spreadsheet rows now pass a display sentence; JSON stays available through `ToJson` and the keys.
