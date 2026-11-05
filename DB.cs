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
using System.Data;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using MySql.Data.MySqlClient;
using Terraria;
using TShockAPI;
using TShockAPI.DB;

namespace HorizonChests
{
    public static class DB
    {
        private static IDbConnection db;

        // Una única conexión compartida: se serializa el acceso.
        private static readonly object Sync = new object();

        public static void Connect()
        {
            switch (TShock.Config.Settings.StorageType.ToLower())
            {
                case "mysql":
                    string[] host = TShock.Config.Settings.MySqlHost.Split(':');
                    db = new MySqlConnection
                    {
                        ConnectionString = string.Format("Server={0}; Port={1}; Database={2}; Uid={3}; Pwd={4};",
                            host[0],
                            host.Length == 1 ? "3306" : host[1],
                            TShock.Config.Settings.MySqlDbName,
                            TShock.Config.Settings.MySqlUsername,
                            TShock.Config.Settings.MySqlPassword)
                    };
                    break;
                case "sqlite":
                    db = new SqliteConnection("Data Source=" + Path.Combine(TShock.SavePath, "HorizonChests.sqlite"));
                    break;
                default:
                    throw new InvalidOperationException("HorizonChests: StorageType no soportado.");
            }

            // Se crea la tabla con SQL directo: las clases SqlTableCreator/IQueryBuilder
            // ya no son accesibles/estables para plugins en TShock 6.x.
            string idColumn = db.GetSqlType() == SqlType.Sqlite
                ? "ID INTEGER PRIMARY KEY AUTOINCREMENT"
                : "ID INT NOT NULL AUTO_INCREMENT PRIMARY KEY";

            db.Query(
                "CREATE TABLE IF NOT EXISTS HorizonChests (" +
                idColumn + ", " +
                "UserID INT, X INT, Y INT, " +
                "Items TEXT, " +
                "Public INT, " +
                "Users TEXT, " +
                "`Groups` TEXT, " +
                "Refill INT, " +
                "WorldID BIGINT, " +
                "PlayerPlaced INT DEFAULT 0);");

            // Migración: bases de datos creadas antes de esta columna (los cofres existentes quedan como "del mundo").
            try
            {
                using (db.QueryReader("SELECT PlayerPlaced FROM HorizonChests LIMIT 1;")) { }
            }
            catch (Exception)
            {
                db.Query("ALTER TABLE HorizonChests ADD COLUMN PlayerPlaced INT DEFAULT 0;");
            }

            // Copias personales: cada jugador tiene su propio contenido de cada cofre "instanciado".
            // PlayerKey = "a:<id de cuenta>" o "u:<UUID>" si no ha iniciado sesión.
            db.Query(
                "CREATE TABLE IF NOT EXISTS HorizonChestsPlayer (" +
                "ChestID INT NOT NULL, " +
                "PlayerKey VARCHAR(100) NOT NULL, " +
                "Items TEXT, " +
                "Opened BIGINT, " +
                "PRIMARY KEY (ChestID, PlayerKey));");

            // Limpieza de copias de cofres que ya no existen.
            db.Query("DELETE FROM HorizonChestsPlayer WHERE ChestID NOT IN (SELECT ID FROM HorizonChests);");
        }

        public sealed class InstanceData
        {
            public string Items;
            public long Opened;
        }

        public static InstanceData GetInstance(int chestId, string key)
        {
            lock (Sync)
            {
                using (QueryResult r = db.QueryReader(
                    "SELECT Items, Opened FROM HorizonChestsPlayer WHERE ChestID = @0 AND PlayerKey = @1;", chestId, key))
                {
                    return r.Read()
                        ? new InstanceData { Items = r.Get<string>("Items"), Opened = r.Get<long>("Opened") }
                        : null;
                }
            }
        }

        public static void SaveInstance(int chestId, string key, string items, long opened)
        {
            lock (Sync)
            {
                db.Query("DELETE FROM HorizonChestsPlayer WHERE ChestID = @0 AND PlayerKey = @1;", chestId, key);
                db.Query("INSERT INTO HorizonChestsPlayer (ChestID, PlayerKey, Items, Opened) VALUES (@0, @1, @2, @3);",
                    chestId, key, items, opened);
            }
        }

        public static void DeleteInstance(int chestId, string key)
        {
            Exec("DELETE FROM HorizonChestsPlayer WHERE ChestID = @0 AND PlayerKey = @1;", chestId, key);
        }

        // Pone el tiempo de reinicio de las copias personales en TODOS los cofres del mundo.
        public static int SetWorldChestRefill(int secs)
        {
            lock (Sync)
            {
                return db.Query("UPDATE HorizonChests SET Refill = @0 WHERE WorldID = @1 AND PlayerPlaced = 0;", secs, Main.worldID);
            }
        }

        private static InfChest Build(QueryResult r)
        {
            var chest = new InfChest(r.Get<int>("UserID"), r.Get<int>("X"), r.Get<int>("Y"), Main.worldID)
            {
                id = r.Get<int>("ID"),
                isPublic = r.Get<int>("Public") == 1,
                refill = r.Get<int>("Refill")
            };
            chest.playerPlaced = r.Get<int>("PlayerPlaced") == 1;
            chest.StringToGroups(r.Get<string>("Groups"));
            chest.StringToUsers(r.Get<string>("Users"));
            chest.StringToItems(r.Get<string>("Items"));
            return chest;
        }

        public static InfChest GetChest(int x, int y)
        {
            lock (Sync)
            {
                using (QueryResult r = db.QueryReader(
                    "SELECT * FROM HorizonChests WHERE X = @0 AND Y = @1 AND WorldID = @2;", x, y, Main.worldID))
                {
                    return r.Read() ? Build(r) : null;
                }
            }
        }

        public static InfChest GetChest(int id)
        {
            lock (Sync)
            {
                using (QueryResult r = db.QueryReader(
                    "SELECT * FROM HorizonChests WHERE ID = @0 AND WorldID = @1;", id, Main.worldID))
                {
                    return r.Read() ? Build(r) : null;
                }
            }
        }

        public static List<Terraria.Chest> GetAllChests()
        {
            var list = new List<Terraria.Chest>();
            lock (Sync)
            {
                using (QueryResult r = db.QueryReader("SELECT * FROM HorizonChests WHERE WorldID = @0;", Main.worldID))
                {
                    while (r.Read())
                    {
                        var inf = new InfChest(-1, r.Get<int>("X"), r.Get<int>("Y"), Main.worldID);
                        inf.StringToItems(r.Get<string>("Items"));
                        // El índice es la ranura que ocupará en Main.chest (se usa en AddAllChest).
                        list.Add(ChestCompat.Create(inf.items, inf.x, inf.y, list.Count));
                    }
                }
            }
            return list;
        }
        public static List<InfChest> GetChestsInArea(int x1, int y1, int x2, int y2)
        {
            var list = new List<InfChest>();
            lock (Sync)
            {
                using (QueryResult r = db.QueryReader(
                    "SELECT * FROM HorizonChests WHERE WorldID = @0 AND X BETWEEN @1 AND @2 AND Y BETWEEN @3 AND @4 ORDER BY ID;",
                    Main.worldID, x1, x2, y1, y2))
                {
                    while (r.Read())
                    {
                        try { list.Add(Build(r)); }
                        catch (Exception ex) { TShock.Log.ConsoleError("[HorizonChests] Fila ilegible: " + ex.Message); }
                    }
                }
            }
            return list;
        }
        public static List<InfChest> GetAllRows()
        {
            var list = new List<InfChest>();
            lock (Sync)
            {
                using (QueryResult r = db.QueryReader("SELECT * FROM HorizonChests WHERE WorldID = @0 ORDER BY ID;", Main.worldID))
                {
                    while (r.Read())
                    {
                        try { list.Add(Build(r)); }
                        catch (Exception ex) { TShock.Log.ConsoleError("[HorizonChests] Fila ilegible ID=" + r.Get<int>("ID") + ": " + ex.Message); }
                    }
                }
            }
            return list;
        }

        public static void AddChest(InfChest chest)
        {
            lock (Sync)
            {
                // Evita duplicados: si quedó una fila vieja (huérfana) en esta posición, se elimina antes,
                // para que GetChest(x, y) nunca devuelva datos/dueño de un cofre anterior.
                db.Query("DELETE FROM HorizonChestsPlayer WHERE ChestID IN (SELECT ID FROM HorizonChests WHERE X = @0 AND Y = @1 AND WorldID = @2);", chest.x, chest.y, Main.worldID);
                db.Query("DELETE FROM HorizonChests WHERE X = @0 AND Y = @1 AND WorldID = @2;", chest.x, chest.y, Main.worldID);
                db.Query(
                    "INSERT INTO HorizonChests (UserID, X, Y, Items, Public, Users, `Groups`, Refill, WorldID, PlayerPlaced) " +
                    "VALUES (@0, @1, @2, @3, @4, @5, @6, @7, @8, @9);",
                    chest.userid, chest.x, chest.y, chest.ItemsToString(), chest.isPublic ? 1 : 0,
                    chest.UsersToString(), chest.GroupsToString(), chest.refill, Main.worldID, chest.playerPlaced ? 1 : 0);
            }
        }

        public static void UpdateUser(InfChest c)   { Exec("UPDATE HorizonChests SET UserID = @0 WHERE ID = @1;", c.userid, c.id); }
        public static void UpdateItems(InfChest c)  { Exec("UPDATE HorizonChests SET Items = @0 WHERE ID = @1;", c.ItemsToString(), c.id); }
        public static void UpdatePublic(InfChest c) { Exec("UPDATE HorizonChests SET Public = @0 WHERE ID = @1;", c.isPublic ? 1 : 0, c.id); }
        public static void UpdateUsers(InfChest c)  { Exec("UPDATE HorizonChests SET Users = @0 WHERE ID = @1;", c.UsersToString(), c.id); }
        // "Groups" es palabra reservada en MySQL 8: se entrecomilla con backticks (SQLite también lo admite).
        public static void UpdateGroups(InfChest c) { Exec("UPDATE HorizonChests SET `Groups` = @0 WHERE ID = @1;", c.GroupsToString(), c.id); }
        public static void UpdateRefill(InfChest c) { Exec("UPDATE HorizonChests SET Refill = @0 WHERE ID = @1;", c.refill, c.id); }
        public static void UpdatePlayerPlaced(InfChest c) { Exec("UPDATE HorizonChests SET PlayerPlaced = @0 WHERE ID = @1;", c.playerPlaced ? 1 : 0, c.id); }
        public static void DeleteChest(int id)      { Exec("DELETE FROM HorizonChestsPlayer WHERE ChestID = @0;", id); Exec("DELETE FROM HorizonChests WHERE ID = @0;", id); }
        public static void DeleteAllChests()        { Exec("DELETE FROM HorizonChestsPlayer WHERE ChestID IN (SELECT ID FROM HorizonChests WHERE WorldID = @0);", Main.worldID); Exec("DELETE FROM HorizonChests WHERE WorldID = @0;", Main.worldID); }

        private static void Exec(string query, params object[] args)
        {
            lock (Sync)
            {
                db.Query(query, args);
            }
        }

        public static int SearchChests(int itemID)
        {
            lock (Sync)
            {
                int n = 0;
                using (QueryResult r = db.QueryReader(
                    "SELECT ID FROM HorizonChests WHERE Items LIKE @0 AND WorldID = @1;", "%~" + itemID + ",%", Main.worldID))
                {
                    while (r.Read()) n++;
                }
                return n;
            }
        }

        public static List<(int X, int Y)> DeleteEmptyChests()
        {
            string empty = "~" + string.Concat(Enumerable.Repeat("0,0,0~", InfChest.Slots));
            var list = new List<(int X, int Y)>();
            lock (Sync)
            {
                using (QueryResult r = db.QueryReader(
                    "SELECT X, Y FROM HorizonChests WHERE Items = @0 AND WorldID = @1 AND Refill = -1 AND PlayerPlaced = 1;", empty, Main.worldID))
                {
                    while (r.Read())
                        list.Add((r.Get<int>("X"), r.Get<int>("Y")));
                }
                db.Query("DELETE FROM HorizonChests WHERE Items = @0 AND WorldID = @1 AND Refill = -1 AND PlayerPlaced = 1;", empty, Main.worldID);
            }
            return list;
        }

        public static int GetCount()
        {
            lock (Sync)
            {
                int n = 0;
                using (QueryResult r = db.QueryReader("SELECT ID FROM HorizonChests WHERE WorldID = @0;", Main.worldID))
                {
                    while (r.Read()) n++;
                }
                return n;
            }
        }

        // ---------------------------------------------------------------- Migración desde V1 / V2

        public static int TransferV1() { return Transfer(false); }
        public static int TransferV2() { return Transfer(true); }

        // ---------------------------------------------------------------- Migración desde InfiniteChestsV3
        // Copia los cofres del MUNDO ACTUAL desde las tablas de InfiniteChestsV3 (InfChests3 / InfChestsPlayer)
        // a las de HorizonChests (HorizonChests / HorizonChestsPlayer). Se puede repetir sin duplicar:
        // un cofre en la misma posición se sustituye. Los datos originales no se tocan.
        // Con SQLite lee el archivo InfChests3.sqlite de la carpeta tshock; con MySQL lee las tablas
        // InfChests3 de la misma base de datos.
        public static int TransferV3()
        {
            InfMain.lockChests = true;
            SqliteConnection own = null;
            try
            {
                IDbConnection src = db;
                if (TShock.Config.Settings.StorageType.Equals("sqlite", StringComparison.OrdinalIgnoreCase))
                {
                    string oldFile = Path.Combine(TShock.SavePath, "InfChests3.sqlite");
                    if (!File.Exists(oldFile))
                        throw new FileNotFoundException("No se encontró la base de datos de InfiniteChestsV3 (InfChests3.sqlite) en " + TShock.SavePath);
                    own = new SqliteConnection("Data Source=" + oldFile);
                    src = own;
                }

                // Las bases de datos de V3 puras no tienen la columna PlayerPlaced.
                bool hasPlayerPlaced = true;
                try { using (src.QueryReader("SELECT PlayerPlaced FROM InfChests3 LIMIT 1;")) { } }
                catch (Exception) { hasPlayerPlaced = false; }

                var chests = new List<InfChest>();
                var oldIds = new List<int>();
                using (QueryResult r = src.QueryReader("SELECT * FROM InfChests3 WHERE WorldID = @0 ORDER BY ID;", Main.worldID))
                {
                    while (r.Read())
                    {
                        try
                        {
                            int userId = r.Get<int>("UserID");
                            var chest = new InfChest(userId, r.Get<int>("X"), r.Get<int>("Y"), Main.worldID)
                            {
                                isPublic = r.Get<int>("Public") == 1,
                                refill = r.Get<int>("Refill")
                            };
                            // V3 puro no distingue cofres del mundo y de jugador: un cofre con dueño se considera de jugador.
                            chest.playerPlaced = hasPlayerPlaced ? r.Get<int>("PlayerPlaced") == 1 : userId != -1;
                            chest.StringToGroups(r.Get<string>("Groups"));
                            chest.StringToUsers(r.Get<string>("Users"));
                            chest.StringToItems(r.Get<string>("Items"));
                            chests.Add(chest);
                            oldIds.Add(r.Get<int>("ID"));
                        }
                        catch (Exception ex)
                        {
                            TShock.Log.ConsoleError("[HorizonChests] Cofre de V3 ilegible, se omite: " + ex.Message);
                        }
                    }
                }

                // Copias personales (solo existen si esa base de datos ya la usó una versión de HorizonChests).
                var copies = new List<InstanceCopy>();
                try
                {
                    using (QueryResult r = src.QueryReader("SELECT ChestID, PlayerKey, Items, Opened FROM InfChestsPlayer;"))
                    {
                        while (r.Read())
                            copies.Add(new InstanceCopy
                            {
                                ChestId = r.Get<int>("ChestID"),
                                Key = r.Get<string>("PlayerKey"),
                                Items = r.Get<string>("Items"),
                                Opened = r.Get<long>("Opened")
                            });
                    }
                }
                catch (Exception) { /* InfiniteChestsV3 original: la tabla no existe. */ }

                for (int i = 0; i < chests.Count; i++)
                {
                    AddChest(chests[i]);
                    InfChest added = GetChest(chests[i].x, chests[i].y);
                    if (added == null)
                        continue;
                    foreach (InstanceCopy c in copies)
                        if (c.ChestId == oldIds[i])
                            SaveInstance(added.id, c.Key, c.Items, c.Opened);
                }
                return chests.Count;
            }
            finally
            {
                own?.Dispose();
                InfMain.lockChests = false;
            }
        }

        private sealed class InstanceCopy
        {
            public int ChestId;
            public string Key;
            public string Items;
            public long Opened;
        }

        private static int Transfer(bool v2)
        {
            InfMain.lockChests = true;
            SqliteConnection own = null;
            try
            {
                IDbConnection src = db;
                if (TShock.Config.Settings.StorageType.Equals("sqlite", StringComparison.OrdinalIgnoreCase))
                {
                    // Microsoft.Data.Sqlite NO acepta el formato antiguo "uri=file://...,Version=3".
                    own = new SqliteConnection("Data Source=" + Path.Combine(TShock.SavePath, "chests.sqlite"));
                    src = own;
                }

                List<InfChest> list = v2 ? ReadV2(src) : ReadV1(src);
                foreach (InfChest chest in list)
                    AddChest(chest);
                return list.Count;
            }
            finally
            {
                own?.Dispose();
                InfMain.lockChests = false;
            }
        }

        private static List<InfChest> ReadV1(IDbConnection src)
        {
            var list = new List<InfChest>();
            using (QueryResult r = src.QueryReader("SELECT * FROM Chests WHERE WorldID = @0;", Main.worldID))
            {
                while (r.Read())
                {
                    var account = TShock.UserAccounts.GetUserAccountByName(r.Get<string>("Account"));
                    var chest = new InfChest(account == null ? -1 : account.ID, r.Get<int>("X"), r.Get<int>("Y"), Main.worldID);

                    int refill = r.Get<int>("RefillTime");
                    if (refill > -1)
                        chest.refill = refill;

                    string[] p = r.Get<string>("Items").Split(',');
                    for (int i = 0; i < InfChest.Slots && 3 * i + 2 < p.Length; i++)
                    {
                        var item = new Item();
                        item.SetDefaults(int.Parse(p[3 * i]));
                        item.stack = int.Parse(p[3 * i + 1]);
                        item.prefix = byte.Parse(p[3 * i + 2]);
                        chest.items[i] = item;
                    }
                    list.Add(chest);
                }
            }
            return list;
        }

        private static List<InfChest> ReadV2(IDbConnection src)
        {
            var list = new List<InfChest>();
            using (QueryResult r = src.QueryReader("SELECT * FROM InfChests WHERE WorldID = @0;", Main.worldID))
            {
                while (r.Read())
                {
                    string users = r.Get<string>("Users");
                    string groups = r.Get<string>("Groups");
                    var chest = new InfChest(r.Get<int>("UserID"), r.Get<int>("X"), r.Get<int>("Y"), Main.worldID)
                    {
                        id = r.Get<int>("ID"),
                        isPublic = r.Get<int>("Public") == 1,
                        refill = r.Get<int>("Refill")
                    };
                    chest.StringToUsers(users);
                    chest.StringToGroups(groups);
                    list.Add(chest);
                }
            }

            foreach (InfChest chest in list)
            {
                using (QueryResult r = src.QueryReader("SELECT * FROM ChestItems WHERE ChestID = @0;", chest.id))
                {
                    while (r.Read())
                    {
                        int slot = r.Get<int>("Slot");
                        if (slot < 0 || slot >= InfChest.Slots)
                            continue;
                        var item = new Item();
                        item.SetDefaults(r.Get<int>("Type"));
                        item.stack = r.Get<int>("Stack");
                        item.prefix = (byte)r.Get<int>("Prefix");
                        chest.items[slot] = item;
                    }
                }
            }
            return list;
        }
    }
}