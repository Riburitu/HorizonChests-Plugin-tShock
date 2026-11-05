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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Terraria;

namespace HorizonChests
{
    /// <summary>
    /// Capa de compatibilidad para Terraria 1.4.5.x, donde Terraria.Chest cambió:
    ///  - el constructor es Chest(int index, int x, int y, bool bank, int maxItems),
    ///  - x / y / index son campos de solo lectura (se fijan en el constructor),
    ///  - Chest.chestItemSpawn desapareció,
    ///  - Chest.FindChest(x, y) usa un diccionario por coordenadas (_chestsByCoords) que solo se
    ///    rellena con Chest.Assign: un Chest creado con "new" NO queda registrado (justo lo que
    ///    queremos para los cofres temporales).
    /// </summary>
    public static class ChestCompat
    {
        /// <summary>
        /// Crea un Chest vanilla (sin registrarlo en Main.chest ni en el diccionario de coordenadas)
        /// que envuelve el array de objetos indicado. "index" debe ser la ranura de Main.chest donde
        /// se va a colocar, porque Chest.index se usa al serializar paquetes.
        /// </summary>
        public static Chest Create(Item[] items, int x, int y, int index = 0)
        {
            var chest = new Chest(index, x, y, false, items.Length);
            chest.item = items;
            return chest;
        }

        // ------------------------------------------------------------------ objeto del cofre a partir de tile/estilo

        private static Dictionary<(int tile, int style), int> placeables;

        /// <summary>
        /// Sustituye a Chest.chestItemSpawn[style]: busca el objeto que crea ese tile con ese estilo.
        /// Devuelve 0 si no se encuentra.
        /// </summary>
        public static int ItemForTile(int tileType, int style)
        {
            if (placeables == null)
            {
                var map = new Dictionary<(int, int), int>();
                for (int i = 1; i < ItemID_Count; i++)
                {
                    var item = new Item();
                    item.SetDefaults(i);
                    if (item.createTile >= 0 && item.type > 0)
                    {
                        var key = (item.createTile, item.placeStyle);
                        if (!map.ContainsKey(key))
                            map[key] = item.type;
                    }
                }
                placeables = map;
            }

            return placeables.TryGetValue((tileType, style), out int found) ? found : 0;
        }

        private static int ItemID_Count => Terraria.ID.ItemID.Count;
    }
}