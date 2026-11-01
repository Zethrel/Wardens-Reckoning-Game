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
    return {
        "markets": [{"made": m.made, "needed": m.needed, "bought": m.bought, "sold": m.sold} for m in markets],
        "fed": fed,
        "deals": len(deals),
        "volumes": [route.volume for route in routes],
        "metal": [deposit.metal for deposit in deposits],
        "dry": [event.text for event in events],
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
    (HERE / "expected.json").write_text(json.dumps(expected, indent=1), encoding="utf-8")

    grid = [[(x * x * 7 + y * 31 + (x ^ y) * 13) % 256 for x in range(24)] for y in range(20)]
    grid[0][:5], grid[1][:5] = [10, 20, 30, 40, 50], [10, 20, 30, 40, 50]    # equal neighbours: the filters' ties
    (HERE / "filtered.png").write_bytes(png(grid, colour=0, step=1, filters=(0, 1, 2, 3, 4)))
    (HERE / "filtered.json").write_text(json.dumps(grid), encoding="utf-8")


if __name__ == "__main__":
    main(Path(sys.argv[1]).resolve())
