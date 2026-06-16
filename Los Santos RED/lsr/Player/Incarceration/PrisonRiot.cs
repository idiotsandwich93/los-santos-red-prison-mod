using LosSantosRED.lsr.Interface;
using Rage;
using Rage.Native;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A prison riot: arms nearby inmates with improvised melee weapons and sets inmates and
/// guards fighting each other. Intentionally conservative for realism and stability:
/// - Operates only on peds that ALREADY exist (no spawning -> respects the spawn-logic rule).
/// - Inmates get melee only (shivs/bats), never firearms or anything heavy.
/// - Uses per-ped combat tasking, not global relationship-group edits, so LSR's own
///   relationship/dispatch state is left intact.
/// </summary>
public class PrisonRiot
{
    private readonly Mod.Player Player;
    private readonly ISettingsProvideable Settings;

    private float RiotRadius => Settings.SettingsManager.PrisonSettings.RiotRadius;
    private int RiotDurationMs => Settings.SettingsManager.PrisonSettings.RiotDurationSeconds * 1000;
    private static readonly Random Rng = new Random();
    private static readonly string[] ImprovisedWeapons =
    {
        "WEAPON_KNIFE", "WEAPON_DAGGER", "WEAPON_BAT", "WEAPON_CROWBAR", "WEAPON_HAMMER", "WEAPON_BATTLEAXE"
    };

    public PrisonRiot(Mod.Player player, ISettingsProvideable settings)
    {
        Player = player;
        Settings = settings;
    }

    public bool IsActive { get; private set; }

    public void Trigger()
    {
        if (IsActive)
        {
            return;
        }
        GameFiber.StartNew(delegate
        {
            try
            {
                IsActive = true;
                Game.DisplaySubtitle("~r~Riot!~s~ The yard has gone wild.", 5000);
                EntryPoint.WriteToConsole("PrisonRiot: triggered");

                Vector3 playerPos = Game.LocalPlayer.Character.Position;
                List<Ped> inmates = new List<Ped>();
                List<Ped> guards = new List<Ped>();
                foreach (Ped p in Rage.World.GetAllPeds())
                {
                    if (p == null || !p.Exists() || p == Game.LocalPlayer.Character || !p.IsAlive)
                    {
                        continue;
                    }
                    if (p.Position.DistanceTo(playerPos) > RiotRadius)
                    {
                        continue;
                    }
                    string model = (p.Model.Name ?? "").ToLower();
                    if (model.Contains("prisoner"))
                    {
                        inmates.Add(p);
                    }
                    else if (model.Contains("prisguard") || model.Contains("guard") || model.Contains("sheriff") || model.Contains("cop") || model.Contains("swat"))
                    {
                        guards.Add(p);
                    }
                }

                foreach (Ped inmate in inmates)
                {
                    GiveImprovisedWeapon(inmate);
                    MakeAggressive(inmate);
                    Ped target = NearestAlive(inmate, guards);
                    if (target != null)
                    {
                        NativeFunction.CallByName<int>("TASK_COMBAT_PED", inmate, target, 0, 16);
                    }
                }
                foreach (Ped guard in guards)
                {
                    MakeAggressive(guard);
                    Ped target = NearestAlive(guard, inmates);
                    if (target != null)
                    {
                        NativeFunction.CallByName<int>("TASK_COMBAT_PED", guard, target, 0, 16);
                    }
                }

                EntryPoint.WriteToConsole($"PrisonRiot: {inmates.Count} inmates vs {guards.Count} guards");
                GameFiber.Sleep(RiotDurationMs);
                IsActive = false;
            }
            catch (Exception ex)
            {
                EntryPoint.WriteToConsole("PrisonRiot error: " + ex.Message + " " + ex.StackTrace);
                IsActive = false;
            }
        }, "PrisonRiot");
    }

    private void GiveImprovisedWeapon(Ped ped)
    {
        string weapon = ImprovisedWeapons[Rng.Next(ImprovisedWeapons.Length)];
        uint hash = (uint)Game.GetHashKey(weapon);
        NativeFunction.CallByName<uint>("GIVE_WEAPON_TO_PED", ped, hash, 1, false, true);
    }

    private void MakeAggressive(Ped ped)
    {
        NativeFunction.CallByName<bool>("SET_PED_COMBAT_ATTRIBUTES", ped, 46, true); // BF_AlwaysFight
        NativeFunction.CallByName<bool>("SET_PED_COMBAT_ATTRIBUTES", ped, 5, true);  // BF_CanFightArmedPedsWhenNotArmed
        NativeFunction.CallByName<bool>("SET_PED_FLEE_ATTRIBUTES", ped, 0, false);
        NativeFunction.CallByName<int>("SET_BLOCKING_OF_NON_TEMPORARY_EVENTS", ped, true);
        NativeFunction.CallByName<int>("SET_PED_COMBAT_RANGE", ped, 2); // far
    }

    private Ped NearestAlive(Ped from, List<Ped> candidates)
    {
        return candidates
            .Where(x => x != null && x.Exists() && x.IsAlive)
            .OrderBy(x => x.Position.DistanceTo(from.Position))
            .FirstOrDefault();
    }
}
