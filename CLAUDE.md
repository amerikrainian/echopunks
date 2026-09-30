# Echopunks — Accessibility Mod for EXAPUNKS

Screen-reader accessibility mod for blind players. Speaks screens today; grows into a
model-driven reader of the desktop, EXA code editor, and simulation via Prism. Sibling
to WrathAccess (`../wotr-access`) and SayTheSpire — reuse those patterns where they fit.

## Game facts
- **Engine**: NOT Unity (despite shipping SDL2). `EXAPUNKS.exe` (internal name
  `Burbank`) is one self-contained **.NET Framework 4.x, x64** assembly running
  Zachtronics' own SDL2 + Direct3D11 engine via P/Invoke. No BepInEx/Doorstop/UMM
  machinery applies — plain Harmony on a plain managed exe.
- **Install**: `C:\Program Files (x86)\Steam\steamapps\common\EXAPUNKS`
- **Steam**: appid **716490**; the Steam client must be running. `GameLogic` init calls
  `SteamAPI.RestartAppIfNecessary`, so `steam_appid.txt` (716490) sits in the game
  folder (build writes it; Bootstrap re-writes as fallback). Without it a non-Steam
  launch restarts through Steam — cosmetic double launch; the relaunch still loads us.
- **Injection**: `deploy/EXAPUNKS.exe.config` next to the game names
  `Echopunks.Bootstrap` as the process **AppDomainManager** → the CLR instantiates the
  mod inside the **stock exe** before any game code (even static ctors) runs. No game
  file is modified ("verify integrity" ignores extra files); deleting the config =
  vanilla. There is no loader exe anymore; `Echopunks.dll` is the whole mod.
- **Obfuscation**: Eazfuscator.NET. The shipping exe keeps real TYPE names
  (`GameLogic`, `IScreen`, `DesktopScreen`, `Sim`, …) but MEMBERS are `#=q…` gibberish.
  See "Analysis workspace" for how we target them anyway.
- **Boot click-gate**: the loading screen blocks until a mouse click (SDL event type
  1025, `MOUSEBUTTONDOWN`) before init returns and the first screen appears — game
  behavior; nothing ticks until then (our tick prefix included). THE MOD FIXES THIS:
  `module/src/Patches/SplashPatches.cs` announces a localized "Press any key to
  continue" when the gate opens and advances it on any key press. Mechanics (from the
  decompile): the gate waits on LOCALS (nothing to poke) but accepts any type-1025 SDL
  event; its event loop DISCARDS key events, so `SDL_GetKeyboardState` is the only key
  sensor there; we postfix the per-frame splash draw (the unique `void(float,bool)` on
  `LoadingScreenRenderer`), edge-detect keys against a baseline, and `SDL_PushEvent` a
  synthetic 1025 — the game then runs its own click path (music, sfx, fade) identically
  to a real mouse click. Armed by `LoadingScreenRenderer` ordinal-2 postfix, disarmed by
  `GameLogic` ordinal-24 prefix, so it can never fire during gameplay.
- **Harmony**: vendored **2.4.2** net48 (`third_party/harmony/`) — the game ships no
  Harmony of its own (unlike WotR, where the game's 2.0.4 pins the API).
- **Target framework**: `net48`, `PlatformTarget=x64` (must match the game exactly).
  Needs the .NET Framework 4.8 targeting pack to build.

## Analysis workspace: `game/` (gitignored — NEVER commit or ship any of it)
- `game/EXAPUNKS.orig.exe` — pristine copy of the shipping exe.
- `game/EXAPUNKS-deob.exe` — de4dot output: readable member names (`method_8`,
  `class5_0`), decrypted strings.
- `game/decompiled/` — `ilspycmd -p` dump of the deob exe (~480 files). Read this.
- `game/Steamworks.NET.dll`, `Ionic.Zip.Reduced.dll` — game deps copied so tools resolve.
- **The fact that makes targeting work**: de4dot does NOT preserve metadata tokens, but
  it DOES preserve each type's member ORDER. Its `method_N` is exactly the N-th method
  in MetadataToken order, and the same ordinal selects the same member on the shipping
  exe. So `GameState` resolves **types by real name, members by token-order ordinal**,
  cross-checked by signature (fields pinned by unique type first, ordinal as fallback)
  so a game update fails loudly instead of patching the wrong member.
- Known ordinals (`GameLogic`): method[8]=init, method[25]=per-frame tick,
  field[0]=static self-reference, field[21]=`Class5<IScreen>` screen stack (top = last
  element; its single `List<>` field is matched by type, not name).
- **Re-analyzing** (after a game update — none in years): `tools\prepare-game.ps1
  -Force -Decompile` regenerates orig + deob + `game/decompiled/` (de4dot is vendored
  and pinned; ilspycmd via `dotnet tool install -g ilspycmd`). Then re-verify every
  ordinal and the NameMap anchors (the module build fails loudly if alignment broke).
- **Translating an obfuscated `#=q…` TYPE to its decompiled name** (types work like
  members: row ORDER is preserved): get the live index via `/eval`
  (`asm.ManifestModule.GetTypes().OrderBy(t => t.MetadataToken)`, IndexOf), then
  `dotnet run --project tools/TypeMap -- game/EXAPUNKS-deob.exe` prints deob TypeDef
  rows as `index<TAB>name`. Calibrate the offset with name-preserving anchors
  (GetTypes omits `<Module>` and de4dot strips obfuscator types): THIS build,
  deob row = live index − 14 (GameLogic 1205→1191; verified DesktopScreen 1148→1134,
  LocString 1296→1282). NEVER reflection-load the deob exe (initializers stack-overflow).
  (For code the full deob↔shipping map exists anyway — see "Typed game access" below;
  TypeMap remains the quick one-off lookup tool.)

## Typed game access (the remap pipeline)
The MODULE compiles directly against `game/EXAPUNKS-deob.exe` (readable de4dot names;
`Private=false`, never shipped — and a hard BUILD PREREQUISITE: no deob exe, no module
build). Both binaries carry assembly name "Burbank", so references bind to the loaded
shipping game at runtime once names are fixed up:

- `tools/NameMap` (runs from the module build, incremental) aligns orig vs deob —
  two-pointer walk over TypeDef rows, junk types skipped by shape, per-type members
  zipped with the same skip logic — and emits `namemap.tsv` (deob → shipping renames;
  ~10.5k entries). It FAILS the build unless every real-name anchor pairs exactly and
  the skip count matches; a game update dies here loudly instead of misbinding.
- The host's `Modularity/GameRefRemapper` (Mono.Cecil) rewrites the module BYTES at
  load (the same seam hot reload uses): member refs first via a METHOD-BODY OPERAND
  walk — Cecil materializes a fresh reference per call site for members on
  generic-instance parents (`GClass52<bool>::method_0`), so renaming the
  GetMemberReferences() table view silently misses those — then type refs. Missing
  namemap.tsv = module load fails loudly.
- So module source reads like WrathAccess: `GameLogic.gameLogic_0.method_12(screen)`,
  `S.gclass52_5.method_2(v)`, `new GClass21(cell)` — compiler-checked, IntelliSense'd.
- The two escape hatches: PRIVATE members can't compile — resolve by deob name via
  `Game/Deobf` (reads the same namemap; works on RENAMED types too — the map's T rows
  translate the runtime shipping type name back to the deob name the M/F rows key by —
  and on NESTED types: the map nests Cecil-style `Outer/Inner`, runtime FullName uses
  `+`; Deobf normalizes both directions — fixed 2026-08-22, first hit by
  SpecialPuzzleLogics.HighwaySign; RENAMED nested types never match on the full path —
  the T rows carry only the BARE inner shipping name — so the last segment translates
  alone, verified against the DeclaringType chain so a reused obfuscated name fails
  closed — fixed 2026-08-29, first hit by SpecialPuzzleLogics.GClass313);
  Harmony targets for PUBLIC methods via `Game/Expr.MethodOf(() => …)` so the ldtoken
  remaps (STRING-based reflection with deob names does NOT remap — never use it).
- **Title screen decoded** (deob `GClass368`, live type index 1055): the room scene.
  Per-frame `imethod_1(float)` draws save-progress overlays (gated by
  `saveData_0.method_13("ghast-1", 0)`-style story keys) and THREE mouse hotspots via
  its `method_0` helper (hover fade + two LocString labels, click = act):
  Sawayama Z7 computer → `GameLogic.method_12(new DesktopScreen())`;
  TEC Constellation II tablet → `new ControlPanelScreen()` — ALSO opened by the
  game's own Escape key handling; TRASH WORLD NEWS (only once "ghast-1" is done) →
  `new GClass214(0)`. So: `GameLogic.method_12(IScreen)` = screen PUSH,
  `GameLogic.method_6()` = active screen. The game's own localization is
  `GClass7.smethod_5(key, …) → LocString` — labels are localized game-side.
- **Desktop decoded** (`DesktopScreen`, name-preserved; `game/decompiled/DesktopScreen.cs`):
  a FIXED layout (no window system, nothing draggable) drawn immediate-mode — AXIOM
  organizer (task list + detail pane + typed action button), CHATSUBO (chat log + the
  10-character user roster; Chat/Tasks tabs appear after story flag "ember-7"),
  right-edge program launchers (solitaire/sandbox/arcade/custom, unlock-gated) + close.
  Campaign data: `GClass61.gclass290_0` (main) / `gclass290_1` (side jobs), each
  `.list_0` of `CampaignItem` — ALL PUBLIC: locString_0/1/2 = title/date/description,
  string_0 = id, genum20_0 = type (0 puzzle→EditorScreen, 1 cutscene, 3 solitaire,
  5 arcade, 6 custom), maybe_0 = hostname/file, method_0() = completed,
  method_6() = the action-button LocString ("PLAY CUTSCENE", …). Selection is GLOBAL:
  `GameLogic.campaignItem_0`. Desktop privates (via `Deobf`): method_9(item) = select
  (the game's own arrow/click path), method_11() = open selected (the double-click
  path), list_0 = chat log (`List<GClass24>`), bool_2 = Chat/Tasks flag, tuple_0 =
  roster. Task reveal/complete markers accumulate in `GameLogic.hashSet_0`
  (GClass37 = task appeared, GClass38 = completed). Chat display names:
  `Vignette.dictionary_0`; text through `Vignette.smethod_0` (profanity mask) + the
  `<INSERT_RUN_TO_INSTRUCTION_KEY_COMBO>` platform substitution. THE GAME READS THE
  KEYBOARD HERE (arrows/Enter/Home/End/PgUp/PgDn drive task selection) through its
  input facade `GClass64` — pure per-frame snapshot reads, safe to prefix-skip:
  smethod_17(key) = just-pressed (Enter/Escape/Home/End; smethod_15/16 compose over
  it), smethod_22(key) = pressed-with-repeat (arrows + numpad aliases),
  smethod_23(GEnum1 0–3) = left/right/up/down actions over smethod_22.

- **EXA editor decoded** (`EditorScreen`, ~3700 lines; deep notes re-derivable from the
  decompile — the load-bearing facts): ONE window model `dictionary_1 =
  Dictionary<EntityID, EditorWindow>`; `EntityID` (Exa/MovableFile/ImmovableFile) is
  the ONLY identity that survives — while editing, the whole `Sim` (every
  SimExa/SimFile) is REBUILT EVERY FRAME (`method_25`). Code: one flat '\n' string per
  EXA (`SolutionExa.string_1`, max 1000 lines × 24 cols — enforced by SILENT ROLLBACK
  after the edit), caret = char offsets in `CodeEditorWidget` (`int_0` anchor/`int_1`
  caret; line/col derived), typed chars UPPERCASED + font-filtered, undo = Solution-
  level snapshots. The game's one focus notion: `EditorScreen.maybe_3 =
  Maybe<(EntityID, name|code)>`; focused-window walk = Ctrl+Up/Down (EXAs only).
  NATIVE KEYS: full text editing (arrows/Home/End/PgUp/Dn/clipboard/Ctrl+A, Shift
  select), Tab=step (hold=repeat), F3=pause, F4=run, F5=fast, Esc=reset-or-leave,
  F1(HELD)=show goal (alternate render from `sim.list_5` DiffReportEntry), Ctrl+Enter=
  new EXA, Ctrl+O=solution browser, Ctrl+Z/Y undo, Ctrl+Shift+F11=dev skip-win.
  MOUSE-ONLY: window collapse/expand/pop-out/delete, M-bus Local/Global, test-run
  arrows + field, Create New EXA button (alias exists), scrollbar, map inspection,
  error-hover tooltips. Sim state: SimExa `int_0` current instr (indexes the MACRO-
  EXPANDED pass `gclass286_1`; map back via `GClass286.dictionary_0`), X=`exaValue_0`
  T=`exaValue_1`, F=held `maybe_3`+cursor `int_1`, M=`maybe_4`+`mbusMode_0`; errors:
  `bool_0`+`string_1` (localized, Class37) live ~ONE CYCLE before the sim deletes the
  EXA. Hosts `sim.list_0`, entities `list_1`, goals `list_2` (label `imethod_0()` +
  GClass234 state), cycles `method_52()`, activity `method_53()`; completion pushes
  PuzzleCompletionScreen. Nearly EVERYTHING user-facing is REAL DRAWN TEXT (task
  locString, goal rows, registers, host names, file plates, error text, tooltips —
  the game even ships tooltip LocStrings for the art-only sim buttons); art is just
  chrome/sprites/buttons. Files: `SimFile.list_0 = List<ExaValue>` (int-or-string).

## Build & deploy
```
dotnet build
```
Building the solution (repo root) builds host + module + tests. BUILD PREREQUISITE:
`game/EXAPUNKS-deob.exe` (the module compiles against the deob names and the build
generates `namemap.tsv` — see "Typed game access"). Fresh-machine setup is ONE command
with the game installed: `tools\prepare-game.ps1` — it copies the orig exe from the
Steam dir, extracts the VENDORED de4dot (third_party/de4dot, version-pinned
3.1.41592.3405, SHA-checked; the pin matters — de4dot's generated names ARE the
module's source identifiers, and this version is verified to reproduce a byte-identical
namemap), and runs it. `-Force` refreshes after a game update; `-Decompile` also
rebuilds `game/decompiled/` (needs ilspycmd). The build itself auto-copies the orig exe
when missing; only the deob half needs the script. A machine without a game install
still cannot build the module.
A Debug build deploys into the game folder: `Echopunks.dll`, `Echopunks.Module.dll`,
`Mono.Cecil.dll` (the remapper), `0Harmony.dll`, `prism.dll` (native screen-reader
bridge), `EXAPUNKS.exe.config`, `Mono.CSharp.dll` (dev REPL), `Echopunks\namemap.tsv`
+ `Echopunks\locale\` + `Echopunks\docs\` (the zine text documents from repo `docs/game/`),
writes `steam_appid.txt`, and deletes any stale pre-DLL `Echopunks.exe`.
The HOST dll copy needs the game closed (file-locked; the deploy warns and continues);
the MODULE dll deploys fine with the game running — that's the hot-reload loop.
`dotnet build -c Release` compiles without deploying and contains zero dev tooling.
Override the install path with `-p:GameDir="…"`.

Tests: `dotnet test` from the repo root (`Echopunks.sln` = mod + `tests/Echopunks.Tests`, xunit on
net48, InternalsVisibleTo). Game-independent logic (resolution, text mapping, loc, UI graph core)
belongs there — grow the suite with each subsystem.

User install (the future installer) = copy 7 files into the game folder —
`EXAPUNKS.exe.config`, `Echopunks.dll`, `Echopunks.Module.dll`, `Mono.Cecil.dll`,
`0Harmony.dll`, `prism.dll`, `steam_appid.txt` — plus the `Echopunks\` folder
(`namemap.tsv` + `locale\` + `docs\`). Uninstall = delete the config. (namemap.tsv ships name
pairs only — the same information our ordinals always encoded; no game code ships.) The config binds the
host assembly by **full display name**, so the host's `AssemblyVersion` is pinned at
**1.0.0.0** in its csproj — bump both in lockstep or the mod silently stops loading
(release versioning goes in FileVersion instead).
RENAMED 2026-09-26: ExaAccess → Echopunks (repo amerikrainian/echopunks; GitHub redirects the old
URL). Everything carries the new name — assemblies, namespaces, the `Echopunks\` game-folder dir,
`%LOCALAPPDATA%\Echopunks`, the `ECHOPUNKS_*` env vars, zip/installer names. Migration seams, all
keyed to the literal legacy name: the host csproj deploy deletes ExaAccess.exe/.dll/.Module.dll +
the `ExaAccess\` dir; HostConfig copies a legacy settings.json once. The INSTALLER has no legacy
handling (user decision, 2026-09-26): an ExaAccess install is uninstalled with its own installer
first; pre-rename installers cannot find the new zip name.
RELEASE PIPELINE (ported 2026-08-23 from the Harkest Dungeon tooling, itself adapted from
Rashad Naqeeb's Non-Visual Calculus installer, MIT — attribution headers on every file,
license text in `installer/LICENSE-NonVisualCalculus.txt`): `Directory.Build.props` holds
the release `Version` (FileVersion/InformationalVersion of every assembly; the host's
AssemblyVersion stays pinned separately). `build.ps1` = dev Debug build + deploy with the
Steam install located (EXAPUNKS_DIR overrides). `build_release.ps1` = Release build →
`releases\Echopunks-vX.Y.Z.zip`, zip root = game folder, exactly the file set above (it
refuses to ship Mono.CSharp.dll). `installer\` = the Rust + wxWidgets installer
(`EchopunksInstaller.exe` via `build-installer.ps1`; `test-installer.ps1` = cargo test;
`tools\installer-toolchain.ps1` probes libclang/ninja for both): finds the game (EXAPUNKS_DIR,
Steam registry + library folders; a dir is the game when `EXAPUNKS.exe` AND
`Renderer_D3D11.dll` are present), reads the GitHub releases feed
(amerikrainian/echopunks; `ECHOPUNKS_INSTALLER_RELEASES_URL` overrides it for tests),
downloads the `Echopunks-v<semver>.zip` asset, verifies its sha256 digest, extracts it over
the game folder recording every file in `Echopunks\install.json` and backing up anything it
overwrote under `Echopunks\backups\`, prunes files a newer zip no longer ships, and
uninstalls by the record (restoring backups). `installer\examples\cli.rs` is the same CLI
without the requireAdministrator manifest — `tools\installer-e2e.ps1` drives it against a
throwaway game folder and a locally served release feed (install → assert the file set and
the backup → uninstall → assert the folder is pristine). `create-release.ps1 vX.Y.Z` = gh
release with the zip + installer and the CHANGELOG.md section as notes.

## Logs
`%LOCALAPPDATA%\Echopunks\echopunks.log` — fresh file per launch; the one path to give
testers. The stock exe is a GUI app, so console output goes nowhere — the file is the
only log surface.

## Dev loop (DEBUG builds)
Set `ECHOPUNKS_DEV=1` (or drop a `devserver.enable` marker file in the game folder) and
launch `EXAPUNKS.exe`. A loopback HTTP server comes up on `127.0.0.1:8772`
(`ECHOPUNKS_DEV_PORT` overrides; WotR uses 8771 — keep them distinct):

| Route | Purpose |
|---|---|
| `GET /health` | liveness |
| `POST /say` | speak the body through the real speech path |
| `GET /speech?since=N` | read back what was spoken (we can't hear TTS) |
| `GET /screen` | active screen + full stack, by type name |
| `GET /gui` | reflection dump of the active screen model — fields in TOKEN ORDER as `f[N]` (lines up with de4dot names/ordinals), collections expanded, depth-capped |
| `GET /screenshot` | capture the game window to `%LOCALAPPDATA%\Echopunks\screenshots\*.png`, path returned. In-process PrintWindow(PW_RENDERFULLCONTENT): NO focus change, works occluded, full render resolution. NEVER SetForegroundWindow for captures |
| `POST /eval` | C# against the live game, main thread, persistent REPL state (C# 6 — no pattern matching) |

`/eval` and `/screen` run on the game's main thread (queued, pumped from the tick
prefix); `/say` and `/speech` answer directly off the HTTP thread. Reach live game
state from `/eval` via `Echopunks.GameState`.

**Click-gate in automation**: nothing ticks (and `/screen`/`/eval` time out) until the
gate gets a click. Post one: find the EXAPUNKS window HWND, `PostMessage`
`WM_LBUTTONDOWN`(0x201) + `WM_LBUTTONUP`(0x202) with REAL client coordinates packed in
lParam (e.g. `(100 << 16) | 100`) — lParam 0, i.e. (0,0), does NOT release the gate.
(With the any-key splash advance in place, posting a `WM_KEYDOWN` with a real scancode
in lParam is the simpler route.)

**Synthetic keyboard caveat**: posted `WM_KEYDOWN` for SHIFT does not reach SDL's
modifier state (SDL normalizes shift against the thread keyboard state, which
PostMessage doesn't update) — so scripted Shift+chords (Shift+Tab) silently act
unshifted. Real keyboards are unaffected; test chorded bindings by hand.
Two more, learned the hard way: (1) keys posted while the game window is UNFOCUSED
drop intermittently (SDL discards key events without window focus — the terminal
running the script usually holds it), so `SetForegroundWindow` the game window before
posting key SEQUENCES (screenshots must still never do this — `/screenshot` works
occluded by design); (2) arrow keys need the EXTENDED-KEY bit in lParam
(`1 << 24`, e.g. Down = `(1) | (0x50 << 16) | (1 << 24)`) or SDL maps them to numpad
scancodes.

## Architecture (current): permanent HOST + reloadable MODULE
Two assemblies (pattern ported from NonVisualCalculus). The HOST (`Echopunks.dll`,
root `src/`) is the config-bound permanent half; the MODULE (`Echopunks.Module.dll`,
`module/src/`) holds every feature and hot-reloads (see "Hot reload" below).

Host:
- `src/Bootstrap.cs` — THE entry point: `AppDomainManager` the CLR instantiates inside
  the stock exe. Pins cwd, writes `steam_appid.txt`, boots speech, binds `GameState`,
  attaches the host Harmony patches, loads the module, starts the dev server, hooks
  `ProcessExit`. Owns `TickFrame` (dev pump → `Module.Tick`, read fresh each frame) and
  `ReloadModule`. Swallows every exception — see Hard rules.
- `src/GameState.cs` + `src/MemberResolver.cs` — reflection-cached view of the live
  game; the ordinal/signature resolution primitive (unit-tested).
- `src/Patches/GameLogicPatches.cs` — the two host hooks: init postfix (sets
  GameInitialized) and tick prefix (calls `Bootstrap.TickFrame`). Applied ONCE, never
  unpatched — they are the module's heartbeat. Attached manually via reflected
  `MethodInfo`s (no `typeof(GameLogic)` at compile time).
- `src/Modularity/` — `IModModule`/`ModHost` contract + `ModuleLoader`
  (byte-load, load-then-swap; the module dll is never file-locked).
- `src/Speech/` — the WrathAccess handler stack, host-side because engines hold
  native/OS resources that must survive module reloads: `Tts.cs` (the facade/chokepoint),
  `SpeechManager` (priority chain Prism → SAPI → clipboard; lazy Detect/Load; auto
  fallback — never strand a blind user voiceless), `PrismHandler`+`PrismNative`,
  `SapiHandler` (in-box System.Speech — replaces WotR's 500 lines of manual COM),
  `ClipboardHandler` (raw user32). Output choice: `speech.output` in
  `%LOCALAPPDATA%\Echopunks\settings.json` (flat dotted-key JSON, `src/HostConfig.cs`)
  or the `ECHOPUNKS_SPEECH` env var for dev runs; default auto.
- `src/Dev/` — DEBUG-only dev server (+ `/reload`); `src/Log.cs` — file logger.

Module (each reload starts this half cold — statics are per-load):
- `module/src/EchopunksModule.cs` — `IModModule` implementation, module composition
  root: loc first, then patches/hooks/FrameLoop steps; speaks "Echopunks ready." once
  at boot (generation 1 only — the splash prompt is the next thing the user hears).
  TWO-PHASE PATCH ARMING (hard-won, 2026-08-23): only SplashPatches applies in Load;
  every other Harmony patch arms on the FIRST TICK (ticks start only after GameLogic
  init returns). Harmony's detour JIT-prepares the patched method, WHICH RUNS ITS
  DECLARING TYPE'S STATIC CONSTRUCTOR — at Load that's pre-init, and EditorScreen's
  cctor builds 16 tooltip LocStrings via GClass7.smethod_5, whose backing dictionary
  only exists once init loads Content\strings.csv. The NRE is swallowed inside
  MonoMod, the CLR CACHES the cctor failure for the process lifetime, and the game
  then crashes ("type initializer for 'EditorScreen'") the first time ANY puzzle
  opens — on COLD BOOTS only, which is why a whole day of hot-reload dev sessions
  (reloads arm post-init) never saw it. Audited 2026-08-23: of all load-patched
  types only EditorScreen has a loc-reading cctor — but never move patch arming
  back into Load.
- `module/src/Localization/` — the WrathAccess loc layer: `Loc.T`, lazy `Message` with
  `{var}` substitution, `LocalizationManager` (enGB fallback manifest + per-frame
  language poll via the pluggable `LanguageSource`; game-language mapping is future
  work). Tables load from `<game>\Echopunks\locale\<lang>\<table>.json` — rooted off
  the HOST dll (the module is byte-loaded, it has no disk location). Module-side so a
  hot reload re-reads the JSON: edit a string, build/copy, /reload, hear it.
- `module/src/Update/` — the LAUNCH UPDATE CHECK, ported from Guildrun Access: `UpdateCheck`
  (pure, unit-tested: tag parsing, strict numeric IsNewer, plus CleanLocal — the SDK stamps
  "+{commit}" onto InformationalVersion and a suffixed last component would parse as zero)
  and `UpdateChecker` (one thread-pool request per GAME LAUNCH — generation 1 only — to
  GitHub's releases/latest; HttpWebRequest, and TLS 1.2 switched on explicitly because the
  process is the game's exe and its target framework picks the default protocols). Only a
  strictly newer release speaks ("Echopunks update {version} available.", from Tick — so
  always after the boot prompt); up to date / dev build ahead / offline / rate-limited /
  no release published (404) are log lines only. `ECHOPUNKS_UPDATE_URL` overrides the feed
  for end-to-end checks (verified against a real GitHub repo's payload, 2026-09-20).
- `module/src/FrameLoop.cs` — ordered, defensive per-frame step registry (steps:
  keyboard snapshot → input → loc poll → screens). `FrameClock.cs` — Stopwatch clock.
- `module/src/UI/` — `ScreenNames` (locale-backed labels + obfuscation filter),
  `ControlTypes` (the role-word/type registry), and the NAVIGATOR layer ported from
  WrathAccess: `Navigator` (contract) / `Navigation` (static facade) /
  `GraphNavigator` (pull-based announce differ over the graph core: per-frame
  EnsureFocus, live-part watch, the ui.* input vocabulary; type-ahead and sound cues
  deliberately deferred). TRAP: SCREEN-SCOPED action ids (ui.step, ui.runto,
  ui.followNext, …) are dispatched through an explicit whitelist switch in
  OnInputJustPressed — a new action id must be added there or its binding
  matches and then goes nowhere (bit ui.followNext, 2026-08-23). GLOBAL toggles (ui.echo =
F6, `TypingEcho` — silences TYPED characters only, deletions always speak; persisted as
`speech.typingEcho` via `HostConfig.Set`, which rewrites settings.json whole and refuses to
overwrite a file that failed to parse) are handled in
`Navigation.DispatchJustPressed` AHEAD of the navigator, so a caret-owning text node's
key bubbling can't swallow them. SELECTION-FOLLOWS-FOCUS: a node with `NodeVtable.OnSelect`
  runs it on every DIRECTIONAL landing (arrows/Home/End/region jumps — never Tab-stop
  cycling), before the announce; tabs and list rows select as you scroll, no Enter
  needed (Enter/OnActivate stays separate — task rows open on it). Such controls
  declare `NodeVtable.Selected` (engine-only — drives stop/entry landings) INSTEAD of
  a spoken Selected part: selection that always follows focus is never announced
  (user rules, 2026-08-22); radio options keep speaking theirs. TEXT ENTRY
  (`NodeVtable.TextEntry` + `TextValue`): a typing-first field — focus arriving ANY
  way (arrows, Tab-stop landing, programmatic) runs OnSelect to arm the game's field,
  printable keys flow via SDL TEXTINPUT (a channel suppression never touches), the
  navigator stands its Space/Backspace bindings down and passes keycode 8 through the
  suppression seam, and typed/DELETED characters echo bare — deletions speak just the
  character, single capitals speak "Cap X" (user rules, 2026-08-22), suppressible per
  node via `TextEchoCaps` for widgets that uppercase every insert. `FocusMode` —
  plain flag, ON by default; its game-side half is `Patches/GameKeySuppression.cs`.
- `module/src/Screens/` — `Screen` base + `ScreenManager` (WrathAccess poll-and-diff
  lifecycle; resolution = registered screens polling the GAME's screen stack via
  GameState). Unmodeled game screens still announce by friendly name (the retired
  ScreenAnnouncer's behavior, now the manager's fallback — obfuscated names logged,
  never spoken). `TitleScreen` — the first modeled screen: resolves its obfuscated
  game type BY SHAPE (the unique IScreen carrying bool(float,Texture,Vector2,
  LocString,LocString,*,Vector2,Bounds2)), two buttons (computer → DesktopScreen,
  tablet → ControlPanelScreen) activated via `Game/GameApi.PushScreen` —
  `GameLogic.method_12(IScreen)` (ordinal 12, signature cross-checked, unique-scan
  fallback), byte-identical to the game's own hotspot click.
- `module/src/UI/Graph/` — the WrathAccess KEY-GRAPH CORE, ported verbatim (BCL-pure by
  design — keep it that way; its 900+ lines of tests came over verbatim too): ControlId
  two-tier identity, GraphTypes (nodes/vtables/stops/regions/parent chains), KeyGraph
  (moves, Tab-stops, regions, trees, focus reconcile-on-rebuild), GraphBuilder
  (menu/raw modes, groups, position stamping), GraphAnnouncer (path-diff speech;
  wording hooks localized in module Load). Immediate-mode: screens re-declare controls
  each render; focus persists by ControlId.
  TRAP (the ПАСЬЯНС double-focus, 2026-08-23): focus recovery on rebuild follows a
  Referenced id's OBJECT to the first node referencing it, so two nodes sharing a backing
  object (a campaign item listed as a task AND as a launcher) snapped focus to the earlier
  one every frame. Reconcile now keeps the exact surviving node first (tier 0); still, give
  a node a Structural id unless it IS the object's one representation.
- `module/src/Input/` — WrathAccess input substrate over SDL: `SdlKeyboard` (per-frame
  snapshot of `SDL_GetKeyboardState` — no obfuscated members needed, independent of
  what the game consumes), `Scancode`, `SdlKeyboardBinding` (exact-match modifiers),
  `InputAction`/`InputBinding`/`InputCategory`/`InputManager` (category priority +
  chord shadowing + OS typematic repeat via `OsKeyboard`). The navigator/screen-stack
  hooks are pluggable funcs (`UiDispatcher`, `ActiveCategoriesProvider`,
  `SuppressPoll`) — wired when GraphNavigator/ScreenManager land with the first screen.
- `module/src/Game/SdlNative.cs` — P/Invoke over the game's own SDL2.dll: keyboard
  state reads, `SDL_GetKeyName`, + `SDL_PushEvent` (56-byte union — Size=64 on
  purpose): the splash's synthetic mouse click and `PushKey` (synthetic keyboard
  events). TWO PushKey traps, both hit and diagnosed live: the game's event loop
  ROUTES key events by the event's WINDOW ID through its private window map
  (GameLogic dictionary_0 — wrong/zero id = silently dropped; read the real id via
  Deobf, it is 1 in practice), and a key-DOWN and its key-UP processed in the same
  event pump cancel before the game's per-frame poll — hold the up back two frames.
- `module/src/Game/` — the thin typed game boundary (see "Typed game access"):
  `GameText` (the game's own localized strings — see Hard rules), `GameApi`
  (push/pop/quit wrappers over `GameLogic.method_12/15/32`), `Deobf` (private-member
  lookup via the namemap) + `Expr.MethodOf` (remappable Harmony targets), `SdlNative`
  (our own SDL P/Invokes for the splash/keyboard). Everything else touches the game
  directly in typed code at the call sites — the WrathAccess shape.
- `module/src/Screens/ControlPanelScreens.cs` — the control panel accessified: ONE game
  screen modeled as THREE mod screens keyed to its private page field (home / Options /
  Controls — page flips announce as screen changes, each keeps focus memory), tabs as a
  horizontal Tab-node row, labeled radio rows, volume sliders (±5%, percent feedback),
  the Redshift key bindings (activation defers the game's raw capture screen until all
  keys are up — it binds the first HELD key, which would otherwise be our Enter), the
  keyboard-reference table, and `GameKeyCaptureScreen` (CapturesRawInput, layer 10)
  over the game's capture overlay. Native mouse + Escape handling untouched throughout.
- `module/src/Screens/DesktopScreens.cs` — the desktop hub (`DesktopHubScreen`, see
  "Desktop decoded"): four Tab stops — organizer task list (rows ENUMERATED live from
  the campaign lists, the first place the WotR items-from-collections pattern applies;
  selection-follows-focus via OnSelect = the game's method_9, Enter opens via
  method_11), detail pane (title/date/description/location-or-hostname + the game's
  own action-button label), CHATSUBO (log + user roster as Ctrl+arrow regions; the
  Chat/Tasks tabs once unlocked), program launchers + close. KeepStateOnPop (desktop
  survives beneath cutscene/editor pushes). OnUpdate announces NEW chat lines and
  task appeared/completed events, watermarked PER GAME-DESKTOP INSTANCE — priming on
  focus would swallow the queued lines the game flushes on re-expose. SIDE JOBS
  live-verified 2026-09-20 WITHOUT an ember-7 save: the game's in-memory show-all dev flag
  (GClass1.bool_5 — probe `devflag on|off`, never saved) reveals the Chat/Tasks tabs and
  every row. The Tasks tab draws each job as POSTER HANDLE + title + a check that follows
  the desktop's completion MARKER (private method_7), so side rows speak "handle, title"
  and read the marker; side jobs ship no EMBER vignettes (Enter goes straight to the
  editor). The details stop also carries the EMBER-2 panel's mouse-only INTRO / OUTRO
  replay buttons (Ember2CutsceneScreen.smethod_0's gates: present when the task has the
  vignette, "unavailable" until seen). Unrevealed side jobs are drawn as dim EMPTY SLOTS,
  so the list ends with one text row "{n} more, not yet available" (user decision,
  2026-09-20; checked against the real 4-revealed / 5-hidden state via probe `markseen
  ember-7`). The side-job COMPLETION flow is verified too (probe `devwin` = exactly what
  the game's Ctrl+Shift+F11 dev skip pushes): completion screen incl. its leaderboards,
  Return to Desktop, then "Task complete: …" and the row's check. Returning lands focus on
  the Tasks tab (a fresh DesktopScreen instance starts on Chat; the tab re-selects on
  landing) — the list is one Down away. Deferred: leaderboards/histograms, the multiplayer
  opponent table, the custom-win hold-button. HACK*MATCH (campaign type 5, "arcade-win")
  is DELIBERATELY NOT accessified (user decision, 2026-09-26: a real-time falling-block
  game): the shared Open path speaks desktop.arcade.inaccessible for the task row, the
  details button and the launcher; mouse paths untouched. Trap: no `const` of a game enum
  type in module code — Cecil's writer resolves the constant's assembly and the reload dies.
- `module/src/Screens/CutsceneScreens.cs` — `CutsceneScreen` over the visual-novel
  cutscene player (deob GClass255, obfuscated live — the nivas/ghast/isadora scenes;
  FULLY LINEAR: vignette script + current-line int, no choices): ANNOUNCE-ONLY with
  CapturesRawInput — the game's own Space/Tab/Enter/click advance and Escape skip stay
  live; OnUpdate watches the line index (privates via Deobf's T-row bridge) and speaks
  each line in full as it appears, speaker-prefixed per the drawn name plate (Moss =
  the player's plate-less narration, spoken bare; non-Moss lines are VOICE-ACTED and
  TTS currently reads them anyway, subtitle-style). ONE PRESS = THE NEXT LINE in both
  players (`Patches/CutsceneAdvance.cs`, 2026-09-30): the players pace for sighted reading
  (first press completes the typewriter, 0.25s cooldown, input dead during door animations,
  slide-ins, pre-delays, EMBER's 5s blackout, screen transitions), so a prefix on each
  imethod_1 watches the game's own advance predicates, collapses the blocking timers and
  FORCES the advance key for that frame via GameKeySuppression.ForcedKey (cleared by a
  finalizer) — the game's own advance path runs. A press the scene can't take yet
  (transition; blackout's last frame, which switches the music) is held ≤~2s. EMBER's single
  scripted reply accepts only click/digit 1, so the press forces 1 there; a real choice
  drops it (the menu owns Enter). Lines after a scene's first speak INTERRUPTING (each
  arrived on a press). `EmberCutsceneScreen` over the
  EMBER comic player (Ember2CutsceneScreen, name-preserved) — the game's only DIALOGUE
  CHOICES: each script line carries a LIST of texts (Ember2 lines = response variants
  picked by the player's previous choice int_2; non-Ember multi-text lines = answer
  bubbles, natively choosable by mouse or the undocumented number keys 1-9).
  Announce-only with CapturesRawInput EXCEPT while a choice is pending: then the
  options become a MENU — bare labels "1: text" (no role word, no position counts —
  user rule, 2026-08-22), arrows re-read, Enter picks by pushing the option's digit
  as a synthetic SDL key so the game's own choice path runs byte-identically; native
  digits keep working in parallel. Covers the fullscreen story mode. Plus
  `TrashWorldNewsScreen` — the zine reader (deob GClass214, obfuscated live; ghast-1/2
  cutscenes end by pushing it): issue tabs (unlock flags mirrored), the per-tab buttons,
  close, and the art-only printing instructions. THE ZINES SHIP AS HTML DOCUMENTS
  (repo `docs/game/twn-1.html`, `twn-2.html`, `twn-epilogue.html` + their figures in
  `docs/game/images/` → `<game>\Echopunks\docs\` and `docs\images\`, deployed by the
  module build and staged into the release zip; Markdown until 2026-09-28): each tab's
  DIGITAL VERSION button shell-opens the matching page in the default browser (ids are the
  file names; a missing document falls back to the game's PDF); the PRINTABLE buttons still open
  the game's PDFs, byte-identical to the click (user rule, 2026-09-24).
  MOD NARRATION IN A GAME SCRIPT (`Screens/CutsceneNotes.cs`, user decision 2026-09-26 — the
  first mod-authored prose inside the game): 14 Moss lines (ui.json cutscene.notes.ghast1.*)
  recalling the editor layout in the mod's terms, inserted IN MEMORY into ghast-1's vignette at
  parsed index 7 (after the TRASH WORLD NEWS line, before Ghast resumes) at first tick, removed
  by reference in Dispose. Vignette scripts are on-disk CSVs (`Content\vignettes\<id>.csv`,
  parsed at init into CampaignItem.vignette_0.list_0 — public and mutable). Rules learned: the
  visual-novel player picks voice clips by COUNTING NON-MOSS LINES, so only Moss insertions are
  audio-safe; never index 0 (the first line's door action sets the opening state) and never after
  the game's last line (seen flag + zine-reader push fire on it); LocString.ToString has NO
  language fallback (fill every Language slot). The EMBER comic player is UNUSABLE for narration:
  a Moss line is a pick-me reply bubble (advances only by click or digit) and the previous line is
  drawn in EMBER's speech slot (both verified live). Played through end to end 2026-09-26.
- `module/src/Screens/CreditsScreen.cs` — the game's VICTORY screen: the credits roll (deob
  GClass252, obfuscated live), pushed by Ember2CutsceneScreen when the final story
  epilogue's last line is advanced. ~73s, timed, non-interactive (black screen + music;
  Escape skips only once config `CreditsSeen` is set — the roll sets it itself at the end,
  then pops + transitions out). Announce-only with CapturesRawInput: PanelCapture's CREDITS
  TAP (prefix/finalizer on GClass252.imethod_1) publishes each frame's drawn strings and
  OnUpdate speaks the ARRIVALS (the staggered cast block accumulates on screen); the logo
  card is a sprite, transcribed. Test via the probe: `credits` pushes it, `creditseek <t>`
  sets its clock — POP it before ~73s or the game writes CreditsSeen into the user's config.
- `module/src/Screens/SolitaireGameScreen.cs` — ПАСЬЯНС (game type SolitaireScreen,
  name-preserved; state PUBLIC on GameLogic.solitaireState_0, persists across visits;
  natively 100% mouse — press=grab, release=drop-or-silent-snapback; game reads only
  Escape). 36 cards: suits 0=spades 1=hearts 2=diamonds 3=clubs (0,3 black —
  screenshot-verified), ranks 6-10 number / 11-14 = jack/queen/king/ace. Board = a
  RAW-WIRED grid (GraphBuilder AddNode/Connect — VerticalTarget clamps by index so
  menu rows would drift over variable column heights): left/right = same depth in the
  next column (clamped), up/down = within a column; locked (bool_0) and empty columns
  are single nodes. Moves are a SELECTION model, not a drag: Enter picks (validated by
  the screen's private method_5 via Deobf), Enter on a destination drops (method_6 +
  the public SolitaireItem.method_4 re-parent + the game's own sounds) — ATOMIC, so
  the per-frame lock/win logic never sees a detached card and the mouse still works;
  illegal moves are SPOKEN (the mouse's silent snap-back made audible); Backspace
  cancels (nothing was detached). Watches: deal phases (genum157_0), per-column lock
  flips, the win (which itself calls saveData method_21 = the campaign task). The
  INSTRUCTIONS view (private bool_0) = 4 paragraphs of game text (loc keys ARE the
  literals; '*' bold markup stripped) + RETURN TO GAME; buttons/win-count labels are
  game loc keys ("INSTRUCTIONS"/"NEW GAME"/"WIN COUNT"). Escape stays native (leave /
  close instructions).
- `module/src/Screens/CustomPuzzleScreens.cs` — Axiom VirtualNetwork+ (CustomPuzzleScreen,
  name-preserved; the desktop's fourth launcher): the custom-network manager. Two tabs
  (Remote = subscribed Workshop items, Personal = the player's own scripts — `.js` files
  under `<user data>\custom\`, edited OUTSIDE the game; the game watches the folder),
  date-ordered rows (privates via Deobf: maybe_0 selection, genum145_0 tab, method_0 tab
  click — which also selects the tab's first row — method_1 select, method_3 create,
  method_4..8 = the row menu's Edit/Test/Upload/Copy/Delete, method_9 the close X; the
  puzzle list + metadata come from the INTERNAL static `Puzzles` via Deobf), the
  tab-specific bottom button (Browse Steam Workshop = the Steam overlay / Create New Virtual
  Network), the menu verbs as an actions stop on the SELECTED network (Test dims on a script
  error, Upload until solved — "solved" for a personal network = the save's hash matches the
  CURRENT script, GClass287.method_19), Close, and the detail pane (personal: title /
  subtitle / description or the script error text; remote: the browser-layout histogram
  stops via LeaderboardRows, or the unsolved notice). Row states speak the game's own words
  ("Solved", "Uploaded", "Script error"). Enter = play (the double-click path), Backspace =
  delete (confirm-less like the menu, announced), Escape native. Edit launches the OS file
  browser with zero in-game feedback — the script path is announced. The map PREVIEW image
  is art (the description carries the words). Plus `NetworkMenuScreen` over the mouse-only
  row menu (deob GClass245) and `NetworkUploadScreen` over the Workshop upload dialog (deob
  GClass277: announce-only while uploading; the manager's error text speaks once and gets a
  Close row). Tested 2026-09-06 by driving the graph's own node actions (create → rows /
  actions / details → delete); Upload and the Steam overlay untested (real side effects).
- `module/src/Patches/GameKeySuppression.cs` — the focus-mode key-suppression seam:
  Harmony prefixes on `GClass64.smethod_17/22` (via `Expr.MethodOf`; positional `__0`
  binding — shipping param names are obfuscated) return not-pressed for the navigator's
  keys (arrows + numpad, Enter/KP-Enter, Tab, Space, Backspace, Home/End/PgUp/PgDn)
  while FocusMode is on AND a modeled screen is focused AND it doesn't
  CapturesRawInput. Escape is deliberately NOT swallowed (native back/close paths
  stay). Unmodeled screens see every key — the game stays fully playable.
- `module/src/Screens/WorkhouseScreen.cs` — the Workhouse (deob GClass50, the one-time
  receipt-transcription minigame) — the first TEXT ENTRY screen, typing-first: field
  nodes are TextEntry (landing arms the game's own field via its method_3+method_2
  keyboard path; Enter hops to the next field; the game's append-only GClass60 widget
  needs no arrows, so navigation and typing never collide). Receipt = nine read-only
  rows mirroring the game's grading literal (the same lines the receipt art shows);
  SUBMIT/EXIT (mouse-only in the game) replicate the click paths exactly, including
  submit's normalize-then-Levenshtein grading via the game's own method_4; the
  art-only dialogs (exit confirm / rejected / success) become tiny button graphs with
  mirrored prompts, announced via flag watches; the fake-loading phase announces the
  game's own three loc strings. Falling-edge field release: when OUR focus leaves the
  fields, the game's field unfocuses like a click would (never fights a mouse user).
- `module/src/Patches/SplashPatches.cs` — the any-key splash advance (see "Boot
  click-gate" above).

## Hot reload (DEBUG loop for feature work)
The module is `Assembly.Load(byte[])`'d, so `dotnet build module/Echopunks.Module.csproj`
works with the game RUNNING (the Debug build auto-deploys it), then `POST /reload`
swaps it in — no restart, no click-gate. Load-then-swap: a broken build
leaves the old module running. Old copies leak until exit (dev cost only). The REPL
resets on reload so `/eval` sees the new types. Host/contract changes still need a full
game restart (the host is file-locked and loaded once). Rules for module code are in
`src/Modularity/IModModule.cs` — notably: module Harmony patches use a per-load unique
id + `UnpatchSelf` in Dispose, and native handles live host-side only.
`/eval` gotcha: after a reload, every module generation is still loaded, and a bare
type reference (`Loc.T(...)`) may bind to an OLD generation. `GetAssemblies()...Last()`
is NOT reliable either (with dozens of generations loaded it has picked a stale copy).
Ask the host which module actually runs:
`typeof(Echopunks.Bootstrap).GetField("_moduleLoader", NonPublic|Static).GetValue(null)`,
then `.GetType().GetProperty("Module").GetValue(...)` → `.GetType().Assembly` is the
live generation.
AUDIT PROBE (DEBUG only, `Screens/Editor/ExaEditorScreen.AuditProbe.cs`): one typed
entry point, `ExaEditorScreen.Audit(cmd, arg)`, invoked by reflection on the LIVE
generation — `list` (campaign rows + visibility), `open <row id>` (the desktop's public
opener DesktopScreen.smethod_0 — opens ANY puzzle, locked or not, no save surgery; it
may still create a solution file, so back the save folder up first), `leave`, `model`
(hosts / both-side link ids + plate style / registers / files / goals), `host <n>`,
`dump` (every node of the navigator's current render, fully composed), `act <idpart>`,
`close`, `wins` (the game's window-cache ids), `devflag on|off`, `credits` / `creditseek`,
and for SCRIPTED RUN PASSES `code <program, '|' = newline>` (first EXA), `devwin` / `markseen <id>` (both WRITE THE SAVE — back up, restore), `step` (the mod's
own F2 path — StepSim, narration included; calling the game's advance seam directly
skips the step echo and looks like missing speech), `reset`, `lockcodes` (PB056). The whole post-modem campaign + bonus
sweep (2026-09-20) ran on it.
More `/eval` traps, all hit live:
- While EDITING a puzzle the sim is rebuilt EVERY FRAME — game objects captured by an
  earlier `/eval` are stale by the next call; identity-based reads (home-plate
  substitution, HolderOf) silently miss on them. Fetch sim + object + read in ONE eval.
- A failed compile of a block containing an anonymous type can wedge Mono.CSharp
  ("builder already exists" on every subsequent input) — `/reload` resets the evaluator.
- Several SimExa/SimFile members (method_0 = current host, team_0) are declared on the
  SimEntity BASE — Deobf is DeclaredOnly; probes need a base-type fallback (typed
  module code never hits this).
- Comparing non-ASCII literals ("¶") needs a UTF-8 request body
  (WebClient.UploadData + charset=utf-8) — a default-encoded POST mangles them and the
  compare silently misses.
- READ-ONLY audit tricks: PanelCapture can be armed PASSIVELY for a frame (no goal
  force, no input) to verify a panel's text reaches the tap; any sprite needing
  transcription can be read back off the GPU via Renderer.smethod_9 + a VERTICAL FLIP
  (readbacks come out upside down) — that's how the battle rating badges were done.
- Judge information loss by the MECHANISM, never by the /speech tail — a tail ending
  mid-popup usually means the user closed it there.

## Hard rules
- **Never commit or ship game code.** `game/` (deob exe, decompiled source, copied game
  dlls) is a local analysis workspace only, and stays gitignored. We ship exactly the
  5-file install set above — nothing derived from the game's binaries.
- **Never crash the game.** Every Harmony hook body catches all exceptions; `Bootstrap`
  swallows everything (an exception escaping `InitializeNewDomain` kills the process
  before the game starts); speech is optional. Whatever breaks, the user still gets
  their vanilla game.
- **Read the model, never the pixels** — and route every game read through `GameState`,
  so obfuscation-resolution stays in one file.
- **Game access is TYPED through the remap pipeline** (module side — see "Typed game
  access"): write plain C# against the deob names; never string-based reflection with
  deob names (it does not remap — `Deobf` for privates, `Expr.MethodOf` for patch
  targets). The HOST (which loads before any remapping exists) keeps the original
  discipline for its few members: types by real name, members by token-order ordinal,
  cross-checked by signature — never raw metadata tokens.
- **Never speak `#=q…`** — filter obfuscated names (`GameLogicPatches.IsObfuscated`)
  before anything reaches `Tts`; map screens to friendly labels as they're identified.
- **Localize every string the mod speaks** — `Loc.T(key[, args])` / `Message` + an
  entry in `module/assets/locale/enGB/ui.json` (the complete translation manifest;
  another language = a dropped-in folder under `<game>\Echopunks\locale\`). Named
  `{placeholders}`, not string.Format. Exemptions: HOST emergency strings (spoken when
  the module itself failed to load — localization is module-side) and dev-only tooling.
- **Never duplicate a string the game already has.** Anything the game displays is read
  LIVE via `module/src/Game/GameText.cs` (the game's own loc registry, deob
  `GClass7.smethod_5(key, …) → LocString` — resolved by shape; six shipped languages,
  keys are literally the English text so failed lookups stay readable). `TSpeech`
  massages newlines/backslashes/emphasis markup for TTS — NEVER slashes: the game's
  entire text corpus holds exactly one " / " and it is PB023's DIVISION sign (an old
  separator→comma rule spoke the formula as "(BA + ZA + APB), 3" — PB023 audit,
  2026-08-30). ui.json is ONLY for text the game
  genuinely lacks: role words, prompts for otherwise-silent screens, names for
  unlabeled/art-labeled things.
- **No mod-authored hints. Ever.** If the game doesn't provide hint/description text
  for a control, we don't invent it — sighted players get the art, blind players get
  the same label + role + value and nothing more (user rule, 2026-08-21). The Tooltip
  announcement channel exists only for tooltip text the game itself displays.
- **Speech never interrupts by default** (SayTheSpire house preference), and all
  user-facing output flows through `Tts.Speak` — the single chokepoint (it also feeds
  the dev `/speech` tap).
- **All dev tooling is `#if DEBUG` and loopback-only.** Release builds ship zero dev
  surface (no server, no Mono.CSharp, no speech tap).
- **`AssemblyVersion` stays 1.0.0.0** unless `deploy/EXAPUNKS.exe.config` is updated in
  the same commit.
- **Keep `module/src/UI/Graph` BCL-pure** (no game/SDL/host-dev references) — its test
  suite compiles it standalone in spirit; purity is what made the WotR port free.
- **Never apply a Harmony patch before game init** (SplashPatches is the one vetted
  exception): patching runs the target type's STATIC CONSTRUCTOR, a cctor that touches
  uninitialized game state fails, and the CLR caches that failure until the game
  crashes on first real use of the type. Game patches arm on the module's first tick
  (post-init by construction) — see EchopunksModule and the 2026-08-23 note above.
  Cold-boot test editor-open before shipping any new load-order change: the hot-reload
  dev loop CANNOT catch this class of bug.

## Roadmap
1. **(done)** In-process injection, Harmony on obfuscated members, Prism speech, dev
   server, screen-change announcements.
2. **(done)** Zero-loader: stock exe boots the mod via AppDomainManager config.
3. **(done)** Host/module split with hot reload.
4. **(done)** Localization layer + speech handler chain (Prism → SAPI → clipboard).
5. **(done)** Boot click-gate: localized prompt + any-key advance.
6. **(done)** UI graph core + input substrate ported (with the WotR test suites).
7. **(done)** Navigator glue + TitleScreen (keyboard-only from launch, verified live).
8. **(done)** Control panel end to end (see ControlPanelScreens.cs notes above).
   Deferred there: hostname EDITING; Exit Game quits instantly (faithful).
9. **(done)** Desktop hub + the key-suppression seam. Deferred on the desktop:
   leaderboards/histograms, the multiplayer opponent table,
   the custom-win hold-button.
10. **(done)** Desktop destinations: both cutscene players, the Workhouse, TRASH WORLD
    NEWS launcher (the game's PDFs are password-protected against text extraction, so
    since 2026-09-24 the zines ship as text documents under `docs/game/` and the Digital
    Version buttons open those — see TrashWorldNewsScreen).
11. **(in progress)** The EXA editor — see "Editor reference" below. Deferred:
    live Redshift play narration (the sound registers are the
    game's own real-time channel — by design, play by ear), GClass26 row context menu
    (mouse-only; every verb it carries is a first-class key in our browser).
12. Map the remaining obfuscated transition/overlay screens to friendly names.
13. Type-ahead search (WotR's TypeAheadSearch is pure — port with SDL TEXTINPUT), the
    settings tree, the mod menu, and the TextEntry port (unlocks hostname editing).
14. **(done)** Installer + release pipeline — see "Build & deploy" (RELEASE PIPELINE).

## Editor reference (`module/src/Screens/Editor/` — one partial ExaEditorScreen per section)
Models the name-preserved `EditorScreen` for every mode (meta.genum18_0: 0 normal,
1 battle, 2 sandbox — GameMode/BattleMode/SandboxMode helpers). Escape is native
throughout (reset-or-leave), except while a mod-side popup holds ModalCapturesEscape.

**Tab stops, in order** (each absent when empty/inapplicable): task → windows → hosts
→ links → files → registers → sign → problems → code → sprite (sandbox) → screen
(sandbox, armed) → stats → controls → go-to-cycle → test log → execution log →
solution. Host-scoped info (links/files/registers/sign) always follows the SELECTED
host, never a general stop (user rule).
Alt+1..6 (screen-scoped ui.jump.1..6, user layout 2026-09-30) jump to task / windows /
hosts / code / testlog / solution (JumpStops in FilePopup.cs's GetActions); an absent
stop speaks "unavailable" via Navigation.StopPresent (FocusStop alone silently drops an
absent stop). Alt+digit produces no SDL TEXTINPUT on Windows, so the jumps are safe from
inside the code and name fields (verified live). A jump onto a text field ARMS it (the
deferred _pendingStop landing runs FocusArrived + ArmTextEntry like LandOnStop — before
2026-09-30 Alt+4 left the code widget unfocused, arrows and typing dead).

**Task stop**: description (rows build only when they carry content; the whole stop
only when it has rows — the sandbox ships a 0-byte description), the "Network logo: …"
row (see parity rules), the live goal checklist, Show Goal (mode 0 only — the game
draws neither goals nor the button in battles/sandbox).

**Sim control**: F2 = step (screen-scoped ui.step; the game's Tab-step is suppressed so
Tab stays navigation), Run/Fast/Pause/Reset buttons via the game handler seams
(method_18/19/20 + method_57 pre-action; native F3/F4/F5 equivalent). Compile errors
are SPOKEN instead of entering the game's LOCKED error view (which would trap editing
until reset). Step echo speaks "Cycle n" + the pending instruction (incl. cycle 0) —
VIEWING-TEAM EXAs only, like every code surface (the fallback once slid to the first
live EXA of ANY team: with the player's EXAs all dead it read the OPPONENT's pending
instruction under its raw name — PB022 leak, fixed 2026-08-29; with no live player
EXA the echo is a bare "Cycle n", enemy moves stay exec-log effect rows);
run/stop transitions announce ("Running." / "Stopped at cycle n."). The Test Run row is
an adjustable slider (arrows; PgUp/PgDn = the coarse step — every slider takes
ui.pageUp/Down → OnAdjust large) whose Enter opens the game's inline typed field.

**Register keys**: bare X / T / F / M (screen-scoped ui.reg.*) speak that register of
the FOCUSED EXA — the focused window row's EXA (copies included), else the instance the
code view follows (last-armed program, else the first). Worded like the window readout
("F 200, cursor at MOVE"); the EXA is NEVER named — the user knows who they focused.
WITHHELD while any TextEntry node has focus (code, go-to-cycle) — there letters type.

**Run-to** (the game's Alt+Click, via public method_52; marker is ONE-SHOT — first
arrival pauses AND clears it, re-arm each time): F8 = run to caret line, mode 1
(matches ANY instance of the program, copies included); Shift+Enter = pin ONE EXA,
mode 0, row-aware with no preconditions — on an EXA window row it pins THAT row's EXA
(line = the read cursor when the code stop is on its program, else the frozen edit
caret — always defined), in the code field it pins whoever the label says. Targets
SNAP FORWARD to the first real instruction at-or-after the line (blank/NOTE/MARK
compile to EmptyLine and OCCUPY indices — refusing was a keyboard dead-end). Every arm
announces "Running to line N: TEXT[, NAME]" (name only when >1 live instance — with
one it is noise, user rule); an OnUpdate watch narrates the arrival pause and arms the
step echo. Compile errors speak instead of arming (the game cancels silently).

**Code stop**: ONE caret-owning TextEntryCaret node — the navigator bubbles all
directional/edit input, suppression swallows ONLY Tab; arming = the game's focus QUEUE
(maybe_4) + method_58 scroll, released on falling edge. Typing echoes via
prefix/suffix diff; caret narration = line on vertical moves, char on horizontal, end
of line; the game's native Ctrl+Up/Down program switch announces. While ARMED the node
runs a VIRTUAL read cursor over SimExa.method_9() (the executing macro-expanded
listing; the real caret is frozen) — the node's landing line is MODE-AWARE (armed =
the virtual line, never the frozen caret) and F8 targets the virtual line. THE LISTING
LINE of a pending instruction is method_10().maybe_0 (what the game highlights);
SimExa.int_0 is the instruction COUNTER and drifts off the listing when EmptyLines are
consumed — never use it as a line (CurrentListingLine is the one accessor). FOLLOWED
INSTANCE (armed only): the code view tracks one live EXA of the focused program —
original by default, a REPL copy via Enter on its window row or Alt+Up/Down cycling
(Ctrl walks programs, Alt walks instances; actions exist only while armed). Falls back
to the original when the instance dies/program changes/run stops. Current markers mark
EVERY instance's line and name theirs only when >1 is alive ("current, XA:1"; bare
"current" with one — user spec).

**Problems stop** (editing + compile errors only): "Line n: message" rows
(EXA-prefixed when several), Enter jumps the caret there.

**Windows stop**: one row per drawn window, EXAs then FILE windows (private
dictionary_1; EntityID keys are Type/Hostname/Number — immovable files key
id@hostname). FILE ROWS ARE ORDERED AS DRAWN: dictionary_1 is an insertion-ordered
window-STATE cache that never prunes (it accumulates every id windowed across
test-run switches), while the game sorts its window set by
SimEntity.entityDisplayRank_0 (method_40's OrderBy — rank = (group, creation counter):
an EXA's group is its program's index in solution_0.list_0, a REPL copy inherits its
parent's; files are group 9000/9001, so file rows run host by host as authored);
enumerating the cache read PB024's column roughly backwards (audit 2026-09-06). EXA rows
sort by (LIVE solution-list index, counter) — the game's order, each copy right after its
parent (sim.list_1 appends copies last), and current the instant a reorder lands (the
sim's ranks only catch up on the next game frame). REORDER (2026-09-30): Shift+Up/Down
(screen-scoped ui.moveUp/Down, offered only on an ORIGINAL's EXA cell so the chord stays
the code field's native selection) swaps the program in solution_0.list_0 — the game's
mouse drag re-sorts that same list by window height (~2606) — then dirty + snapshot
(Ctrl+Z restores) and Navigation.ReannounceCurrent (just the row, new position). Editing
only; edges/armed = silent no-op (user spec). THE ORDER IS SIM-SIGNIFICANT: the list
index is the display rank, the input to Sim.method_54's per-cycle Fisher-Yates shuffle
(seeded from the puzzle id in normal mode), so it decides same-cycle races — M
(readers take the first sender in shuffled order), GRAB, LINK capacity, RAND/KILL
draws, MAKE ids — and the home-host starting cells. Rows are keyed by ENTITY number — unique even for REPL copies (a
replicated SimExa shares its parent's SOLUTION number but gets a fresh EntityID and
the game's ":n" name; keying by solution number created duplicate ControlIds which
KILL the whole graph rebuild). Copies get read-only rows; edit (Enter → code,
following that instance), delete (Backspace, undoable method_36), the NAME field and
the M-bus toggle stay on the original, like the drawn buttons — ONE UNKEYED ROW per
original: EXA → Right → M-bus → Right → Name, so Up/Down always land on the EXA cell
(user layout, 2026-09-24). POSITIONS are stamped by hand (WindowPosition, a Position-kind
part): every WINDOW row — EXA cells, copies, file rows — counts "n of {windows}" over the
whole drawn column, and the bar's buttons (M-bus, Name) speak NO position
(SpeaksOwnPosition; user rule, 2026-09-24) — the builder would otherwise count the bar's
cells "1 of 3" and files only among themselves. RENAME (2026-09-24):
the original's "Name" TextEntry cell (editing only) fronts the title's append-only
GClass60 field over SolutionExa.string_0 (GClass68.int_1 = 2 chars, uppercased, the
widget's caller snapshots every change so Ctrl+Z undoes it); landing arms it the way
the title click does (maybe_4 kind 0 + EditorWindow.method_18 backup), Enter or leaving
the node commits the way the game's focus change does (method_19 restores an emptied
name, method_54 closes) — via NodeVtable.OnBlur (graph core: runs when focus leaves a
node for another, by any route, BEFORE the newcomer's OnSelect/announce — added for
this, so the neighbour's landing never reads an emptied name; the window-row labels
read the AUTHORED SolutionExa name because the SimExa copy lags a game frame behind a
synchronous commit), with the OnUpdate falling edge as backstop; both are keyed to the
EXA WE armed, so a mouse user's own title click is never closed under them. Rows gate the way the game draws:
entity gone or in a hidden host = no row; the game windows only YOUR team's EXAs plus
unheld files (a held file's window merges into its holder; an enemy/NPC EXA and its
held file draw no window for anyone); the game also PRE-OPENS windows for files the
player will MAKE (EntityID numbers minted ahead via Sim.method_79) — entities that
don't exist yet, skipped by the null gate. EXA readouts: "on {host}. X, T, F (held id
+ cursor), M + Local/Global", plus the pending instruction and the error text while
armed (errors live ~one cycle before the sim deletes the EXA); sandbox adds "G
gx,gy,gz. C co, ci." (drawn literals while editing, live while armed).

**Run narration + logs** (Patches/SimNarration on the error seam Sim.smethod_16, gated
on method_0() — the edit-time per-frame rebuild re-errors empty EXAs constantly;
Patches/ExecutionCapture on Sim.method_55, the single per-EXA dispatch):
- Speech rules (user rules): STEPPING voices every error and both goal directions; a
  FREE run voices GOAL FAILURES only (routine probe deaths × 100 auto-advancing tests
  were hundreds of lines). Any event landing on a LATER test than the run started on
  is prefixed "Test n:" (baseline = method_23 at arming) — an unprefixed failure is
  the visible layout. Battles announce no per-round spam; the Win Count row is LIVE
  instead (ticks only while focused — the register-plate pattern; user rule).
- TEST LOG stop: one region per test run, rows = errors + goal flips in arrival
  order. EXECUTION LOG stop: one region per CYCLE; rows = every instruction the
  player's EXAs executed ("XB: LINK 800", the macro-expanded listing line; a BLOCKED
  instruction re-records next cycle = the drawn stay-highlighted line; link
  travel/dying record nothing) followed by enemy/NPC VISIBLE-EFFECT rows (a
  prefix/postfix diff around the cycle: travel, replication, grab/drop with the drawn
  file id, MAKE id-less, kills, deaths incl. the forced file drop, and HARDWARE
  WRITES — "{exa} at {host}: wrote {reg}, now {value}", the drawn plate flip; the
  vmethod_8 seam carries the WRITING EXA (base + every override slot-patched, the
  PanelCapture technique), so attribution is exact, own writes never duplicate their
  instruction rows, and the value spoken is the post-cycle viewing-team plate read.
  NOT logged because NOT drawn: enemy instructions/INTERNAL registers, enemy M,
  held-file WIPEs, victimless kill poses). Names via ExaDisplayName, hosts/ids via
  HostName/FileId, plates via RegName; hidden hosts mute.
- Both logs: cleared on each ARMING (the last run stays browsable after it stops),
  absent while empty, UNCAPPED stores (shared BCL-pure UI/GroupedLog; a silent
  insurance cap — 10M exec / 2M test entries — only so an unattended loop can't OOM;
  sheds oldest whole). COMPACT storage (2026-09-30, a 1M-cycle freeze report): texts are
  interned into a string table, entries are int ids in one chunked arrival-order sequence,
  a group is a contiguous run of it (adds to an OLDER group go to a per-group late list) —
  that run's 8M entries / 9 distinct texts went from ~610 MB live to ~80 MB, with nothing
  per entry for the GC to scan; GroupedLogTests drives it against the old store verbatim.
  CYCLES RESTART AT 0 every test/round, so labels name their
  test when the log spans several; Ctrl+Up/Down hops cycles on a single-test run but
  TESTS when it spans several (NodeVtable.OnRegionJump — the adjacent test is usually
  outside the materialized window). The GRAPH materializes only a WINDOW (≤51
  regions / ≤1200 rows) around the focus anchor, recentered DURING each rebuild so
  edge moves find the next chunk materialized. Home/End jump to the whole log's
  first/last row via NodeVtable.OnJumpEdge + a 2-rebuild JUMP HOLD pinning the anchor
  while the deferred focus applies. RE-ARM WIPE guards (all three needed): windowing
  anchor-follow reads the PERSISTED focus cursor (GraphNavigator.FocusCursorId), not
  the render-dependent FocusedNodeId; ui.regionPrev/Next Rerender BEFORE gating; an
  arming with focus in a log stop remembers it and FocusStop()s back once the store
  refills (TTL ~3s). "GO TO CYCLE" is its own stop: a SYNTHETIC TextEntry field (no
  game widget — digits/period/Backspace polled off SdlKeyboard while focused); Enter
  jumps to the nearest cycle, newest-test-first on ties; "test.cycle" pins a test.
- SimNarration events are STRUCTURED (test index + message; prefix applied at speech
  time); its queue cap is 512 (fast-forward raises hundreds per frame — they all
  belong in the log). ExecutionCapture's listing split is cached by string identity.

**Popups** (mod-side modals; ModalCapturesEscape; Escape unwinds one at a time, focus
restored to the opener):
- GOAL POPUP (Show Goal button or a native F1 press): the F1 view as terse rows for
  every puzzle type — required files (60-value cap trimmed to whole rows + "and N
  more"; Enter on a file row stacks the values popup — a goal spec is a
  SimRequiredFile with no SimFile, re-resolved by (host, required) indexes per read),
  hardware registers with badge labels ("CNS #NERV at host: value"; write-only plates
  plain), then the captured panel lines. Opening prefixes EditorScreen.method_7()
  TRUE so the game ITSELF renders its F1 view (draw-only by construction — the sim's
  register reads hardcode the flag false; visible parity for sighted co-players);
  open defers 2 ticks so a full goal-view frame publishes first; zero rows = "No
  details".
- VALUES POPUP (Enter on any file row/window): every value, one item per value — or
  per AUTHORED ROW / PARAGRAPH (see file speaking rules). Host-qualified source.
- PanelCapture mechanics: patches every GClass298 draw-hook override (vmethod_0..3,
  found by slot at load) + the three GClass230 text statics (smethod_33/34/36),
  recording (string, pos) while armed; UI/PanelText (BCL-pure, tested) rebuilds
  reading order: cells first split into BLOCKS on >600-unit x-gaps (one special draw
  can paint several spatially separate elements — PB038 exposed none, PB020 draws the
  request table AND the disc code strip ~1700 apart; y-clustering across them shuffled
  unrelated cells into one row), blocks read top-down; within a block rows cluster by
  y (y-up, CONSECUTIVE-cell chaining at tolerance 15 — PB020's strip is one glyph per
  call along an isometric diagonal, 14.17 y-step, and must stay ONE spoken row; table
  rows step 41.5 and never chain), cells left-to-right.
- TABLES (2026-09-28): every panel table in the game (all I/O logs, the DISC log, custom-
  puzzle tables) is drawn by ONE private static, SpecialPuzzleLogics.smethod_2(Vector2,
  TableColumn[]) — PanelCapture records it STRUCTURALLY (per column: header LocString +
  ExaValue[] via method_2(true)) and mutes the text statics inside it; PanelText places the
  table as one line at its header row. The popup builds it as a raw-wired GRID: each column a
  context labeled by its header (explicit PushContext id — equal headers must not share one),
  up/down within a column, left/right to the same row clamped; empty cells and empty columns
  say "N/A" (user wording); header-less columns "Column n". Every vertical seam between popup
  rows is wired explicitly (ColumnGrid.Edges). The columns sit in one label-less TABLE
  container (ColumnGrid.Build's tableRole, role.table — user rule 2026-09-30): "table" speaks
  only on entering from outside, never between columns, so the landing says Right has more. The grid itself is the shared UI/ColumnGrid
  (also the histogram panels).

**File speaking rules** (Readouts.cs helpers, used by every surface):
- IDENTITY: file ids repeat across hosts — rows, readouts and the popup are all
  HOST-QUALIFIED (FindFileAt; ControlId ed.file.{host}.{id}), never resolved by id
  alone. Battle file ids are PER-SIDE (GStruct16) — FileId reads the VIEWING team.
- ROW STRUCTURE: SimFile.int_0 (the puzzles' fluent method_10) is an AUTHORED
  values-per-row width — the drawn window FORCE-BREAKS after every Nth value
  (method_43's i % int_7; the ~35-char cosmetic wrap is separate and never spoken).
  Phone numbers (11), table records (2/3/4), song pairs (2) read off that wrap:
  summary readouts join rows with ";" (audible pause; the 60-value cap trims to WHOLE
  rows), the values popup makes each row ONE item, goal-required rows take columns
  from the SimFile BACKING the spec (live file it shadows, else the goal ghost in
  sim.list_6 — the F1 view's own precedence; specs carry no width).
- PROSE: a file with NO authored width whose values include the game's "¶" paragraph
  token (books/articles ship as word+punctuation tokens; the drawn window shows the
  SAME comma'd stream) speaks as FLOWING PROSE — the TSpeech massaging precedent:
  words space-joined, punctuation tokens attached to the word before, "¶" a "; "
  pause; the popup makes each PARAGRAPH one item.
- Held files read with holder + cursor; an ENEMY holder's cursor is never drawn — its
  readout omits it. A file in an OFF-TEAM hand is ANONYMOUS on the map: the game's
  plate loop walks unheld drawables only (no id plate on the card), opens no window
  for it, and the F1 view windows it neither (its id never enters the window set) —
  so the files stop SKIPS it (HeldByOffTeam) while the hosts row speaks the card as
  "{exa} holding a file" (any holder, own team too — the held file has just left the
  occupant list). PB024's scripted players, 2026-08-30. The GOAL VIEW is different:
  it builds its window set from the DIFF REPORT (EditorScreen Class107) — a
  FileIsMissing entry adds the required spec's EntityID, so the F1 view WINDOWS such
  a file, id and values, and draws its ghost card with the id plate. GoalViewWindows
  mirrors that through DiffReportEntry.method_21 (the public tagged-union dispatch):
  entry present = a full values row; absent = the id-only ghost row
  (editor.goal.file.plain). Verified 2026-09-20 off the game's own window cache
  (dictionary_1 gains exactly the held files' ids once the goal view draws) on PB054
  AND PB024 — correcting the 2026-09-06 note that called the window absent for
  everyone. Immovable files speak "immovable".
- REDACTION BARS: a value made only of U+2588 full blocks (PB056's censored reports)
  speaks "redacted" (text.redacted) — in prose flow it is a WORD slot; the punctuation
  rule once glued it onto the previous word.

**Registers stop**: grouped by selected host, after files; badge label + name +
LIVE plate value (the value part is Live — a register changing on its own re-announces
while focused, the sighted player's ticking plate); write-only registers speak
"write-only" with no value, like the plain drawn plate.

**Sign grid** (SFCTA highway sign — the first sprite-content model read): host-scoped
"{host} sign" stop; 3x9 cells speak "{col}, {row}, {char}" — 0-based, the numbers a
#DATA write addresses (user rule, 2026-09-30); char = the glyph or "blank"/dot-question-
exclamation names (a bare "." is TTS silence); coordinates are a static Label part, the
character a Live Value part (a write re-speaks only the character), re-resolved per
announce; up/down preserves the column (grid rows MUST share one StartRow key —
GraphBuilder.VerticalTarget column-navigates only between same-key rows). The goal popup
renders the TARGET message as the same grid (a size row, then a raw-wired 3x9 GoalRow.Grid,
blanks included, cells worded identically). The
drawn WAITING FOR ROW/COLUMN/CHARACTER protocol status is deliberately NOT a live node
(zine-documented; user decision) — popup capture still carries it. Same rule reused
for the Redshift modem's status panel.

**Battle mode**: the stats stop mirrors the swapped drawn panel — Win Count
(int_1/int_0 + method_47; LIVE row), Cycles vs sim.int_5 (per-round cap), Size, Points
(logic dictionary_0[team]), Storage Limit (method_76(team) / int_6) as "You n,
Opponent m" rows, no Activity; the Size tooltip is MODE-AWARE (battle = hard cap,
normal = leaderboard eligibility). The opponent home link's ids are Nothing for my
team on BOTH sides, and the map draws its corridor with a blank plate at each end —
so it speaks "blank, {opponent}" / "blank, {host}" under the both-blank connector rule
(PB028; PB019 re-verified 2026-09-06: the corridor is visibly where the opponent enters
the network). SELECT OPPONENT (mouse-only hotspot)
is a battle-only solution-stop button pushing OpponentBrowserScreen (modeled in
BattleScreens.cs — rows "name, Beaten/Not beaten, Changed date"; Steam rows
unavailable until the NPC falls). Battles end at BattleCompletionScreen (modeled:
result heading as screen name, title, "{you} versus {opponent}", W/D/L, Your Rating —
the badge is lettered sprite art, transcribed N/A + C (51-59) / B (60-79) / A (80-94)
/ S (95-99) / S+ (100) mirroring the draw's ternary — the win-gated upload notice,
and the three buttons with Return-to-Desktop's win gate; the win-column highlight box
only restates the spoken heading).

**Anonymous EXAs**: the game draws name tags ONLY for the viewing team (EditorScreen
~2076); ExaDisplayName mirrors that gate exactly and the label is MODE-AWARE (user
rule): battles say "enemy EXA", a normal puzzle's off-team EXA (a scripted NPC) says
"other EXA". Viewing-team NPC terminals with drawn "???" tags speak raw (entity names
pass through raw — user rule; glyph-naming stays confined to sign cells).

**Redshift sandbox** (PB039, mode 2): the homebrew dev kit — the editor plus the drawn
handheld console; brain = SpecialPuzzleLogics.GClass313. Pad registers #PADX/#PADY
(-1/0/+1), #PADB (X=1 Y=10 Z=100 START=1000), #EN3D; sound #SQR0/#SQR1/#TRI0/#NSE0
(read-write 0-99 → the synth every frame, audible natively; game music mutes while
armed). The 120x100 screen composites EVERY live EXA: bool_4[100] = the 10x10 sprite
at int_4/5/6 = GX/GY/GZ (GY 0 = TOP); GP write = op digit (0 clear/1 set/2 toggle
pixel, 3 load glyph) + 2 digits; CO = int_7 free tag; CI = int_8 = highest
pixel-overlapping CO else -9999 (both default -9999 = the drawn edit-time
placeholders). ~30 cycles/sec wall-clock; no tests/goals/scores/completion. Pad
KEYBOARD reads are RAW held keys (GClass64.smethod_10; bindings gclass52_19..26,
defaults WASD/JKL/Enter=START), live whenever armed — suppression's THIRD seam covers
smethod_10 (caller-audited: only the pad readers, smethod_11's modifiers and the F1
button helper ride it). PLAY MODE (no key of its own — user design): sandbox + armed +
FREE-RUNNING flips CapturesRawInput, standing the navigator and all three seams down
("Play mode." after "Running."); any native pause (F3/Tab-step) returns browse, speaks
"Paused." and arms the step echo; the game's own text-input gate already kills pad
reads while a game field is armed. GraphNavigator's live-watch MUTES (baselining
silently) whenever the focused screen captures raw input. Mod surfaces: task + scores
stops absent (the game draws neither); window G/C readouts; "{exa} sprite" stop after
code (the window's mouse-only 10x10 paint grid, index r*10+c row 0 top — the same
index GP addresses; Enter toggles SolutionExa.bool_0 via the game's snapshot+dirty
path, Ctrl+Z restores; armed = the followed instance's live pattern, read-only); the
2D/3D switch (mouse-only hotspot → bool_10 + sound_29) as a toggle ending the
Simulation stop; "Screen" stop (armed only) = the sprite TABLE in reading order ("XA,
at 10, 5: A" — pattern-matched against the game's glyph cache GClass313.dictionary_1,
force-loadable via public smethod_0; the font transcribes to " A-Z 0-9 . ? !" by
index; unmatched = "custom shape, N pixels"; ", depth z" only in 3D; nothing Live —
pause to orient). Real gamepads feed the pad natively — zero key conflicts.

**Solution browser** (deob GClass253, obfuscated live; Ctrl+O/folder button;
SolutionBrowserScreen.cs, all modes): rows = SolutionManager.smethod_3(puzzle) with
the drawn per-mode stats (sandbox Size = solution.int_1 + "{0} EXAS"/"1 EXA"; battle
WINS = int_0; normal Cycles/Size/Activity from solution.dictionary_0 else "Unsolved");
selection-follows-focus via the PUBLIC method_0 + sound_43 on change; Enter = open
(method_2, the double-click path), Backspace = delete (method_5 — faithfully
confirm-less, announced). Actions stop under the game's GClass26 menu labels: Create
New Solution (method_1), Copy (method_4), Export (sandbox, method_3 — GClass32 writes
the solution into a disc PNG on the DESKTOP with zero visual feedback; the derived
filename is announced) — each focus-follows onto the resulting row; a Back row voices
the game's close gate (back/Escape DEAD while the editor's open solution was
deleted). Sandbox adds the drawn export/import help LocStrings; normal mode mirrors
the right-half panel (selected-solution gated): unsolved = the drawn notice, solved =
the three histogram panels as per-stat stops. IMPORT is native OS drag-and-drop
(armed while the browser is open; the game decodes, duplicates, saves and SELECTS) —
an OnUpdate watch follows selection changes the mod didn't drive into focus (gated to
the solutions stop so a mouse click never yanks focus out of the action rows).

**Leaderboard panels** (shared LeaderboardRows.cs — feeds PuzzleCompleteScreen's
Leaderboards view AND the browser; ONE Tab stop "lb", a UI/ColumnGrid — each stat a
column named Cycles/Size/Activity, left/right switch stats at the same row (clamped),
up/down walk a stat; user design 2026-09-28, replacing one stop per stat; wrapped as a
"table" container like the goal-popup tables, 2026-09-30): per stat, the
caption, percentile cutoffs + friends' scores merged best-first (each behind the game's
own Steam options: EnableHistograms/EnableLeaderboards default ON,
ShowTop/TenthPercentile default OFF — absent rows are usually that gate), then one row
per NON-EMPTY histogram bin "lo to hi: N%" = the bin's SHARE OF ALL PLAYERS, one
decimal, "under 0.1%" for tiny non-empty bins (user expectation, 2026-09-28 — the old
percent-of-the-tallest-bin summed past 100). The data is NOT from a server: it is the
shipped `Content\histograms.txt` (`F.<puzzle>.<stat>|max,count,count,...` = real
player counts), which ScoreManager divides by the largest count at load — ratios
survive, so bin / sum is exact. Single-value bins read as the bare number; empty bins
skipped; ", your score" on the marker bucket ((score-1)*len/max — speaks even when
empty). Bin ranges are
the exact CEIL-based INVERSE of the bucket map (the floor form drifted low whenever
max % len != 0). THE PANEL IS LAYOUT-PARAMETERIZED — Theme.smethod_5's call sites
genuinely differ: completion = caption spoken + marker from the RUN's score
(GClass296.maybe_3); browser = NO caption + marker from the SELECTED solution's score
(maybe_2, refreshed by the browser's own select), marker suppressed when over the
size limit (the panel still draws — unlike the completion screen's whole-notice
ineligibility treatment). PuzzleCompleteScreen: scores as rows, the Leaderboards/Test
Run Data flip, Record GIF, and a leave button under the game's own label; Enter
activates the FOCUSED node (never a global leave pass-through); Escape (Continue
Editing) native. The solution stop's ed.exacount row counts AUTHORED programs against
the create gate min(home free cells, storage cap) — labeled "EXA programs" so it
can't read as a live-EXA limit.

## Map parity rules (what the audits enforce — see the /parity-audit skill)
Ground truth is THREE-WAY: pixels (screenshot) vs model (/eval) vs speech (/speech +
calling the live module's helpers). Everything drawn must be hearable; nothing hidden
may leak. The rulebook accumulated so far:

- HOST NAMES (all in HostName, in precedence order): hidden hosts
  (SimHost.method_10(goal); the goal flag can reveal) gate their CONTENTS
  (occupants/files/registers) on the map AND in the goal popup — but the game's
  name-plate draw has NO hidden gate (EditorScreen's plate loop letters every
  plate/name-mode host, covered or not — PB020's disc draws "LOCKED" across the cover
  AND "DISC" on the edge frame, found 2026-08-29), so a covered host speaks its drawn
  name as the label with the cover caption as the hosts-row VALUE (HiddenStateValue);
  the caption serves as the label only when nothing letters the host; NAME-DISPLAY MODE
  (meta.genum145_0 != 0) letters every non-home host's INTERNAL name along a free
  3-cell edge strip — mirrored incl. the free-strip scan (NameLabelDrawn: no free
  strip = unnamed for everyone); plate-0 (genum154_0==0) means "the ART carries the
  name" — speak "Unnamed host" UNLESS the puzzle's baked-art letters it (UC Berkeley:
  multi-cell plate-0 hosts speak string_0.ToUpper; 1x1 relays stay unlettered); when
  a map has SEVERAL unnamed hosts they carry their hosts-stop POSITION ("Unnamed host
  4" = the 4th host row — the number the navigator already speaks there, nothing
  invented) so a link's destination stays identifiable, as it is by eye; a lone one
  stays bare (UnnamedLabel; PB023's one-way ring of four plate-0 bases, 2026-08-30 —
  the diamond art identifies them pictorially, not by lettering); home
  and opponent plates substitute by IDENTITY (sim.dictionary_0[team].simHost_0;
  sandbox excepted; opponent = Steam persona else the battle character); the logic's
  vmethod_10 override applies last; then the map's #-suffix truncation. Plate enums
  1/2/3 (and 4 on a non-home host) draw the internal name — speak it.
- LINKS: rows read "id, destination", "One way" prefixed when the far side has no
  return id. PLATE-LESS links (SimHostLink.genum177_0 == 0 — the plate draw is gated on
  it) draw a connector with NO id plate at either end. The artists use style 0 exactly
  where something ELSE letters the ids — a LINK LEGEND decal with direction arrows
  (PB016, PB027, PB029B, PB030), the compass rose (PB018, PB021, PB055), or the game's
  hardcoded mid-link lettering (Puzzles.puzzle_29 = PB019) — so ids stay spoken by
  DEFAULT; only maps verified to letter them nowhere (UnletteredStyle0Maps, Network.cs:
  PB058, whose bus ids hide in a file) read "blank, destination", never "One way". All
  48 puzzles swept 2026-09-20; the first cut blanked every style-0 link and silenced
  the legend maps for a few commits. Also from that re-read: overlay sprites with
  GClass229.bool_0 == false are painted AFTER the plates, so baked art can cover a drawn
  plate (PB055's long bridge) — judge a missing plate by pixels, not by style alone.
  The bool_0 flag is
  NOT a drawn "locked" state on art maps: its only drawn form is the red link tile of
  the flat-tile maps (meta.bool_1), and it suppresses the auto bridge sprite — so
  "locked" speaks only on flat-tile maps or on id-less flagged rows (the modems'
  undialed lines); an id-carrying flagged link (PB056's home link) reads like any
  other. Flagged links speak even id-less; a link with NO id on a side and NOT locked is silent from that side — that
  matches the blank drawn connector plate — EXCEPT when the far side is blank too: a
  connector blank at BOTH ends is drawn (a ramp with an empty plate each side) and
  would be heard from nowhere, so both sides speak "blank, destination" (text.blank,
  the empty-drawn-value word; PB028's scripted-terminal corridor, 2026-09-06).
  Single-cell hosts DO draw their link ids —
  speak them. Runtime flips (a modem dial setting ids + unlocking) are pure model
  state the per-frame re-resolve already speaks.
- ART-LETTERED HOSTS beyond UC Berkeley go one at a time through the ArtLetteredHosts
  table (Readouts.cs, keyed "puzzle id/internal name" — PB032's mainframe slab).
  COVER CAPTIONS shared by several covered hosts carry the hosts-stop position exactly
  like unnamed hosts ("Locked 8", editor.host.caption.n — PB056's field of identical
  covers made every link destination the same word).
- DECALS / BAKED LETTERING: every puzzle meta's texture_0 (and method_34 overlay art)
  is baked brand art. If its lettering appears in NO spoken string, the task stop owes
  a "Network logo:" row via the NetworkLogos transcription table (Goals.cs — 
  language-invariant, keyed by puzzle id; no entry = no row). Sublines and taglines
  count; a FULLER brand name counts; a distinct lettered MARK counts (PB012's numeral,
  PB023's "XLB" initialism — letters the long-form title never speaks); title
  restatements do not. Grep descriptions/ + strings.csv to check. The GIS maps'
  COMPASS ROSE decal (four link-id plates + a
  drawn north arrow — the id→cardinal mapping the task text depends on) gets its own
  task-stop row via the CompassRoses table (ids in N,E,S,W order, editor.compass
  template); a mere link LEGEND that restates per-host-spoken ids owes nothing.
- EMPTY STRING VALUES in comma joins speak "blank" (ValueSpeech — the game draws
  their underline styling with no glyphs, e.g. empty rating slots; consecutive
  empties must stay countable by ear — the sign-cell precedent). Prose flow exempt.
- PANELS: special-puzzle panel content drawn as TEXT is captured whole by PanelCapture
  into the goal popup (markup stripped by GameText.Speech). Zine-documented protocol
  status stays popup-only, never a live node (user decision). Panel content drawn as
  SPRITES needs a per-puzzle MODEL read (the highway sign, UC Berkeley's banners) —
  unless it mirrors already-hearable state (DIGICAM feeds mirror register plates;
  PB024's captioned "live view" panel draws 0–3 fixed character sprites for the
  scripted-EXA count in ONE host — the hosts row's "other EXA" entries — with baked
  gamer-tag lettering that is art, not model; the caption is text and PanelCapture
  carries it).
  Popup refinements, all 2026-09-20: GoalPanelLettering (Goals.cs) transcribes a
  LETTERED SPRITE a panel shows in the goal view (PB029B's dead camera frame; PB056's
  baked frame text, gated by PanelLetteringShown while a photo sprite covers it);
  PanelCapture drops MATRIX-TRANSFORMED text that equals a spoken host name (PB032's
  logic paints every host name's floor reflection through its draw hook — phantom
  rows) but keeps flat captions that name a host (PB054's camera panel); GoalRowHost
  keeps the map-side host name where the goal-view vmethod_10 override swaps the
  lettering for status text (PB035B read "#DATA at {message}"). And one panel is NOT
  popup-only: PB053's queue board (baked "NOW SERVING" + a live logic-drawn value) is
  map STATE a sighted player reads at any time — a Live node on the host-scoped sign
  stop of the host it stands in, caption-led in the popup too.
- ENTITY NAMES PASS THROUGH RAW (user rule): "???" and friends go to TTS as-is.
- STATES SPEAK: immovable files, write-only registers, locked links, cover captions.
- The window column, file identity, row structure and prose rules above are part of
  parity too — the drawn wrap of an authored-width file IS sighted-readable structure.
