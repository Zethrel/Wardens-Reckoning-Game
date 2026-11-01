# Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
"""Makes the test world and what the C# reader must find in it, with the generator's own reader.

Run from this folder, with the generator's folder (Zethrel/Wardens-Reckoning) as the argument:

    python make_expected.py D:\\Zethrel-Github-Projects\\Wardens-Reckoning

It exports a small world (seed 7, history 7, 128 x 128, 100 years) into world-7/ without the sprites,
writes expected.json (what north/world_file.py reads from it in a few years, and how the history atlas
would draw its towns), and filtered.png (a grid saved with every PNG row filter, as a paint program may).
"""
import hashlib
import json
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).parent
YEARS = [0, 1, 37, 64, 100]


def towns_drawn(world, year):
    """The history atlas's drawing of the towns (the JavaScript in north/atlas.py), in Python."""
    data, record = world.data, world.data["history"]
    capitals = set()
    for index in range(len(data["factions"])):
        state = world.faction(index, year) or {}
        people = data["population"][index][year] if year < len(data["population"][index]) else 0
        if people > 0 and state.get("capital") is not None:
            capitals.add(state["capital"])
    sizes = data["pictures"]["settlement_sizes"]

    def size_of(people):
        name = sizes[0][1]
        for low, n in sizes:
            if people >= low:
                name = n
        return name

    def peak_to(town):
        return max([0] + town["people"][:min(year - town["from"], len(town["people"]) - 1) + 1])

    order = [i for i, town in enumerate(record["towns"]) if town["from"] <= year]
    order.sort(key=lambda i: -peak_to(record["towns"][i]))
    drawn = []
    for i in order:
        town, place, state = record["towns"][i], data["settlements"][i], world.town(i, year)
        builder = state["holder"]
        if state["ruined"]:
            for since, faction in town["owners"]:
                if since <= year and faction is not None:
                    builder = faction
        size = size_of(peak_to(town) if state["ruined"] else state["people"])
        drawn.append([i, place["x"], place["y"], builder, size, state["ruined"],
                      not state["ruined"] and builder is not None and i in capitals])
    return drawn


def one_year(data):
    """One year's core for every standing town, worked out with the generator's own functions, with an ordinary
    year's weather (1.0) and no trade: the harvest as path and ruler change it, how well the town is fed, its
    people after growing or starving, and its size after (history.path_harvests, ruled_harvests, fed_share with
    a trade.Market of its own land, grow, keep_band)."""
    from types import SimpleNamespace
    from north import history, trade
    going = data["continue"]
    settings = going["settings"]
    goods = settings["trade"]["GOODS"]
    trading = SimpleNamespace(GOODS=goods)
    staples, extras = trade.staple_goods(trading), trade.extra_goods(trading)
    factions_now = [SimpleNamespace(path=f["path"], government=f["government"],
                                    ruler=None if f["ruler"] is None else SimpleNamespace(traits=f["ruler"]["traits"]))
                    for f in data["factions"]]
    lows = [low for low, *_rest in settings["settlement"]["SETTLEMENT_SIZES"]]
    growing = SimpleNamespace(**settings["history"])
    path_settings = SimpleNamespace(PATH_HARVEST=settings["factions"]["PATH_HARVEST"],
                                    PATH_STEADINESS=settings["factions"]["PATH_STEADINESS"])
    result = []
    for index, (place, town) in enumerate(zip(data["settlements"], going["towns"])):
        if place["ruined"]:
            result.append(None)
            continue
        here = SimpleNamespace(culture=place["culture"])
        harvest = history.path_harvests([1.0], [here], factions_now, path_settings)
        harvest = history.ruled_harvests(harvest, [here], factions_now, settings["leaders"]["GOVERNMENTS"])[0]
        path = factions_now[place["culture"]].path if place["culture"] is not None else None
        market = trade.Market()
        for good, amount in town["yields"].items():
            market.made[good] = amount * harvest if good in staples + extras else amount
        for good, details in goods.items():
            market.needed[good] = place["population"] / 1000 * trade.need_of(details, path)
        fed = history.fed_share(market, staples, extras)
        people = history.grow(town["people"], place["capacity"], fed, growing)
        size = history.keep_band(round(people), lows, town["size"], lows[town["size"]] * history.SIZE_MARGIN)
        result.append({"harvest": harvest, "fed": fed, "people": people, "size": size})
    return result


def one_year_of_trade(data):
    """One year's markets and trade, with an ordinary year's weather, worked out with the generator's own
    trade.make_markets, trade.trade_goods, history.fed_share and history.dig, on the exported towns, factions,
    mines and routes (map cells for places, as the game has them). What each town made, needed, bought and
    sold of every good, how well it was fed, how much passed along each route, and the metal left in each mine."""
    from collections import defaultdict
    from types import SimpleNamespace
    from north import history, trade
    going = data["continue"]
    settings = going["settings"]
    places = [SimpleNamespace(x=p["x"], y=p["y"], culture=p["culture"], ruined=p["ruined"],
                              population=p["population"], name=p["name"]) for p in data["settlements"]]
    owner = defaultdict(dict)
    for mine, kept in zip(data["mines"], going["mines"]):
        owner[mine["y"]][mine["x"]] = kept["land_town"]
    near = defaultdict(lambda: defaultdict(lambda: defaultdict(bool)))
    for place, town in zip(places, going["towns"]):
        for kind in town["near"]:
            near[kind][place.y][place.x] = True
    settled = SimpleNamespace(owner=owner, near_places=near)
    factions = [SimpleNamespace(index=i, name=f["name"], path=f["path"], lineage=f["lineage"], learned=set(f["learned"]),
                                skills=going["factions"][i]["skills"], traits=f["traits"],
                                opinions={int(k): v for k, v in f["opinions"].items()},
                                government=f["government"],
                                ruler=None if f["ruler"] is None else SimpleNamespace(traits=f["ruler"]["traits"]))
                for i, f in enumerate(data["factions"])]
    deposits = [SimpleNamespace(x=m["x"], y=m["y"], metal=m["metal"], owner=m["owner"], first=m["first"], name=m["name"])
                for m in data["mines"]]
    powers = SimpleNamespace(factions=factions, deposits=deposits)
    trading = SimpleNamespace(**settings["trade"])
    path_settings = SimpleNamespace(PATH_HARVEST=settings["factions"]["PATH_HARVEST"],
                                    PATH_STEADINESS=settings["factions"]["PATH_STEADINESS"])
    harvest = history.path_harvests([1.0] * len(places), places, factions, path_settings)
    harvest = history.ruled_harvests(harvest, places, factions, settings["leaders"]["GOVERNMENTS"])
    yields = [town["yields"] for town in going["towns"]]
    markets = trade.make_markets(places, settled, None, powers, settings["factions"]["METAL_NEED"], trading,
                                 yields, harvest)
    routes = [trade.Route(r["a"], r["b"], [], r["cost"]) for r in data["routes"]]
    deals = trade.trade_goods(places, routes, markets, powers, trading, None, going["history_columns"] / 256)
    staples, extras = trade.staple_goods(trading), trade.extra_goods(trading)
    fed = [None if place.ruined else history.fed_share(market, staples, extras) for place, market in zip(places, markets)]
    events = []
    history.dig(powers, places, settled, markets, 1, events)
    from north import factions as faction_rules, leaders, war
    supply = war.supplies(places, markets, staples, len(factions))
    faction_settings = SimpleNamespace(**settings["factions"])
    people = [f["population"] for f in data["factions"]]
    memory = {(a, b): value for a, b, value in going["memory"]}
    judged = []
    for a, f in enumerate(data["factions"]):
        mine = SimpleNamespace(index=a, path=f["path"], traits=f["traits"], prejudice=f["prejudice"],
                               metal_supply=f["metal_supply"], metal_need=f["metal_need"], belief=f["belief"],
                               border={int(k): v for k, v in f["border"].items()})
        mine.metal_balance = lambda m=mine: faction_rules.Faction.metal_balance(m)
        judged.append(mine)
    opinions = [[a, b, faction_rules.opinion(judged[a], judged[b], faction_settings, memory.get((a, b), 0.0),
                                             war.covet(supply, a, b))]
                for a in range(len(judged)) for b in range(len(judged))
                if a != b and people[a] > 0 and people[b] > 0]
    fit = [leaders.path_fit(f["values"]) for f in data["factions"]]
    against = [None if f["ruler"] is None else leaders.against_values(SimpleNamespace(traits=f["ruler"]["traits"]),
                                                                      f["values"]) for f in data["factions"]]
    # War (step 5): each living faction's whole strength, the reasons to fight each neighbour, the fronts between
    # them; how much each conquered town resents its rulers next year, and its unrest; the borders counted on the
    # export's last borders; and the land shared out from the farms (factions.spread_territory, without costs).
    governments = settings["leaders"]["GOVERNMENTS"]
    rulers = [SimpleNamespace(index=i, population=f["population"], government=f["government"],
                              traits=f["traits"], belief=f["belief"], grievance=f["grievance"],
                              opinions={int(k): v for k, v in f["opinions"].items()},
                              ruler=None if f["ruler"] is None else SimpleNamespace(traits=f["ruler"]["traits"]))
              for i, f in enumerate(data["factions"])]
    alive = [f.index for f in rulers if f.population > 0]
    strengths = {str(i): war.strength(rulers[i], supply, governments) for i in alive}
    # An army fighting from one town: that town's people and reinforcements from the rest (as in a battle).
    size_in_battle = settings["war"]["SIZE_IN_BATTLE"]
    fighting_from = {}
    for i in alive:
        mine = [t for t, place in enumerate(places) if place.culture == i and not place.ruined]
        if mine:
            local = round(going["towns"][mine[0]]["people"])   # the population, as the game counts it
            fighting_from[str(i)] = [mine[0], war.strength(rulers[i], supply, governments, local, war.held(places, i),
                                                           size_in_battle)]
    from north.death import WORSHIP_WAR
    constants = {name: getattr(war, name) for name in (
        "BATTLE_BITTERNESS", "CONQUEST_BITTERNESS", "BETRAYAL", "ALLY_MARGIN", "PEACE_CHANCE", "QUIET_YEARS",
        "WINNER_LOSSES", "REINFORCEMENTS", "DEFENDER_ADVANTAGE", "TAKE_CHANCE", "REBELLION", "RESENTMENT_HALF_LIFE",
        "BORDER_YEARS", "WAR_COMPETENCE")}
    constants["WORSHIP_WAR"] = WORSHIP_WAR
    war_routes = [SimpleNamespace(a=r["a"], b=r["b"]) for r in data["routes"]]
    neighbours, reasons, chances, odds = [], [], [], []
    beliefs = settings["factions"]["BELIEF_OPINIONS"] if "belief" in settings["war"]["WAR_WEIGHTS"] else None
    for a in alive:
        for b in alive:
            front = war.fronts(places, war_routes, a, b)
            if a != b and front:
                neighbours.append([a, b, front])
                why = war.reasons(rulers[a], rulers[b], supply, beliefs)
                reasons.append([a, b, why])
                # As war.year_of_war weighs them (its own lines, without the happenings' readiness of step 9).
                weighed = {reason: settings["war"]["WAR_WEIGHTS"][reason] * value for reason, value in why.items()}
                ready = WORSHIP_WAR if rulers[a].belief == "worship" else 1.0
                chances.append([a, b, settings["war"]["WARLIKE"] * ready * sum(weighed.values()),
                                max(sorted(weighed), key=lambda r: weighed[r])])
                # As war.battle reckons the two armies over the first route of the front, a attacking.
                here, there = front[0]
                odds.append([a, b, here, there,
                             war.strength(rulers[a], supply, governments, places[here].population,
                                          war.held(places, a), size_in_battle),
                             war.strength(rulers[b], supply, governments, places[there].population,
                                          war.held(places, b), size_in_battle) * war.DEFENDER_ADVANTAGE])
    year = data["years"] + 1
    war_settings = SimpleNamespace(**settings["war"])
    fighting = SimpleNamespace(wars=[SimpleNamespace(ended=w["ended"], sides=[set(s) for s in w["sides"]])
                                     for w in data["wars"]])
    towns_now = [SimpleNamespace(ruined=p["ruined"], origin=p["origin"], culture=p["culture"],
                                 conquered_in=p["conquered_in"]) for p in data["settlements"]]
    unrest = []
    for index, town in enumerate(towns_now):
        resented = war.resentment(town, year, war_settings)
        if resented > 0:
            unrest.append([index, resented, war.unrest(town, index, SimpleNamespace(factions=rulers, fighting=fighting),
                                                       towns_now, war_routes, governments)])
    grid = going["grid"]
    columns, rows = grid["columns"], grid["rows"]
    def unrun(runs):
        flat = []
        for i in range(0, len(runs), 2):
            flat += [runs[i]] * runs[i + 1]
        return flat
    owner_flat, ocean_flat = unrun(grid["owner"]), unrun(grid["ocean"])
    culture = data["history"]["borders"]
    borders_flat = None
    for since, shot in culture["snapshots"]:
        if "full" in shot:
            borders_flat = unrun(shot["full"])
        else:
            for i in range(0, len(shot["changes"]), 3):
                start, count, holder = shot["changes"][i:i + 3]
                borders_flat[start:start + count] = [holder] * count
    culture_of = [[None if v < 0 else v for v in borders_flat[y * columns:(y + 1) * columns]] for y in range(rows)]
    ocean = [[bool(v) for v in ocean_flat[y * columns:(y + 1) * columns]] for y in range(rows)]
    counted = [SimpleNamespace(border={}) for _ in rulers]
    faction_rules.count_borders(SimpleNamespace(ocean=ocean), culture_of, counted)
    owner = [[None if v < 0 else v for v in owner_flat[y * columns:(y + 1) * columns]] for y in range(rows)]
    spread = [[None] * columns for _ in range(rows)]
    faction_rules.spread_territory(places, owner, spread, ocean)
    return {
        "strengths": strengths,
        "fighting_from": fighting_from,
        "war_constants": constants,
        "war_chances": chances,
        "odds": odds,
        "fronts": neighbours,
        "reasons": reasons,
        "unrest": unrest,
        "borders_counted": [{str(k): v for k, v in f.border.items()} for f in counted],
        "spread": [-1 if v is None else v for row in spread for v in row],
        "opinions": opinions,
        "path_fit": fit,
        "against_values": against,
        "markets": [{"made": m.made, "needed": m.needed, "bought": m.bought, "sold": m.sold} for m in markets],
        "fed": fed,
        "deals": len(deals),
        "volumes": [route.volume for route in routes],
        "metal": [deposit.metal for deposit in deposits],
        "dry": [event.text for event in events],
    }


def splits_without_dice(data):
    """Splits (the game's step 6), where no dice are rolled, by the generator's own functions on the export: each
    faction's capital (splits.capital_of), fragility and how divided it is; for each faction of two towns or more
    with a ruler, its towns' journeys from the capital (scaled as year_of_splits does), its outer towns in order, and
    the province breaking away from the farthest and from the nearest of them; each faction's hold
    (splits.update_empires); how many factions hold land; and a new faction's shade (colours.kin_colour)."""
    from types import SimpleNamespace
    from north import colours, splits, trade
    from north.death import BELIEFS
    from north.factions import EXTRA_TRAITS, TRAITS
    going = data["continue"]
    settings = going["settings"]
    governments = settings["leaders"]["GOVERNMENTS"]
    places = [SimpleNamespace(x=p["x"], y=p["y"], culture=p["culture"], ruined=p["ruined"], population=p["population"])
              for p in data["settlements"]]
    routes = [trade.Route(r["a"], r["b"], [], r["cost"]) for r in data["routes"]]
    journeys = trade.all_journeys(places, routes)
    scale = 256 / going["history_columns"]
    factions = [SimpleNamespace(index=i, capital=f["capital"], traits=f["traits"], values=f["values"],
                                government=f["government"], grievance=f["grievance"], hold=1.0,
                                ruler=None if f["ruler"] is None else SimpleNamespace(traits=f["ruler"]["traits"]))
                for i, f in enumerate(data["factions"])]
    capitals = [splits.capital_of(f, places) for f in factions]
    provinces = []
    for f in factions:
        held = splits.holdings(places, f.index)
        if len(held) < 2 or f.ruler is None:
            continue
        distances = {town: cost * scale for town, cost in journeys[f.capital][0].items()}
        outer = sorted((t for t in held if t != f.capital), key=lambda t: (-distances.get(t, 0.0), t))
        provinces.append({"faction": f.index, "outer": outer,
                          "distances": {str(t): distances.get(t, 0.0) for t in held},
                          "farthest": splits.province(outer[0], f.index, distances, places, routes),
                          "nearest": splits.province(outer[-1], f.index, distances, places, routes)})
    powers = SimpleNamespace(factions=factions)
    splits.update_empires(powers, places, SimpleNamespace(**settings["splits"]))
    living = [f.index for f in factions if splits.holdings(places, f.index)]
    others = [tuple(data["factions"][i]["colour"]) for i in living]
    shades = [[i, list(colours.kin_colour(tuple(data["factions"][i]["colour"]), others))] for i in living]
    shades.append([-1, list(colours.kin_colour((22, 18, 28), others))])
    shades.append([-2, list(colours.kin_colour((200, 40, 40), []))])
    return {
        "capitals": capitals,
        "fragility": [splits.fragility(f) for f in factions],
        "divided": [splits.divided(f, governments) for f in factions],
        "provinces": provinces,
        "holds": [f.hold for f in factions],
        "living": splits.living(powers, places),
        "kin_colours": shades,
        "constants": {"BITTER": splits.BITTER, "WARM": splits.WARM, "SMALL": splits.SMALL,
                      "MERGE_CHANCE": splits.MERGE_CHANCE, "FRAGILITY_RANGE": list(splits.FRAGILITY_RANGE),
                      "SHADES": list(colours.SHADES), "SPLIT_KINDS": splits.SPLIT_KINDS,
                      "TRAITS": list(TRAITS), "EXTRA_TRAITS": list(EXTRA_TRAITS), "BELIEFS": list(BELIEFS)},
    }


def main(generator):
    sys.path.insert(0, str(generator))
    from north import world_file
    from tests.test_pictures import png

    with tempfile.TemporaryDirectory() as out:
        subprocess.run([sys.executable, str(generator / "worldgen.py"), "--seed", "7", "--history", "7",
                        "--size", "128", "--years", "100", "--export", "--out", out], check=True,
                       stdout=subprocess.DEVNULL)
        exported = Path(out, "world-7", "h7", "export")
        target = HERE / "world-7"
        shutil.rmtree(target, ignore_errors=True)
        target.mkdir()
        for path in exported.iterdir():
            if path.is_file():
                shutil.copyfile(path, target / path.name)

    world = world_file.read_world(HERE / "world-7")
    expected = {"grids": {}, "years": {}}
    for name in world.data["grids"]:
        grid = world.grid(name)
        flat = ",".join(str(v) for row in grid for v in row)
        expected["grids"][name] = {"width": len(grid[0]), "height": len(grid),
                                   "sha256": hashlib.sha256(flat.encode()).hexdigest()}
    for year in YEARS:
        borders = world.borders(year)
        flat = ",".join(str(-1 if v is None else v) for row in borders for v in row) if borders else ""
        expected["years"][str(year)] = {
            "towns": [world.town(i, year) for i in range(len(world.data["history"]["towns"]))],
            "factions": [world.faction(i, year) for i in range(len(world.data["history"]["factions"]))],
            "borders_sha256": hashlib.sha256(flat.encode()).hexdigest(),
            "drawn": towns_drawn(world, year),
        }
    expected["one_year"] = one_year(world.data)
    expected["trade"] = one_year_of_trade(world.data)
    expected["splits"] = splits_without_dice(world.data)
    (HERE / "expected.json").write_text(json.dumps(expected, indent=1), encoding="utf-8")

    grid = [[(x * x * 7 + y * 31 + (x ^ y) * 13) % 256 for x in range(24)] for y in range(20)]
    grid[0][:5], grid[1][:5] = [10, 20, 30, 40, 50], [10, 20, 30, 40, 50]    # equal neighbours: the filters' ties
    (HERE / "filtered.png").write_bytes(png(grid, colour=0, step=1, filters=(0, 1, 2, 3, 4)))
    (HERE / "filtered.json").write_text(json.dumps(grid), encoding="utf-8")


if __name__ == "__main__":
    main(Path(sys.argv[1]).resolve())
