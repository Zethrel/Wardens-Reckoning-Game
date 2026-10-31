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
    (HERE / "expected.json").write_text(json.dumps(expected, indent=1), encoding="utf-8")

    grid = [[(x * x * 7 + y * 31 + (x ^ y) * 13) % 256 for x in range(24)] for y in range(20)]
    grid[0][:5], grid[1][:5] = [10, 20, 30, 40, 50], [10, 20, 30, 40, 50]    # equal neighbours: the filters' ties
    (HERE / "filtered.png").write_bytes(png(grid, colour=0, step=1, filters=(0, 1, 2, 3, 4)))
    (HERE / "filtered.json").write_text(json.dumps(grid), encoding="utf-8")


if __name__ == "__main__":
    main(Path(sys.argv[1]).resolve())
