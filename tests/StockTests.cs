// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Colony;
using ColonyState = WardensReckoning.Colony.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 13a: the colony's goods. Inventories, claims (reservations), the ledger, and exactly-once operations: the
/// ledger and custody half of FP-05 (an interrupted haul itself comes with the carriers, step 13b).
/// </summary>
public class StockTests
{
    static readonly Moment Start = new(Calendar.TickOf(301, 1, 1));

    sealed class Setup
    {
        public readonly ColonyState Colony = new(42, Start);
        public Stocks Stocks => Colony.Stocks;
        public readonly Inventory Carts, Quern, Arms;

        public Setup(int wheat = 50)
        {
            Carts = Stocks.Open("The founders' carts", Holder.Store, 0, 0);
            Quern = Stocks.Open("Quern", Holder.WorkshopInput, 40, 0);
            Arms = Stocks.Open("Ada's arms", Holder.Carrier, 0, 0);
            if (wheat > 0)
                Assert.True(Stocks.Arrive(Op(), 0, Carts.Id, "wheat", wheat).Done);
        }

        public OperationId Op() => Colony.Ids.Next<OperationId>();

        public Reservation Claim(Inventory where, string good, int amount)
        {
            var (claim, outcome) = Stocks.Reserve(where.Id, good, amount, "test");
            Assert.True(outcome.Done, outcome.Why);
            return claim!;
        }
    }

    [Fact]
    public void Arrived_goods_are_held_and_written_down()
    {
        var s = new Setup();
        Assert.Equal(50, s.Carts.Count("wheat"));
        var entry = Assert.Single(s.Stocks.Ledger);
        Assert.Equal((Change.Arrived, "wheat", 50, (InventoryId?)null, (InventoryId?)s.Carts.Id), (entry.Change, entry.Good, entry.Amount, entry.From, entry.To));
        Assert.Equal(50, s.Stocks.Accounted("wheat"));
    }

    [Fact]
    public void A_claim_moves_its_goods_and_is_used_up()
    {
        var s = new Setup();
        var claim = s.Claim(s.Carts, "wheat", 20);
        Assert.Equal(30, s.Stocks.Free(s.Carts.Id, "wheat"));
        Assert.True(s.Stocks.Move(s.Op(), 5, claim.Id, s.Arms.Id).Done);
        Assert.Equal((30, 20), (s.Carts.Count("wheat"), s.Arms.Count("wheat")));
        Assert.Empty(s.Stocks.Reservations);
        Assert.Equal(50, s.Stocks.Total("wheat"));
    }

    [Fact]
    public void Claimed_goods_cannot_be_claimed_again()
    {
        var s = new Setup();
        s.Claim(s.Carts, "wheat", 40);
        var (claim, outcome) = s.Stocks.Reserve(s.Carts.Id, "wheat", 20, "test");
        Assert.Null(claim);
        Assert.Equal("The founders' carts has 50 wheat, 40 already claimed: 10 free, 20 wanted", outcome.Why);
    }

    [Fact]
    public void A_protected_reserve_cannot_be_claimed()
    {
        var s = new Setup();
        s.Carts.Keep("wheat", 45);
        var (_, outcome) = s.Stocks.Reserve(s.Carts.Id, "wheat", 10, "test");
        Assert.Equal("The founders' carts has 50 wheat, 45 kept as a reserve: 5 free, 10 wanted", outcome.Why);
        s.Carts.Keep("wheat", 0);
        Assert.Empty(s.Carts.Kept);
        Assert.Equal(50, s.Stocks.Free(s.Carts.Id, "wheat"));
    }

    [Fact]
    public void A_claim_let_go_frees_its_goods()
    {
        var s = new Setup();
        var claim = s.Claim(s.Carts, "wheat", 50);
        s.Stocks.Release(claim.Id);
        Assert.Equal(50, s.Stocks.Free(s.Carts.Id, "wheat"));
        Assert.False(s.Stocks.Move(s.Op(), 1, claim.Id, s.Arms.Id).Done);
        Assert.Equal(50, s.Carts.Count("wheat"));
    }

    [Fact]
    public void Nothing_arrives_of_nothing()
    {
        var s = new Setup();
        Assert.Equal("nothing arrives", s.Stocks.Arrive(s.Op(), 1, s.Carts.Id, "wheat", 0).Why);
        Assert.Single(s.Stocks.Ledger);
    }

    [Fact]
    public void Nothing_can_be_claimed_of_nothing() =>
        Assert.Equal("nothing to claim", new Setup().Stocks.Reserve(new Setup().Carts.Id, "wheat", 0, "x").Outcome.Why);

    [Fact]
    public void FP05_an_operation_is_done_exactly_once()
    {
        var s = new Setup();
        var claim = s.Claim(s.Carts, "wheat", 10);
        var op = s.Op();
        Assert.True(s.Stocks.Move(op, 1, claim.Id, s.Arms.Id).Done);
        var again = s.Stocks.Move(op, 1, claim.Id, s.Arms.Id);     // the same receipt twice
        Assert.True(again.AlreadyDone);
        Assert.Equal((40, 10), (s.Carts.Count("wheat"), s.Arms.Count("wheat")));
        Assert.Equal(2, s.Stocks.Ledger.Count);
        var arrival = s.Op();
        s.Stocks.Arrive(arrival, 2, s.Carts.Id, "wheat", 5);
        Assert.True(s.Stocks.Arrive(arrival, 2, s.Carts.Id, "wheat", 5).AlreadyDone);
        Assert.Equal(55, s.Stocks.Total("wheat"));
    }

    [Fact]
    public void A_recipe_uses_its_inputs_and_makes_its_outputs_at_once()
    {
        var s = new Setup();
        var claim = s.Claim(s.Carts, "wheat", 5);
        var op = s.Op();
        Assert.True(s.Stocks.Make(op, 180, new[] { claim.Id }, s.Quern.Id, new Dictionary<string, int> { ["flour"] = 4 }).Done);
        Assert.Equal((45, 4), (s.Carts.Count("wheat"), s.Quern.Count("flour")));
        Assert.Equal((45, 4), (s.Stocks.Accounted("wheat"), s.Stocks.Accounted("flour")));
        Assert.True(s.Stocks.Make(op, 180, new[] { claim.Id }, s.Quern.Id, new Dictionary<string, int> { ["flour"] = 4 }).AlreadyDone);
        Assert.Equal(4, s.Stocks.Total("flour"));
    }

    [Fact]
    public void A_recipe_with_a_lost_claim_changes_nothing()
    {
        var s = new Setup();
        var kept = s.Claim(s.Carts, "wheat", 5);
        var lost = s.Claim(s.Carts, "wheat", 5);
        s.Stocks.Release(lost.Id);
        var outcome = s.Stocks.Make(s.Op(), 1, new[] { kept.Id, lost.Id }, s.Quern.Id, new Dictionary<string, int> { ["flour"] = 4 });
        Assert.False(outcome.Done);
        Assert.Equal((50, 0), (s.Carts.Count("wheat"), s.Stocks.Total("flour")));
        Assert.Single(s.Stocks.Reservations);
    }

    [Fact]
    public void Goods_cannot_move_to_where_they_are()
    {
        var s = new Setup();
        var claim = s.Claim(s.Carts, "wheat", 5);
        Assert.Equal("the goods are already there", s.Stocks.Move(s.Op(), 1, claim.Id, s.Carts.Id).Why);
    }

    [Fact]
    public void FP05_a_thousand_random_operations_never_lose_or_make_goods()
    {
        // Claims, moves, let-goes, recipes and doubled receipts in a random order: after each, every unit is held
        // somewhere (no inventory below 0), and what is held is what the ledger accounts for.
        var s = new Setup();
        var places = new[] { s.Carts, s.Quern, s.Arms };
        var dice = s.Colony.DiceFor("test");
        var claims = new List<Reservation>();
        var used = new List<OperationId>();
        for (int i = 0; i < 1000; i++)
        {
            var place = places[dice.Int(0, 2)];
            switch (dice.Int(0, 4))
            {
                case 0:
                    var (claim, _) = s.Stocks.Reserve(place.Id, dice.Chance(0.5) ? "wheat" : "flour", dice.Int(1, 8), "t");
                    if (claim != null)
                        claims.Add(claim);
                    break;
                case 1 when claims.Count > 0:
                    var c = claims[dice.Int(0, claims.Count - 1)];
                    var op = s.Op();
                    if (s.Stocks.Move(op, i, c.Id, places[dice.Int(0, 2)].Id).Done)
                        used.Add(op);
                    break;
                case 2 when claims.Count > 0:
                    s.Stocks.Release(claims[dice.Int(0, claims.Count - 1)].Id);
                    break;
                case 3:
                    var wheat = s.Stocks.Reserve(place.Id, "wheat", 5, "grind").Reservation;
                    if (wheat != null)
                        s.Stocks.Make(s.Op(), i, new[] { wheat.Id }, places[dice.Int(0, 2)].Id, new Dictionary<string, int> { ["flour"] = 4 });
                    break;
                case 4 when used.Count > 0:
                    // A receipt submitted again: never does anything.
                    Assert.True(s.Stocks.Move(used[dice.Int(0, used.Count - 1)], i, new ReservationId(1), s.Carts.Id).AlreadyDone);
                    break;
            }
            foreach (string good in new[] { "wheat", "flour" })
            {
                Assert.Equal(s.Stocks.Accounted(good), s.Stocks.Total(good));
                Assert.All(places, p => Assert.True(p.Count(good) >= s.Stocks.Reserved(p.Id, good)));
            }
        }
        Assert.True(s.Stocks.Total("flour") > 0);
    }

    // ---- The save -----------------------------------------------------------------------------------------

    static Setup Busy()
    {
        var s = new Setup();
        s.Carts.Keep("wheat", 10);
        s.Claim(s.Carts, "wheat", 7);
        var claim = s.Claim(s.Carts, "wheat", 5);
        s.Stocks.Move(s.Op(), 3, claim.Id, s.Arms.Id);
        var grind = s.Claim(s.Arms, "wheat", 5);
        s.Stocks.Make(s.Op(), 200, new[] { grind.Id }, s.Quern.Id, new Dictionary<string, int> { ["flour"] = 4 });
        s.Quern.X = 41.25;
        return s;
    }

    [Fact]
    public void A_save_keeps_the_goods_claims_and_ledger_exactly()
    {
        var s = Busy();
        var content = Content.Load(Path.Combine(AppContext.BaseDirectory, "content"));
        var loaded = ColonySave.Read(ColonySave.Text(s.Colony), content);
        Assert.Equal(ColonySave.Text(s.Colony), ColonySave.Text(loaded));
        Assert.Equal(41.25, loaded.Stocks[s.Quern.Id].X);
        Assert.Equal(new Dictionary<string, int> { ["wheat"] = 10 }, loaded.Stocks[s.Carts.Id].Kept);
        // An operation done before the save is still done after it.
        Assert.True(loaded.Stocks.Done(s.Stocks.Ledger[^1].Operation));
        // And both go on the same way.
        foreach (var colony in new[] { s.Colony, loaded })
        {
            var (claim, _) = colony.Stocks.Reserve(s.Carts.Id, "wheat", 3, "after");
            colony.Stocks.Move(colony.Ids.Next<OperationId>(), 400, claim!.Id, s.Arms.Id);
        }
        Assert.Equal(ColonySave.Fingerprint(s.Colony), ColonySave.Fingerprint(loaded));
    }

    public static IEnumerable<object[]> BrokenStocks()
    {
        object[] Case(Action<JsonObject> change, string reason)
        {
            var save = ColonySave.Write(Busy().Colony);
            change(save);
            return new object[] { save.ToJsonString(), reason };
        }
        JsonObject Inv(JsonObject s, int i) => s["inventories"]![i]!.AsObject();
        yield return Case(s => Inv(s, 0)["goods"]!["wheat"] = 999, "its goods do not add up");
        yield return Case(s => Inv(s, 0)["goods"]!["barley"] = 1, "names the good \"barley\", which the content does not have");
        yield return Case(s => Inv(s, 0)["goods"]!["wheat"] = 0, "inventories[0].goods.wheat is not a whole number of 1 or more");
        yield return Case(s => Inv(s, 0)["holder"] = "Cellar", "inventories[0].holder is not a kind of inventory");
        yield return Case(s => Inv(s, 0)["holder"] = "7", "inventories[0].holder is not a kind of inventory");
        yield return Case(s => Inv(s, 0)["x"] = "far", "inventories[0].x is not a number");
        yield return Case(s => Inv(s, 1)["id"] = 1, "inventories[1].id 1 was never given out, or is given twice");
        yield return Case(s => Inv(s, 1)["id"] = 99, "inventories[1].id 99 was never given out, or is given twice");
        yield return Case(s => s["reservations"]![0]!["inventory"] = 99, "claims goods in inventory 99, which does not exist");
        yield return Case(s => s["reservations"]![0]!["amount"] = 9999, "claims more wheat than inventory 1 has");
        yield return Case(s => s["reservations"]![0]!["id"] = 99, "reservations[0].id 99 was never given out");
        // Each claim alone fits, but together they claim more than is there (the carts hold 45 wheat).
        yield return Case(s =>
        {
            s["reservations"]![0]!["amount"] = 30;
            s["reservations"]!.AsArray().Add(new JsonObject { ["id"] = 2, ["inventory"] = 1, ["good"] = "wheat", ["amount"] = 30, ["purpose"] = "x" });
        }, "claims more wheat than inventory 1 has");
        yield return Case(s => s["ledger"]![0]![2] = "Stolen", "ledger[0] is not a ledger line");
        yield return Case(s => s["ledger"]![0]![6] = 99, "ledger[0] names inventory 99, which does not exist");
        yield return Case(s => s["inventories"] = new JsonObject(), "its \"inventories\" is not a list");
    }

    [Theory]
    [MemberData(nameof(BrokenStocks))]
    public void A_save_with_broken_goods_is_refused(string text, string reason)
    {
        var content = Content.Load(Path.Combine(AppContext.BaseDirectory, "content"));
        Assert.Contains(reason, Assert.Throws<ColonySaveException>(() => ColonySave.Read(text, content)).Message);
    }

    [Fact]
    public void A_reserve_cannot_be_below_zero() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Setup().Carts.Keep("wheat", -1));
}
