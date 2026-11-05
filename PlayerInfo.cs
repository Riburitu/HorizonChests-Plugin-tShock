// HorizonChests - TShock plugin: database-backed chests / cofres en base de datos
// Copyright (C) 2026 Riburitu
//
// Based on InfiniteChests V3 by Zaicon, which is in turn
// based on InfiniteChests by MarioE. / Basado en InfiniteChests V3 de Zaicon,
// que a su vez se basa en InfiniteChests de MarioE.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using Terraria;
using System.Collections.Generic;

namespace HorizonChests
{
    public class PlayerInfo
    {
        public int ChestIdInUse = -1;
        public ChestAction Action = ChestAction.None;
        public string ExtraInfo = "";

        // Copia personal del cofre que tiene abierto (null = cofre compartido, o contenido "maestro").
        public Item[] InstanceItems;
        public string InstanceKey = "";
        // Unix (segundos) en que se creó la copia guardada; 0 = todavía no se ha guardado nada.
        public long InstanceOpened;
        // Ids de cofres de la BD cuyo contenido ya tiene este cliente en su Main.chest.
        public HashSet<int> KnownChests = new HashSet<int>();

        // 1.4.5: envío automático de los cofres cercanos (para craftear sin abrirlos).
        // Casilla donde se hizo el último escaneo (int.MinValue = nunca).
        public int PushX = int.MinValue;
        public int PushY;
        // Unix (segundos) del próximo reescaneo aunque el jugador no se mueva.
        public long NextScan;
        // Ids de cofres cercanos aún por enviar (los más próximos primero).
        public List<int> PendingChests = new List<int>();
    }
}