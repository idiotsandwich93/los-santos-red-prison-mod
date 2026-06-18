# Prison-mod integration for Los Santos RED

A native **RAGEPluginHook (RPH)** prison/incarceration system built into Los Santos RED (LSR),
inspired by the ScriptHookVDotNet "prison-mod" but reimplemented as native LSR code (the two are
different, incompatible runtimes — `GTA.Ped` ≠ `Rage.Ped` — so prison-mod could not be linked).

It adds a **"Serve Your Sentence"** option to the Busted menu: you get sent to the prison interior
(an MLO), do your time against a sentence timer in a populated yard, and get released at the gate —
with optional **escape**, **riot**, and **solitary**, all kept grounded in LSR's realism (no
rockets/heavy weapons; prison-appropriate gear; LSR's own economy/dispatch).

Current build: **v1.0.0.1010** (stamped in `AssemblyInfo.cs`; shown in the LSR load notification so the
loaded DLL is verifiable).

---

## What it does (gameplay)

- **Serve a sentence.** Get arrested → Busted menu → **Serve Your Sentence** → fade to the prison
  interior, swap into a jumpsuit, and a countdown HUD runs while you roam a populated yard. On expiry
  you're released at the prison gate and your clothes are restored.
- **Sentence length scales with your crimes:** `days = (wantedLevel × 2) + (policeKilled × 5) +
  (civiliansKilled × 3)`, clamped to `[MinSentenceDays, MaxSentenceDays]`.
- **Escape.** Physically leave the prison perimeter and you become a fugitive in a jumpsuit — a wanted
  level is applied (default 4) and you keep running. No magic helicopter; you flee on foot.
- **Riot.** A button prompt (`Cover`) incites a melee-only riot: nearby inmates get improvised weapons
  (knife/dagger/bat/crowbar/hammer/battleaxe) and fight the guards. No firearms, no heavy weapons.
- **Solitary.** Assault a nearby inmate/guard and you're thrown in the hole: teleported to the solitary
  prison, sentence extended, riot prompt disabled, then auto-returned to general population.
- **Per-save isolation.** A sentence belongs to the save file it started on. Load a different
  character and the sentence ends immediately — no carry-over, no phantom "prison break" wanted level.

---

## New files (additive)

All new gameplay code lives in `lsr/Player/Incarceration/` plus one settings class.

| File | Purpose |
|------|---------|
| `lsr/Player/Incarceration/IncarcerationManager.cs` | The orchestrator. Runs the serve-time `GameFiber` loop: fade → `SendToPrison` → apply jumpsuit → fade in → populate yard → countdown. Handles escape (perimeter check), solitary, riot prompt, save-change abort, death, and release. Sets/clears the `EntryPoint.PlayerIsIncarcerated` flag. Picks the prison for the player's current state (San Andreas → Bolingbroke; Liberty/Alderney → Alderney prison via sister-state matching). |
| `lsr/Player/Incarceration/PrisonOutfit.cs` | Jumpsuit via **clothing components only** — identity preserved. Saves the current drawable/texture/palette of 8 specific slots, applies the jumpsuit, and restores them on release. Components (componentID, drawable, texture, palette): `{2,19,3,0} {11,32,0,0} {6,7,0,0} {7,103,0,0} {8,15,0,0} {4,27,2,0} {3,3,0,0} {10,0,0,0}`. Head/face (component 0), head-blend, and model are never touched. |
| `lsr/Player/Incarceration/PrisonPopulation.cs` | Spawns the yard population from LSR's **`PrisonPeds`** dispatchable-person group (splitting guards vs inmates by debug-name/model), applying each `DispatchablePerson`'s `RequiredVariation` + a randomized head for freemode models. Falls back to `s_m_y_prisoner_01` / `s_m_m_prisguard_01` so the yard is never empty. Spawns are **snapped to ground** (`GET_GROUND_Z_FOR_3D_COORD`) so they aren't buried/floating in the multi-level MLO. Peds are persistent + `BlockPermanentEvents`; cleaned up on release/escape/death. |
| `lsr/Player/Incarceration/PrisonRiot.cs` | Melee-only riot. Operates on already-existing peds (no core spawn logic touched): arms nearby inmates with improvised melee, sets combat attributes, and `TASK_COMBAT_PED`s inmates vs guards within `RiotRadius` for `RiotDurationSeconds`. |
| `lsr/Data/Settings/PlayerGeneralSettings/PrisonSettings.cs` | All tunables (`ISettingsDefaultable`). See **Settings** below. |

---

## Modified LSR files (surgical hooks)

Everything below is keyed off a single static flag, **`EntryPoint.PlayerIsIncarcerated`**, which is
`true` only while a sentence is being served.

### Wiring / entry points
| File | Change |
|------|--------|
| `EntryPoint.cs` | Added `public static bool PlayerIsIncarcerated { get; set; }` — the master "serving time" flag. |
| `lsr/Player/Player.cs` | Constructs and exposes `public IncarcerationManager Incarceration`; in `Reset(...)` the `resetSavedGame` branch calls `Incarceration?.Reset()` so a sentence never carries across characters. |
| `lsr/Player/Interface/IPoliceRespondable.cs` | Exposes `IncarcerationManager Incarceration { get; }`. |
| `lsr/Data/Settings/SettingsManager.cs` | Registers `PrisonSettings` (Player settings category). |
| `lsr/UI/Menu/Main/BustedMenu.cs` | Adds the **"Serve Your Sentence"** menu item (in the arrested / high-level item builders) which calls `Player.Incarceration.StartServingNearest()`. |
| `Los Santos RED.csproj` | `<Compile>` includes for the 4 Incarceration files + `PrisonSettings.cs`; `<HintPath>`s for `RagePluginHookSDK`, `RAGENativeUI`, `NAudio` repointed to `..\libs\` for the local build. |
| `Properties/AssemblyInfo.cs` | Version bumped to `1.0.0.1010` (verifiable in the load notification). |

### The serve / release pipeline
| File | Change |
|------|--------|
| `lsr/Player/Respawning/Respawning.cs` | New `SendToPrison(ILocationRespawnable)` — the canonical reset + teleport into the intake point (mirrors `SurrenderToPolice` **minus** the time-skip/bail, since the interactive sentence handles time; it does call `ResetPlayer(resetWanted:true,…)`, so you arrive wanted-free). Also `ReleaseToCoordinates(Vector3,float)`, `PlaceAtRespawnLocation(ILocationRespawnable)`, `ReleaseFromPrison(...)`, and a private `EnsureGroundLoaded(...)` that freezes the player and waits on `REQUEST_COLLISION_AT_COORD` / `HAS_COLLISION_LOADED_AROUND_ENTITY` so they don't fall through the MLO / into the ocean while it streams. |

### "While serving" exemptions — the part that fought us, and the actual fixes

The recurring symptom was an **immediate wanted level on spawning into the prison**. Root cause (found in
the LSR data + source, not assumed): the Bolingbroke `Prison` location in the deployed **base
`Locations.xml`** carries a `VanillaRestrictedArea` (a set of angled boxes) blanketing the whole prison,
and your spawn point sits inside it. Being inside it set `IsTrespassing` → the `Trespassing` crime
(`ResultingWantedLevel = 2`) → wanted; the `IsRestrictedDuringWanted` JAIL zone then piled on
"Trespassing on Government Property". The decisive fix was at the **source** of the violation.

| File | Change |
|------|--------|
| `lsr/Locations/.../RestrictedAreas/Vanilla/VanillaRestrictedArea.cs` | **The key fix.** `Update()` now sets `isPlayerViolating = false` and bails when `EntryPoint.PlayerIsIncarcerated \|\| player.Violations.CanEnterRestrictedAreas`. The custom `RestrictedArea.Update()` already honored `CanEnterRestrictedAreas`; the *vanilla* path was missing it — that omission is what flagged the inmate as trespassing. |
| `lsr/Player/Violations/Violations.cs` | `CanEnterRestrictedAreas` now also returns true when `EntryPoint.PlayerIsIncarcerated` — the single chokepoint that clears restricted-area state, unlocks gates, suppresses the camera report, and blocks every trespassing crime for an inmate. |
| `lsr/Player/Crime/RestrictedAreaManager.cs` | `Update()` early-returns (no trespassing flags set) while incarcerated. |
| `lsr/Player/Violations/OtherViolations.cs` | The "Trespassing on Government Property" check (`IsRestrictedDuringWanted` JAIL zone) is gated with `&& !EntryPoint.PlayerIsIncarcerated`. |

### Keeping the yard populated (stop LSR culling / suppressing prison peds)
| File | Change |
|------|--------|
| `lsr/World/World.cs` | `SetDensity()` forces the spawn multiplier to `1.0` while incarcerated, so LSR's per-frame `SET_PED/SCENARIO_PED_DENSITY_MULTIPLIER_THIS_FRAME` throttle can't thin the yard. |
| `lsr/World/Pedestrians/Pedestrians.cs` | `CreateNew()` and `Prune()` early-return while incarcerated, so LSR's ped sweep doesn't delete the prison guards (`s_m_m_prisguard_01` would otherwise be routed to `AddAmbientCop` and deleted when no agency matches the zone) or strip/despawn the spawned inmates. |
| `lsr/VanillaManager/VanillaWorldManager.cs` | `TerminateScenarioPeds()` guarded by the flag. (Holdover from the first attempt — this method isn't on the live call path; the effective density/cull fixes are the two rows above. Left in place, harmless.) |

### Per-save sentence isolation
| File | Change |
|------|--------|
| `lsr/Data/Interface/IGameSaves.cs` + `lsr/Data/Saves/GameSaves.cs` | Added `int CurrentSaveNumber => PlayingSave != null ? PlayingSave.SaveNumber : -1`. The serve loop snapshots this at sentence start and ends the sentence the instant it changes (character/save swap) — quietly, with no escape/wanted carry-over. |

---

## Settings (`PrisonSettings`, defaults)

Tunables live in LSR's settings system (Player category). Defaults:

| Setting | Default | Meaning |
|---|---|---|
| `IsEnabled` | `true` | Master toggle for the system. |
| `RealSecondsPerSentenceDay` | `12` | Real seconds served per sentence-day. |
| `MinSentenceDays` / `MaxSentenceDays` | `1` / `30` | Sentence clamp. |
| `AllowEscape` | `true` | Allow breaking out. |
| `EscapeRadius` | `220` | Metres from the gate that counts as "outside the walls". |
| `EscapeWantedLevel` | `4` | Wanted level applied on escape. |
| `AllowRiot` | `true` | Enable the riot prompt. |
| `RiotRadius` / `RiotDurationSeconds` | `60` / `120` | Riot reach and duration. |
| `AllowSolitary` | `true` | Send to solitary on assault. |
| `SolitaryDays` / `SolitaryRealSecondsPerDay` | `3` / `12` | Solitary penalty. |
| `YardPrisonerCount` / `YardGuardCount` | `10` / `4` | Fallback spawn counts. |
| `Release X/Y/Z/Heading` | `1856.91, 2607.069, 45.67218, 256.0832` | Release point. |

> **Note on hardcoded critical values:** because LSR's `XmlSerializer` lets a stale deployed `settings.xml`
> override code defaults, two safety-critical things are pinned in code regardless of settings: the San
> Andreas **release point** (`1856.91, 2607.069, 45.67218` @ `256.0832`) in `IncarcerationManager.Release`,
> and the **escape perimeter check** (uses a `220` floor if `EscapeRadius < 50`, a 6-second post-spawn
> grace, and 2D distance — so a stale/zero setting or a Z-fall during streaming can't read as "already
> escaped" the instant you spawn). The 8 jumpsuit clothing components are likewise hardcoded.

---

## Building locally

LSR targets **.NET Framework 4.8** / **C# 7.3**. Two folders are **gitignored** and must be set up per
machine (they are *not* in the repo):

- `libs/` — `RagePluginHookSDK.dll`, `RAGENativeUI.dll`, `NAudio.dll` copied from your RAGEPluginHook /
  game install. **`RagePluginHookSDK.dll` is proprietary and is deliberately never committed.** The
  csproj `<HintPath>`s point at `..\libs\`.
- `.buildtools/` — the `Microsoft.NETFramework.ReferenceAssemblies.net48` NuGet package extracted
  (needed because no .NET Framework targeting pack is installed system-wide).

Then run `build_lsr.ps1` (VS BuildTools MSBuild + `FrameworkPathOverride`). Output:
`build_out/Los Santos RED.dll` → deploy to your RPH `Plugins\` folder.

---

## Notes

- **Location data is user-managed.** The prison, its spawn/release points, and the `PrisonPeds` group
  come from your deployed LSR config XMLs (`Locations.xml`, `DispatchablePeople*.xml`), not from this
  code. The code resolves the prison at runtime from `PlacesOfInterest`.
- **Config loading:** LSR loads `Locations.xml` (base) when `LocationsConfig = Default`; a named variant
  like `Locations_LPP.xml` only loads when a config selects it. Additive `Locations+_*.xml` files always
  layer on top.
- This is a **personal fork** of Los Santos RED (by Greskrendtregk) for prison-mod integration work.
