# Echopunks

Screen-reader accessibility mod for [EXAPUNKS](https://store.steampowered.com/app/716490/EXAPUNKS/).

## Keys

### Everywhere

| Key | What it does |
|---|---|
| Any key | At the loading screen: continue (the mod announces the prompt) |
| Up / Down | Previous / next item in the current group |
| Left / Right | Move along a row (tabs, grids, the solitaire board); adjust a slider |
| Tab / Shift+Tab | Next / previous group (Tab stop) |
| Home / End | First / last item |
| Page Up / Page Down | Adjust a slider by a large step |
| Ctrl+Up / Ctrl+Down | Previous / next region inside a group (chat log vs. roster, log cycles or tests) |
| Enter (or keypad Enter) | Activate the focused item |
| Backspace | Secondary action where one exists: delete the focused solution / network / EXA window, cancel a picked-up solitaire card, close a popup |
| Space | Read the game's own tooltip for the focused item, when it has one |
| Escape | Back / close / leave — handled by the game itself |
| F6 | Toggle typing echo in text fields ("Echo characters" / "Echo off"); deleted characters are always spoken, and the choice is remembered |

### EXA editor

| Key | What it does |
|---|---|
| Alt+1 to Alt+6 | Jump to the task, EXA windows, hosts, code, test log (errors and goals completed or failed) or solution section; "unavailable" when that section isn't there right now |
| F2 | Step the simulation one cycle (hold to repeat) |
| F3 / F4 / F5 | Pause / run / fast-forward  |
| F1 | Open the goal view as a readable popup (game key; also the Show Goal button) |
| F8 | Run to the caret's line — stops when any copy of that program reaches it |
| Shift+Enter | Run to line for one specific EXA (on an EXA window row, or in the code field) |
| Ctrl+Up / Ctrl+Down | In the code field: switch to the previous / next EXA program  |
| Alt+Up / Alt+Down | While running: follow the previous / next live instance of the focused program |
| Ctrl+Enter | Create a new EXA |
| Ctrl+O | Open the solution browser |
| Ctrl+Z / Ctrl+Y | Undo / redo  |
| Arrows, Home, End, Page Up / Down, Shift+arrows, Ctrl+A, Ctrl+C / X / V | Normal text editing in the code field ; the mod reads the line or character you land on |
| Right / Left on an EXA window row | Along the row: the EXA, its M-bus toggle, its Name field |
| Type in an EXA's Name field | Rename the EXA (two characters; Backspace deletes; Enter or moving away commits) |
| Enter on a file row | Open the file's values, one row per item |
| Enter on a link row | Jump to the host at the other end |
| Enter on a problem row | Jump the caret to the offending line |
| Digits, period, Backspace, then Enter | In the "Go to cycle" field: jump the execution log to a cycle (`test.cycle` pins a test run) |
| Escape | Stop and reset the run, or leave the editor |

In the Redshift sandbox, while a program is free-running every key belongs to the game
(the pad: W A S D, J K L, Enter for START by default); pause with F3 to browse again.

### Cutscenes and the credits

| Key | What it does |
|---|---|
| Space, Tab or Enter | Advance to the next line |
| Escape | Skip |
| Up / Down, Enter | In an EMBER-2 conversation with answer choices: review the choices and pick one |
| 1 – 9 | Pick an answer directly  |

### ПАСЬЯНС

| Key | What it does |
|---|---|
| Arrows | Move around the board (left / right between columns, up / down within one) |
| Enter | Pick up the focused card or run; Enter again on a destination to drop it |
| Backspace | Put the picked-up cards back |
