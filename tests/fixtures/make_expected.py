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
    markets = trade.make_markets(places, settled, None, powers, settings["factions"]["METAL_NEED"], trading,
                                 yields, harvest)
    routes = [trade.Route(r["a"], r["b"], [], r["cost"]) for r in data["routes"]]
    deals = trade.trade_goods(places, routes, markets, powers, trading, None, going["history_columns"] / 256)
    return SimpleNamespace(places=places, factions=factions, markets=markets, deals=deals, settled=settled,
                           powers=powers, deposits=deposits, trading=trading, routes=routes, going=going, settings=settings)


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
    first two living factions, and the first held town taken by the next living faction; then a year of learning
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
    winner = next(i for i in alive if i != loser)
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
    (HERE / "expected.json").write_text(json.dumps(expected, separators=(",", ":")), encoding="utf-8")

    grid = [[(x * x * 7 + y * 31 + (x ^ y) * 13) % 256 for x in range(24)] for y in range(20)]
    grid[0][:5], grid[1][:5] = [10, 20, 30, 40, 50], [10, 20, 30, 40, 50]    # equal neighbours: the filters' ties
    (HERE / "filtered.png").write_bytes(png(grid, colour=0, step=1, filters=(0, 1, 2, 3, 4)))
    (HERE / "filtered.json").write_text(json.dumps(grid), encoding="utf-8")


if __name__ == "__main__":
    main(Path(sys.argv[1]).resolve())
