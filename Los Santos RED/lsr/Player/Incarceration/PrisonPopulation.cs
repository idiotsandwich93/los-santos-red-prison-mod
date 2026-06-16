using LosSantosRED.lsr.Interface;
using Rage;
using Rage.Native;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Populates the prison around the player with inmates and guards while a sentence is served.
/// Prefers LSR's own "PrisonPeds" dispatchable-person group (the custom MP-model prison peds, with
/// their variations applied), and falls back to the base prison ped models so the yard can never end
/// up empty. Spawning is scoped to this feature - it creates its own peds and deletes them on release.
/// </summary>
public class PrisonPopulation
{
    private readonly IDispatchablePeople DispatchablePeople;
    private readonly List<Ped> Spawned = new List<Ped>();
    private static readonly Random Rng = new Random();
    private const string PrisonGroupID = "PrisonPeds";
    private const string FallbackInmateModel = "s_m_y_prisoner_01";
    private const string FallbackGuardModel = "s_m_m_prisguard_01";
    private static readonly string[] InmateScenarios =
    {
        "WORLD_HUMAN_STAND_MOBILE", "WORLD_HUMAN_SMOKING", "WORLD_HUMAN_STAND_MOB",
        "WORLD_HUMAN_MUSCLE_FLEX", "WORLD_HUMAN_LEANING", "WORLD_HUMAN_HANG_OUT_STREET"
    };
    private static readonly string[] GuardScenarios = { "WORLD_HUMAN_GUARD_STAND" };

    public PrisonPopulation(IDispatchablePeople dispatchablePeople)
    {
        DispatchablePeople = dispatchablePeople;
    }

    public void Spawn(Vector3 center, int inmates, int guards)
    {
        Cleanup();
        List<DispatchablePerson> group = DispatchablePeople?.GetPersonData(PrisonGroupID) ?? new List<DispatchablePerson>();
        List<DispatchablePerson> guardPeople = group.Where(x => x != null && !string.IsNullOrEmpty(x.ModelName) && IsGuard(x)).ToList();
        List<DispatchablePerson> inmatePeople = group.Where(x => x != null && !string.IsNullOrEmpty(x.ModelName) && !IsGuard(x)).ToList();

        for (int i = 0; i < inmates; i++)
        {
            SpawnPerson(center, PickOrNull(inmatePeople), FallbackInmateModel, InmateScenarios);
        }
        for (int i = 0; i < guards; i++)
        {
            SpawnPerson(center, PickOrNull(guardPeople), FallbackGuardModel, GuardScenarios);
        }
        EntryPoint.WriteToConsole($"Incarceration: populated prison with {Spawned.Count} peds (group '{PrisonGroupID}' had {inmatePeople.Count} inmates / {guardPeople.Count} guards)", 0);
    }

    public void Cleanup()
    {
        foreach (Ped p in Spawned)
        {
            if (p != null && p.Exists())
            {
                p.Delete();
            }
        }
        Spawned.Clear();
    }

    private void SpawnPerson(Vector3 center, DispatchablePerson person, string fallbackModel, string[] scenarios)
    {
        try
        {
            string modelName = person != null ? person.ModelName : fallbackModel;
            Vector3 pos = SnapToGround(RandomNear(center, 6f, 28f), center.Z);
            Model model = new Model(modelName);
            model.LoadAndWait();
            Ped ped = new Ped(model, pos, (float)Rng.Next(0, 360));
            if (ped == null || !ped.Exists())
            {
                NativeFunction.Natives.SET_MODEL_AS_NO_LONGER_NEEDED(Game.GetHashKey(modelName));
                return;
            }
            ped.IsPersistent = true;
            ped.BlockPermanentEvents = true;
            Spawned.Add(ped);

            // Give freemode peds a random face, then apply the prison-ped variation (the jumpsuit/uniform).
            if (person != null)
            {
                if (person.RandomizeHead)
                {
                    RandomizeHead(ped);
                }
                try
                {
                    person.RequiredVariation?.ApplyToPed(ped, false);
                }
                catch
                {
                }
            }

            try
            {
                NativeFunction.CallByName<bool>("TASK_START_SCENARIO_IN_PLACE", ped, scenarios[Rng.Next(scenarios.Length)], 0, true);
            }
            catch
            {
            }
            NativeFunction.Natives.SET_MODEL_AS_NO_LONGER_NEEDED(Game.GetHashKey(modelName));
        }
        catch (Exception ex)
        {
            EntryPoint.WriteToConsole("PrisonPopulation spawn error: " + ex.Message, 0);
        }
    }

    private void RandomizeHead(Ped ped)
    {
        try
        {
            int shape1 = Rng.Next(0, 46);
            int shape2 = Rng.Next(0, 46);
            int skin1 = Rng.Next(0, 46);
            int skin2 = Rng.Next(0, 46);
            float mix = (float)Rng.NextDouble();
            NativeFunction.CallByName<int>("SET_PED_HEAD_BLEND_DATA", ped, shape1, shape2, 0, skin1, skin2, 0, mix, mix, 0f, false);
            NativeFunction.CallByName<int>("SET_PED_RANDOM_COMPONENT_VARIATION", ped, 0);
        }
        catch
        {
        }
    }

    private bool IsGuard(DispatchablePerson person)
    {
        string name = (person.DebugName ?? "").ToLower();
        string model = (person.ModelName ?? "").ToLower();
        return name.Contains("guard") || model.Contains("guard") || model.Contains("prisguard");
    }

    private DispatchablePerson PickOrNull(List<DispatchablePerson> people) =>
        people.Count > 0 ? people[Rng.Next(people.Count)] : null;

    private Vector3 RandomNear(Vector3 center, float minDist, float maxDist)
    {
        double angle = Rng.NextDouble() * Math.PI * 2.0;
        double dist = minDist + (Rng.NextDouble() * (maxDist - minDist));
        return new Vector3(
            center.X + (float)(Math.Cos(angle) * dist),
            center.Y + (float)(Math.Sin(angle) * dist),
            center.Z);
    }

    /// <summary>
    /// Snap a candidate position onto the prison floor. In a multi-level MLO the raw center Z can bury a
    /// ped in geometry or float it in the air (so it never appears where the player is). We probe ground
    /// from slightly above the player's own Z and fall back to that Z if the probe fails.
    /// </summary>
    private Vector3 SnapToGround(Vector3 pos, float fallbackZ)
    {
        try
        {
            float groundZ;
            bool found = NativeFunction.Natives.GET_GROUND_Z_FOR_3D_COORD<bool>(
                pos.X, pos.Y, fallbackZ + 2.0f, out groundZ, false, false);
            if (found && Math.Abs(groundZ - fallbackZ) <= 5.0f)
            {
                return new Vector3(pos.X, pos.Y, groundZ);
            }
        }
        catch
        {
        }
        return new Vector3(pos.X, pos.Y, fallbackZ);
    }
}
