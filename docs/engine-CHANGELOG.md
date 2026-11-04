# Engine packages changelog

Applies to `Flowmate.Core`, `Flowmate.Actions.Content`, `Flowmate.Actions.Documents`, `Flowmate.Actions.Windows` and
`Flowmate.Triggers`. See `docs/HOSTING.md` for versioning.

## 1.0.0-preview.2

- File actions: `file.renamePattern`, `file.compare` and `file.hash`.
- Power plans: `dev.powerPlan.*` actions, the `power.plan` trigger and the `cond.powerPlan` condition, backed by
  `Flowmate.Core.Platform.PowerPlans` (`IPowerPlanApi`, `WindowsPowerPlanApi`).
- Power Plan Manager actions (`ppm.*`) with a status-pipe client.
- Default WinRT picture backend (`WinRtImageBackend`), used when the host sets no picture backend. Gaps: JPEG to JPEG
  keeps the common photo tags rather than every EXIF entry, resampling is a triangle filter (not WPF's scaler), text is
  drawn with GDI+ so glyph edges differ slightly, and GIF keeps only the first frame (no animation).
- `PublicAPI.Shipped.txt` updated with the power plan types.
## 1.0.0-preview.1

- First packaged release of the engine and action libraries for hosting in other apps.
- No WPF or Windows Forms dependency; `FlowmateEngine` builder in Core.
- `Flowmate.Core` public API frozen in `PublicAPI.Shipped.txt`.
