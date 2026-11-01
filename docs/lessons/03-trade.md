# Lesson 3: trade

Towns now trade again, as they did in the generator's history: what one town
has to spare goes to a neighbour that needs it, hand to hand along the routes.
A town whose own land grows too little food is fed by its neighbours, and the
mines are dug for the metal the crafters need.

## 1. Run it

1. Export a world again in the generator (the export now holds what trade needs
   to go on: each town's near places, each mine's town, each faction's skills):
   `python worldgen.py --seed 42 --history 42 --size 512 --export`
2. In Godot, F5, open it, and play some years. Open the **Book**: mines run dry
   as the years go on ("The mine ... ran dry").

## 2. A year's trade, step by step

Open **`src/Sim/Trade.cs`** beside the generator's `north/trade.py`. Each
function is named after the Python one it ports:

1. **`MakeMarkets`** (`make_markets`): what every town makes (its land's yields,
   food times the year's harvest; metal from the mines it works) and needs (each
   good, so much per 1000 people of its path). Crafters can make crafted goods
   (by their path, their faction's lineage, the paths they have learned), and
   need metal for them.
2. **`TradeGoods`** (`trade_goods`): metal and the land's goods first, then the
   crafters work (`Craft`), then crafted goods, then the staple foods together.
3. **`LocalJourneys`** (`local_journeys`): with TRADE_HANDS = 4 (in
   `trade_settings.py`), goods go along at most 4 routes, none longer than
   TRADE_REACH, and only between towns whose factions think well enough of each
   other (MIN_OPINION_TO_TRADE).
4. **`Match`** (`match`): every seller with every buyer it can reach, the
   cheapest journey first, as much as one can spare and the other needs.
5. **`Dig`** (`dig`): the metal is taken out of the mines; greed digs more.

Then **`YearCore.UpdateFactions`**: each faction's people and metal, and every
memory fades, warmed by trade with those it traded with. (Opinions themselves
come with step 4.)

How the tests prove it: `tests/fixtures/make_expected.py` runs the generator's
own `make_markets`, `trade_goods`, `fed_share` and `dig` on the test world;
`tests/TradeTests.cs` makes the C# give the same markets, sales, route volumes,
food and metal, to nine decimals.

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `var offers = new List<(double Cost, int Seller, int Buyer)>(); offers.Sort();` | a list of tuples, sorted | `Match` |
| `PriorityQueue<int, (double, int)>` | `heapq` | `AllJourneys` |
| `markets[i].Made.GetValueOrDefault(good)` | `markets[i].made.get(good, 0.0)` | everywhere |
| `public sealed record Journeys(...)` | a small class holding two dictionaries | `Trade.Journeys` |

## 3. Exercise

1. In the generator's `trade_settings.py`, set `TRADE_HANDS = 1` (goods only to
   direct neighbours). Export, play 50 years: are more towns hungry (look at
   the book for towns that shrink)? Set it back.
2. Set `MIN_OPINION_TO_TRADE = 0.5`: only friends trade. What happens?
3. In `src/Sim/Trade.cs`, change `DigPerYear` to `1.0 / 10`. Play 100 years:
   how soon do the mines run dry? Change it back (it is the generator's
   DIG_PER_YEAR in `north/factions.py`).
