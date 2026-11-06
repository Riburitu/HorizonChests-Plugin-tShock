# HorizonChests

**English** · **[Español](#español)**

## English

TShock plugin that stores chests in a **database** instead of the world file, so a world can have far more than 1,000 chests, with protection, sharing, personal copies, refill, search and an anti-griefing audit log. It is a **fork of InfiniteChests V3 ported to Terraria 1.4.5.x** with improvements.

- **Compatible with:** Terraria 1.4.5.8 · TShock 6.2.1 (.NET 9) · plugin API 2.1
- **Status:** finished and tested by the author on Terraria 1.4.5.8 / TShock 6.2.1. The compiled `HorizonChests.dll` is published on the project's *Releases* page.
- **License:** [GNU GPL v3](LICENSE) for the code written in this fork — you may use, modify and share it, but any modified version you distribute must also be open source under the same license. See [Credits and license](#credits-and-license) for the status of the original code.

## Demo video
▶️ **[Watch the demo on YouTube](https://www.youtube.com/watch?v=KrSJbTuVm6o)** — *HorizonChests Exposition - TShock Plugin 1.4.5.8*

[![HorizonChests demo video](https://img.youtube.com/vi/KrSJbTuVm6o/hqdefault.jpg)](https://www.youtube.com/watch?v=KrSJbTuVm6o)

## Features
- Chests saved in the database configured in TShock (`sqlite` or `mysql`).
- Claim / unclaim chests, make them public, and share them with players or groups.
- **Individual world chests:** Every world chest has a separate personal instance for each player. All players receive the same original rewards independently, meaning one player's loot never affects or takes away another player's rewards. Everyone can loot the same chest and receive their own copy of its contents.
- Optional refill timer for personal world chest inventories.
- Edit the *master* contents of a chest, mark chests as breakable, search the whole database for an item, and check the database against the world (`/chest verify [fix]`).
- **Audit log** for anti-griefing: one line per modified slot in `tshock/HorizonChestsAudit/audit-YYYY-MM-DD.txt`.
- Chests near a player are sent automatically so they can be used for crafting without opening them.
- Tools to convert chests back into the world file, prune empty chests and **import from older plugins**.

## Dependencies / Required References
To compile, copy these 5 assemblies from your TShock folder into the `References` folder (they are not redistributed):

1. `ModFramework`
2. `MySql.Data`
3. `OTAPI`
4. `TerrariaServer`
5. `TShockAPI`

`Microsoft.Data.Sqlite` is restored automatically from NuGet; nothing to copy.

## Installation
1. **Remove `InfiniteChestsV3` from `ServerPlugins`** (both plugins use the same commands and hooks and must not run together).
2. Download `HorizonChests.dll` from *Releases* and copy it to `ServerPlugins`.
3. Restart the server. The tables `HorizonChests` and `HorizonChestsPlayer` are created automatically (SQLite file: `tshock/HorizonChests.sqlite`).
4. Give permissions to your groups (see below).

## Moving from InfiniteChestsV3 (new in 1.7.0)
HorizonChests uses its **own tables and its own permissions**, so it never touches your old data.

1. Keep your old database in place (`tshock/InfChests3.sqlite`, or the `InfChests3` table in MySQL).
2. Start the server with HorizonChests, load the world you want to migrate and run, from the console or as an admin:

   `/transferchests 3`

3. Chests **of the current world** are copied into HorizonChests. It can be repeated safely: a chest in the same position is replaced, not duplicated. Repeat it for each world.
4. Chests that come from a pure V3 database have no "placed by a player" flag, so a chest **with an owner** is treated as player-placed (can be destroyed by its owner) and an **unclaimed** one as a world chest (protected). Change it afterwards with `/chest breakable` if needed.
5. Rename the permissions of your groups (table below), e.g. `/group addperm default hc.use`.

`/transferchests 1` and `/transferchests 2` still import from the very old InfiniteChests V1 / V2.

> **Back up your world and your database before migrating.**

## Commands and permissions
Permissions changed from `ic.*` to `hc.*` in 1.7.0 so they do not mix with InfiniteChestsV3.

| Old (V3) | New | Allows |
|---|---|---|
| `ic.use` | `hc.use` | Use `/chest`. |
| `ic.claim` | `hc.claim` | `/chest claim`, `unclaim`, `allow`, `remove`, `allowgroup`, `removegroup`. |
| `ic.info` | `hc.info` | `/chest info`. |
| `ic.public` | `hc.public` | `/chest public`. |
| `ic.refill` | `hc.refill` | `/chest refill <seconds>`, `/chest refillall <seconds>`. |
| `ic.search` | `hc.search` | `/chest search <item name>`. |
| `ic.edit` | `hc.edit` | Manage other players' chests, `/chest master`, `breakable`, `verify [fix]`. |
| `ic.protect` | `hc.protect` | Chests the player places are claimed automatically. |
| `ic.breakworld` | `hc.breakworld` | Destroy protected world chests. |
| `ic.convert` | `hc.convert` | `/convchests` — convert all chests back into the world and disable the plugin. |
| `ic.prune` | `hc.prune` | `/prunechests` — delete empty chests. |
| `ic.transfer` | `hc.transfer` | `/transferchests <1\|2\|3>` — import from older versions. |

## Building from source
Install the .NET 9 SDK, create a `References` folder and copy `TShockAPI.dll`, `OTAPI.dll`, `TerrariaServer.dll`, `ModFramework.dll` and `MySql.Data.dll` from your TShock into it (do not redistribute them), then run `Compilar.bat` or `dotnet build -c Release`.

## Notes
- In-game messages are currently in English.
- It relies on Terraria internals that may change with game updates; the tested versions are listed above.

## Credits and license
- **Riburitu** — port to Terraria 1.4.5.x, improvements and this fork.
- [**Zaicon**](https://github.com/Zaicon/InfiniteChestsV3) — author of *InfiniteChests V3*; [**MarioE**](https://github.com/MarioE/InfiniteChests) — author of the original *InfiniteChests*. Thank you.
- **Note on the original code:** neither *InfiniteChests* (MarioE) nor *InfiniteChests V3* (Zaicon) include a license file, and their authors could not be contacted. The original code therefore remains the property of its authors and is credited to them; no additional rights over it are claimed here. The GNU GPL v3 applies to the additions and modifications written for this fork.

---

## Español

Plugin de TShock que guarda los cofres en una **base de datos** en lugar del archivo del mundo, de modo que un mundo puede tener muchísimos más de 1.000 cofres, con protección, compartición, copias personales, reposición, búsqueda y un registro de auditoría anti-griefing. Es un **fork de InfiniteChests V3 adaptado a Terraria 1.4.5.x** con mejoras.

- **Compatible con:** Terraria 1.4.5.8 · TShock 6.2.1 (.NET 9) · API de plugins 2.1
- **Estado:** terminado y probado por el autor en Terraria 1.4.5.8 / TShock 6.2.1. El `HorizonChests.dll` ya compilado se publica en la página de *Releases* del proyecto.
- **Licencia:** [GNU GPL v3](LICENSE) para el código escrito en este fork — puede usarlo, modificarlo y compartirlo, pero toda versión modificada que distribuya debe seguir siendo de código abierto con la misma licencia. Consulte [Créditos y licencia](#créditos-y-licencia) para el estado del código original.

## Vídeo de demostración
▶️ **[Ver la demostración en YouTube](https://www.youtube.com/watch?v=KrSJbTuVm6o)** — *HorizonChests Exposition - TShock Plugin 1.4.5.8*

[![Vídeo de demostración de HorizonChests](https://img.youtube.com/vi/KrSJbTuVm6o/hqdefault.jpg)](https://www.youtube.com/watch?v=KrSJbTuVm6o)

## Características
- Cofres guardados en la base de datos configurada en TShock (`sqlite` o `mysql`).
- Reclamar / liberar cofres, hacerlos públicos y compartirlos con jugadores o grupos.
- **Cofres del mundo individuales:** Cada cofre del mundo tiene una instancia personal e independiente para cada jugador. Todos los jugadores reciben las mismas recompensas originales de forma independiente, lo que significa que el botín de un jugador nunca afecta ni quita las recompensas de los demás. Todos pueden abrir el mismo cofre y recibir su propia copia del contenido.
- Temporizador de reposición opcional para los inventarios personales de los cofres del mundo.
- Editar el contenido *maestro* de un cofre, marcarlos como rompibles, buscar un objeto en toda la base de datos y comprobar la base de datos contra el mundo (`/chest verify [fix]`).
- **Registro de auditoría** anti-griefing: una línea por hueco modificado en `tshock/HorizonChestsAudit/audit-AAAA-MM-DD.txt`.
- Los cofres cercanos a un jugador se le envían automáticamente para poder craftear sin abrirlos.
- Herramientas para volver a pasar los cofres al archivo del mundo, eliminar cofres vacíos e **importar desde plugins anteriores**.

## Dependencias / Referencias necesarias
Para compilar, copie estos 5 ensamblados de la carpeta de su TShock a la carpeta `References` (no se redistribuyen):

1. `ModFramework`
2. `MySql.Data`
3. `OTAPI`
4. `TerrariaServer`
5. `TShockAPI`

`Microsoft.Data.Sqlite` se restaura solo desde NuGet; no hay que copiarlo.

## Instalación
1. **Quite `InfiniteChestsV3` de `ServerPlugins`** (ambos plugins usan los mismos comandos y eventos y no deben ejecutarse a la vez).
2. Descargue `HorizonChests.dll` desde *Releases* y cópielo a `ServerPlugins`.
3. Reinicie el servidor. Las tablas `HorizonChests` y `HorizonChestsPlayer` se crean solas (archivo SQLite: `tshock/HorizonChests.sqlite`).
4. Dé los permisos a sus grupos (ver más abajo).

## Pasar de InfiniteChestsV3 (novedad de la 1.7.0)
HorizonChests usa **sus propias tablas y sus propios permisos**, así que nunca toca sus datos antiguos.

1. Deje su base de datos antigua donde está (`tshock/InfChests3.sqlite`, o la tabla `InfChests3` en MySQL).
2. Arranque el servidor con HorizonChests, cargue el mundo que quiere migrar y ejecute, desde la consola o como administrador:

   `/transferchests 3`

3. Se copian los cofres **del mundo actual** a HorizonChests. Puede repetirse sin riesgo: un cofre en la misma posición se sustituye, no se duplica. Repítalo con cada mundo.
4. Los cofres de una base de datos V3 pura no tienen la marca «colocado por un jugador», así que un cofre **con dueño** se trata como colocado por un jugador (su dueño puede destruirlo) y uno **sin reclamar** como cofre del mundo (protegido). Si hace falta, cámbielo después con `/chest breakable`.
5. Renombre los permisos de sus grupos (tabla de abajo), por ejemplo `/group addperm default hc.use`.

`/transferchests 1` y `/transferchests 2` siguen importando desde las antiguas versiones V1 / V2 de InfiniteChests.

> **Haga una copia de seguridad del mundo y de la base de datos antes de migrar.**

## Comandos y permisos
Los permisos pasaron de `ic.*` a `hc.*` en la 1.7.0 para que no se mezclen con InfiniteChestsV3.

| Antiguo (V3) | Nuevo | Permite |
|---|---|---|
| `ic.use` | `hc.use` | Usar `/chest`. |
| `ic.claim` | `hc.claim` | `/chest claim`, `unclaim`, `allow`, `remove`, `allowgroup`, `removegroup`. |
| `ic.info` | `hc.info` | `/chest info`. |
| `ic.public` | `hc.public` | `/chest public`. |
| `ic.refill` | `hc.refill` | `/chest refill <segundos>`, `/chest refillall <segundos>`. |
| `ic.search` | `hc.search` | `/chest search <nombre del objeto>`. |
| `ic.edit` | `hc.edit` | Gestionar cofres de otros jugadores, `/chest master`, `breakable`, `verify [fix]`. |
| `ic.protect` | `hc.protect` | Los cofres que coloca el jugador se reclaman automáticamente. |
| `ic.breakworld` | `hc.breakworld` | Destruir cofres del mundo protegidos. |
| `ic.convert` | `hc.convert` | `/convchests` — devuelve todos los cofres al mundo y desactiva el plugin. |
| `ic.prune` | `hc.prune` | `/prunechests` — elimina los cofres vacíos. |
| `ic.transfer` | `hc.transfer` | `/transferchests <1\|2\|3>` — importa desde versiones anteriores. |

## Compilar desde el código fuente
Instale el SDK de .NET 9, cree una carpeta `References` y copie en ella `TShockAPI.dll`, `OTAPI.dll`, `TerrariaServer.dll`, `ModFramework.dll` y `MySql.Data.dll` de su TShock (no las redistribuya); después ejecute `Compilar.bat` o `dotnet build -c Release`.

## Notas
- Los mensajes del juego están por ahora en inglés.
- Depende de partes internas de Terraria que pueden cambiar con las actualizaciones del juego; las versiones probadas figuran arriba.

## Créditos y licencia
- **Riburitu** — adaptación a Terraria 1.4.5.x, mejoras y este fork.
- [**Zaicon**](https://github.com/Zaicon/InfiniteChestsV3) — autor de *InfiniteChests V3*; [**MarioE**](https://github.com/MarioE/InfiniteChests) — autor del *InfiniteChests* original. Gracias.
- **Nota sobre el código original:** ni *InfiniteChests* (MarioE) ni *InfiniteChests V3* (Zaicon) incluyen archivo de licencia, y no ha sido posible contactar con sus autores. Por tanto, el código original sigue siendo propiedad de sus autores y se les acredita; aquí no se reclama ningún derecho adicional sobre él. La GNU GPL v3 se aplica a las adiciones y modificaciones escritas para este fork.
