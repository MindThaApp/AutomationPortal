# Tour screenshots

The "Explore Automation Portal" tour shows one screenshot per stop. Each file is currently a placeholder (the app icon). Replace it with a real capture under the same file name; nothing else needs to change.

Files live in `src/AutomationPortal.App/Assets/Tour/`, named `<id>.png`, matching `tour.json`.

## Capture rules

- PNG, 2:1 aspect ratio, 1360 x 680 px (the picture area is 2:1 and scales down, never up).
- Capture the app window only, at 100% scale, light theme, default accent (Windows accent set to blue), Mica background, Standard density.
- Demo data: a Library of about 8 shortcuts with friendly names (Good Morning, Resize Photos, Rename by Date, Make it Shout, Backup Documents, Tidy Downloads, Weather Report, Clean Clipboard), 3 pinned. Use a throwaway data root so no real files, names or paths show.
- Crop to the part of the screen the stop is about; leave no personal data, usernames or folder paths visible.

## Stops

| File | Stop | What to show |
|---|---|---|
| `library.png` | Your shortcuts, all in one place | Main window, All Shortcuts page with the demo tiles (Cards look), sidebar visible, one tile hovered. |
| `editor.png` | Build a shortcut from steps | Editor open on "Rename by Date": 4 to 5 steps in the list, the step search box open on the right with a few results, one blank filled with a magic variable. |
| `run.png` | Run it and see what happened | Editor after pressing F5 on "Make it Shout": results drawer open, every step showing a green tick and its short result. |
| `quickrun.png` | Quick Run from anywhere | Quick Run bar over a plain desktop with "gm" typed and "Good Morning" highlighted as the top match. |
| `tray.png` | In the tray, and at your pointer | Quick Panel open at the pointer with 3 pinned shortcuts plus Open, Quick Run, Record and Pause rows; the tray area visible behind it if it fits. |
| `automations.png` | Let things start themselves | Automations page with 3 automations (for example "New file in Downloads", "Every weekday 08:00", "On battery"), one showing its next run time. |
| `snippets.png` | Type a short code, get the text | Snippets page with `;sig`, `;date` and `;addr` in the list, the editor on `;date` with a Preview showing today's date, switch on. |
| `recorder.png` | Do it once, and let it repeat | The recording bar floating over a simple app (for example Notepad) while recording, with the Stop button visible. |
| `gallery.png` | Start from a ready-made shortcut | Gallery page with a section of cards, one card selected and its preview showing, the Add shortcut button visible. |
| `history.png` | A record of every run, and Undo | Run History with a run selected: its steps on the right, a Files changed list and the Undo file changes button visible. |
| `ai.png` | AI steps, on your terms | Settings > AI with Portal AI Host connected and chosen as default, the three ways to get an AI listed. |
| `explorer.png` | From Explorer and the command line | Split image: File Explorer right-click menu with the Automation Portal entry open (Show more options) on the left, a terminal on the right showing `ap run "Good Morning"` and its result. |
| `appearance.png` | Make it look the way you like | Settings > Appearance with the accent colour swatches, Window background, Corners, Density and Motion options visible. |

After replacing the images, run the App tests: they check that every stop's image is a compiled resource.
