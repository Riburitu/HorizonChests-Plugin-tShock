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
    public static class ChestCompat
    {
        public static Chest Create(Item[] items, int x, int y, int index = 0)
        {
            var chest = new Chest(index, x, y, false, items.Length);
            chest.item = items;
            return chest;
        }


        private static Dictionary<(int tile, int style), int> placeables;

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