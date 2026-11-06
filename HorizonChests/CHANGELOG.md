# Changelog

## 1.7.0
- Own tables (`HorizonChests`, `HorizonChestsPlayer`) and own permissions (`hc.*`, replacing `ic.*`), so it no longer mixes with InfiniteChestsV3.
- `/transferchests 3` imports chests of the current world from an InfiniteChestsV3 database.
- Ported to Terraria 1.4.5.x / TShock 6.2.1 (.NET 9).
- Individual world chests, optional refill timer, master-contents editing, `/chest verify [fix]`, anti-griefing audit log.
- Console debug traces (`[HC]`) are disabled in release builds.
