using LosSantosRED.lsr.Interface;
using Rage;
using Rage.Native;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

public class BurnerPhoneSettingsVolumeEntry : BurnerPhoneSettingsAppEntry
{
    private List<BurnerPhoneSettingTracker> BurnerPhoneSettingTrackers;

    public BurnerPhoneSettingsVolumeEntry(BurnerPhoneSettingsApp burnerPhoneSettingsApp, ISettingsProvideable settings, string name, int index, int icon) : base(burnerPhoneSettingsApp, settings, name, index, icon)
    {
        SelectedItemIcon = (int)BurnerPhoneSettingsIcon.Volume;
    }
    public override void Open(bool Reset)
    {
        BurnerPhoneSettingsApp.BurnerPhone.SetHeader(Text);
        if (Reset)
        {
            CurrentRow = 0;
        }
        NativeFunction.Natives.BEGIN_SCALEFORM_MOVIE_METHOD(BurnerPhoneSettingsApp.BurnerPhone.GlobalScaleformID, "SET_DATA_SLOT_EMPTY");
        NativeFunction.Natives.xC3D0841A0CC546A6(22);//2
        NativeFunction.Natives.END_SCALEFORM_MOVIE_METHOD();

        DisplayVolume();

        NativeFunction.Natives.BEGIN_SCALEFORM_MOVIE_METHOD(BurnerPhoneSettingsApp.BurnerPhone.GlobalScaleformID, "DISPLAY_VIEW");
        NativeFunction.Natives.xC3D0841A0CC546A6(22);
        NativeFunction.Natives.xC3D0841A0CC546A6(CurrentRow);
        NativeFunction.Natives.END_SCALEFORM_MOVIE_METHOD();
    }

    public override void HandleInput()
    {
        HandleIndex();
        HandleVolumeSelection();
        HandleBack();
        SetRingtoneSoftKeys();
    }
    private void DisplayVolume()
    {
        BurnerPhoneSettingTrackers = new List<BurnerPhoneSettingTracker>();
        for (int i = 0; i < 21; i++)
        {
            float percentValue = ((float)i) / 20.0f;
            string percentString = percentValue.ToString("P0");
            BurnerPhoneSettingTracker burnerPhoneSettingTracker = new BurnerPhoneSettingTracker(i, percentString) { Value = percentValue };
            if(BurnerPhoneSettingsApp.Player.CellPhone.Volume == percentValue)// if (Settings.SettingsManager.CellphoneSettings.DefaultCustomToneVolume == percentValue)
            {
                burnerPhoneSettingTracker.IsSelected = true;
            }
            BurnerPhoneSettingTrackers.Add(burnerPhoneSettingTracker);
            DrawSettingsItem(burnerPhoneSettingTracker.IsSelected ? SelectedItemIcon : NonSelectedItemIcon, burnerPhoneSettingTracker.Index, burnerPhoneSettingTracker.Name);
        }
        TotalItems = 21;
    }
    private void HandleVolumeSelection()
    {
        if (NativeFunction.Natives.x91AEF906BCA88877<bool>(3, 176))//SELECT
        {
            BurnerPhoneSettingsApp.BurnerPhone.MoveFinger(5);
            BurnerPhoneSettingsApp.BurnerPhone.PlayAcceptedSound();
            BurnerPhoneSettingTracker selectedItem = BurnerPhoneSettingTrackers.FirstOrDefault(x => x.Index == CurrentRow);
            if (selectedItem == null)
            {
                return;
            }
            BurnerPhoneSettingTracker oldSelected = BurnerPhoneSettingTrackers.FirstOrDefault(x => x.IsSelected);
            if (oldSelected != null)
            {
                oldSelected.IsSelected = false;
            }
            selectedItem.IsSelected = true;
            BurnerPhoneSettingsApp.Player.CellPhone.CustomVolume = selectedItem.Value;
            BurnerPhoneSettingsApp.Player.CellPhone.PreviewRingtoneSound();
            Open(false);
        }
    }
    protected void SetRingtoneSoftKeys()
    {
        BurnerPhoneSettingsApp.BurnerPhone.SetSoftKey((int)SoftKey.Left, SoftKeyIcon.Blank, Color.Red);
        BurnerPhoneSettingsApp.BurnerPhone.SetSoftKey((int)SoftKey.Middle, SoftKeyIcon.Select, Color.LightGreen);
        BurnerPhoneSettingsApp.BurnerPhone.SetSoftKey((int)SoftKey.Right, SoftKeyIcon.Back, Color.Red);
    }
}

