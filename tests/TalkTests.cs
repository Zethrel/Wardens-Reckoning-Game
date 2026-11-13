// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using WardensReckoning.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 25b: talk (library Review 42, Daily Life §11.1, the maintainer's R42-C03, and its verification table).
/// Founders on the meadow of FreeTimeTests, in their evening free time, with every other activity put off so that
/// they talk: nobody is made to talk, a talk is one event with one topic, recorded once, eased only for the time
/// there; someone leaving leaves it to go on or end; and all of it the same every run and across a save.
/// </summary>
public class TalkTests
{
    // Founders at `hour` with only talking to do, lonely enough to say yes now and then.
    static Colony.Colony OnlyTalk(int hour = 17, int seed = 7)
    {
        var colony = FreeTimeTests.Found(hour, seed: seed);
        foreach (var w in colony.Work!.Workers)
        {
            w.FreeNeeds["social"] = 80;
            foreach (var activity in colony.Content!.Leisure.Activities.Where(a => a.Place != "company"))
                w.NotBefore[activity.Id] = long.MaxValue;
        }
        return colony;
    }

    static void Until(Colony.Colony colony, Func<bool> done, int most = 2 * Calendar.TicksPerHour)
    {
        for (int i = 0; i < most && !done(); i++)
            colony.Run(1);
        Assert.True(done(), "not in " + most + " ticks");
    }

    [Fact]
    public void Two_free_colonists_talk_once_about_one_topic_and_it_is_recorded_once()
    {
        var colony = OnlyTalk();
        var work = colony.Work!;
        Until(colony, () => work.Talks.Any(t => t.Began != null));
        var talk = work.Talks.First(t => t.Began != null);
        Assert.Equal(2, talk.With.Count);
        string topic = talk.Topic!;
        var with = talk.With.ToList();
        // One end for both: they finish on the same tick, and the talk is recorded once, with both.
        long length = colony.Content!.Leisure.Activities.Single(a => a.Place == "company").MinutesEach * Calendar.TicksPerMinute;
        Until(colony, () => !work.Talks.Contains(talk));
        Assert.Equal(talk.Began!.Value + length, colony.Now.Tick);
        var record = Assert.Single(work.Talked);
        Assert.Equal((talk.Began.Value, colony.Now.Tick, topic), (record.Began, record.Ended, record.Topic));
        Assert.Subset(record.Took.ToHashSet(), with.ToHashSet());       // the two (and whoever joined them)
        Assert.Equal(record.Took.Count, record.Took.Distinct().Count());    // each once
    }

    [Fact]
    public void Talking_eases_social_for_the_time_there_and_the_same_for_both()
    {
        var colony = OnlyTalk();
        var work = colony.Work!;
        Until(colony, () => work.Talks.Any(t => t.Began != null));
        var talk = work.Talks.First(t => t.Began != null);
        var (a, b) = (work.Workers.Single(w => w.Id == talk.With[0]), work.Workers.Single(w => w.Id == talk.With[1]));
        double beforeA = a.FreeNeeds["social"], beforeB = b.FreeNeeds["social"];
        Until(colony, () => !work.Talks.Contains(talk));
        // 20 minutes at 60 an hour: 20 points, less the minute-by-minute rise (3 an hour: 1); the same for both.
        Assert.Equal(beforeA - a.FreeNeeds["social"], beforeB - b.FreeNeeds["social"], 6);
        Assert.InRange(beforeA - a.FreeNeeds["social"], 18.5, 20.01);
    }

    [Fact]
    public void Nobody_is_made_to_talk_who_is_asleep_at_work_or_has_said_no()
    {
        var colony = OnlyTalk();
        var work = colony.Work!;
        var all = work.Workers.ToList();
        work.Stop(all[2].Id);                             // ordered to stand: not free for it
        all[1].FreeNeeds["social"] = 0;
        all[1].Interests["company"] = Interest.Dislikes;  // 0.4 - 0.25: says yes about one time in seven
        var asked = new List<ColonistId>();
        for (int i = 0; i < 3 * Calendar.TicksPerHour; i++)
        {
            colony.Run(1);
            foreach (var talk in work.Talks)
                Assert.DoesNotContain(all[2].Id, talk.With);
        }
        Assert.NotEmpty(work.Talked.Concat(work.Talks.Select(t => new TalkRecord(0, 0, t.With, "")))
                            .Where(t => t.Took.Contains(all[1].Id)));    // asked often enough, they did say yes now and then
    }

    [Fact]
    public void Someone_leaving_a_talk_of_three_leaves_it_going_on_and_of_two_ends_it_for_the_other()
    {
        var colony = OnlyTalk();
        var work = colony.Work!;
        bool There(ColonistId id) => work.Workers.Single(w => w.Id == id).Outing?.Left != null;
        Until(colony, () => work.Talks.Any(t => t.Began != null && t.With.Count == 3 && t.With.All(There)), 3 * Calendar.TicksPerHour);
        var talk = work.Talks.First(t => t.With.Count == 3 && t.With.All(There));
        var first = work.Workers.Single(w => w.Id == talk.With[^1]);
        work.Stop(first.Id);                              // an order: they leave it
        Until(colony, () => !talk.With.Contains(first.Id), 20);
        Assert.Contains(talk, work.Talks);                // two remain: it goes on
        Assert.Equal(2, talk.With.Count);
        var second = work.Workers.Single(w => w.Id == talk.With[0]);
        var last = work.Workers.Single(w => w.Id == talk.With[1]);
        work.Stop(second.Id);
        Until(colony, () => !work.Talks.Contains(talk), 20);   // one left: it ends
        Until(colony, () => last.Outing == null, 20);           // and the one left stops
        var record = work.Talked.Single(r => r.Began == talk.Began);
        Assert.Equal(3, record.Took.Count);
        Assert.Single(work.Talked, r => r.Began == talk.Began);    // recorded once
    }

    [Fact]
    public void Topics_come_from_what_they_have_lived_and_never_tell_what_cannot_be_known()
    {
        var colony = OnlyTalk();
        var work = colony.Work!;
        var all = work.Workers.ToList();
        // A wall built today; an illness that has not shown yet (it must never be talked about).
        var (wall, _) = work.Plan("wall", 5, 25);
        all[0].Illness = new Illness("Upset stomach", colony.Now.Tick + 10 * Calendar.TicksPerDay, colony.Now.Tick + 11 * Calendar.TicksPerDay);
        var topics = new List<string>();
        for (int day = 0; day < 3; day++)
        {
            for (int i = 0; i < 3 * Calendar.TicksPerHour; i++)
            {
                colony.Run(1);
                foreach (var talk in work.Talks.Where(t => t.Topic != null))
                    if (!topics.Contains(talk.Topic!))
                        topics.Add(talk.Topic!);
            }
            colony.Run(21 * Calendar.TicksPerHour);
            foreach (var w in all)
                w.FreeNeeds["social"] = 80;
        }
        Assert.Contains("the journey here", topics);
        Assert.DoesNotContain(topics, t => t.Contains("stomach"));
        Assert.All(topics, t => Assert.True(t.Split(' ').Length <= 5, "\"" + t + "\" is more than a few words"));
        Assert.True(topics.Count >= 3, string.Join(", ", topics));
    }

    [Fact]
    public void Each_speaker_shows_the_bubble_in_turn()
    {
        var colony = OnlyTalk();
        var work = colony.Work!;
        Until(colony, () => work.Talks.Any(t => t.Began != null && t.With.All(id => work.Workers.Single(w => w.Id == id).Outing?.Left != null)));
        var talk = work.Talks.First(t => t.Began != null);
        var speakers = new HashSet<ColonistId>();
        int turn = colony.Content!.Leisure.Talk.BubbleTicks;
        for (int i = 0; i < 2 * turn && work.Talks.Contains(talk); i++)
        {
            var (speaker, words) = work.Bubble(talk)!.Value;
            Assert.Equal(talk.Topic, words);
            speakers.Add(speaker);
            colony.Run(1);
        }
        Assert.Equal(2, speakers.Count);
    }

    [Fact]
    public void The_same_seeds_give_the_same_talks_and_topics_and_a_save_goes_on_as_if_never_saved()
    {
        string Talks(Colony.Colony c) => string.Join(";", c.Work!.Talked.Select(r => r.Began + "-" + r.Ended + ":" + r.Topic + ":" + string.Join(",", r.Took.Select(i => i.Value))));
        var a = OnlyTalk();
        var b = OnlyTalk();
        a.Run(3 * Calendar.TicksPerHour);
        b.Run(3 * Calendar.TicksPerHour);
        Assert.Equal(Talks(a), Talks(b));
        Assert.NotEmpty(a.Work!.Talked);

        var colony = OnlyTalk();
        long end = colony.Now.Tick + 3 * Calendar.TicksPerHour;
        var copies = new List<Colony.Colony>();
        bool caught = false;
        colony.Run(end - colony.Now.Tick, c =>
        {
            if (c.Now.Tick % 61 != 0)
                return;
            caught |= c.Work!.Talks.Any(t => t.Began != null) && c.Work.Talks.Any();
            string text = ColonySave.Text(c);
            var copy = ColonySave.Read(text, c.Content!);
            Assert.Equal(text, ColonySave.Text(copy));
            copies.Add(copy);
        });
        Assert.True(caught, "no save caught a talk");
        string fingerprint = ColonySave.Fingerprint(colony);
        foreach (var copy in copies.Where((_, i) => i % 12 == 0))
        {
            copy.Run(end - copy.Now.Tick);
            Assert.Equal(fingerprint, ColonySave.Fingerprint(copy));
        }
    }
}
