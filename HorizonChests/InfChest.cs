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
using System.Text;
using Terraria;

namespace HorizonChests
{
    public class InfChest
    {
        public const int Slots = 40;

        public int id;
        public int userid;
        public int x;
        public int y;
        public Item[] items;
        public bool isPublic;
        public List<string> groups;
        public List<int> users;
        public int refill;
        public int worldid;
        public bool playerPlaced;

        public bool isRefill => refill > -1;

        public bool isEmpty
        {
            get
            {
                foreach (Item item in items)
                {
                    if (item != null && item.type != 0)
                        return false;
                }
                return true;
            }
        }

        public InfChest(int _userid, int _x, int _y, int _worldid)
        {
            userid = _userid;
            x = _x;
            y = _y;
            isPublic = false;
            groups = new List<string>();
            users = new List<int>();
            refill = -1;
            worldid = _worldid;
            items = new Item[Slots];
            for (int i = 0; i < Slots; i++)
                items[i] = new Item();
        }

        public void StringToItems(string input)
        {
            var result = new Item[Slots];
            for (int i = 0; i < Slots; i++)
                result[i] = new Item();

            if (!string.IsNullOrWhiteSpace(input))
            {
                int slot = 0;
                foreach (string entry in input.Split('~'))
                {
                    if (string.IsNullOrWhiteSpace(entry))
                        continue;
                    if (slot >= Slots)
                        break;

                    string[] p = entry.Split(',');
                    var item = new Item();
                    item.SetDefaults(int.Parse(p[0]));
                    item.stack = int.Parse(p[1]);
                    item.prefix = byte.Parse(p[2]);
                    result[slot++] = item;
                }
            }
            items = result;
        }

        public string ItemsToString()
        {
            var sb = new StringBuilder("~");
            foreach (Item item in items)
            {
                if (item == null)
                    sb.Append("0,0,0~");
                else
                    sb.Append(item.type).Append(',').Append(item.stack).Append(',').Append(item.prefix).Append('~');
            }
            return sb.ToString();
        }

        public void StringToUsers(string input)
        {
            users = string.IsNullOrWhiteSpace(input)
                ? new List<int>()
                : input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .Select(int.Parse).ToList();
        }

        public string UsersToString() => string.Join(",", users);

        public void StringToGroups(string input)
        {
            groups = string.IsNullOrWhiteSpace(input)
                ? new List<string>()
                : input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }

        public string GroupsToString() => string.Join(",", groups);

        public static Item[] ParseItems(string s)
        {
            var c = new InfChest(-1, 0, 0, 0);
            c.StringToItems(s);
            return c.items;
        }

        public static string SerializeItems(Item[] items)
        {
            var c = new InfChest(-1, 0, 0, 0) { items = items };
            return c.ItemsToString();
        }
    }
}