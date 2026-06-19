using Rage;
using Rage.Native;
using System.Collections.Generic;

/// <summary>
/// Swaps the player into the prison jumpsuit by setting ONLY the body clothing component slots below,
/// then restores the originals on release. No head components are touched - no head/face (component 0),
/// no hair (component 2), no head blend, no overlays (eyebrows/beard/etc.), no model. So the player
/// keeps their identity AND their hair/eyebrows; only the body clothing slots change.
/// </summary>
public class PrisonOutfit
{
    private readonly Mod.Player Player;

    // Prison jumpsuit body-clothing components only. componentID, drawableID, textureID, paletteID.
    // Head components (0 head/face, 1 mask, 2 hair) are deliberately excluded so the player's
    // hair and eyebrows are never altered.
    private static readonly int[][] Jumpsuit =
    {
        new[] { 11, 32, 0, 0 },
        new[] { 6, 7, 0, 0 },
        new[] { 7, 103, 0, 0 },
        new[] { 8, 15, 0, 0 },
        new[] { 4, 27, 2, 0 },
        new[] { 3, 3, 0, 0 },
        new[] { 10, 0, 0, 0 },
    };

    private readonly List<int[]> Saved = new List<int[]>();
    private bool HasSaved;

    public PrisonOutfit(Mod.Player player)
    {
        Player = player;
    }

    public void Apply()
    {
        Ped ped = Player.Character;
        if (ped == null || !ped.Exists())
        {
            return;
        }
        Saved.Clear();
        foreach (int[] c in Jumpsuit)
        {
            int componentID = c[0];
            int drawable = NativeFunction.Natives.GET_PED_DRAWABLE_VARIATION<int>(ped, componentID);
            int texture = NativeFunction.Natives.GET_PED_TEXTURE_VARIATION<int>(ped, componentID);
            int palette = NativeFunction.Natives.GET_PED_PALETTE_VARIATION<int>(ped, componentID);
            Saved.Add(new[] { componentID, drawable, texture, palette });
            NativeFunction.Natives.SET_PED_COMPONENT_VARIATION(ped, componentID, c[1], c[2], c[3]);
        }
        HasSaved = true;
        EntryPoint.WriteToConsole($"Incarceration: applied prison jumpsuit ({Jumpsuit.Length} clothing slots, head untouched)", 0);
    }

    public void Restore()
    {
        if (!HasSaved)
        {
            return;
        }
        Ped ped = Player.Character;
        if (ped == null || !ped.Exists())
        {
            return;
        }
        foreach (int[] s in Saved)
        {
            NativeFunction.Natives.SET_PED_COMPONENT_VARIATION(ped, s[0], s[1], s[2], s[3]);
        }
        EntryPoint.WriteToConsole("Incarceration: restored original clothing", 0);
    }
}
