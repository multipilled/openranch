# The plort market

How plort prices are set and how selling lowers them.

Code: `src/OpenRanch.Simulation/PlortMarket.cs`. Data reader: `src/OpenRanch.Formats/Game/MarketData.cs`.

## Where the numbers come from

The world scene holds one `EconomyDirector` script component. Its fields:

- `baseValueMap`: one row per item the market buys. `accept` points at the item's prefab (its
  `Identifiable` component gives the item id), `value` is the **base price** and `fullSaturation` is
  how many recent sales count as a fully saturated market for that plort.
- `saturationRecovery`: the fraction of saturation that wears off at each daily update.
- `dailyShutdownMins`: how many minutes after midnight the market stays closed.
- `saturationSensitivity`: present in the data but not used by the price rules we observed.

Checked: read from the game's data (`MarketData.Read`); the install test sells a pink plort at the
price worked out from the base value it reads.

## Saturation

- Every plort the market buys has a saturation number. A new game starts each at half of its
  `fullSaturation` (code-only; `PlortMarket.StartingSaturationFraction`).
- Each plort sold adds 1 to that plort's saturation, straight away.
- Once a day, at midnight, every saturation is multiplied by (1 − `saturationRecovery`), so a
  plort nobody sells recovers toward zero. The very first update of a new game skips this step.

## Price

Prices are worked out once a day, at midnight, and stay fixed until the next midnight: selling a
pile of plorts pays today's price for every one, and the lower price shows up tomorrow.

Today's price for a plort is the base price times three factors:

1. **Demand**: 1 plus the unsold share of the market, that is
   1 + (fullSaturation − saturation) ÷ fullSaturation, kept between 1 and 2. A plort nobody has sold
   lately pays up to double its base price; at or past full saturation it pays exactly the base
   price.
2. **Market mood**: a factor shared by every plort that wanders smoothly from day to day.
3. **Plort mood**: a second wandering factor of the same kind, separate for each plort.

Both mood factors drift between about 0.1 and 1.3, centred on 0.7, and change gradually over a
span of roughly ten days. The original draws them from a smooth noise function seeded once per new
game; openranch uses its own smooth random mood (`WanderingMood`) with the same spread and pace, so
exact daily prices differ from the original even for the same save. These numbers live only in the
game's code and are named constants in `PlortMarket.cs`.

The board shows the price rounded to a whole coin (halves round to the even coin), and the change
from yesterday's rounded price.

## Selling

- Selling pays the current rounded price per plort and raises that plort's saturation.
- The market refuses sales for the first `dailyShutdownMins` minutes after midnight while prices
  change.
- In game modes with a fixed market, there is no daily change and no shutdown: every plort pays
  1.5 × its base price forever (code-only; `PlortMarket.FixedPriceFactor`).

## The market at the ranch

The market stand near the ranch house (`techMarket` in the world scene) has a deposit hole: a trigger
sphere (`triggerDeposit`) with the `ScorePlort` component. Read from the game's data.

Studied on a developer's PC from how the original works:

- Anything that enters the hole is offered to the market. If the market buys that item (and isn't
  in its midnight shutdown), it pays today's price per item, the sale counts toward saturation, and
  the item disappears in a little burst. Anything else is left alone and falls back out.
- The money goes to the player's coins (newbucks), shown on the HUD.

## Not modelled yet

- After the Mochi story progress, each sale has a small chance to pay double in a different
  currency.
- Mods (game-mode cheats) can scale individual plort prices.
- Drones and plort collectors selling on the player's behalf go through the same rules.
