# Lesson 9d: chokepoints, part 1

Some factions **need** some goods (`chokepoint_settings.py` `NEEDS`): every
town in Hearthfall needs irrigation rights, every attunement town crystals,
everyone minted currency and notarial seals. The game's trade now works as
the generator's history did:

1. **Need.** A town of a faction that needs a good (by its path, its
   lineage, or `"all"`) needs `"amount"` more of it for every thousand people.
   Running short of it is hardship, like any good it needs.
2. **The year after.** A town short of a good last year has a smaller harvest
   (`"harvest"`), buys less from other factions (`"trade"`), crafts less
   (`"craft"`), and its faction works fewer rituals (`"rituals"`): each a
   share lost when wholly short, less when partly.
3. **Embargo.** A faction will not sell a needed good to a faction it thinks
   less of than `EMBARGO_OPINION`. Once it refuses, it sells again only when
   it thinks `LIFT_MARGIN` (0.1) better than that, so an embargo does not
   come and go with every small change of heart. The Book tells both.
4. **Resentment.** A faction short of a good blames (`RESENTMENT` times how
   short) those who refused it, and neighbours who had some to spare.
5. **Stand-ins** (`STAND_INS`). A town still short of a good after trade buys
   what stands in for it: block ice for rock salt.
6. **Smugglers** (`SMUGGLERS`). The towns of those factions hand goods on to
   anyone, and goods passing through them slip past an embargo.

**Part 2 (lesson 9e)** brings the rest: new supply contracts, sabotage, labour
displacement, secrets, legitimacy, the tech leak and debased coin. Until then
the game keeps what the history left of them and trades by it. A supply
contract goes first and is never embargoed, and it ends as in the generator.
A sabotaged town makes its good again after its years. Debased coin is
crafted less. But none of these is made new.

**Fixed on the way.** The game added up a town's staples with a plain sum,
where Python's `sum()` (since 3.12) adds decimals more carefully. The two can
differ in the last digit, and with chokepoints' limit on buying, that digit
showed. The game now adds them up as Python does.

## 1. Run it

1. Pull the generator and export a world again (the export now holds last
   year's shortages and the embargoes):
   `python worldgen.py --seed 7 --history 7 --size 512 --export`
2. In Godot, F5, open it, play 100 years. In the Book search for `refuse`
   (an embargo) and `again` (one lifted).

In three game centuries of seed 7 at 512 (game seeds 1 to 3) there were 9 to
20 embargoes, two lifted in each, and 71 to 82 towns short of something
at the end. The generator's own last century of that world had 12, and 76
towns short. On seed 42, history 42: 8 to 14 against the generator's 7.
History 44: 0 or 1 against 8. That world's history also had 45 sabotages,
and secrets that sour opinions, which come with part 2, so its factions grow
less hostile in the game for now.

(History 43 of seed 42 ends one year before every town falls: the great
dying struck in year 292, and all its towns have been below
`RUIN_POPULATION` for 9 of `RUIN_YEARS`, so year 301 ruins them all, as it
would in the generator.)

Saves are still version 8. An older save of version 8 loads: it keeps the
export's chokepoints.

## 2. Where the code is

- **`src/Sim/Chokepoints.cs`**: `AddNeeds` and `NeedsOf` (who needs what);
  `BeginYear` (last year's shortages: each town's harvest, each faction's
  rituals) and `Adjust` (crafting, debased coin, how much a town may buy from
  other factions, sabotage); `Refuses` (an embargo) and `ContractFirst`;
  `AfterTrade` (this year's shortages, resentment, the embargoes' news, and
  the contracts that end).
- **`src/Sim/Trade.cs`**: `MakeMarkets` adds the needs, `TradeGoods` runs the
  stand-ins (`StandIn`) after the crafted goods, `Match` puts contracts first,
  caps what a town buys from others, and asks `Embargoed`; `LocalJourneys`
  lets goods through a smuggler's town.
- **`src/Sim/YearCore.cs`**: a shortage's harvest first of all the harvest's
  factors, and `AfterTrade` right after the year's trade.
- **`src/Sim/Rituals.cs`**: `ByPath`, fewer rituals for a faction short of
  what they need.

How the tests prove it (`tests/ChokepointTests.cs`): `make_expected.py` runs
two years of the generator's own chokepoints and trade on the test world:
embargoes at an opinion of 0 (so many begin), the first living faction with a
lineage made smugglers, a stand-in for a good still short, a supply contract
that ends, and a sabotaged town. Between the years every embargo's holder
comes to think well of its buyer, so all of them are lifted. The game does the
same, and both years are compared to the last digit: every market (made,
needed, bought, sold, stood in, needs, trade factor), every sale, the harvest
and ritual factors, the shortages, the embargoes, the contracts, the
smuggled goods and the events. The blame is compared to twelve decimals. The
generator adds a shortage's blame over a set of goods, whose order changes
from run to run, so the last digits can too. The game adds them in order of
name. Other tests check an embargo, a contract and smugglers on their own,
that an embargo lifts only past the margin, fewer rituals, sabotage, no
chokepoints without needs, and that a save plays on the same.

All the other cross-checks (trade, learning, war, rulers and the rest) now
trade with the test world's chokepoints on both sides.

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `HashSet<(int Holder, int Buyer, string Good)>` | a set of tuples | `Game.Embargoes` |
| `game.Refused.Except(game.Embargoes)` | `self.refused - self.embargoes` | `Chokepoints.TellEmbargoes` |
| `string?` as the answer, `null` for none | `return None` | `Chokepoints.Refuses` |
| `market.CraftingMetal.Remove(good, out double metal)` | `market.crafting_metal.pop(good, 0.0)` | `Chokepoints.Adjust` |

## 3. Exercise

1. In `chokepoint_settings.py`, set `EMBARGO_OPINION` to 0.0. Export, play 100
   years: how many embargoes does the Book show (`refuse`)?
2. Make `RESENTMENT` 0.3. Do more wars begin over want (search `war`)?
3. Take `"whisperward"` out of `SMUGGLERS`. Are there more famines (`ruin`)?
4. Give attunement's crystals `"rituals": 1.0`. Do the attunement factions
   work fewer harvest rituals?
