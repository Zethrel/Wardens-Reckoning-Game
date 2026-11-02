// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WardensReckoning.Sim;

/// <summary>A sentence in chronicle_settings.py that cannot be filled: it names a slot the event does not have.</summary>
public sealed class ChronicleException(string message) : Exception(message);

/// <summary>
/// The chronicle as it happens (the generator's north/chronicle.py; your sentences are in chronicle_settings.py),
/// step 10. The game's years are cut into eras of ERA_YEARS on the history's grid (300 to 349, 350 to 399...). When
/// an era ends, its MOST_PER_ERA most notable events get your sentences, as the generator tells the history's eras:
///
/// - **Notable**: an event's score is its kind's weight in NOTABLE (0 or missing: never told), DEAD_BONUS more for
///   every ten times as many people it touched (its dead, or the people who moved).
/// - **Chosen one at a time**: the highest score first (the earlier on a tie); every event of a kind already chosen n
///   times counts REPEAT_FADE to the power n. Only kinds you wrote a sentence for are told. The chosen are told in
///   the order they happened.
/// - **Your sentence**: one of the kind's TEMPLATES, chosen by the chronicle's dice ("chronicle"), with {year} and the
///   event's facts in its slots, and the first letter of every sentence made a capital.
/// </summary>
public static class Chronicle
{
    /// <summary>How notable an event is (chronicle.score): its weight, DEAD_BONUS more for every ten times as many
    /// people it touched.</summary>
    public static double Score(GameEvent happened, GameSettings settings)
    {
        double weight = settings.Notable.GetValueOrDefault(happened.What);
        if (weight <= 0)
            return 0.0;
        // facts.get("dead", facts.get("people", 0)): a number, or 0.
        string? fact = happened.Facts.TryGetValue("dead", out var dead) ? dead
                       : happened.Facts.TryGetValue("people", out var people) ? people : null;
        double touched = fact != null && double.TryParse(fact, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && n > 0
            ? n : 0.0;
        return weight * (1 + Math.Log10(1 + touched) * settings.DeadBonus);
    }

    /// <summary>
    /// The `most` highest-scoring of `events` (chronicle.notable), as their places in `events`, in the order they
    /// happened: chosen one at a time, the highest score first (the earlier on a tie); every event of a kind already
    /// chosen n times counts `fade` to the power n of its score.
    /// </summary>
    public static List<int> Notable(IReadOnlyList<GameEvent> events, GameSettings settings)
    {
        var waiting = new List<(double Score, int Number)>();
        for (int i = 0; i < events.Count; i++)
        {
            double score = Score(events[i], settings);
            if (score > 0)
                waiting.Add((score, i));
        }
        var told = new Dictionary<string, int>();
        var chosen = new List<int>();
        while (waiting.Count > 0 && chosen.Count < settings.MostPerEra)
        {
            // max(waiting, key=(score * fade ** times told, -number)): the first of the highest.
            int best = 0;
            double bestValue = double.NegativeInfinity;
            for (int k = 0; k < waiting.Count; k++)
            {
                var (score, number) = waiting[k];
                double value = score * Math.Pow(settings.RepeatFade, told.GetValueOrDefault(events[number].What));
                if (value > bestValue)
                    (best, bestValue) = (k, value);
            }
            int picked = waiting[best].Number;
            waiting.RemoveAt(best);
            chosen.Add(picked);
            told[events[picked].What] = told.GetValueOrDefault(events[picked].What) + 1;
        }
        chosen.Sort();
        return chosen;
    }

    /// <summary>
    /// One sentence (chronicle.fill): `template` with {year} and the event's facts in its slots, as Python's
    /// str.format fills them ("{{" and "}}" are braces), and every sentence's first letter a capital.
    /// </summary>
    public static string Fill(string template, GameEvent happened)
    {
        var text = new StringBuilder();
        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];
            if ((c == '{' || c == '}') && i + 1 < template.Length && template[i + 1] == c)
            {
                text.Append(c);
                i++;
                continue;
            }
            if (c == '}')
                throw new ChronicleException("chronicle_settings.py: the template \"" + template + "\" for \"" + happened.What
                                             + "\" has a } without its {");
            if (c != '{')
            {
                text.Append(c);
                continue;
            }
            int close = template.IndexOf('}', i + 1);
            if (close < 0)
                throw new ChronicleException("chronicle_settings.py: the template \"" + template + "\" for \"" + happened.What
                                             + "\" has a { without its }");
            string slot = template[(i + 1)..close];
            if (slot == "year")
                text.Append(happened.Year.ToString(CultureInfo.InvariantCulture));
            else if (happened.Facts.TryGetValue(slot, out var value))
                text.Append(value);
            else
                throw new ChronicleException("chronicle_settings.py: the template \"" + template + "\" for \"" + happened.What
                    + "\" cannot be filled: no {" + slot + "}; it can use: {year}"
                    + string.Concat(happened.Facts.Keys.Order(StringComparer.Ordinal).Select(k => ", {" + k + "}")));
            i = close;
        }
        return Capitals(text.ToString());
    }

    /// <summary>chronicle.SENTENCE_START: the start of the text, or ".", "!" or "?" (not the last dot of "...") and
    /// spaces; then any opening quotes or brackets, and the first letter or digit.</summary>
    static readonly Regex SentenceStart = new(@"(^|(?<!\.\.)[.!?]\s+)([""'(\[]*)(\w)");

    /// <summary>The text with the first letter of every sentence in it made a capital (chronicle.capitals).</summary>
    public static string Capitals(string text) =>
        SentenceStart.Replace(text, m => m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value.ToUpperInvariant());

    /// <summary>
    /// The sentences for the notable events of one era (the events given), with `random` for the choice among a
    /// kind's sentences (random.choice): (place in `events`, sentence), in the order they happened.
    /// </summary>
    public static List<(int Index, string Sentence)> Tell(IReadOnlyList<GameEvent> events, GameSettings settings,
                                                          Func<double> random)
    {
        var written = new List<int>();
        for (int i = 0; i < events.Count; i++)
            if (Score(events[i], settings) > 0 && settings.Templates.ContainsKey(events[i].What))
                written.Add(i);
        var picked = Notable(written.Select(i => events[i]).ToList(), settings);
        var told = new List<(int, string)>();
        foreach (int k in picked)
        {
            var happened = events[written[k]];
            var templates = settings.Templates[happened.What];
            told.Add((written[k], Fill(templates[(int)(random() * templates.Count)], happened)));
        }
        return told;
    }

    /// <summary>The eras of `years` years (chronicle.eras): (first year, last year) from year 0 every `eraYears`; a
    /// last era shorter than half of `eraYears` joins the one before.</summary>
    public static List<(int Start, int End)> Eras(int years, int eraYears)
    {
        var chapters = new List<(int, int)>();
        for (int start = 0; start <= years; start += eraYears)
            chapters.Add((start, Math.Min(years, start + eraYears - 1)));
        if (chapters.Count > 1 && chapters[^1].Item2 - chapters[^1].Item1 + 1 < eraYears / 2.0)
        {
            var last = chapters[^1];
            chapters.RemoveAt(chapters.Count - 1);
            var before = chapters[^1];
            chapters[^1] = (before.Item1, last.Item2);
        }
        return chapters;
    }

    /// <summary>
    /// The end of a game year (step 10): when `year` ends an era on the history's grid (the next year is a multiple of
    /// ERA_YEARS), the era's game events are told. Without the chronicle's settings, nothing.
    /// </summary>
    public static void YearEnds(Game game, int year)
    {
        var settings = game.Settings;
        if (settings.ChronicleEraYears <= 0 || (year + 1) % settings.ChronicleEraYears != 0)
            return;
        int start = year + 1 - settings.ChronicleEraYears;
        var indexes = Enumerable.Range(0, game.Events.Count)
                                .Where(i => game.Events[i].Year >= start && game.Events[i].Year <= year).ToList();
        var era = indexes.Select(i => game.Events[i]).ToList();
        foreach (var (k, sentence) in Tell(era, settings, game.DiceFor("chronicle").Random))
            game.Tell(indexes[k], sentence);
    }
}
