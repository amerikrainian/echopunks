# The EXA Editor

## The Layout

Tab goes through these groups in this order. Empty groups are skipped.

| Group | What's in it | Shortcut |
|---|---|---|
| Task | The task description, the goal checklist, and the Show Goal button. | Alt+1 |
| Windows | Your EXAs, then file windows. | Alt+2 |
| Hosts | One row per host, with who's standing in it. | Alt+3 |
| Grid | The hosts as a map you walk with the arrows. See "The Grid" below. | |
| *Host* links | Links leaving the host selected in Hosts. | |
| *Host* files | Files in the selected host. | |
| *Host* registers | Hardware registers in the selected host, with live values. | |
| Problems | Compile errors. | |
| Code | The code of the EXA you're editing, announced by its name. | Alt+4 |
| Scores | Test Run slider, Cycles, Size, Activity. | |
| Simulation | Step, Run, Fast-forward, Pause, Reset. | |
| Test log | After a run: errors and goal changes, by test run. | Alt+5 |
| Go to cycle | Jump the execution log to a cycle. | |
| Execution log | After a run: a table of every instruction executed, by cycle, with the EXA's registers, file, host and M as it ran each one. Left and Right move between columns. | |
| Solution | Solution name, EXODUS Solution Browser, EXA program count, Create New EXA. | Alt+6 |

Links, files and registers always follow the host selected in Hosts: arrow to a host, then Tab forward to see what's in it. A puzzle with special hardware (a sign, for example) adds its group next to its host.

## Reading a Network

Consider five hosts:

- YOU, with link 800 to A.
- A, with link -1 back to YOU, link 800 to B, and link 801 to D.
- B, with link -1 back to A and link 800 to C.
- C and D, with no further links.

Go to Hosts, arrow to A, and Tab once. You're in "A links":

```text
-1, YOU
800, B
801, D
```

Enter on "800, B" travels the link, as if you'd run `LINK 800`: B becomes the selected host and you're now in "B links". So:

- `LINK 800`, `LINK 800` puts you at B.
- `LINK 800`, `LINK 801` puts you at D, and you're stuck there.
- `LINK 800`, `LINK 800`, `LINK 800` puts you at C, stuck again.

On links and hosts you'll also hear:

- **"One way"**: no link back from the other side.
- **"blank"** instead of a number: the connection is drawn with an empty number plate.
- **"locked"**: the connection is drawn but closed for now.
- **"Unnamed host 4"**: a host drawn without a name, numbered by its position in Hosts.

## The Grid

The Grid group is the same network laid out as a map. Each host reads its name, then the ways out of it: "A, up right left".

- An arrow travels the link drawn leaving the host that way and reads it like its link row: "800, B, up down". The selected host moves with you, so Links, files and registers follow.
- If several links leave the same way (a row of hosts along one side), a list of them opens instead. Up and Down pick, Enter travels, Escape or Backspace goes back.
- Backspace steps back to the host you came from, one host per press, even across a one-way link. If a link leads back it reads like a travel; if not, just the host's name. Changing host from Hosts or Links starts a fresh trail.

## Windows

An EXA row reads its name, host and registers; while running, also its next instruction and any error. `REPL` copies get their own rows ("XA:1") after their parent. Right from an EXA goes to its M-bus toggle, then its Name field (yes, only two characters).

- Enter: open the EXA's code.
- Backspace: delete the EXA.
- Shift+Up/Shift+Down: move the EXA in the list. The order decides who goes first when EXAs compete in the same cycle.

Enter on a file row, here or in a host, opens its values one per line. Escape or Backspace closes it.

## Writing Code

The Code group is a normal text field. Lines are limited to 24 characters (not my choice). Ctrl+Up/Ctrl+Down switch to the previous or next EXA's program, and Ctrl+Enter creates a new EXA. Tab leaves the field.

Compile errors appear in the Problems group; Enter on one puts the cursor on its line.

## Running and Stepping

- F2: step one cycle. It reads the cycle and what the focused EXA is about to do: "Cycle 4. XA: LINK 800". With Step Narration set to All EXAs (control panel, Mod tab) it first reads what other EXAs visibly did during the cycle, then the cycle and what every one of your EXAs is about to do. Ctrl+F2 switches between the two from anywhere.
- F3: pause. F4: run. F5: fast-forward.
- Escape: reset the run.

Stepping speaks every error and goal change. A free run speaks only goal failures; the rest is in the logs.

## Checking Registers

Outside text fields, X, T, F and M read that register of the focused EXA: the one whose window row you're on, otherwise the one whose code you were last in.

## Following Execution

While running, the code field is read-only: arrows read lines, and the line an EXA is about to execute is marked "current". Alt+Up/Alt+Down switch between live copies of the program; Enter on a copy's row in Windows does the same.

## Running to a Line

- F8 in the code field: run until any copy of the program reaches the cursor's line.
- Shift+Enter: run until one EXA reaches it (the one you're following in the code field, or that row's EXA in Windows).

## The Goal

Show Goal (or F1) opens a popup of the finished state: files, values, hardware. Enter on a file opens its values. Tables are walked like tables: Up/Down within a column, Left/Right across.

## Test Runs and Solutions

Test Run picks which of the 100 test runs you start from: Left/Right by one, Page Up/Page Down by ten, Enter to type a number.

The solution browser (Ctrl+O): Enter opens a solution, Backspace deletes it without asking.

## Things Worth Knowing

- EXAs that aren't yours are "other EXA", or "enemy EXA" in battles.
- A file held by someone else's EXA has no number on screen; the host says "other EXA holding a file".
- Ctrl+Z also undoes the mod's deleting, reordering and renaming.
