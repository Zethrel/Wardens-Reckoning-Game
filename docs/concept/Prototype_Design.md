# Warden's Reckoning

## Prototype Design Document

**Version:** 0.1 --- Prototype planning\
**Status:** Working concept, not a final production specification\
**Genre:** Colony simulation / settlement builder / logistics and
underground engineering\
**Primary perspective:** Top-down\
**World structure:** Surface plus layered underground maps\
**Logistics model:** Physical transport, with abstracted inventory
quantities

------------------------------------------------------------------------

## 1. High-level vision

*Warden's Reckoning* is a colony and settlement simulation about
building a society in a world whose people, economy, geology,
infrastructure and environment are interconnected.

The player guides a growing settlement from a small group of inhabitants
into a complex society. Colonists perform the work, form relationships,
develop skills and respond to changing circumstances. The player shapes
priorities, infrastructure, production, land use and long-term
direction.

The game combines four broad design inspirations:

-   **RimWorld:** Character-driven stories, colonist traits,
    relationships and emergent events.
-   **Dwarf Fortress:** Deep simulation, geology, underground
    construction and interacting world systems.
-   **Towns:** Interdependent crafting and production chains.
-   **The Settlers:** Physical logistics, transport networks and
    settlement development.

These are inspirations, not features to copy literally. The goal is to
make the systems work together as one coherent simulation.

### Core design statement

> The player sees the world from above, manages what happens beneath it,
> and watches the consequences unfold through physical activity.

The surface provides the settlement. The underground provides resources
and engineering challenges. Physical logistics connects the two.
Colonists give the simulation its human dimension.

------------------------------------------------------------------------

## 2. Core design pillars

### 2.1 A living settlement

Colonists are individuals rather than interchangeable workers.

Long-term design goals include:

-   Traits, skills, interests and personal histories.
-   Relationships, friendships, rivalries and family connections.
-   Needs, ambitions, fears and personal preferences.
-   Work schedules and daily routines.
-   Skills that improve through relevant work.
-   Stories that emerge from the interaction of individuals and world
    events.

The player should normally manage priorities, jobs and policies rather
than manually control every colonist.

### 2.2 A physically connected world

The surface and underground are different views and layers of the same
world.

-   Underground tunnels connect to actual surface entrances and shafts.
-   Excavation changes the geological structure.
-   Water can move through connected spaces.
-   Roads and transport routes connect production sites.
-   Resources must be moved between buildings.
-   Construction and mining can affect the stability of nearby terrain.

### 2.3 Interdependent production

Resources should have origins, uses, storage locations and transport
requirements.

A mine producing ore does not automatically supply a distant workshop.
Goods need to be collected, transported, stored and delivered.

The player should be able to trace a shortage back to its cause.

### 2.4 Consequences at multiple scales

A small decision can have wider consequences.

For example:

1.  Workers are reassigned from a mine.
2.  Ore production decreases.
3.  The smelter runs out of input.
4.  Metal production slows.
5.  Construction projects are delayed.
6.  The settlement has fewer resources available for expansion.

This cascading interaction is the proposed **Resonance Engine**: a
design principle for connecting local events to settlement-wide
outcomes.

------------------------------------------------------------------------

## 3. Camera and world presentation

### 3.1 Top-down camera

The selected camera direction is top-down rather than traditional
isometric.

The camera should prioritise:

-   Clear building footprints.
-   Readable terrain and elevation.
-   Visible roads and transport routes.
-   Understandable distances between buildings.
-   Visibility of workers, carts and other activity.
-   Efficient planning across large areas.

Buildings should not obscure too much of the surrounding map. Roofs may
fade or become transparent when selected or when they obstruct an
important view.

### 3.2 Zoom levels

Use smooth zoom transitions with three broad presentation levels.

**Close zoom --- Activity**

-   Individual colonists and their tasks.
-   Doors, entrances and building activity.
-   Carts and visible cargo.
-   Local environmental details.

**Medium zoom --- Settlement**

-   Buildings and their footprints.
-   Farms, roads, workshops and warehouses.
-   Neighbourhoods and industrial districts.
-   Local resource and traffic overlays.

**Far zoom --- Region**

-   Settlement boundaries.
-   Rivers, forests, hills and resource deposits.
-   Routes between settlements.
-   Regional territory and points of interest.

The simulation should not depend on the camera zoom. Zoom changes
presentation, not the underlying world state.

### 3.3 Visual clarity

Large settlements can become visually crowded. Use contextual
presentation:

-   Reduce decorative detail at distant zoom levels.
-   Hide transport-route overlays unless requested.
-   Use small, consistent indicators for shortages and warnings.
-   Allow the player to highlight professions, resources or building
    types.
-   Avoid filling the screen with permanent notifications.

------------------------------------------------------------------------

## 4. Three complementary gameplay views

These are not separate game modes. They are different perspectives on
one continuous simulation.

### 4.1 Settlement view --- Life above ground

This is the primary view and likely where the player spends most of
their time.

It displays:

-   Colonists and their daily activities.
-   Houses and communal buildings.
-   Farms, forests and natural terrain.
-   Workshops and industrial buildings.
-   Roads, paths and transport activity.
-   Seasonal changes and environmental events.

Typical player actions:

-   Designate construction areas.
-   Place buildings and infrastructure.
-   Assign job priorities.
-   Manage food, housing and other necessities.
-   Observe colonists and their relationships.
-   Respond to shortages and unexpected events.
-   Expand when population and resources permit.

**Example:** Winter is approaching and the settlement's food stores are
low. The player prioritises harvesting, expands storage, delays
nonessential construction or arranges trade.

The player should make strategic decisions without needing to direct
every worker individually.

### 4.2 Production and logistics view --- The economic machinery

This view explains how resources move through the settlement.

It should show:

-   Production buildings and their inputs and outputs.
-   Resource inventories and storage capacity.
-   Transport routes and delivery activity.
-   Worker allocation.
-   Supply and demand.
-   Bottlenecks and blocked production chains.

A player should be able to select a resource or building and trace its
supply chain.

**Example: Metal production**

1.  A mine extracts ore.
2.  Workers or carts move ore to a smelter.
3.  The smelter processes ore using fuel.
4.  Metal is delivered to a smithy or construction store.
5.  A workshop turns the material into products.

If the smithy lacks metal, the player should be able to determine
whether the cause is low extraction, insufficient fuel, missing workers,
a full intermediate store or delayed transport.

The interface should explain the reason for a shortage rather than only
displaying an error.

### 4.3 Underground view --- Engineering and exploration

The underground is represented as multiple vertically stacked maps
sharing the same world coordinates.

Proposed initial layers:

-   **Level 0 --- Surface:** Settlement, farms, forests, rivers, roads
    and entrances.
-   **Level -1 --- Shallow underground:** Soil, shallow deposits, caves
    and groundwater.
-   **Level -2 --- Intermediate underground:** Larger deposits, mining
    operations and underground construction.
-   **Level -3 and below --- Deep underground:** Deeper geological
    formations, rare resources and more challenging environmental
    conditions.

These are gameplay layers, not a claim that every real geological region
follows the same structure.

Player actions:

-   Designate mining areas.
-   Construct tunnels and underground rooms.
-   Build support structures.
-   Locate and extract deposits.
-   Manage drainage and water.
-   Connect mines to surface transport.
-   Expand underground workshops and storage.

The layers must remain physically connected. A shaft on one layer should
connect to a real location on the layer above. Water, excavation and
structural changes should affect connected spaces.

### 4.4 Switching between views

The player should be able to switch views without losing context.

Examples:

-   Select a mine in the settlement view, then open its production
    connections.
-   Select a mine in the production view, then inspect its underground
    location.
-   Select a warehouse, then highlight all routes delivering goods to
    it.
-   Select a geological deposit, then identify possible surface
    entrances.

A persistent interface should keep the selected building, resource or
location when changing perspective.

------------------------------------------------------------------------

## 5. Resources: A chemically inspired model

### 5.1 Design recommendation

Use the periodic table as the foundation of the resource model, but do
not attempt to simulate all chemistry in the first prototype.

The long-term architecture may support all 118 elements. The initial
game should use a carefully selected subset.

The goal is **chemically inspired material simulation**, not an
atomic-level chemistry simulator.

### 5.2 Three levels of resources

Keep these concepts separate.

**Elements**

The underlying chemical building blocks, such as iron, copper, carbon,
silicon, oxygen and sulfur.

**Materials**

Useful substances made from elements or naturally occurring mixtures,
such as:

-   Iron ore.
-   Copper ore.
-   Native copper.
-   Iron.
-   Steel.
-   Glass.
-   Clay.
-   Limestone.
-   Timber.
-   Charcoal.
-   Stone.
-   Concrete or mortar, if included in the chosen technology level.

**Products**

Objects made from materials:

-   Tools.
-   Pipes.
-   Building components.
-   Furniture.
-   Carts.
-   Machinery.
-   Containers.
-   Household goods.

This separation allows the game to model meaningful material
transformations without requiring every item to be a chemical
simulation.

### 5.3 Suggested prototype elements

Start with a small selection that supports mining, metallurgy,
construction and basic production.

  Element     Symbol   Potential gameplay role
  ----------- -------- ------------------------------------------------
  Iron        Fe       Metal production and tools
  Copper      Cu       Conductive material and metalworking
  Carbon      C        Charcoal, fuel and material processing
  Oxygen      O        Part of oxides and environmental chemistry
  Silicon     Si       Silicate minerals and glass-related production
  Sulfur      S        Mineral processing and specialist materials
  Calcium     Ca       Limestone and lime-related materials
  Aluminium   Al       Later metalworking and advanced industry
  Tin         Sn       Alloy production
  Zinc        Zn       Alloy production and specialist industry
  Sodium      Na       Salt and selected chemical chains
  Chlorine    Cl       Salt compounds and selected chemistry

The prototype does not need to expose every element directly to the
player. Some may be represented through ores or compounds until the
relevant technology is available.

### 5.4 Compounds and mixtures

Not every resource should be a pure element.

For example:

-   Water is H₂O.
-   Common salt is NaCl.
-   Limestone is primarily calcium carbonate, CaCO₃.
-   Silica is SiO₂.
-   Iron ores may contain iron oxides and other minerals.

A deposit can therefore contain a mineral or ore mixture rather than a
pure element.

For the prototype, recipes can use simplified named resources such as
`iron_ore`, `charcoal`, `iron` and `steel`. Their underlying composition
can be added as data fields without simulating every atom.

### 5.5 Materials should have properties

Materials should not merely be different icons with different prices.

Potential properties:

-   Density.
-   Hardness.
-   Brittleness.
-   Toughness.
-   Thermal conductivity.
-   Electrical conductivity.
-   Melting point.
-   Corrosion resistance.
-   Flammability.
-   Structural strength.
-   Workability.

Not every property needs to affect the prototype. Start with properties
that create meaningful gameplay.

For example:

-   Structural strength influences support and construction.
-   Hardness influences mining effort and tool wear.
-   Flammability influences fire behaviour.
-   Corrosion resistance influences long-term infrastructure durability.
-   Density influences transport mass and load limits.

Use simplified game values, not a full scientific material database.

### 5.6 Recipes and transformations

A recipe defines inputs, outputs, processing time, required building and
optional conditions.

Example:

**Smelting iron**

-   Input: iron ore.
-   Input: fuel.
-   Output: iron.
-   Building: smelter.
-   Conditions: available worker and operating capacity.

**Making steel**

-   Input: iron.
-   Input: carbon-bearing material.
-   Output: steel.
-   Building: appropriate metalworking facility.
-   Conditions: technology and sufficient processing capability.

These are simplified gameplay recipes. Exact chemistry and historical
production methods can be refined later.

### 5.7 Data-driven resource definitions

Resource definitions should live in data files rather than being
hard-coded into individual buildings.

Illustrative resource record:

``` json
{
  "id": "iron",
  "display_name": "Iron",
  "category": "element",
  "symbol": "Fe",
  "density": 7.87,
  "hardness": 4,
  "tags": ["metal", "structural", "conductive"],
  "prototype_enabled": true
}
```

Illustrative recipe:

``` json
{
  "id": "smelt_iron",
  "building": "smelter",
  "inputs": {
    "iron_ore": 3,
    "charcoal": 1
  },
  "outputs": {
    "iron": 2
  },
  "duration_seconds": 20
}
```

These values are placeholders for testing, not scientifically accurate
production ratios.

The data model should eventually support:

-   Elements.
-   Compounds.
-   Minerals and ores.
-   Materials.
-   Products.
-   Recipes.
-   Material properties.
-   Technology requirements.
-   Resource deposits.

------------------------------------------------------------------------

## 6. Physical logistics

### 6.1 Core principle

Goods should physically travel through the world, but the simulation
should not create a separate complex object for every unit of inventory.

A warehouse can hold a stack of 100 logs. A cart can transport 20 logs
as one delivery job. The cart has a visible load, while the simulation
tracks the exact quantity.

This provides visible logistics without unnecessary object overhead.

### 6.2 Transport methods

Introduce transport gradually.

**Manual carrying**

-   Suitable for early settlements.
-   Flexible and simple.
-   Limited carrying capacity.

**Carts and wagons**

-   Move larger quantities.
-   Benefit from roads.
-   Require transport workers and, where appropriate, animals.
-   Can be delayed by congestion or long routes.

**Conveyors**

-   Specialised infrastructure for continuous industrial movement.
-   Require construction and maintenance.
-   Useful for compact production districts.

**Mine carts and rail**

-   Specialised underground transport.
-   Useful for larger mining operations.
-   Require connected tunnels and infrastructure.

### 6.3 Roads

Roads should have meaningful mechanical effects.

Suggested categories:

  Road type          Gameplay role
  ------------------ -------------------------------
  Dirt path          Basic movement
  Improved road      Better movement and transport
  Paved road         Higher traffic capacity
  Industrial road    Supports heavy transport
  Underground rail   Specialised mine transport

Roads should influence travel time, movement capacity and transport
efficiency.

### 6.4 Warehouses and distribution

Warehouses should be active parts of the economy.

Possible settings:

-   Accepted resource categories.
-   Maximum storage capacity.
-   Minimum stock targets.
-   Delivery priorities.
-   Preferred destinations.
-   Assigned transport workers.

The player should be able to create local distribution hubs near
industrial areas.

Avoid making every building automatically access every resource in the
entire settlement.

### 6.5 Delivery jobs

A delivery job should describe:

-   Resource type.
-   Quantity.
-   Source inventory.
-   Destination inventory.
-   Transport method.
-   Priority.
-   Current status.

Possible statuses:

-   Waiting for pickup.
-   Assigned.
-   In transit.
-   Delivered.
-   Blocked.
-   Cancelled.

A delivery job is a logical simulation record. A visible worker or cart
can represent it.

### 6.6 Logistics overlays

The production view should provide overlays for:

-   Resource movement.
-   Delivery destinations.
-   Congested routes.
-   Warehouse capacity.
-   Production shortages.
-   Transport availability.

The player should be able to trace a resource from extraction to final
use.

------------------------------------------------------------------------

## 7. Underground geology and engineering

### 7.1 Layered maps

Each layer should use the same horizontal coordinate system. A tile at
`(x, y)` on one level should correspond to the same world location on
other levels.

Vertical connections link layers:

-   Stairways.
-   Shafts.
-   Ramps.
-   Natural caves.
-   Specialised lifts, if technology supports them.

### 7.2 Geological data

For the prototype, each underground tile may store:

-   Geological material.
-   Hardness.
-   Excavation state.
-   Known resource deposit.
-   Water state.
-   Structural support state.

Later systems may add:

-   Geological strata.
-   Aquifers.
-   Fluid pressure.
-   Heat.
-   Gas.
-   Erosion.
-   More detailed structural simulation.

### 7.3 Structural integrity

Structural stability should be understandable and predictable.

Potential influences:

-   Material properties.
-   Tunnel span.
-   Support placement.
-   Geological conditions.
-   Water exposure.
-   Nearby excavation.

The player should receive warnings and have opportunities to respond.
Avoid relying on arbitrary random collapses.

### 7.4 Water

Water should eventually be able to move through connected underground
spaces and drainage systems.

For the first prototype, use a simplified model:

-   A tile can be dry, damp or flooded.
-   A mine can accumulate water if drainage is inadequate.
-   Drainage infrastructure can move water towards an outlet.
-   Flooded tiles can prevent or slow work.

A detailed fluid simulation is not required to prove the gameplay
concept.

------------------------------------------------------------------------

## 8. Resonance Engine: Cascading consequences

The Resonance Engine is the design principle that makes local systems
affect the wider settlement.

Example:

1.  A mine loses workers.
2.  Ore output falls.
3.  The smelter receives fewer deliveries.
4.  Metal production decreases.
5.  Construction and manufacturing are delayed.
6.  The settlement's plans must change.

Other examples:

-   Deforestation can affect soil and erosion.
-   A respected healer can influence morale and diplomacy.
-   A transport bottleneck can cause shortages despite sufficient
    production.
-   A damaged road can isolate a production site.
-   A historical site can influence colonists who have a connection to
    it.

The important requirement is traceability: players should be able to
understand why a consequence occurred.

------------------------------------------------------------------------

## 9. Interface proposal

Use one persistent interface across all views.

  Screen area   Purpose
  ------------- -------------------------------------------------------
  Top bar       Resources, population, food, season and alerts
  Left panel    Selected building, colonist or infrastructure details
  Main area     Game world
  Right panel   Construction, management and logistics tools
  Bottom bar    View switching and underground layer selection
  Minimap       Settlement and surrounding region

### Suggested overlays

-   Logistics: goods movement and transport congestion.
-   Employment: assigned workers and available labour.
-   Geology: known underground deposits.
-   Water: known water sources and flow.
-   Structural: potentially unstable structures.
-   Territory: ownership and faction borders.

Overlays should be optional and readable. The player should not need to
display every system at once.

------------------------------------------------------------------------

## 10. Engine and technical architecture

### 10.1 Recommendation: use an existing engine

Do not begin by writing a complete custom game engine.

A custom engine would require substantial work on rendering, input,
asset management, editor tools, saving, audio and platform support
before the core game can be tested.

Instead, use an existing engine for presentation and build a custom
simulation architecture inside it.

### 10.2 Engine options

#### Godot 4

**Best for:** An accessible 2D prototype and rapid visual iteration.

Advantages:

-   Open-source engine.
-   Strong 2D workflow.
-   Flexible interface and scene tools.
-   Can use C# or GDScript.
-   Supports multithreading.

Considerations:

-   The simulation architecture must be designed carefully.
-   Avoid creating every resource stack, delivery and tile as a
    heavyweight scene node.
-   Performance-critical simulation may eventually need a native
    extension or separate core.

Official documentation:\
https://godotengine.org/\
https://docs.godotengine.org/en/stable/tutorials/performance/using_multiple_threads.html

#### Unity with DOTS

**Best for:** A team or developer prioritising large-scale data-oriented
simulation and a mature editor ecosystem.

Relevant technologies:

-   Entities / ECS.
-   C# Job System.
-   Burst Compiler.
-   Data-oriented collections.

Advantages:

-   Designed for data-oriented processing.
-   Supports parallel work across CPU cores.
-   Can combine ECS and traditional GameObjects.
-   Strong tooling and learning resources.

Considerations:

-   DOTS introduces architectural complexity.
-   It is important to understand the ECS workflow rather than mixing
    architectures without a plan.

Official documentation:\
https://unity.com/ecs\
https://unity.com/dots

#### Bevy

**Best for:** A Rust-oriented project where ECS and data-oriented design
are central from the beginning.

Advantages:

-   Built around ECS.
-   Parallel system scheduling.
-   Open source under MIT or Apache 2.0 licensing.
-   2D and 3D rendering support.

Considerations:

-   More engineering-oriented than beginner-oriented.
-   APIs and ecosystem are still evolving.
-   Requires learning Rust if it is not already familiar.

Official documentation:\
https://bevy.org/\
https://docs.rs/bevy/latest/bevy/ecs/

### 10.3 Final recommendation

For an initial prototype, use **Godot 4** unless you already have
substantial Unity or Rust experience.

Keep the simulation independent from the rendering layer. If profiling
later shows that the simulation needs more performance, move the hot
paths into a native module or a separate simulation library.

If the project grows into a large-scale simulation with many thousands
of active agents and complex parallel processing, evaluate Unity DOTS or
Bevy before committing to a custom engine.

A custom simulation core is sensible. A complete custom engine is not
necessary at this stage.

### 10.4 Simulation architecture

Separate the game into broad systems:

1.  **World system:** Tiles, layers, terrain and world coordinates.
2.  **Colonist system:** Needs, skills, jobs, relationships and
    movement.
3.  **Resource system:** Inventories, elements, materials and products.
4.  **Production system:** Recipes, buildings and processing.
5.  **Logistics system:** Delivery jobs, transport and routes.
6.  **Geology system:** Deposits, excavation and structural properties.
7.  **Water system:** Simplified water movement and drainage.
8.  **Presentation system:** Rendering, animation, UI and overlays.

The simulation should own the authoritative world state. The
presentation layer reads that state and displays it.

### 10.5 Avoid one-object-per-resource

Do not represent every log, stone or iron bar as an independent scene
object.

Prefer:

-   Inventory stacks for stored quantities.
-   Delivery records for transport tasks.
-   A small number of visible carriers representing active jobs.
-   Tile data stored in arrays or chunked structures.
-   Buildings represented as structured simulation records.

This is one of the most important performance decisions in the project.

### 10.6 Update frequency

Not every system needs to update every frame.

Possible approach:

-   Rendering and camera: every frame.
-   Character movement: regular movement updates.
-   Delivery assignment: periodic or event-driven.
-   Production: when inputs, outputs or timers change.
-   Needs and relationships: scheduled simulation ticks.
-   Geological changes: when relevant events occur.
-   Distant settlement activity: lower-frequency updates.

Use profiling to decide update rates. Do not optimise based only on
assumptions.

------------------------------------------------------------------------

## 11. Prototype scope

The first prototype should prove that the three selected gameplay views
work together.

Do not begin with hundreds of resources, a large world, advanced
diplomacy or a complete colony simulation.

### 11.1 Prototype objective

Build a small settlement that can extract a resource underground,
transport it physically, process it and consume the resulting material.

### 11.2 Minimum feature list

**World**

-   [ ] Top-down surface map.
-   [ ] One underground layer.
-   [ ] Shared horizontal coordinates.
-   [ ] Layer switching.
-   [ ] One mine entrance connecting the layers.

**Resources**

-   [ ] Stone.
-   [ ] Timber.
-   [ ] Iron ore.
-   [ ] Charcoal.
-   [ ] Iron.
-   [ ] A data-driven resource definition format.
-   [ ] Basic inventory stacks.

**Buildings**

-   [ ] Mine.
-   [ ] Warehouse.
-   [ ] Smelter.
-   [ ] Construction building.

**Colonists**

-   [ ] A handful of workers.
-   [ ] Basic movement.
-   [ ] Simple work assignments.
-   [ ] Manual carrying.

**Logistics**

-   [ ] Delivery jobs.
-   [ ] Resource pickup and delivery.
-   [ ] Visible transport.
-   [ ] Basic road movement.
-   [ ] Warehouse inventories.

**Production**

-   [ ] Mine produces iron ore.
-   [ ] Ore is delivered to warehouse or smelter.
-   [ ] Smelter consumes ore and charcoal.
-   [ ] Smelter produces iron.
-   [ ] Iron is delivered to construction.

**Interface**

-   [ ] Resource display.
-   [ ] Building selection.
-   [ ] Inventory panel.
-   [ ] Production-chain view.
-   [ ] Underground layer selector.
-   [ ] Basic delivery status.

### 11.3 Prototype gameplay loop

1.  Place a mine entrance on the surface.
2.  Switch to the underground layer.
3.  Designate an iron deposit for extraction.
4.  Assign workers to the mine.
5.  Produce iron ore.
6.  Create a delivery job.
7.  Have a worker or cart transport the ore.
8.  Store or deliver the ore to the smelter.
9.  Produce iron using ore and charcoal.
10. Deliver iron to the construction building.
11. Observe the resulting inventory and production changes.

The prototype succeeds when this process is understandable, reliable and
satisfying to observe.

------------------------------------------------------------------------

## 12. Suggested development milestones

### Milestone 1 --- World and camera

-   Create a top-down tile map.
-   Implement camera movement and zoom.
-   Add building placement.
-   Add a second map layer.
-   Implement layer switching.

**Success condition:** The player can move around the surface, place a
mine entrance and inspect the underground.

### Milestone 2 --- Resource data

-   Create resource definitions.
-   Add inventories and stacks.
-   Add mine output.
-   Add basic storage.
-   Add resource inspection UI.

**Success condition:** Resources can be created, stored, inspected and
saved.

### Milestone 3 --- Workers and transport

-   Add colonists.
-   Implement movement and pathfinding.
-   Add delivery jobs.
-   Implement pickup and delivery.
-   Display cargo visually.

**Success condition:** A worker can carry ore from a mine to a
warehouse.

### Milestone 4 --- Production chains

-   Add the smelter.
-   Add recipes.
-   Add input and output inventories.
-   Add production timers.
-   Add shortage explanations.

**Success condition:** Ore and fuel become iron through a visible
production chain.

### Milestone 5 --- Integrated gameplay

-   Add the construction building.
-   Consume iron in construction.
-   Add logistics overlays.
-   Add basic transport bottlenecks.
-   Add a simple way to trace shortages.

**Success condition:** The player can identify and resolve a production
or transport bottleneck.

### Milestone 6 --- Simulation foundations

Only after the core loop works:

-   Add material properties.
-   Add basic geology.
-   Add structural stability.
-   Add simple underground water.
-   Add more colonist needs and skills.
-   Profile performance and identify bottlenecks.

------------------------------------------------------------------------

## 13. Features deliberately postponed

These are part of the broader vision but should not block the first
prototype:

-   All 118 elements and detailed chemistry.
-   Advanced metallurgy and large recipe catalogues.
-   Multiple underground geological layers.
-   Detailed aquifer and fluid simulation.
-   Large-scale structural simulation.
-   Complex colonist relationships and family systems.
-   Diplomacy and regional factions.
-   Military and territorial systems.
-   AI storytellers.
-   Large-scale trade.
-   Modding API.
-   Very large populations and millions of simulated objects.
-   Long-term victory conditions.

These features should be introduced when the prototype demonstrates that
the core simulation is worth expanding.

------------------------------------------------------------------------

## 14. Technical principles to preserve

1.  **Data-driven content:** Resources, recipes, buildings and materials
    should be defined in data files where practical.
2.  **Simulation/rendering separation:** The world simulation must not
    depend on visible scene objects.
3.  **Stable identifiers:** Buildings, inventories, resources and
    delivery jobs need persistent identifiers.
4.  **Traceable outcomes:** The player should be able to understand why
    production or transport stopped.
5.  **Event-driven updates where practical:** Avoid recalculating every
    system every frame.
6.  **Profile before optimising:** Measure real bottlenecks.
7.  **Save compatibility:** Version save data from the beginning, even
    in prototype form.
8.  **Incremental complexity:** Add systems only when the previous
    systems work together.
9.  **Deterministic simulation where feasible:** Consistent simulation
    results make debugging and testing easier.
10. **No premature engine development:** Build the game, not an engine,
    until a measured limitation proves otherwise.

------------------------------------------------------------------------

## 15. Open design questions

These questions should be resolved during prototyping rather than
assumed now.

-   Should the world use square tiles, hexes or a continuous grid?
-   Should colonists move freely within tiles or follow tile-based
    paths?
-   How large should a world chunk be?
-   Should transport workers use dedicated jobs or general labour
    priorities?
-   Should warehouses automatically distribute goods or require explicit
    rules?
-   How detailed should material properties become?
-   Which geological processes need continuous simulation and which can
    be event-driven?
-   How should the game represent time and simulation speed?
-   What population size should the first performance target support?
-   Should the game eventually support mod-defined elements, materials
    and recipes?

------------------------------------------------------------------------

## 16. Prototype success criteria

The first prototype does not need to look like a finished game.

It should demonstrate these five things:

1.  The surface and underground feel like parts of one world.
2.  The player can understand the relationship between extraction,
    storage, transport and production.
3.  Goods visibly move through the settlement.
4.  A shortage can be traced to a specific cause.
5.  The simulation remains responsive with multiple workers, inventories
    and delivery jobs.

If these are successful, the project has a solid foundation for adding
the deeper colony, geological, economic and historical systems.

------------------------------------------------------------------------

## 17. Reference links

These links are starting points for engine evaluation and technical
learning.

-   Godot Engine: https://godotengine.org/
-   Godot multithreading documentation:
    https://docs.godotengine.org/en/stable/tutorials/performance/using_multiple_threads.html
-   Unity ECS: https://unity.com/ecs
-   Unity DOTS: https://unity.com/dots
-   Bevy Engine: https://bevy.org/
-   Bevy ECS documentation: https://docs.rs/bevy/latest/bevy/ecs/

------------------------------------------------------------------------

**End of prototype design document.**
