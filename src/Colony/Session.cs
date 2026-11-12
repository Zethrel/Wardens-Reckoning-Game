// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Colony;

/// <summary>
/// A colony being played (step 16): what the game's screen will hold and call once a frame. It turns real time
/// into ticks at the chosen speed (the `Pacer`), runs the colony, makes the morning autosave into its three slots
/// (library Review 30), and saves and loads the player's own saves.
///
/// When saving fails (a full disk, a missing folder), play goes on: the failure is added to `Reports`, for the
/// screen to show the player (library Review 35). Nothing in here touches Godot, so it is tested like the rest.
/// </summary>
public sealed class Session
{
    readonly List<string> reports = new();

    /// <summary>Plays `colony`, writing its autosaves into `autosaves` (a folder of their own, apart from the
    /// player's saves; it is made if it is not there).</summary>
    public Session(Colony colony, string autosaves)
    {
        Colony = colony;
        Autosaves = autosaves;
    }

    public Colony Colony { get; private set; }
    public string Autosaves { get; }
    public Pacer Pacer { get; } = new();

    /// <summary>The speed the player chose (paused, ×1, ×2, ×3 or ×5: library Review 41).</summary>
    public Speed Speed
    {
        get => Pacer.Speed;
        set => Pacer.Speed = value;
    }

    /// <summary>Called just before each tick a frame runs (the screen notes where everyone stands, to draw them
    /// walking smoothly from there).</summary>
    public event Action? BeforeTick;

    /// <summary>What the player should be told, oldest first: failed saves, warnings about a loaded save.</summary>
    public IReadOnlyList<string> Reports => reports;

    /// <summary>
    /// A frame: `seconds` of real time have passed. Runs the ticks now due, one at a time, and makes the autosave
    /// as soon as a morning (06:00) is passed. Returns how many ticks ran.
    /// </summary>
    public int Frame(double seconds)
    {
        int ticks = Pacer.TicksFor(seconds);
        for (int i = 0; i < ticks; i++)
        {
            var before = Colony.Now;
            BeforeTick?.Invoke();
            Colony.Run(1);
            if (Autosave.MorningPassed(before, Colony.Now))
                AutosaveNow();
        }
        return ticks;
    }

    /// <summary>Writes the autosave for today into its slot (no backup: the three slots are the history). Returns
    /// the file written, or null if it failed (the failure is reported, and play goes on).</summary>
    public string? AutosaveNow()
    {
        string path = Autosave.PathOf(Autosaves, Autosave.SlotFor(Colony.Now));
        try
        {
            Directory.CreateDirectory(Autosaves);
            ColonySave.Save(path, Colony, backup: false);
            return path;
        }
        catch (Exception e) when (e is ColonySaveException or IOException or UnauthorizedAccessException)
        {
            reports.Add("The morning autosave failed: " + e.Message);
            return null;
        }
    }

    /// <summary>The player saves to `path` (the save it replaces is kept as a backup). Returns whether it worked;
    /// if not, the reason is reported and the old save is as it was.</summary>
    public bool SaveAs(string path)
    {
        try
        {
            ColonySave.Save(path, Colony);
            return true;
        }
        catch (ColonySaveException e)
        {
            reports.Add(e.Message);
            return false;
        }
    }

    /// <summary>
    /// The player loads `path` in place of the colony being played. If the save cannot be loaded, the reason is
    /// reported and the colony being played goes on as it was. A save changed outside the game is only loaded if
    /// `acceptChanged` (the screen asks the player first). Warnings about the loaded save are reported.
    /// </summary>
    public bool LoadFrom(string path, bool acceptChanged = false)
    {
        try
        {
            var opened = ColonySave.Open(path, Colony.Content, acceptChanged);
            Colony = opened.Colony;
            reports.AddRange(opened.Warnings);
            return true;
        }
        catch (ColonySaveException e)
        {
            reports.Add(e.Message);
            return false;
        }
    }

    /// <summary>The newest autosave in `folder` that loads, or null: the slots are tried newest first (by the
    /// colony time they hold), and a slot that cannot be loaded is skipped and reported in `skipped`.</summary>
    public static Opened? Newest(string folder, Content content, List<string> skipped)
    {
        var found = new List<(long Now, Opened Opened)>();
        for (int slot = 1; slot <= Autosave.Slots; slot++)
        {
            string path = Autosave.PathOf(folder, slot);
            if (!File.Exists(path))
                continue;
            try
            {
                var opened = ColonySave.Open(path, content);
                found.Add((opened.Colony.Now.Tick, opened));
            }
            catch (ColonySaveException e)
            {
                skipped.Add(e.Message);
            }
        }
        return found.Count == 0 ? null : found.OrderByDescending(f => f.Now).First().Opened;
    }
}
