using LosSantosRED.lsr.Interface;
using System.Collections.Generic;


public class VanillaRestrictedArea
{
    private bool isPlayerViolating;
    public bool IsPlayerViolating => isPlayerViolating;
    public List<AngledRestrictedArea> AngledRestrictedAreas { get; set; }
    public void Update(ILocationInteractable player)
    {
        isPlayerViolating = false;
        if (EntryPoint.IsLSPDFRIntegrationEnabled)
        {
            return;
        }
        // An inmate serving their sentence (and an authorized cop) is NOT trespassing inside the prison's
        // restricted area. The custom RestrictedArea.Update already honors this via CanEnterRestrictedAreas;
        // this vanilla path was missing the same check - that omission is what flagged the inmate as
        // trespassing and gave the immediate wanted level the moment they spawned into Bolingbroke.
        if (EntryPoint.PlayerIsIncarcerated || player.Violations.CanEnterRestrictedAreas)
        {
            return;
        }
        foreach(AngledRestrictedArea angledRestrictedArea in AngledRestrictedAreas)
        {
            if(angledRestrictedArea.CheckInside(player.Position))
            {
                isPlayerViolating = true;
                return;
            }
        }
    }
}

