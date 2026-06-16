using System.ComponentModel;
using System.Runtime.Serialization;

public class PrisonSettings : ISettingsDefaultable
{
    [Description("Master toggle for the interactive 'Serve Your Sentence' prison system.")]
    public bool IsEnabled { get; set; }
    [Description("Real seconds of serving per day of sentence. Sentence length scales with crimes.")]
    public float RealSecondsPerSentenceDay { get; set; }
    [Description("Minimum sentence length in days.")]
    public int MinSentenceDays { get; set; }
    [Description("Maximum sentence length in days.")]
    public int MaxSentenceDays { get; set; }
    [Description("Metres from the prison gate that counts as outside the walls (triggers an escape).")]
    public float EscapeRadius { get; set; }
    [Description("Allow breaking out of prison during a sentence.")]
    public bool AllowEscape { get; set; }
    [Description("Wanted level applied when the player escapes prison.")]
    public int EscapeWantedLevel { get; set; }
    [Description("Allow the player to incite a riot while serving a sentence.")]
    public bool AllowRiot { get; set; }
    [Description("Radius (metres) around the player that inmates/guards are pulled into a riot.")]
    public float RiotRadius { get; set; }
    [Description("How long a riot stays active, in seconds.")]
    public int RiotDurationSeconds { get; set; }
    [Description("Send the player to solitary confinement if they assault someone while serving.")]
    public bool AllowSolitary { get; set; }
    [Description("Extra days added to the sentence for being thrown in solitary.")]
    public int SolitaryDays { get; set; }
    [Description("Real seconds spent in solitary before returning to general population, per solitary day.")]
    public float SolitaryRealSecondsPerDay { get; set; }
    [Description("Let vanilla prison scenario peds (guards/inmates) populate the yard while serving, instead of LSR suppressing them. The prison-mod-faithful way.")]
    public bool UnsuppressVanillaPeds { get; set; }
    [Description("Manually spawn extra prisoners/guards while serving (fallback for MLOs that have no vanilla prison peds). Off by default since UnsuppressVanillaPeds handles population.")]
    public bool PopulateYard { get; set; }
    [Description("Number of inmates to spawn around the player while serving.")]
    public int YardPrisonerCount { get; set; }
    [Description("Number of guards to spawn around the player while serving.")]
    public int YardGuardCount { get; set; }
    [Description("Release X coordinate (where the player is let out after serving).")]
    public float ReleaseX { get; set; }
    [Description("Release Y coordinate.")]
    public float ReleaseY { get; set; }
    [Description("Release Z coordinate.")]
    public float ReleaseZ { get; set; }
    [Description("Release heading.")]
    public float ReleaseHeading { get; set; }

    public PrisonSettings()
    {
        SetDefault();
    }
    [OnDeserialized()]
    private void SetValuesOnDeserialized(StreamingContext context)
    {
        SetDefault();
    }
    public void SetDefault()
    {
        IsEnabled = true;
        RealSecondsPerSentenceDay = 12f;
        MinSentenceDays = 1;
        MaxSentenceDays = 30;
        EscapeRadius = 220f;
        AllowEscape = true;
        EscapeWantedLevel = 4;
        AllowRiot = true;
        RiotRadius = 60f;
        RiotDurationSeconds = 120;
        AllowSolitary = true;
        SolitaryDays = 3;
        SolitaryRealSecondsPerDay = 12f;
        UnsuppressVanillaPeds = true;
        PopulateYard = true;
        YardPrisonerCount = 10;
        YardGuardCount = 4;
        ReleaseX = 1856.91f;
        ReleaseY = 2607.069f;
        ReleaseZ = 45.67218f;
        ReleaseHeading = 256.0832f;
    }
}
