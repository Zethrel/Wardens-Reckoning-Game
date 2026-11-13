// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Colony;

/// <summary>
/// A talk under way (step 25b; library Review 42, Daily Life §11.1): who is in it (in the order they joined), where
/// (the tile the first one asked was on), when it began (null while the one who asked is still on the way), what it
/// is about (chosen when it begins), and everyone who took part once it began (for the record).
/// </summary>
public sealed class Talk
{
    public long Id { get; init; }
    public List<ColonistId> With { get; } = new();
    public double X { get; init; }
    public double Y { get; init; }
    public long? Began { get; internal set; }
    public string? Topic { get; internal set; }
    public List<ColonistId> Took { get; } = new();
}

/// <summary>A finished talk, kept for the relationships to come (Social Relationships §10): when, who took part,
/// what about.</summary>
public sealed record TalkRecord(long Began, long Ended, IReadOnlyList<ColonistId> Took, string Topic);

/// <summary>Something that happened in the colony, worth talking about for a while (step 25b): a piece built, a
/// collapse, the arrival. `Words` fills the content's pattern (the piece's name, the colonist's name).</summary>
public sealed record Happening(long Tick, string Kind, string Words);

/// <summary>
/// Talk (step 25b; library Review 42, Daily Life §11.1, with the maintainer's R42-C03): two or more colonists in their
/// free time talk.
///
/// - **Asking.** At a think, talk is one of the free-time activities a colonist may choose (content: the activity in
///   "company"). They ask the nearest colonist who is **free for it**: in their own free time, not asleep, collapsed,
///   at work, stopped or seeing to a need, nothing urgent, and not already talking or on their way somewhere. That
///   colonist **agrees or not** on dice of their own ("talk"): more likely the lonelier they are and if they enjoy
///   company. Nobody is made to talk. Or the colonist joins a talk under way nearby, up to `Most`.
/// - **One talk.** The one asked stays where they are; the one who asked walks over. When they arrive the talk
///   begins: one topic, one end, the same for everyone in it, each eased for the time they were there.
/// - **Leaving.** Someone who leaves (their block ends, a need, an order) leaves the talk: it goes on with two or
///   more, and otherwise ends; the others stop at their next processing, credited up to the tick it ended.
/// - **Recorded once,** when it ends, with everyone who took part.
/// </summary>
public sealed partial class Work
{
    readonly SortedDictionary<long, Talk> talks = new();
    readonly List<TalkRecord> talked = new();
    readonly List<Happening> happenings = new();
    long nextTalk = 1;

    /// <summary>The talks under way.</summary>
    public IReadOnlyCollection<Talk> Talks => talks.Values;
    /// <summary>The finished talks kept (the latest `Kept`).</summary>
    public IReadOnlyList<TalkRecord> Talked => talked;
    /// <summary>What has happened lately, to talk about.</summary>
    public IReadOnlyList<Happening> Happenings => happenings;

    /// <summary>The talk state of a save (step 25b): what a loaded colony starts from.</summary>
    internal void PutTalks(long next, IEnumerable<Talk> under, IEnumerable<TalkRecord> records, IEnumerable<Happening> happened)
    {
        nextTalk = next;
        foreach (var talk in under)
            talks[talk.Id] = talk;
        talked.AddRange(records);
        happenings.AddRange(happened);
    }

    /// <summary>The next talk's number (for the save).</summary>
    internal long NextTalk => nextTalk;

    /// <summary>Something worth talking about happened (step 25b). Older happenings are forgotten.</summary>
    internal void Note(string kind, string words)
    {
        happenings.RemoveAll(h => colony.Now.Tick - h.Tick > content.Leisure.Talk.RecentHours * Calendar.TicksPerHour);
        happenings.Add(new Happening(colony.Now.Tick, kind, words));
    }

    /// <summary>Whether a colonist is free to be asked to talk (step 25b): in their free time, nothing urgent, not
    /// busy, and not on their way anywhere (at a stop of an outing is fine: they may leave it to talk).</summary>
    bool FreeToTalk(Worker p) =>
        FreeBlock(p) && p.Job == null && !p.Stopped && !p.Asleep && p.Collapsed == null && p.Personal == null && Level(p) < 3
        && (p.Outing == null || p.Outing.Talk == null && p.Outing.Left != null);

    /// <summary>
    /// Where a talk could be had (step 25b), for choosing: a talk under way within reach, not full, joined beside its
    /// place; or else the nearest colonist free for it, walked to. Null: nobody. Asking (and the answer) comes only if
    /// talking is chosen.
    /// </summary>
    (double X, double Y, long? Join, ColonistId? Ask)? TalkSpot(Worker w)
    {
        double within = content.Leisure.WithinMetres;
        double Dist(double x, double y) => Math.Sqrt((x - w.X) * (x - w.X) + (y - w.Y) * (y - w.Y));
        foreach (var talk in talks.Values.Where(t => t.Began != null && t.With.Count < content.Leisure.Talk.Most)
                                         .OrderBy(t => Dist(t.X, t.Y)).ThenBy(t => t.Id))
            if (Dist(talk.X, talk.Y) <= within && Beside(w, talk.X, talk.Y) is var (jx, jy))
                return (jx, jy, talk.Id, null);
        foreach (var p in workers.Values.Where(p => p != w && FreeToTalk(p)).OrderBy(p => Dist(p.X, p.Y)).ThenBy(p => p.Id.Value))
            if (Dist(p.X, p.Y) <= within && Beside(w, p.X, p.Y) is var (bx, by))
                return (bx, by, null, p.Id);
        return null;
    }

    /// <summary>A spot to stand beside (x, y) to talk, reachable from the colonist: the nearest open tile next to it
    /// held by no outing (without a ground: a metre away).</summary>
    (double X, double Y)? Beside(Worker w, double x, double y)
    {
        if (Ground is not Ground g)
            return (x + 1, y);
        var held = workers.Values.Where(o => o != w && o.Outing != null).Select(o => Ground.TileOf(o.Outing!.X, o.Outing.Y)).ToHashSet();
        var (tx, ty) = Ground.TileOf(x, y);
        var (wx, wy) = Ground.TileOf(w.X, w.Y);
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, 1), (1, -1), (-1, -1) })
        {
            int bx = tx + dx, by = ty + dy;
            if (g.Open(bx, by) && g.PieceAt(bx, by) == null && !held.Contains((bx, by)) && g.CanReach(wx, wy, bx, by))
                return Ground.Centre(bx, by);
        }
        return null;
    }

    /// <summary>
    /// A colonist who chose to talk sets about it (step 25b): joins the talk, or asks. The one asked agrees on the
    /// "talk" dice (`Willing`, plus their Social need, plus or minus an interest in company); if so, a talk is made
    /// with both, the one asked leaves what they were doing (no wait: they chose this) and stays where they are. True
    /// if there is a talk to go to.
    /// </summary>
    bool StartTalk(Worker w, (double X, double Y, long? Join, ColonistId? Ask) spot, Activity activity)
    {
        Talk talk;
        if (spot.Join is long join)
            talk = talks[join];
        else
        {
            var p = workers[spot.Ask!.Value.Value];
            double chance = content.Leisure.Talk.Willing + p.FreeNeeds.GetValueOrDefault("social") / 200
                            + p.Interests.GetValueOrDefault("company") switch { Interest.Enjoys => 0.25, Interest.Dislikes => -0.25, _ => 0 };
            if (colony.DiceFor("talk").Uniform(0, 1) >= chance)
                return false;                              // not now: nobody is made to talk
            if (p.Outing != null)
            {
                p.Outing = null;                           // they leave what they were doing to talk
                p.Heading = null;
            }
            talk = new Talk { Id = nextTalk++, X = p.X, Y = p.Y };
            talks[talk.Id] = talk;
            talk.With.Add(p.Id);
            p.Outing = new Outing { Activity = activity.Id, X = p.X, Y = p.Y, Talk = talk.Id };
            p.ActionDue = null;
        }
        talk.With.Add(w.Id);
        w.Outing = new Outing { Activity = activity.Id, X = spot.X, Y = spot.Y, Talk = talk.Id };
        w.LastActivity = activity.Id;
        Head(w, spot.X, spot.Y);
        return true;
    }

    /// <summary>
    /// A talking colonist is processed (step 25b). The one asked waits where they are. The one walking walks; on
    /// arriving, the talk begins (if it has not) or they join it for the time it has left. Once there, the ticks pass
    /// as at any stop. A talk that ended while they were on their way: unavailable.
    /// </summary>
    /// <returns>True if the usual stay below should go on (they are at the talk).</returns>
    bool SpendTalk(Worker w, Outing o)
    {
        if (!talks.TryGetValue(o.Talk!.Value, out var talk))
        {
            if (o.Left == null)
                EndOuting(w, interrupted: true);           // it ended before they got there
            return o.Left != null;                         // credited up to its end (set when it ended), then over
        }
        if (o.Left != null)
            return true;
        bool waiting = talk.With[0] == w.Id && talk.Began == null;
        if (waiting)
            return false;                                  // asked: they stay where they are until the other comes
        if (double.IsPositiveInfinity(Far(w.X, w.Y, o.X, o.Y, Reach)))
        {
            EndOuting(w, interrupted: true);
            return false;
        }
        if (!Walk(w, o.X, o.Y))
            return false;
        long now = colony.Now.Tick;
        var activity = content.Leisure.Activities.First(a => a.Id == o.Activity);
        int length = activity.MinutesEach * Calendar.TicksPerMinute;
        if (talk.Began == null)
        {
            // They are both there: the talk begins, one topic, one end, for both.
            talk.Began = now;
            talk.Topic = TopicFor(talk);
            foreach (var id in talk.With)
            {
                var p = workers[id.Value];
                p.Outing!.Left = length;
                if (!talk.Took.Contains(id))
                    talk.Took.Add(id);
                if (p == w)
                    continue;
                // The one who waited: their time in the talk starts now, not when they were last processed (the wait
                // eases nothing), and they are woken when it ends.
                p.Thought = now;
                p.ActionDue = now + length;
            }
        }
        else
        {
            // Joining a talk under way, for what is left of it.
            o.Left = (int)Math.Max(1, talk.Began.Value + length - now);
            if (!talk.Took.Contains(w.Id))
                talk.Took.Add(w.Id);
        }
        return false;                                      // arriving takes this processing; the stay counts from now
    }

    /// <summary>
    /// A colonist leaves a talk (step 25b): their outing has ended. With fewer than two left, the talk ends: it is
    /// recorded once, and the one left stops at their next processing, credited only up to now (their stay is cut to
    /// the ticks since they were last processed).
    /// </summary>
    void LeaveTalk(Worker w, long id)
    {
        if (!talks.TryGetValue(id, out var talk))
            return;
        talk.With.Remove(w.Id);
        if (talk.With.Count >= 2)
            return;
        talks.Remove(id);
        long now = colony.Now.Tick;
        if (talk.Began is long began && talk.Topic is string topic)
        {
            talked.Add(new TalkRecord(began, now, talk.Took.ToList(), topic));
            if (talked.Count > content.Leisure.Talk.Kept)
                talked.RemoveAt(0);
        }
        foreach (var id2 in talk.With)
        {
            var p = workers[id2.Value];
            if (p.Outing is not Outing po || po.Talk != id)
                continue;
            if (po.Left != null)
            {
                po.Left = (int)Math.Max(0, now - p.Thought);   // only the time until now counts
                p.ActionDue = now + 1;
            }
            else
                p.ActionDue = now + 1;                     // on the way, or waiting: they find it over
        }
    }

    /// <summary>
    /// What a talk is about (step 25b), drawn on the "talk" dice from what its colonists have lived, by weight: what
    /// happened in the colony lately, someone visibly ill, their own wants, work and interests they enjoy, the time
    /// of day; and everyday topics, lightest. Nothing the player could not know: an illness only once it shows.
    /// </summary>
    string TopicFor(Talk talk)
    {
        var t = content.Leisure.Topics;
        var options = new List<(string Words, double Weight)>();
        long now = colony.Now.Tick;
        foreach (var h in happenings.Where(h => now - h.Tick <= content.Leisure.Talk.RecentHours * Calendar.TicksPerHour))
            options.Add((h.Kind switch
            {
                "built" => t.Built.Replace("{piece}", h.Words),
                "collapsed" => t.Collapsed.Replace("{name}", h.Words),
                _ => t.Arrived,
            }, 3));
        foreach (var ill in workers.Values.Where(Ill))
            options.Add((t.Ill.Replace("{name}", ill.Name).Replace("{illness}", ill.Illness!.Name.ToLowerInvariant()), 3));
        foreach (var id in talk.With)
        {
            var p = workers[id.Value];
            foreach (var (need, words) in new[] { ("hunger", t.Hungry), ("thirst", t.Thirsty), ("fatigue", t.Tired), ("hygiene", t.Unwashed) })
                if (content.Needs.TryGetValue(need, out var rule) && p.Needs.TryGetValue(need, out var state) && LevelOf(rule, state.Level) >= 2)
                    options.Add((words, 2));
            foreach (var (what, words) in t.Enjoys)
                if (p.Interests.GetValueOrDefault(what) == Interest.Enjoys)
                    options.Add((words, 2));
        }
        if (content.Leisure.Times.ContainsKey("dusk") && content.Leisure.InTime("dusk", colony.Now.Hour))
            options.Add((t.Dusk, 1));
        if (content.Leisure.Times.ContainsKey("dark") && content.Leisure.InTime("dark", colony.Now.Hour))
            options.Add((t.Dark, 1));
        foreach (string words in t.Everyday)
            options.Add((words, 0.5));
        double roll = colony.DiceFor("talk").Uniform(0, options.Sum(o => o.Weight));
        foreach (var (words, weight) in options)
        {
            if (roll < weight)
                return words;
            roll -= weight;
        }
        return options[^1].Words;
    }

    /// <summary>The words for a talk in the inspector (step 25b).</summary>
    string TalkWords(Worker w, Outing o)
    {
        if (!talks.TryGetValue(o.Talk!.Value, out var talk))
            return "talking";
        string others = string.Join(" and ", talk.With.Where(id => id != w.Id).Select(id => workers[id.Value].Name));
        if (talk.Began == null)
            return talk.With[0] == w.Id ? "waiting for " + others + " to talk" : "going to talk with " + others;
        return o.Left == null ? "going to join " + others + " talking" : "talking with " + others + " about " + talk.Topic;
    }

    /// <summary>Who shows a bubble for a talk now, and its words (step 25b, for the screen): those there take turns,
    /// each for `BubbleTicks`.</summary>
    public (ColonistId Speaker, string Words)? Bubble(Talk talk)
    {
        if (talk.Began is not long began || talk.Topic is not string topic)
            return null;
        var there = talk.With.Where(id => workers[id.Value].Outing?.Left != null).ToList();
        if (there.Count == 0)
            return null;
        long turn = (colony.Now.Tick - began) / Math.Max(1, content.Leisure.Talk.BubbleTicks);
        return (there[(int)(turn % there.Count)], topic);
    }
}
