using ExtensionsMethods;
using LosSantosRED.lsr.Helper;
using LosSantosRED.lsr.Interface;
using Rage;
using Rage.Native;
using System;
using System.Linq;

/// <summary>
/// Drives the interactive prison sentence: sends the player to the prison interior (MLO),
/// swaps them into a jumpsuit, runs a sentence countdown while they roam the populated prison,
/// then releases them at the gate. While serving they may incite a riot, get thrown in solitary
/// for assaulting someone, or break for the fences and trigger a manhunt (escape).
///
/// Location data is resolved at runtime from <see cref="IPlacesOfInterest"/> (loaded from the
/// user's LSR config XML), so no coordinates are hardcoded. Tuning lives in PrisonSettings.
/// </summary>
public class IncarcerationManager
{
    private readonly Mod.Player Player;
    private readonly IPlacesOfInterest PlacesOfInterest;
    private readonly ISettingsProvideable Settings;
    private readonly IGameSaves GameSaves;
    private readonly PrisonOutfit Outfit;
    private readonly PrisonRiot Riot;
    private readonly PrisonPopulation Population;

    private const string RiotPromptID = "IncitePrisonRiot";
    private const string PromptGroup = "PrisonSentence";

    public IncarcerationManager(Mod.Player player, IPlacesOfInterest placesOfInterest, ISettingsProvideable settings, IDispatchablePeople dispatchablePeople, IGameSaves gameSaves)
    {
        Player = player;
        PlacesOfInterest = placesOfInterest;
        Settings = settings;
        GameSaves = gameSaves;
        Outfit = new PrisonOutfit(player);
        Riot = new PrisonRiot(player, settings);
        Population = new PrisonPopulation(dispatchablePeople);
    }

    private PrisonSettings PS => Settings.SettingsManager.PrisonSettings;
    public bool IsServingSentence { get; private set; }

    /// <summary>True if the system is enabled and a general-population prison is configured.</summary>
    public bool HasPrisonAvailable => PS.IsEnabled && GetGeneralPopulationPrison() != null;

    /// <summary>Resolve the nearest general-population prison and begin a sentence there.</summary>
    public void StartServingNearest()
    {
        Prison prison = GetGeneralPopulationPrison();
        if (prison == null)
        {
            EntryPoint.WriteToConsole("Incarceration: StartServingNearest found no prison configured");
            return;
        }
        StartSentence(prison);
    }

    public void StartSentence(Prison prison)
    {
        if (IsServingSentence || prison == null || !PS.IsEnabled)
        {
            return;
        }
        // Never proceed without a real spawn point - that's what caused the ocean teleport + crash.
        if (prison.RespawnLocation == Vector3.Zero)
        {
            EntryPoint.WriteToConsole($"Incarceration: ABORT - {prison.Name} has no RespawnLocation configured", 0);
            Game.DisplayHelp("~r~That prison has no spawn point configured.~s~");
            return;
        }
        EntryPoint.WriteToConsole($"Incarceration: serving at {prison.Name} RespawnLocation {prison.RespawnLocation} State {NormalizeState(prison.StateID)}", 0);
        int sentenceDays = CalculateSentenceDays();
        Vector3 escapeAnchor = prison.EntrancePosition != Vector3.Zero ? prison.EntrancePosition : prison.RespawnLocation;
        Prison solitaryPrison = GetSolitaryPrison();

        GameFiber.StartNew(delegate
        {
            try
            {
                IsServingSentence = true;
                EntryPoint.PlayerIsIncarcerated = true; // serving: un-suppress prison peds + exempt from restricted-area trespassing
                EntryPoint.WriteToConsole($"Incarceration: starting {sentenceDays} day sentence at {prison.Name}");

                Game.FadeScreenOut(1500);
                GameFiber.Sleep(1600);
                Player.Respawning.SendToPrison(prison); // canonical reset + teleport to intake
                GameFiber.Sleep(500);
                Outfit.Apply();
                GameFiber.Sleep(300);
                Game.FadeScreenIn(1500);

                Game.DisplayHelp($"You have been sentenced to ~r~{sentenceDays} days~s~ at {prison.Name}. Do your time, ~b~incite a riot~s~, or run for the fences. Assault someone and you'll end up in ~o~the hole~s~.");
                AddRiotPrompt();
                // Always populate - not gated on settings (which may be stale in the user's settings.xml).
                Population.Spawn(Game.LocalPlayer.Character.Position, 10, 4);

                bool escaped = false;
                bool inSolitary = false;
                uint solitaryReturnTime = 0;
                uint endTime = Game.GameTime + (uint)(sentenceDays * PS.RealSecondsPerSentenceDay * 1000f);
                int servingSaveNumber = GameSaves != null ? GameSaves.CurrentSaveNumber : -1; // sentence belongs to THIS save
                uint sentenceStartTime = Game.GameTime;

                while (Game.GameTime < endTime && EntryPoint.ModController.IsRunning && IsServingSentence)
                {
                    if (Player.IsDead)
                    {
                        CleanupPrompts();
                        Population.Cleanup();
                        EntryPoint.PlayerIsIncarcerated = false;
                        IsServingSentence = false;
                        return; // let LSR's death/respawn flow take over
                    }
                    // The sentence is tied 100% to the loaded save file. If a different save/character is loaded,
                    // end it immediately - no escape, no wanted level, no carry-over to the other save.
                    if (GameSaves != null && GameSaves.CurrentSaveNumber != servingSaveNumber)
                    {
                        EntryPoint.WriteToConsole($"Incarceration: save changed ({servingSaveNumber} -> {GameSaves.CurrentSaveNumber}), ending sentence", 0);
                        CleanupPrompts();
                        Population.Cleanup();
                        EntryPoint.PlayerIsIncarcerated = false;
                        IsServingSentence = false;
                        return;
                    }

                    // Escape = the player physically left the prison perimeter. Guard against false positives that
                    // would fire an escape the moment they spawn: a short grace period, a 2D distance (so a Z-fall
                    // while collision streams in doesn't count), and a sane radius floor so a stale/zero EscapeRadius
                    // in the user's settings.xml can't read as "already escaped".
                    float escapeRadius = PS.EscapeRadius >= 50f ? PS.EscapeRadius : 220f;
                    bool outsideWalls = Game.GameTime - sentenceStartTime > 6000 &&
                        Game.LocalPlayer.Character.Position.DistanceTo2D(escapeAnchor) > escapeRadius;
                    if (outsideWalls)
                    {
                        if (PS.AllowEscape)
                        {
                            escaped = true;
                            break;
                        }
                        // Escape disabled: drag them back inside.
                        Player.Respawning.PlaceAtRespawnLocation(inSolitary && solitaryPrison != null ? (ILocationRespawnable)solitaryPrison : prison);
                        Game.DisplaySubtitle("~r~You can't leave.~s~", 2000);
                    }

                    if (!inSolitary && PS.AllowSolitary && solitaryPrison != null && PlayerAssaultedNearbyPrisonPed())
                    {
                        inSolitary = true;
                        RemoveRiotPrompt();
                        endTime += (uint)(PS.SolitaryDays * PS.RealSecondsPerSentenceDay * 1000f);
                        solitaryReturnTime = Game.GameTime + (uint)(PS.SolitaryDays * PS.SolitaryRealSecondsPerDay * 1000f);
                        SendToSolitary(solitaryPrison);
                    }
                    else if (inSolitary && Game.GameTime >= solitaryReturnTime)
                    {
                        inSolitary = false;
                        ReturnToGeneralPopulation(prison);
                        AddRiotPrompt();
                    }

                    if (!inSolitary && PS.AllowRiot && Player.ButtonPrompts.IsPressed(RiotPromptID))
                    {
                        Riot.Trigger();
                    }

                    int remainingSeconds = (int)((endTime - Game.GameTime) / 1000u);
                    string label = inSolitary ? "~o~Solitary~s~ - time remaining:" : "~y~Time remaining:~s~";
                    Game.DisplaySubtitle($"{label} {FormatTime(remainingSeconds)}", 1100);
                    GameFiber.Sleep(1000);
                }

                CleanupPrompts();
                if (!IsServingSentence)
                {
                    // Sentence was aborted externally (character change / mod reset) - do not release/teleport.
                    Population.Cleanup();
                    EntryPoint.PlayerIsIncarcerated = false;
                    return;
                }
                if (escaped)
                {
                    HandleEscape();
                }
                else
                {
                    Release(prison);
                }
            }
            catch (Exception ex)
            {
                EntryPoint.WriteToConsole("Incarceration error: " + ex.Message + " " + ex.StackTrace);
                CleanupPrompts();
                Population.Cleanup();
                EntryPoint.PlayerIsIncarcerated = false;
                IsServingSentence = false;
            }
        }, "PrisonSentence");
    }

    private void SendToSolitary(Prison solitaryPrison)
    {
        Game.FadeScreenOut(800);
        GameFiber.Sleep(900);
        Player.Respawning.PlaceAtRespawnLocation(solitaryPrison);
        GameFiber.Sleep(200);
        Game.FadeScreenIn(800);
        Game.DisplayHelp("~o~Solitary confinement.~s~ That's what you get for starting trouble. Your stay just got longer.");
        EntryPoint.WriteToConsole("Incarceration: player sent to solitary");
    }

    private void ReturnToGeneralPopulation(Prison prison)
    {
        Game.FadeScreenOut(800);
        GameFiber.Sleep(900);
        Player.Respawning.PlaceAtRespawnLocation(prison);
        GameFiber.Sleep(200);
        Game.FadeScreenIn(800);
        Game.DisplaySubtitle("Back in general population. Keep your nose clean.", 5000);
        EntryPoint.WriteToConsole("Incarceration: player returned to general population");
    }

    private void Release(Prison prison)
    {
        Game.FadeScreenOut(1500);
        GameFiber.Sleep(1600);
        Population.Cleanup();
        Outfit.Restore();
        // Hardcoded San Andreas release point (user-specified), not read from settings so it can't be
        // overridden by a stale settings.xml. Other states fall back to the prison gate.
        Vector3 releasePos;
        float releaseHeading;
        if (NormalizeState(prison.StateID) == StaticStrings.SanAndreasStateID)
        {
            releasePos = new Vector3(1856.91f, 2607.069f, 45.67218f);
            releaseHeading = 256.0832f;
        }
        else
        {
            releasePos = prison.EntrancePosition;
            releaseHeading = prison.EntranceHeading;
        }
        EntryPoint.WriteToConsole($"Incarceration: releasing player to {releasePos} hdg {releaseHeading}", 0);
        Player.Respawning.ReleaseToCoordinates(releasePos, releaseHeading);
        GameFiber.Sleep(300);
        EntryPoint.PlayerIsIncarcerated = false;
        IsServingSentence = false;
        Game.FadeScreenIn(1500);
        Game.DisplayHelp("You have served your sentence. Stay clean.");
        EntryPoint.WriteToConsole("Incarceration: sentence complete, player released");
    }

    private void HandleEscape()
    {
        // No teleport / no outfit restore: you broke out, you're a fugitive in a jumpsuit.
        Population.Cleanup();
        EntryPoint.PlayerIsIncarcerated = false;
        IsServingSentence = false;
        Player.SetWantedLevel(PS.EscapeWantedLevel, "Prison Escape", true);
        Game.DisplayHelp("~r~You've escaped!~s~ Every cop in the state is hunting you. Ditch the jumpsuit and disappear.");
        Game.DisplaySubtitle("~r~Prison break!~s~", 5000);
        EntryPoint.WriteToConsole("Incarceration: player escaped prison");
    }

    private void AddRiotPrompt()
    {
        if (PS.AllowRiot)
        {
            Player.ButtonPrompts.AddPrompt(PromptGroup, "Incite Riot", RiotPromptID, GameControl.Cover, 15);
        }
    }
    private void RemoveRiotPrompt() => Player.ButtonPrompts.RemovePrompts(PromptGroup);
    private void CleanupPrompts() => Player.ButtonPrompts.RemovePrompts(PromptGroup);

    /// <summary>Abort any active sentence without releasing/teleporting. Called when the character changes.</summary>
    public void Reset()
    {
        if (IsServingSentence)
        {
            CleanupPrompts();
            Population.Cleanup();
        }
        EntryPoint.PlayerIsIncarcerated = false;
        IsServingSentence = false;
    }

    private bool PlayerAssaultedNearbyPrisonPed()
    {
        Ped playerPed = Game.LocalPlayer.Character;
        foreach (Ped p in Rage.World.GetAllPeds())
        {
            if (p == null || !p.Exists() || p == playerPed)
            {
                continue;
            }
            if (p.Position.DistanceTo(playerPed.Position) > 20f)
            {
                continue;
            }
            string model = (p.Model.Name ?? "").ToLower();
            bool isPrisonPed = model.Contains("prisoner") || model.Contains("prisguard") || model.Contains("guard");
            if (!isPrisonPed)
            {
                continue;
            }
            if (NativeFunction.CallByName<bool>("HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY", p, playerPed, true))
            {
                NativeFunction.CallByName<int>("CLEAR_ENTITY_LAST_DAMAGE_ENTITY", p);
                return true;
            }
        }
        return false;
    }

    private int CalculateSentenceDays()
    {
        int wanted = Math.Max(Player.WantedLevel, 1);
        int policeKilled = Player.PoliceResponse != null ? Player.PoliceResponse.PoliceKilled : 0;
        int civiliansKilled = Player.PoliceResponse != null ? Player.PoliceResponse.CiviliansKilled : 0;
        int days = (wanted * 2) + (policeKilled * 5) + (civiliansKilled * 3);
        if (days < PS.MinSentenceDays)
        {
            days = PS.MinSentenceDays;
        }
        if (days > PS.MaxSentenceDays)
        {
            days = PS.MaxSentenceDays;
        }
        return days;
    }

    private Prison GetGeneralPopulationPrison() => FindPrison(false);
    private Prison GetSolitaryPrison() => FindPrison(true);

    private Prison FindPrison(bool solitary)
    {
        var prisons = PlacesOfInterest?.PossibleLocations?.Prisons;
        if (prisons == null)
        {
            return null;
        }
        // Only prisons that actually have a RespawnLocation are usable - that exact coordinate is the
        // MLO spawn point. This also structurally prevents picking a blank/defaulted prison whose
        // (0,0,0) coords would teleport the player into the ocean.
        var candidates = prisons
            .Where(x => x != null && x.IsEnabled && IsSolitary(x) == solitary && x.RespawnLocation != Vector3.Zero)
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }
        // Send the player to the prison for their current state: San Andreas -> Bolingbroke, and
        // Liberty City OR Alderney -> the Alderney prison (they are sister states sharing one prison).
        // Matched explicitly so a null/unknown current state defaults to San Andreas rather than
        // matching every prison (which is the one way the LC prison could leak into a Los Santos arrest).
        GameState playerState = Player.CurrentLocation?.CurrentZone?.GameState;
        Prison inState = candidates
            .Where(x => StatesMatch(x.GameState, x.StateID, playerState))
            .OrderBy(x => x.RespawnLocation.DistanceTo2D(Game.LocalPlayer.Character))
            .FirstOrDefault();
        if (inState != null)
        {
            return inState;
        }
        // Fallback (no same-state prison configured): prefer a San Andreas prison, then nearest — so an
        // unknown state still never lands you in the wrong state's prison.
        return candidates
            .OrderBy(x => NormalizeState(x.StateID) == StaticStrings.SanAndreasStateID ? 0 : 1)
            .ThenBy(x => x.RespawnLocation.DistanceTo2D(Game.LocalPlayer.Character))
            .FirstOrDefault();
    }

    private bool StatesMatch(GameState prisonState, string prisonStateID, GameState playerState)
    {
        string playerID = NormalizeState(playerState?.StateID);
        if (NormalizeState(prisonStateID) == playerID)
        {
            return true;
        }
        // Sister states (Liberty <-> Alderney) share a prison. Requires a real current state, so a
        // null/unknown state can't sister its way out of San Andreas.
        return playerState != null && prisonState != null &&
            (prisonState.IsSisterState(playerState) || playerState.IsSisterState(prisonState));
    }

    private static string NormalizeState(string stateID) =>
        string.IsNullOrEmpty(stateID) ? StaticStrings.SanAndreasStateID : stateID;

    private bool IsSolitary(Prison prison) =>
        !string.IsNullOrEmpty(prison.Name) && prison.Name.ToLower().Contains("solitary");

    private string FormatTime(int totalSeconds)
    {
        if (totalSeconds < 0)
        {
            totalSeconds = 0;
        }
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        return $"{minutes:00}:{seconds:00}";
    }
}
