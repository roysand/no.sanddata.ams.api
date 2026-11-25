# Backlog: The car's battery level (Audi Q4 e-tron)

Status: idea, not specified yet. Roy asked for it on 2026-11-24, after spec 021 (the charger's state right now).
First find out which way in works today (see "Ways to read it"). Then discuss with Roy before running
`speckit-specify`.

## What Roy wants

How full the car's battery is ("charged to 72 %"), next to the charger's state on the Car tab and on the Now page's
car tile. Today the app can only say how many kWh this charge has added (spec 021's `sessionKwh`).

## Why the charger can't give it

The Zaptec charger charges with AC. On AC the charger and the car only agree on how much current may flow, and the
car never reports its battery level. Zaptec's state list has no battery percentage for that reason. The level has to
come from the car itself, through Audi's cloud (the one the myAudi app reads).

## Ways to read it (not checked yet)

These are from memory. Check each one's current state before choosing.

1. **The myAudi API, unofficial.** Audi has no public, documented API for owners. Home Assistant's "Audi Connect"
   add-on signs in with the owner's myAudi email and password, the way the app does, and reads the battery level,
   range and charging state. It's free and gives what Roy wants, but it can break whenever Audi changes its sign-in.
2. **A paid car data service** such as Enode, Smartcar or High Mobility. These connect to Audi through agreements
   with the maker, which is more stable. They're mostly sold to companies, and some charge per car. Check which of
   them take a single private user, and what they cost.
3. **Audi's own data access under the EU Data Act.** Since September 2025 the act has required car makers to give
   owners their car's data. Check what Audi offers for this and whether it can be read automatically.

The first guess is option 1, built as its own integration like OSO (water heater) and Zaptec (charger), with a
broken sign-in treated as an expected failure.

## What the spec has to settle

- **The login.** It's Roy's myAudi login, or the service's key, kept in the server's `.env` like the Zaptec login.
  It's never written to the database or logs.
- **How often to read.** Rarely, for example every 15–30 minutes, and maybe only while the car is plugged in (spec
  021's state can tell). Frequent reads can wake the car and drain its small 12 V battery, and VW Group cars are
  known for that. A cache like spec 021's `ChargerStateCache` fits.
- **What to show and how old it is.** Show the battery level in %, perhaps with range and the charge limit if the
  car gives them. Always show when the car last reported, since the car's figure can be much older than the
  charger's.
- **Which location the car belongs to.** For now it's one car on the location that has the charger. Decide whether
  that needs to be stored or can be set in configuration.
- **Data to stay away from.** The car's cloud also gives its GPS position and more. Read only what's needed. Never
  store or log the position, the car's VIN or the account.
- **Logging.** New EventIds are needed. 2100–2199 is the next free range (CLAUDE.md's table).
- **Frontend.** Write a handover note in `specs/_backlog/` once the API side is merged, as was done for spec 021.
