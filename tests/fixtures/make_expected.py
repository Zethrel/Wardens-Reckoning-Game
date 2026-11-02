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


def trade_year(data):
    """One year's markets and deals by the generator's trade.make_markets and trade.trade_goods on the export, with
    an ordinary year's weather: the places, factions, settings and the rest, for the parts that follow."""
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
    choke = chokepoints_of(going)
    markets = trade.make_markets(places, settled, None, powers, settings["factions"]["METAL_NEED"], trading,
                                 yields, harvest, choke)
    routes = [trade.Route(r["a"], r["b"], [], r["cost"]) for r in data["routes"]]
    deals = trade.trade_goods(places, routes, markets, powers, trading, None, going["history_columns"] / 256, choke)
    return SimpleNamespace(places=places, factions=factions, markets=markets, deals=deals, settled=settled,
                           powers=powers, deposits=deposits, trading=trading, routes=routes, going=going, settings=settings,
                           choke=choke)


def chokepoints_of(going):
    """The generator's chokepoints.Chokepoints from an export's "continue" (None without chokepoints): the settings,
    last year's shortages, the embargoes, debased coin, contracts and sabotaged towns."""
    from types import SimpleNamespace
    from north import chokepoints
    kept = going.get("chokepoints")
    choke = chokepoints.from_settings(SimpleNamespace(**going["settings"]["chokepoints"]), 1)
    if choke is None or kept is None:
        return choke
    choke.short = {int(t): dict(goods) for t, goods in kept["short"].items()}
    choke.embargoes = {tuple(e) for e in kept["embargoes"]}
    choke.debased = {int(f): v for f, v in kept["debased"].items()}
    choke.smuggled = kept["smuggled"]
    choke.contracts = {(buyer, good): maker for buyer, good, maker in kept["contracts"]}
    choke.sabotaged = {int(t): tuple(e) for t, e in kept["sabotaged"].items()}
    return choke


def one_year_of_trade(data):
    """One year's markets and trade, with an ordinary year's weather, worked out with the generator's own
    trade.make_markets, trade.trade_goods, history.fed_share and history.dig, on the exported towns, factions,
    mines and routes (map cells for places, as the game has them). What each town made, needed, bought and
    sold of every good, how well it was fed, how much passed along each route, and the metal left in each mine."""
    from collections import defaultdict
    from types import SimpleNamespace
    from north import history, trade
    year = trade_year(data)
    places, factions, markets, deals, settled, powers, trading, routes, going, settings, deposits = (
        year.places, year.factions, year.markets, year.deals, year.settled, year.powers, year.trading, year.routes,
        year.going, year.settings, year.deposits)
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
                               border={int(k): v for k, v in f["border"].items()}, race=data["faction_races"][a])
        mine.metal_balance = lambda m=mine: faction_rules.Faction.metal_balance(m)
        judged.append(mine)
    opinions = [[a, b, faction_rules.opinion(judged[a], judged[b], faction_settings, memory.get((a, b), 0.0),
                                             war.covet(supply, a, b))]
                for a in range(len(judged)) for b in range(len(judged))
                if a != b and people[a] > 0 and people[b] > 0]
    fit = [leaders.path_fit(f["values"]) for f in data["factions"]]
    # How each faction's rulers age (the lore of 1 November 2026: LIFESPAN_SCALING), by its race.
    age_scales = [leaders.age_scale(race, SimpleNamespace(**settings["leaders"])) for race in data["faction_races"]]
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
        "age_scales": age_scales,
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


def feeding_and_drift(data):
    """Feeding by tastes and drift (the game's step 6b), by the generator's own functions on the export: each town's
    food, capacity and harvest worked out for its holder's people and path, and for another people and path
    (tastes.Tastes.capacity and yields, on the cells it farms); and one year of drift from the export's end
    (migration.year_of_drift)."""
    from types import SimpleNamespace
    from north import migration, splits, tastes
    going = data["continue"]
    settings = going["settings"]
    grid, land = going["grid"], going["land"]
    columns, rows = grid["columns"], grid["rows"]

    def unrun(runs):
        flat = []
        for i in range(0, len(runs), 2):
            flat += [runs[i]] * runs[i + 1]
        return flat
    owner, ocean, lake = unrun(grid["owner"]), unrun(grid["ocean"]), unrun(land["lake"])
    names = data["biomes"]
    biomes = [[None if b < 0 else {"name": names[b]} for b in unrun(land["biome"])[y * columns:(y + 1) * columns]]
              for y in range(rows)]
    likes = tastes.Tastes([{"name": p["name"]} for p in data["peoples"]], SimpleNamespace(**settings["settlement"]),
                          SimpleNamespace(**settings["trade"]), biomes, [[0.0] * columns for _ in range(rows)], None,
                          0.0, None, SimpleNamespace(**settings["factions"]))
    cells = [[] for _ in data["settlements"]]
    for cell, town in enumerate(owner):
        if 0 <= town < len(cells) and not ocean[cell] and not lake[cell]:
            cells[town].append((cell % columns, cell // columns))
    paths = sorted(settings["factions"]["PATHS"])
    fed = []
    for index, place in enumerate(data["settlements"]):
        holder = place["culture"]
        mine = (None, None) if holder is None else (data["factions"][holder]["people"], data["factions"][holder]["path"])
        other = (index % len(data["peoples"]), paths[index % len(paths)])
        fed.append({"cells": [y * columns + x for x, y in cells[index]],
                    "mine": [list(mine), *likes.capacity(cells[index], *mine), likes.yields(cells[index], *mine)],
                    "other": [list(other), *likes.capacity(cells[index], *other), likes.yields(cells[index], *other)]})

    culture = unrun(going_borders(data))
    culture_of = [[None if v < 0 else v for v in culture[y * columns:(y + 1) * columns]] for y in range(rows)]
    kinds = []
    for cell, code in enumerate(unrun(land["kinds"])):
        if code:
            kinds.append((cell % columns, cell // columns, bool(code & 2), bool(code & 4), bool(code & 8)))
    factions = [SimpleNamespace(index=i, nature=dict(f["nature"]), values=dict(f["values"]))
                for i, f in enumerate(data["factions"])]
    places = [SimpleNamespace(culture=p["culture"], ruined=p["ruined"]) for p in data["settlements"]]
    moves = migration.Movements()
    moves.targets = {int(k): dict(v) for k, v in going["drift_targets"].items()}
    powers = SimpleNamespace(factions=factions, movements=moves)
    year = data["years"] + 1
    from north.factions import shares_of
    shares = shares_of(kinds, culture_of, len(factions))
    migration.year_of_drift(powers, places, SimpleNamespace(kinds=kinds, culture_of=culture_of),
                            SimpleNamespace(**settings["factions"]), SimpleNamespace(**settings["migration"]), year)
    looked = migration.Movements()
    looked.targets = {}
    again = [SimpleNamespace(index=i, nature=dict(f["nature"]), values=dict(f["values"]))
             for i, f in enumerate(data["factions"])]
    migration.year_of_drift(SimpleNamespace(factions=again, movements=looked), places,
                            SimpleNamespace(kinds=kinds, culture_of=culture_of),
                            SimpleNamespace(**settings["factions"]), SimpleNamespace(**settings["migration"]), 11)
    return {
        "towns": fed,
        "paths": paths,
        "shares": shares,
        "values_next_year": [f.values for f in factions],
        "targets_looked_at": {str(k): v for k, v in looked.targets.items()},
        "values_looked_at": [f.values for f in again],
        "living": [i for i in range(len(factions)) if splits.holdings(places, i)],
    }


def migration_year(data):
    """A year of migration (the game's step 6c) by the generator's own migration.year_of_migration, with MIGRATION
    at 1 so it rolls no dice: every crowded or hungry town with somewhere to go sends MIGRANTS of its people. Also
    the usual free sites worked out again, own site scores for every people on every path, the nearness of the
    mines, and what a new town at the first free site would claim and its route home would cost. Whole grids are
    kept as the SHA-256 of their exact 8-byte numbers, to keep this file small."""
    import base64
    from types import SimpleNamespace
    import numpy as np
    from north import migration, settlements, tastes, trade
    from north.factions import Deposit
    going = data["continue"]
    settings = going["settings"]
    moving = going["migration"]
    grid, land = going["grid"], going["land"]
    columns, rows = grid["columns"], grid["rows"]
    factor = data["history_factor"]

    def unrun(runs):
        flat = []
        for i in range(0, len(runs), 2):
            flat += [runs[i]] * runs[i + 1]
        return flat

    def exact(text):
        return np.frombuffer(base64.b64decode(text), dtype="<f8").reshape(rows, columns).tolist()

    def rows_of(flat, cast=lambda v: v):
        return [[cast(v) for v in flat[y * columns:(y + 1) * columns]] for y in range(rows)]
    owner = rows_of(unrun(grid["owner"]), lambda v: None if v < 0 else v)
    ocean = rows_of(unrun(grid["ocean"]), bool)
    lake_flat = unrun(land["lake"])
    lakes = {(c % columns, c // columns) for c, v in enumerate(lake_flat) if v}
    names = data["biomes"]
    biomes = rows_of(unrun(land["biome"]), lambda b: None if b < 0 else {"name": names[b]})
    town_scores = np.frombuffer(base64.b64decode(moving["town_scores"]), dtype="<f8").tolist()
    places = [settlements.Settlement(p["x"] // factor, p["y"] // factor, town_scores[i], land=p["land"], food=p["food"],
                                     capacity=p["capacity"], population=p["population"], ruined=p["ruined"],
                                     peak=p["peak"], name=p["name"], culture=p["culture"], origin=p["origin"],
                                     conquered_in=p["conquered_in"], founded_from=p["founded_from"])
              for i, p in enumerate(data["settlements"])]
    parts = {name: exact(text) for name, text in moving["parts"].items()}
    settled = SimpleNamespace(scores=exact(moving["scores"]), parts=parts, settlements=places, owner=owner)
    water = SimpleNamespace(ocean=ocean, lake_cells=lambda: set(lakes))
    likes = tastes.Tastes([{"name": p["name"]} for p in data["peoples"]], SimpleNamespace(**settings["settlement"]),
                          SimpleNamespace(**settings["trade"]), biomes, exact(moving["heights"]), water,
                          moving["sea_level"], settled, SimpleNamespace(**settings["factions"]))
    for people, path, text in moving["tastes"]:
        likes.scores[(people, path)] = exact(text)
    factions = [SimpleNamespace(index=i, people=f["people"], path=f["path"], name=f["name"],
                                ruler=None if f["ruler"] is None else SimpleNamespace(name=f["ruler"]["name"]))
                for i, f in enumerate(data["factions"])]
    deposits = [Deposit(m["x"], m["y"], 1.0, owner=d["owner"]) for m, d in zip(going["mines"], data["mines"])]
    marks = None if moving["rifts"] is None else SimpleNamespace(size=moving["rifts"]["size"],
                                                                  severe={tuple(p) for p in moving["rifts"]["severe"]})
    moves = migration.Movements()
    moves.sites = None if moving["sites"] is None else [tuple(s) for s in moving["sites"]]
    moves.own_sites = {(people, path): [tuple(s) for s in listed] for people, path, listed in moving["own_sites"]}
    moves.cost = exact(moving["costs"])
    moves.lakes = set(lakes)
    powers = SimpleNamespace(factions=factions, deposits=deposits, marks=marks, movements=moves, fighting=None)
    culture_of = rows_of(unrun(going_borders(data)), lambda v: None if v < 0 else v)
    routes = [trade.Route(r["a"], r["b"], [], r["cost"]) for r in data["routes"]]
    traded = SimpleNamespace(routes=routes, kind=None)
    pool = going["names"]["settlement"]
    given = [0] * len(pool)

    def name(people, kind):
        given[people] += 1
        return pool[people][given[people] - 1]
    namer = SimpleNamespace(name=name, used=set())
    people = [t["people"] for t in going["towns"]]
    hungry = [t["hungry"] for t in going["towns"]]
    moving_settings = SimpleNamespace(**dict(settings["migration"], MIGRATION=1.0, CROWDED=0.2))
    events = []

    def event(year, kind, text, what, facts):
        return {"year": year, "kind": kind, "text": text, "what": what, "facts": facts}
    year = data["years"] + 1
    usual = migration.free_sites(places, settled, SimpleNamespace(**settings["settlement"]))
    paths = sorted(settings["factions"]["PATHS"])
    fresh = tastes.Tastes([{"name": p["name"]} for p in data["peoples"]], SimpleNamespace(**settings["settlement"]),
                          SimpleNamespace(**settings["trade"]), biomes, exact(moving["heights"]), water,
                          moving["sea_level"], settled, SimpleNamespace(**settings["factions"]))

    def people_of(place):
        return factions[place.culture].people if place.culture is not None else None
    own = []
    for folk in range(len(data["peoples"])):
        for path in paths[:3]:
            if fresh.has_own_score(folk, path):
                grid_scores = fresh.score_grid(folk, places, deposits, people_of, path)
                own.append([folk, path, hashlib.sha256(np.asarray(grid_scores, dtype="<f8").tobytes()).hexdigest()])
    near_metal = settlements.nearness([(d.x, d.y) for d in deposits], columns, rows, tastes.METAL_REACH)
    spacing = migration.apart(settled, SimpleNamespace(**settings["settlement"]))
    first = usual[0]
    claimed = migration.claim(first[1], first[2], owner, ocean, lakes, spacing)
    trial = places + [settlements.Settlement(first[1], first[2], first[0])]
    home = min(range(len(places)), key=lambda i: (abs(places[i].x - first[1]) + abs(places[i].y - first[2]), i))
    route = trade.new_route(trial, moves.cost, home, len(places))

    done = migration.year_of_migration(powers, places, settled, traded, people, hungry, moving_settings,
                                       SimpleNamespace(**settings["settlement"]), __import__("random").Random(0), namer,
                                       year, events, event,
                                       SimpleNamespace(culture_of=culture_of, water=water, tastes=likes, owner=owner),
                                       settings["history"]["RUIN_POPULATION"])
    return {
        "usual_sites": [list(s) for s in usual],
        "own_scores": own,
        "near_metal": hashlib.sha256(np.asarray(near_metal, dtype="<f8").tobytes()).hexdigest(),
        "spacing": spacing,
        "claim": {"site": list(first), "cells": [y * columns + x for x, y in claimed]},
        "route": {"from": home, "cost": None if route is None else route.cost},
        "year": {
            "events": [[e["what"], e["text"]] for e in events],
            "moved": [[source, target, None if cells is None else [y * columns + x for x, y in cells]]
                      for source, target, cells in done],
            "towns": [{"x": p.x, "y": p.y, "name": p.name, "food": p.food, "capacity": p.capacity, "ruined": p.ruined,
                       "culture": p.culture, "origin": p.origin, "founded_from": p.founded_from,
                       "population": p.population} for p in places],
            "people": people,
            "owner": [-1 if v is None else v for row in owner for v in row],
            "regions": [-1 if v is None else v for row in culture_of for v in row],
            "routes": [[r.a, r.b, r.cost] for r in routes[len(data["routes"]):]],
            "sites": [list(s) for s in moves.sites],
            "own_sites": [[people_key, path, [list(s) for s in listed]]
                          for (people_key, path), listed in sorted(moves.own_sites.items(), key=lambda p: (p[0][0], str(p[0][1])))],
            "mine_owners": [d.owner for d in deposits],
        },
    }


def learning_and_capitals(data):
    """Learning and capitals (the game's step 7) by the generator's own learning.Learning and capitals.Capitals on
    the export, with every chance 0 or 1 so that no dice decide: one year of trade (its deals), an alliance of the
    first two living factions, and the first held town taken by the first other living faction that lacks a path
    its holder holds; then a year of learning
    with LEARN_BY_TRADE, _ALLIANCE, _BORDER and _CONQUEST at 1, LEARN_BY_EFFORT at 0 and FORGET_CHANCE at 1; each
    faction's held paths, skills (in order) and hybrids after it, and its events. Then capitals with CAPITAL_CHOICE
    "largest": the conquered town's old holder loses it if it was its capital, and with CAPITAL_MOVE_CHANCE 1 every
    faction of two towns or more moves to its largest other town."""
    from types import SimpleNamespace
    from north import capitals, learning
    year = trade_year(data)
    places, going, settings = year.places, year.going, year.settings
    for place, f in zip(places, data["settlements"]):
        place.conquered_in = None
    factions = []
    for i, f in enumerate(data["factions"]):
        kept = going["factions"][i]
        faction = SimpleNamespace(index=i, name=f["name"], path=f["path"], population=f["population"],
                                  values=f["values"], traits=f["traits"], settings_name=None, lineage=f["lineage"],
                                  border={int(k): v for k, v in f["border"].items()}, capital=f["capital"])
        if kept["held"] is not None:
            faction.held = set(kept["held"])
        factions.append(faction)
    chances = dict(settings["learning"], LEARN_BY_TRADE=1.0, LEARN_BY_ALLIANCE=1.0, LEARN_BY_BORDER=1.0,
                   LEARN_BY_CONQUEST=1.0, LEARN_BY_EFFORT=0.0, FORGET_CHANCE=1.0)
    skilled = learning.Learning(SimpleNamespace(**chances), 7)
    for faction in factions:                      # skills and hybrids as the year began (the game works them out)
        if hasattr(faction, "held"):
            faction.unlocked = set()
            skilled.update_skills(faction, None, None, None)
    start = {"skills": [list(map(list, getattr(f, "skills", {}).items())) for f in factions],
             "hybrids": [sorted(getattr(f, "unlocked", ())) for f in factions]}
    skilled.remember(factions, places)
    alive = [f.index for f in factions if f.population > 0]
    allied = (alive[0], alive[1])
    taken = next(t for t, p in enumerate(places) if not p.ruined and p.culture is not None)
    loser = places[taken].culture

    def holding(f):
        return {factions[f].path} | getattr(factions[f], "held", set())

    # The first living faction that lacks a path the loser holds (so it has something to learn by conquest).
    winner = next(i for i in alive if i != loser and holding(loser) - holding(i))
    places[taken].culture, places[taken].conquered_in = winner, 301
    events = []

    def event(year, kind, text, what, facts):
        return [year, kind, text, what, facts]

    skilled.year(factions, places, year.deals, SimpleNamespace(alliances={allied}), 301, events, event)
    learned = {"allied": list(allied), "taken": taken, "winner": winner, "start": start,
               "held": [sorted(getattr(f, "held", ())) for f in factions],
               "skills": [list(map(list, getattr(f, "skills", {}).items())) for f in factions],
               "hybrids": [sorted(getattr(f, "unlocked", ())) for f in factions],
               "events": [[e[2], e[3]] for e in events]}
    seats = capitals.Capitals("largest", 1.0, 7)
    seats.start(factions)
    events = []
    seats.mend(factions, places, 301, events, event)
    after_mend = [f.capital for f in factions]
    seats.mend(factions, places, 301, events, event, moving=True)
    learned["capitals"] = {"mended": after_mend, "moved": [f.capital for f in factions],
                           "events": [[e[2], e[3]] for e in events]}
    return learned


def marks_of(going):
    """The generator's attunement.Marks from an export's "continue"."marks"."""
    from north import attunement
    kept = going["marks"]
    marks = attunement.Marks(kept["size"], kept["columns"], kept["rows"], {tuple(p) for p in kept["land"]},
                             {(c, r): (c2, r2) for c, r, c2, r2 in kept["downstream"]},
                             [{(c, r): n for c, r, n in land} for land in kept["lands"]])
    for c, r, value, maker in kept["mark"]:
        marks.mark[(c, r)] = value
        if maker is not None:
            marks.maker[(c, r)] = maker
    marks.permanent = {tuple(p) for p in kept["permanent"]}
    marks.severe = {tuple(p) for p in kept["severe"]}
    marks.rifts = [(n, (c, r), y, m) for n, c, r, y, m in kept["rifts"]]
    marks.harvest = list(kept["harvest"])
    marks.preyed, marks.uneasy = set(kept["preyed"]), set(kept["uneasy"])
    marks.abandoned = [tuple(a) for a in kept["abandoned"]]
    for column, row in marks.land:
        marks.around[(column, row)] = [(column + dx, row + dy) for dy in (-1, 0, 1) for dx in (-1, 0, 1)
                                       if (dx or dy) and (column + dx, row + dy) in marks.land]
    marks.near = [attunement.near_of(marks, patches) for patches in marks.lands]
    return marks


def attunement_years(data):
    """Attunement (the game's step 8) by the generator's own attunement.year_of_attunement, which rolls no dice: one
    year of trade (its markets), then MARK_YEARS years of marking, spreading, fading, rifts and what the marks do, on the
    export's marks, with MARK_PER_METAL 30 times the export's so that the land is marked hard (unease, prey, new
    rifts, towns given up); the marks, the towns' people, the blame and the events after each
    year. Before those years, a harvest ritual year (300) with HARVEST_RITUAL_CHANCE 1, HARVEST_RITUAL_POOR 0 and HARVEST_RITUAL (0.5,
    0.5), every town of a faction that works rituals made hungry: each such faction works one, and it goes wrong
    (marking its towns)."""
    from types import SimpleNamespace
    from north import attunement, export, rituals
    year = trade_year(data)
    places, going, settings = year.places, year.going, year.settings
    factor = data["history_factor"]
    for place in places:
        place.x, place.y = place.x // factor, place.y // factor
    marks = marks_of(going)
    factions = [SimpleNamespace(index=i, name=f["name"], path=f["path"], people=f["people"], population=f["population"],
                                traits=f["traits"], ruler=None if f["ruler"] is None else SimpleNamespace(
                                    name=f["ruler"]["name"], traits=f["ruler"]["traits"]))
                for i, f in enumerate(data["factions"])]
    powers = SimpleNamespace(factions=factions, marks=marks, deposits=year.deposits)
    used = {kind: [0] * len(listed) for kind, listed in going["names"].items()}

    class Namer:
        def name(self, people, kind):
            listed = going["names"][kind][people]
            n = used[kind][people]
            used[kind][people] += 1
            return listed[n % len(listed)] + ("" if n < len(listed) else " %d" % (n // len(listed) + 1))

    # The fixture's attunement towns have no metal: each uses 20 a year here, and each faction that works rituals is
    # given a mine of its own (the next with metal left, in order).
    from north.trade import METAL
    attuned = [f.index for f in factions if f.path == "attunement" and f.population > 0]
    for index, place in enumerate(places):
        if not place.ruined and place.culture in attuned:
            year.markets[index].made[METAL] = year.markets[index].needed[METAL] = 20.0
    stocked = iter(d for d in year.deposits if d.metal > 0)
    for f in attuned:
        next(stocked).owner = f
    hard = dict(settings["attunement"], MARK_PER_METAL=settings["attunement"]["MARK_PER_METAL"] * 30)
    people = [town["people"] for town in going["towns"]]
    years = []

    def event(year, kind, text, what, facts):
        return [text, what]

    hungry = [1 if place.culture is not None and factions[place.culture].path == "attunement" else town["hungry"]
              for place, town in zip(places, going["towns"])]
    memory = {(a, b): value for a, b, value in going["memory"]}
    ritual = dict(hard, HARVEST_RITUAL_CHANCE=1.0, HARVEST_RITUAL_POOR=0.0, HARVEST_RITUAL=(0.5, 0.5))
    events = []
    import random
    worked = rituals.year_of_harvest_rituals(powers, places, hungry, SimpleNamespace(**ritual), random.Random(1),
                                             300, events, event, memory)
    ritual_year = {"worked": sorted([f, v] for f, v in worked.items()), "events": events,
                   "marks": export.marks_state(powers), "metal": [deposit.metal for deposit in year.deposits],
                   "memory": sorted([a, b, v] for (a, b), v in memory.items())}
    for step in range(MARK_YEARS):
        hardship = {f.index: {"lost": 0} for f in factions}
        blame, events = {}, []
        attunement.year_of_attunement(powers, places, year.markets, people, SimpleNamespace(**hard), hardship, blame,
                                      Namer(), 301 + step, events, event)
        years.append({"marks": export.marks_state(powers), "people": list(people),
                      "ruined": [place.ruined for place in places],
                      "lost": [hardship[f.index]["lost"] for f in factions],
                      "blame": sorted([a, b, value] for (a, b), value in blame.items()), "events": events})
    return {"years": years, "ritual": ritual_year}


def great_ritual(data):
    """A great ritual (the game's step 8b) by the generator's own rituals.year_of_rituals, made certain: the first
    living attunement faction with towns at war with the first other living faction with towns, its side's dead
    10 000 (losing), the next mine with metal its own, RITUAL_CHANCE 1000 and RITUAL_DEATHS one number, so no dice
    decide. The war's dead, the towns' people, the memories, the mines' metal, the marks and the events after it."""
    from types import SimpleNamespace
    from north import export, rituals
    year = trade_year(data)
    places, going, settings = year.places, year.going, year.settings
    factor = data["history_factor"]
    for place in places:
        place.x, place.y = place.x // factor, place.y // factor
    factions = [SimpleNamespace(index=i, name=f["name"], path=f["path"], population=f["population"],
                                ruler=None if f["ruler"] is None else SimpleNamespace(traits=f["ruler"]["traits"]))
                for i, f in enumerate(data["factions"])]
    with_towns = [f.index for f in factions if f.population > 0
                  and any(p.culture == f.index and not p.ruined for p in places)]
    mage = next(f for f in with_towns if factions[f].path == "attunement")
    enemy = next(f for f in with_towns if f != mage)
    next(d for d in year.deposits if d.metal > 0).owner = mage
    war = SimpleNamespace(ended=None, sides=[{enemy}, {mage}], dead={mage: 10000.0}, rituals=set())
    powers = SimpleNamespace(factions=factions, deposits=year.deposits, marks=marks_of(going), deaths=[], rituals=[],
                             fighting=SimpleNamespace(wars=[war]))
    certain = SimpleNamespace(**dict(settings["attunement"], RITUAL_CHANCE=1000.0, RITUAL_DEATHS=0.3))
    people = [town["people"] for town in going["towns"]]
    memory = {(a, b): value for a, b, value in going["memory"]}
    events = []
    import random
    rituals.year_of_rituals(powers, places, people, certain, random.Random(1), 301, events,
                            lambda y, kind, text, what, facts: [text, what], memory, going["history_columns"])
    ritual = powers.rituals[0]
    return {"mage": mage, "enemy": enemy, "dead": sorted([f, v] for f, v in war.dead.items()),
            "worked": sorted(war.rituals), "people": people, "crater": [ritual.x, ritual.y],
            "pair": [ritual.source, ritual.target], "memory": sorted([a, b, v] for (a, b), v in memory.items()),
            "metal": [d.metal for d in year.deposits], "marks": export.marks_state(powers), "events": events}


def offshoots(data):
    """Offshoot paths (the game's step 8c) by the generator's own paths.Book on the export's settings, with
    PATH_NAMES {"handcraft": ["attunement", "Stonehand"]} and PATH_SPREAD 0 (no value moves): three paths founded by
    the Kilnrow, from handcraft, from the first offshoot, and from handcraft again (named Stonehand, skipping
    attunement, a path already; then "Kilnrow way", "Kilnrow way 2"). Every table's entry for each, as the book holds
    it after all three."""
    import random
    from types import SimpleNamespace
    from north import paths
    settings = data["continue"]["settings"]
    book = paths.Book(SimpleNamespace(**settings["factions"]), SimpleNamespace(**settings["attunement"]),
                      {"handcraft": ["attunement", "Stonehand"]})
    try:
        names = [book.found("handcraft", "The Kilnrow", 0.0, random.Random(1), 301)]
        names.append(book.found(names[0], "The Kilnrow", 0.0, random.Random(1), 302))
        names.append(book.found("handcraft", "The Kilnrow", 0.0, random.Random(1), 303))
        f = book.factions
        return {"names": names, "order": list(book.table), "paths": {name: {
            "root": book.table[name]["root"], "parent": book.table[name]["parent"],
            "rituals": book.table[name]["rituals"],
            "metal_need": f.METAL_NEED.get(name), "harvest": f.PATH_HARVEST.get(name),
            "steadiness": f.PATH_STEADINESS.get(name), "land": f.PATH_LAND.get(name),
            "row": f.PATH_OPINIONS.get(name),
            "column": {other: row[name] for other, row in f.PATH_OPINIONS.items() if name in row and other != name},
            "blame": book.attunement.PATH_BLAME.get(name)} for name in names}}
    finally:
        paths.close()


# The stand-in dice of the god of death's cross-check: handed out in turn, over and over (the game's test has the same).
FIXED_DICE = [0.2, 0.0005, 0.7, 0.35, 0.001, 0.9, 0.55, 0.003, 0.15, 0.8, 0.0002, 0.45]


class FixedDice:
    """Dice that give FIXED_DICE in turn: random() each next one; uniform(a, b) as Python's random does it."""

    def __init__(self):
        self.at = 0

    def random(self):
        value = FIXED_DICE[self.at % len(FIXED_DICE)]
        self.at += 1
        return value

    def uniform(self, a, b):
        return a + (b - a) * self.random()


def god_of_death(data):
    """The god of death and her signs (the game's step 9a) by the generator's own death.year_of_death,
    death.year_of_loss and signs.Signs.year, with FixedDice for their dice: in the year the export's last hidden find
    has formed, a battle at the first town held (3000 dead), a battle where the first hidden find lies (500 dead,
    which joins it), a town of the second faction (the first other that denies her or does not know her) losing
    many at once (800, a mass death), DIG_ANGER 0.05; then that faction losing a fifth of its people to hunger; then a sign "black_birds" (chance 1, dead 100, reach 30,
    belief fear at 1, harvest 0.5 for 3 years). Everything after: the hidden metal, every mine, the memories, the
    beliefs, the signs' lasting harvests and the events."""
    from types import SimpleNamespace
    from north import death, signs
    from north.factions import Deposit
    going, settings = data["continue"], data["continue"]["settings"]
    factor = data["history_factor"]
    places = [SimpleNamespace(x=p["x"] // factor, y=p["y"] // factor, culture=p["culture"], ruined=p["ruined"],
                              population=p["population"], name=p["name"]) for p in data["settlements"]]
    factions = [SimpleNamespace(index=i, name=f["name"], traits=f["traits"], belief=f["belief"],
                                population=f["population"], people=f["people"]) for i, f in enumerate(data["factions"])]
    deposits = []
    for m, kept in zip(data["mines"], going["mines"]):
        deposit = Deposit(kept["x"], kept["y"], m["metal"], owner=m["owner"], name=m["name"])
        deposit.first = m["first"]
        deposits.append(deposit)
    world = death.Underworld(hidden=[death.Hidden(*h) for h in going["underworld"]["hidden"]])
    year = max(h.ready for h in world.hidden)
    first = next(p for p in places if not p.ruined and p.culture is not None)
    second = next(f for f in factions if f.population > 0 and f.index != first.culture
                  and f.belief in ("deny", "unknowing") and any(p.culture == f.index and not p.ruined for p in places))
    mourning = next(p for p in places if not p.ruined and p.culture == second.index)
    h0 = world.hidden[0]
    sites = [(year, first.x, first.y, 3000), (year, h0.x, h0.y, 500)]
    world.mass = [(year, mourning.x, mourning.y, 800)]
    powers = SimpleNamespace(factions=factions, deposits=deposits, deaths=list(sites), underworld=world)
    used = {}

    class Namer:
        def name(self, people, kind):
            listed = going["names"][kind][people]
            n = used.get((kind, people), 0)
            used[(kind, people)] = n + 1
            return listed[n % len(listed)] + ("" if n < len(listed) else " %d" % (n // len(listed) + 1))

    memory = {(a, b): value for a, b, value in going["memory"]}
    columns, rows = data["continue"]["grid"]["columns"], data["continue"]["grid"]["rows"]
    ocean = [[False] * columns for _ in range(rows)]
    from north import record
    flat = []
    runs = going["grid"]["ocean"]
    for value, count in zip(runs[::2], runs[1::2]):
        flat += [bool(value)] * count
    ocean = [flat[r * columns:(r + 1) * columns] for r in range(rows)]
    events = []

    def event(y, kind, text, what, facts):
        return [text, what]

    dice = FixedDice()
    death_settings = SimpleNamespace(**dict(settings["death"], DIG_ANGER=0.05))
    death.year_of_death(powers, places, columns, rows, death_settings, dice, Namer(), year, events, event, ocean,
                        memory)
    second.population = round(second.population * 0.8)
    death.year_of_loss(powers, places, {second.index: second.population / 4}, dice, year, events, event)
    table = {"black_birds": {"chance": 1.0, "dead": 100, "reach": 30, "belief": ["fear", 1.0], "harvest": 0.5,
                             "years": 3}}
    seen = signs.Signs(table, 1, columns)
    seen.lasting = {int(town): [tuple(e) for e in entries] for town, entries in going["underworld"]["lasting"].items()}
    seen.rng = FixedDice()
    seen.year(powers, places, year, events, event)
    return {"year": year, "sites": [list(site) for site in sites], "mass": [list(m) for m in world.mass],
            "mourning": places.index(mourning), "second": second.index, "second_population": second.population,
            "hidden": [[h.x, h.y, h.metal, h.ready, h.died, h.dead] for h in world.hidden],
            "mines": [[d.x, d.y, d.metal, d.first, d.owner, d.name] for d in deposits],
            "memory": sorted([a, b, v] for (a, b), v in memory.items()),
            "beliefs": [f.belief for f in factions],
            "lasting": {str(t): [list(e) for e in entries] for t, entries in sorted(seen.lasting.items())},
            "events": events, "dice_used": dice.at}


# The kinds of threat added for the threats' years (the game's step 9b), to the export's own.
THREAT_TEST_KINDS = {
    "wyrm": {"weight": 20, "strength": [1500, 4000], "reach": 60, "raid": 0.6, "deaths": [0.1, 0.3], "harvest": 0.5,
             "slain": 0.5, "move": 0.4, "sleep": 0.002, "sleep_years": [1, 2], "fade": 0.0003,
             "names": ["Old Smoulder"]},
    "ghast": {"undead": True, "weight": 20, "strength": 50, "reach": 40, "raid": 0.8, "deaths": 0.05, "slain": 0.3,
              "fade": 0.001},
}
THREAT_YEARS = 12


class ThreatDice(FixedDice):
    """FixedDice with the rest of what the threats roll: choices (as Python's random.choices), randint
    (low + int(random() * (high - low + 1))) and sample (as Python's random.sample for a short list), each
    with random() alone."""

    def choices(self, items, weights):
        import bisect
        import itertools
        totals = list(itertools.accumulate(weights))
        return [items[bisect.bisect(totals, self.random() * totals[-1], 0, len(items) - 1)]]

    def randint(self, low, high):
        return low + int(self.random() * (high - low + 1))

    def sample(self, items, count):
        pool, chosen, n = list(items), [], len(items)
        for i in range(count):
            j = int(self.random() * (n - i))
            chosen.append(pool[j])
            pool[j] = pool[n - i - 1]
        return chosen


def threat_years(data):
    """THREAT_YEARS years of named threats (the game's step 9b) by the generator's own threats.Threats.year and
    rituals.year_of_threat_rituals, with ThreatDice for their dice: the export's threats and kinds, and
    THREAT_TEST_KINDS before them (the wyrm's first name is the first town's, which is taken), RISE_CHANCE 1, every faction
    fully armed, RITUAL_DESPERATION 0 and RITUAL_CHANCE 0.5. A listed name is taken when no town, faction or threat
    has it (the game's rule). Everything after: the threats, the names given, the spoiled harvests, every town's
    harvest factor each year, the towns' people, the dead, the mines' metal, the marks and the events."""
    from types import SimpleNamespace
    from north import export, rituals, threats
    year_data = trade_year(data)
    places, going, settings = year_data.places, year_data.going, year_data.settings
    factor = data["history_factor"]
    for place in places:
        place.x, place.y = place.x // factor, place.y // factor
    factions = [SimpleNamespace(index=i, name=f["name"], path=f["path"], population=f["population"],
                                people=f["people"], government=f["government"],
                                ruler=None if f["ruler"] is None else SimpleNamespace(traits=f["ruler"]["traits"]))
                for i, f in enumerate(data["factions"])]
    columns, rows = going["grid"]["columns"], going["grid"]["rows"]

    def unrun(runs):
        flat = []
        for value, count in zip(runs[::2], runs[1::2]):
            flat += [value] * count
        return [flat[r * columns:(r + 1) * columns] for r in range(rows)]

    owner = [[None if v < 0 else v for v in row] for row in unrun(going["grid"]["owner"])]
    biomes = [[None if v < 0 else {"name": data["biomes"][v]} for v in row] for row in unrun(going["land"]["biome"])]
    kinds = {name: dict(about) for name, about in THREAT_TEST_KINDS.items()}
    kinds["wyrm"]["names"] = [places[0].name] + kinds["wyrm"]["names"]
    kinds.update(settings["threats"]["THREAT_KINDS"])
    threatened = threats.Threats(kinds, 0, 1.0, 1, places, owner, biomes, columns, None, factions)
    threatened.rng = ThreatDice()
    used = {}

    class Namer:
        """The export's names in turn, as the game gives them; `used`, every name a town, faction or threat has."""
        def __init__(self):
            self.used = {p.name for p in places} | {f.name for f in factions}

        def name(self, people, kind):
            listed = going["names"][kind][people]
            n = used.get((kind, people), 0)
            used[(kind, people)] = n + 1
            return listed[n % len(listed)] + ("" if n < len(listed) else " %d" % (n // len(listed) + 1))

    threatened.namer = Namer()
    for t in going["threats"]["threats"]:
        threat = threats.Threat(t["name"], t["kind"], t["x"], t["y"], t["strength"], t["risen"], t["asleep_until"],
                                t["ended"], t["end"], t["raids"], t["beaten"], t["dead"], {f: v for f, v in t["harm"]},
                                set(t["rituals"]), [tuple(lair) for lair in t["lairs"]])
        threatened.threats.append(threat)
        threatened.namer.used.add(threat.name)
    threatened.given.update(going["threats"]["given"])
    threatened.spoiled = {int(town): tuple(entry) for town, entry in going["threats"]["spoiled"].items()}
    powers = SimpleNamespace(factions=factions, deposits=year_data.deposits, marks=marks_of(going), deaths=[],
                             rituals=[])
    threatened.marks, threatened.strong = powers.marks, settings["attunement"]["STRONG"]
    attuning = SimpleNamespace(**dict(settings["attunement"], RITUAL_DESPERATION=0.0, RITUAL_CHANCE=0.5))
    people = [town["people"] for town in going["towns"]]
    supply = [{} for _ in factions]
    war_settings = SimpleNamespace(**settings["war"])
    events, factors = [], []

    def event(y, kind, text, what, facts):
        return [text, what]

    first = data["years"]
    for year in range(first + 1, first + THREAT_YEARS + 1):
        factors.append([threatened.factor(i, year) for i in range(len(places))])
        threatened.year(powers, people, supply, settings["leaders"]["GOVERNMENTS"], war_settings, year, events, event)
        rituals.year_of_threat_rituals(powers, places, threatened, attuning, year, events, event, columns)
    state = export.threats_state(SimpleNamespace(threats=threatened))
    return {"kinds": THREAT_TEST_KINDS, "first_name": places[0].name, "years": THREAT_YEARS, "threats": state,
            "factors": factors, "people": people, "deaths": [list(d) for d in powers.deaths],
            "metal": [d.metal for d in year_data.deposits], "marks": export.marks_state(powers), "events": events,
            "dice_used": threatened.rng.at}


# The happenings and tempers of the happenings' years (the game's step 9c): the test tempers come before the export's.
HAPPENING_TEST = {
    "HAPPENINGS": {
        "blight": {"chance": 0.003, "reach": "routes", "spread": 0.5, "years": 3,
                   "effects": {"population": -0.05, "harvest": 0.7, "growth": 0.5}},
        "exodus": {"chance": 0.001, "reach": "town", "years": 2,
                   "effects": {"driven_off": 0.2, "belief": ["worship", 0.6], "war": 1.5}},
        "quake": {"chance": 0.0015, "reach": "region", "radius": 30, "years": 4,
                  "effects": {"population": -0.1, "blame": 0.3, "trade": 0.5}},
        "boom": {"chance": 0.004, "reach": "world", "years": 2, "effects": {"population": 0.05, "war": 0.5}},
    },
    "TEMPERS": {"calm": {"weight": 1, "chance": 0.5, "war": 0.5, "splits": 0.5},
                "wild": {"weight": 2, "chance": 3.0, "happenings": {"quake": 2.0}, "war": 1.5}},
    "ERA_YEARS": [3, 6], "ERA_END_CHANCE": 0.5, "ERA_ENDERS": ["quake", "belief_after_happening"],
}
HAPPENING_YEARS = 8


def happening_years(data):
    """HAPPENING_YEARS years of happenings and eras (the game's step 9c) by the generator's own happenings.Happenings,
    with ThreatDice for both its dice: HAPPENING_TEST's happenings, its tempers before the export's, its era settings;
    the export's lasting effects, frontier, outbreaks and eras to start from. Each year: the era begins, every town's
    factors (growth, harvest, trade, war), the happenings strike, the towns' populations are rounded (as the history's
    year does), the war factors, then the era may end. Everything after: the people, the beliefs, the blame, what each
    year killed, the state as the export holds it and the events."""
    from types import SimpleNamespace
    from north import export, happenings, history
    year_data = trade_year(data)
    places, going, settings = year_data.places, year_data.going, year_data.settings
    factor = data["history_factor"]
    for place in places:
        place.x, place.y = place.x // factor, place.y // factor
    factions = [SimpleNamespace(index=i, name=f["name"], belief=f["belief"], traits=f["traits"])
                for i, f in enumerate(data["factions"])]
    tempers = dict(HAPPENING_TEST["TEMPERS"])
    tempers.update(settings["happenings"]["TEMPERS"])
    happen = happenings.Happenings(HAPPENING_TEST["HAPPENINGS"], 1, year_data.routes, going["history_columns"], tempers,
                                   HAPPENING_TEST["ERA_YEARS"], HAPPENING_TEST["ERA_END_CHANCE"],
                                   HAPPENING_TEST["ERA_ENDERS"])
    happen.rng, happen.era_rng = ThreatDice(), ThreatDice()
    kept = going["happenings"]
    happen.lasting = {int(t): [(n, a, b, dict(e)) for n, a, b, e in entries] for t, entries in kept["lasting"].items()}
    happen.last = {int(t): dict(struck) for t, struck in kept["last"].items()}
    happen.frontier = {name: [tuple(e) for e in entries] for name, entries in kept["frontier"].items()}
    happen.outbreaks = {int(o): set(towns) for o, towns in kept["outbreaks"].items()}
    happen.next_outbreak = kept["next_outbreak"]
    happen.eras = [tuple(era) for era in kept["eras"]]
    if happen.eras:
        happen.take_temper(tempers[happen.eras[-1][0]])
    people = [town["people"] for town in going["towns"]]
    events, blame, killed, factors, war = [], {}, [], [], []

    def event(y, kind, text, what, facts):
        return [text, what]

    first = data["years"]
    for year in range(first + 1, first + HAPPENING_YEARS + 1):
        start = len(events)
        happen.begin_year(year, events, event)
        factors.append([[happen.factor(i, year, key) for key in ("growth", "harvest", "trade", "war")]
                        for i in range(len(places))])
        hit = happen.year(places, factions, None, None, year, people, events, event, blame, history.add_blame)
        killed.append(sorted([town, dead] for town, dead in hit.items()))
        for place, count in zip(places, people):
            place.population = round(count)
        war.append(sorted([f, value] for f, value in happen.war_factors(places, year).items()))
        happen.end_year(year, [SimpleNamespace(what=e[1]) for e in events[start:]])
    return {"test": HAPPENING_TEST, "years": HAPPENING_YEARS, "people": people,
            "beliefs": [f.belief for f in factions], "blame": sorted([a, b, v] for (a, b), v in blame.items()),
            "killed": killed, "factors": factors, "war": war, "splits": happen.splits,
            "state": export.happenings_state(SimpleNamespace(happenings=happen)), "events": events,
            "dice_used": [happen.rng.at, happen.era_rng.at]}


# What the chokepoints' years switch off (the parts that come with the game's step 9e), and EMBARGO_OPINION 0.
CHOKE_OFF = {"LEAK_CHANCE": 0.0, "DEBASER": None, "CONTRACT_CHANCE": 0.0, "SABOTAGE_CHANCE": 0.0, "CONSTRUCTS": {},
             "SECRET_CHANCE": 0.0, "LEGITIMACY": 0.0, "EMBARGO_OPINION": 0.0}


def choke_years(data):
    """Two years of chokepoints (the game's step 9d) by the generator's own chokepoints.Chokepoints with the
    export's state, trade.make_markets and trade.trade_goods: CHOKE_OFF, the first living faction with a lineage as
    the smugglers, and a stand-in for the first needed good still short after a trial year (the good most spare
    anywhere stands in for it). Each year: begin_year (its harvest factors times an ordinary year's), the markets,
    the trade, after_trade. Between them every embargo's holder comes to think 1.0 of its buyer (so they are
    lifted). Each year: every market, the deals, the harvest and ritual factors, the shortages, the embargoes, the
    blame, the smuggled, and the events."""
    import copy
    from types import SimpleNamespace
    from north import chokepoints, history, trade
    going = data["continue"]
    settings = going["settings"]
    factor = data["history_factor"]
    living = [i for i, f in enumerate(data["factions"]) if f["population"] > 0 and f["lineage"]]
    choke_settings = dict(settings["chokepoints"], **CHOKE_OFF)
    choke_settings["SMUGGLERS"] = [data["factions"][living[0]]["lineage"]]

    def run(choke_settings, years, contracts=None, sabotaged=None):
        year = trade_year(data)
        places, factions, powers, settled = year.places, year.factions, year.powers, year.settled
        powers.fighting = None
        for f, d in zip(factions, data["factions"]):
            f.population, f.hindered = d["population"], {}
            f.border = {int(k): v for k, v in d["border"].items()}
        kept = going["chokepoints"]
        choke = chokepoints.Chokepoints(SimpleNamespace(**choke_settings), 1)
        choke.short = {int(t): dict(goods) for t, goods in kept["short"].items()}
        choke.embargoes = {tuple(e) for e in kept["embargoes"]}
        choke.debased = {int(f): v for f, v in kept["debased"].items()}
        choke.smuggled = kept["smuggled"]
        choke.contracts = {(buyer, good): maker for buyer, good, maker in kept["contracts"]}
        choke.sabotaged = {int(t): tuple(e) for t, e in kept["sabotaged"].items()}
        for buyer, good, maker in contracts or []:
            choke.contracts[(buyer, good)] = maker
        for town, good, until in sabotaged or []:
            choke.sabotaged[town] = (good, until)
        trading = year.trading
        base = history.ruled_harvests(history.path_harvests([1.0] * len(places), places, factions, SimpleNamespace(
            PATH_HARVEST=settings["factions"]["PATH_HARVEST"], PATH_STEADINESS=settings["factions"]["PATH_STEADINESS"])),
            places, factions, settings["leaders"]["GOVERNMENTS"])
        yields = [town["yields"] for town in going["towns"]]
        told = []
        for number, when in enumerate(years):
            events, blame = [], {}

            def event(y, kind, text, what, facts):
                return [text, what]

            factors = choke.begin_year(places, powers)
            harvest = [h * f for h, f in zip(base, factors)]
            markets = trade.make_markets(places, settled, None, powers, settings["factions"]["METAL_NEED"], trading,
                                         yields, harvest, choke)
            routes = [trade.Route(r["a"], r["b"], [], r["cost"]) for r in data["routes"]]
            deals = trade.trade_goods(places, routes, markets, powers, trading, None, going["history_columns"] / 256,
                                      choke)
            choke.after_trade(places, markets, powers, deals, blame, when, events, event)
            told.append({"factors": factors,
                         "rituals": sorted([f.index, f.hindered["rituals"]] for f in factions if "rituals" in f.hindered),
                         "markets": [{"made": m.made, "needed": m.needed, "bought": m.bought, "sold": m.sold,
                                      "stood_in": m.stood_in, "needs": sorted(m.needs), "trade_factor": m.trade_factor}
                                     for m in markets],
                         "deals": [list(d) for d in deals],
                         "short": {str(t): dict(sorted(g.items())) for t, g in sorted(choke.short.items())},
                         "embargoes": [list(e) for e in sorted(choke.embargoes)],
                         "contracts": [[b, g, m] for (b, g), m in sorted(choke.contracts.items())],
                         "blame": sorted([a, b, v] for (a, b), v in blame.items()),
                         "smuggled": choke.smuggled, "events": events})
            if number == 0:
                for holder, buyer, _good in sorted(choke.embargoes):
                    factions[holder].opinions[buyer] = 1.0
        return told, markets

    trial, markets = run(choke_settings, [101])
    short = next(good for market in markets for good in sorted(market.needs) if market.shortage(good) > 1e-6)
    spare = {}
    for market in markets:
        for good, made in market.made.items():
            if good != short and good != trade.METAL:
                spare[good] = spare.get(good, 0.0) + max(0.0, made - market.needed.get(good, 0.0) - market.sold.get(good, 0.0))
    other = max(sorted(spare), key=lambda g: spare[g])
    choke_settings = copy.deepcopy(choke_settings)
    choke_settings["STAND_INS"] = dict(choke_settings.get("STAND_INS", {}), **{short: [other]})
    # A contract, for the first good (not a staple) the maker's towns make and the buyer's lack: between the first
    # living pair that think well of each other, if any makes such a good, else the first pair; one that ends (the
    # maker thinks ill of the buyer); and the first town that crafts, sabotaged for its first crafted good until the
    # first year.
    opinions = {i: {int(k): v for k, v in f["opinions"].items()} for i, f in enumerate(data["factions"])}
    alive = [i for i, f in enumerate(data["factions"]) if f["population"] > 0]
    towns = [t["culture"] for t in data["settlements"]]

    def made_by(maker):
        return sorted({g for t, mk in enumerate(markets) if towns[t] == maker and not data["settlements"][t]["ruined"]
                       for g, a in mk.made.items() if a > 0 and g != trade.METAL and g not in staples})

    staples = set(trade.staple_goods(SimpleNamespace(**settings["trade"])))

    pairs = [(b, m) for b in alive for m in alive if b != m and made_by(m)]
    buyer, maker = next((pair for pair in pairs if opinions[pair[0]].get(pair[1], 0) >= 0
                         and opinions[pair[1]].get(pair[0], 0) >= 0), pairs[0])
    made = made_by(maker)
    lacked = [g for g in made if any(towns[t] == buyer and mk.shortage(g) > 1e-9 for t, mk in enumerate(markets))]
    held = (lacked or made)[0]
    sour = next((b, m) for b in alive for m in alive if b != m and opinions[m].get(b, 0) < 0)
    workshop = next(t for t, mk in enumerate(markets) if towns[t] is not None and mk.can_craft)
    contracts = [[buyer, held, maker], [sour[0], "crystals", sour[1]]]
    sabotaged = [[workshop, sorted(markets[workshop].can_craft)[0], 101]]
    years, _markets = run(choke_settings, [101, 102], contracts, sabotaged)
    return {"settings": choke_settings, "contracts": contracts, "sabotaged": sabotaged, "years": years}


MARK_YEARS = 5


def going_borders(data):
    """The borders at the end of the history, as runs."""
    culture = data["history"]["borders"]
    flat = None
    for _since, shot in culture["snapshots"]:
        if "full" in shot:
            flat = []
            for i in range(0, len(shot["full"]), 2):
                flat += [shot["full"][i]] * shot["full"][i + 1]
        else:
            for i in range(0, len(shot["changes"]), 3):
                start, count, holder = shot["changes"][i:i + 3]
                flat[start:start + count] = [holder] * count
    runs = []
    for value in flat:
        if runs and runs[-2] == value:
            runs[-1] += 1
        else:
            runs += [value, 1]
    return runs


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
    expected["feeding"] = feeding_and_drift(world.data)
    expected["migration"] = migration_year(world.data)
    expected["learning"] = learning_and_capitals(world.data)
    expected["attunement"] = attunement_years(world.data)
    expected["great_ritual"] = great_ritual(world.data)
    expected["offshoots"] = offshoots(world.data)
    expected["death"] = god_of_death(world.data)
    expected["threats"] = threat_years(world.data)
    expected["happenings"] = happening_years(world.data)
    expected["chokepoints"] = choke_years(world.data)
    (HERE / "expected.json").write_text(json.dumps(expected, separators=(",", ":")), encoding="utf-8")

    grid = [[(x * x * 7 + y * 31 + (x ^ y) * 13) % 256 for x in range(24)] for y in range(20)]
    grid[0][:5], grid[1][:5] = [10, 20, 30, 40, 50], [10, 20, 30, 40, 50]    # equal neighbours: the filters' ties
    (HERE / "filtered.png").write_bytes(png(grid, colour=0, step=1, filters=(0, 1, 2, 3, 4)))
    (HERE / "filtered.json").write_text(json.dumps(grid), encoding="utf-8")


if __name__ == "__main__":
    main(Path(sys.argv[1]).resolve())
