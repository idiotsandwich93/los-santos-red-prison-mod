# Prison-mod integration for Los Santos RED

A native (RAGEPluginHook) prison/incarceration system built into LSR, inspired by the
ScriptHookVDotNet "prison-mod". Adds a **"Serve Your Sentence"** option to the Busted menu:
get sent to the prison interior (MLO), do your time with a sentence timer, get released — with
optional **escape**, **riot**, and **solitary**, all kept grounded in LSR's realism.

## What it adds
- `lsr/Player/Incarceration/IncarcerationManager.cs` — the serve-time loop (timer, escape, riot,
  solitary), sentence tied to the loaded save file.
- `lsr/Player/Incarceration/PrisonOutfit.cs` — jumpsuit via specific clothing components only (identity preserved).
- `lsr/Player/Incarceration/PrisonPopulation.cs` — spawns inmates/guards from the LSR `PrisonPeds`
  dispatchable group (with a base-model fallback).
- `lsr/Player/Incarceration/PrisonRiot.cs` — melee-only inmate-vs-guard riot.
- `lsr/Data/Settings/PlayerGeneralSettings/PrisonSettings.cs` — tunables (sentence length, escape,
  riot, solitary).
- Surgical hooks in `Respawning.cs`, `BustedMenu.cs`, `Player.cs`, `RestrictedAreaManager.cs`,
  `OtherViolations.cs`, `VanillaWorldManager.cs`, `EntryPoint.cs`, `GameSaves.cs`.

## Building locally
LSR targets **.NET Framework 4.8** and builds against RPH/RAGENativeUI/NAudio. Two folders are
**gitignored** and must be set up per machine:
- `libs/` — `RagePluginHookSDK.dll`, `RAGENativeUI.dll`, `NAudio.dll` copied from your GTA V install
  (the csproj `<HintPath>`s point at `..\libs\`).
- `.buildtools/` — the `Microsoft.NETFramework.ReferenceAssemblies.net48` package extracted (needed
  because no .NET Framework targeting pack is installed system-wide).

Then run `build_lsr.ps1` (uses VS BuildTools MSBuild + `FrameworkPathOverride`). Output:
`build_out/Los Santos RED.dll` — deploy to your RPH `Plugins\` folder.

> This is a personal fork of Los Santos RED (by Greskrendtregk) for prison-mod integration.
