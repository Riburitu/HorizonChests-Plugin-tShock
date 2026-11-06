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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using Terraria;
using Terraria.Localization;
using TShockAPI;

namespace HorizonChests
{
    public static class ChestAudit
    {
        public static bool Enabled = true;

        public static bool LogPersonalCopies = false;

        public const string CatalogLang = "es";

        private const int ValuableRarity = 8;

        private const int BurstThreshold = 8;
        private const int BurstWindowMs = 10_000;
        private const int BurstCooldownMs = 30_000;

        private const string FolderName = "HorizonChestsAudit";
        private const string CatalogFile = "catalog-1_4_5_8.json";

        private sealed class Entry
        {
            public DateTime Time;
            public string Text;
        }

        private sealed class BurstState
        {
            public readonly Queue<long> Takes = new Queue<long>();
            public long LastAlert = long.MinValue / 2;
        }

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static readonly object StateLock = new object();
        private static readonly Dictionary<int, string> OwnerNames = new Dictionary<int, string>();
        private static readonly Dictionary<int, BurstState> Bursts = new Dictionary<int, BurstState>();

        private static BlockingCollection<Entry> queue;
        private static Thread writer;
        private static string dir;
        private static Dictionary<int, string> catalogNames;
        private static bool writeErrorShown;


        public static void Init()
        {
            if (queue != null || !Enabled)
                return;
            try
            {
                dir = Path.Combine(TShock.SavePath, FolderName);
                Directory.CreateDirectory(dir);
                LoadCatalog();

                queue = new BlockingCollection<Entry>(new ConcurrentQueue<Entry>());
                writer = new Thread(WriterLoop) { IsBackground = true, Name = "HC-Audit" };
                writer.Start();

                Write("=== Auditoría de cofres iniciada ===");
                InfMain.Log($"Auditoría activa. Logs en: {dir}");
            }
            catch (Exception ex)
            {
                Enabled = false;
                TShock.Log.ConsoleError("[HorizonChests] No se pudo iniciar la auditoría: " + ex.Message);
            }
        }

        public static void Shutdown()
        {
            BlockingCollection<Entry> q = queue;
            if (q == null)
                return;
            try
            {
                Write("=== Auditoría de cofres detenida ===");
                q.CompleteAdding();
                writer?.Join(3000);
            }
            catch {  }
            queue = null;
            writer = null;
        }

        private static void WriterLoop()
        {
            BlockingCollection<Entry> q = queue;
            foreach (Entry e in q.GetConsumingEnumerable())
            {
                try
                {
                    string path = Path.Combine(dir, "audit-" + e.Time.ToString("yyyy-MM-dd") + ".txt");
                    File.AppendAllText(path, e.Text + Environment.NewLine, Utf8);
                }
                catch (Exception ex)
                {
                    if (!writeErrorShown)
                    {
                        writeErrorShown = true;
                        TShock.Log.ConsoleError("[HorizonChests] Error escribiendo la auditoría: " + ex.Message);
                    }
                }
            }
        }

        private static void Write(string text)
        {
            BlockingCollection<Entry> q = queue;
            if (q == null || q.IsAddingCompleted)
                return;
            try
            {
                var now = DateTime.Now;
                q.Add(new Entry { Time = now, Text = "[" + now.ToString("HH:mm:ss") + "] " + text });
            }
            catch (InvalidOperationException) {  }
        }


        public static void LogSlotChange(TSPlayer player, InfChest chest, int slot, Item before, Item after,
            bool personalCopy, string source = null)
        {
            if (!Enabled || queue == null || player == null || chest == null)
                return;
            if (personalCopy && !LogPersonalCopies)
                return;

            try
            {
                bool bEmpty = IsEmpty(before);
                bool aEmpty = IsEmpty(after);
                if (bEmpty && aEmpty)
                    return;

                Item taken = null, put = null;
                if (!bEmpty && aEmpty)
                    taken = before;
                else if (bEmpty && !aEmpty)
                    put = after;
                else if (before.type == after.type && before.prefix == after.prefix)
                {
                    int delta = after.stack - before.stack;
                    if (delta == 0)
                        return;
                    if (delta > 0) { put = Clone(after, delta); }
                    else { taken = Clone(before, -delta); }
                }
                else
                {
                    taken = before;
                    put = after;
                }

                string action;
                if (taken != null && put != null)
                    action = $"sacó {Fmt(taken)} y metió {Fmt(put)} en";
                else if (taken != null)
                    action = source == "crafteo" ? $"consumió {Fmt(taken)} por crafteo del" : $"sacó {Fmt(taken)} del";
                else
                    action = $"metió {Fmt(put)} en el";
                if (taken != null && put != null)
                    action += " el";

                int accountId = player.IsLoggedIn && player.Account != null ? player.Account.ID : -1;
                bool unowned = chest.userid == -1;
                bool foreign = !unowned && chest.userid != accountId;
                bool valuable = taken != null && taken.rare >= ValuableRarity;

                var sb = new StringBuilder();
                if (taken != null && foreign) sb.Append("[AJENO] ");
                else if (taken != null && unowned) sb.Append("[SIN DUEÑO] ");
                if (valuable) sb.Append("[VALIOSO] ");
                if (personalCopy) sb.Append("[COPIA PERSONAL] ");

                sb.Append($"El jugador '{player.Name}' {action} cofre ID {chest.id} en la posición ({chest.x}, {chest.y})");
                sb.Append($" | hueco {slot}");
                sb.Append($" | dueño: {OwnerName(chest.userid)}{(chest.isPublic ? " (público)" : "")}");
                sb.Append($" | cuenta: {(accountId == -1 ? "(sin login)" : player.Account.Name)} | IP: {player.IP}");

                string line = sb.ToString();
                Write(line);
                if (InfMain.Debug)
                    TShock.Log.ConsoleInfo("[HC][AUDIT] " + line);

                if (taken != null && (foreign || unowned))
                    CheckBurst(player, chest, taken);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError("[HorizonChests] Auditoría (LogSlotChange): " + ex.Message);
            }
        }

        public static void LogDenied(TSPlayer player, InfChest chest, int slot, int netId, int stack, string reason)
        {
            if (!Enabled || queue == null || player == null || chest == null)
                return;
            try
            {
                string line = $"[RECHAZADO] El jugador '{player.Name}' intentó modificar el hueco {slot} del cofre ID {chest.id} " +
                              $"en la posición ({chest.x}, {chest.y}) (tipo={netId}, cantidad={stack}): {reason}" +
                              $" | dueño: {OwnerName(chest.userid)} | IP: {player.IP}";
                Write(line);
                if (InfMain.Debug)
                    TShock.Log.ConsoleInfo("[HC][AUDIT] " + line);
            }
            catch { }
        }

        public static Item FromDescribed(string s)
        {
            var item = new Item();
            if (string.IsNullOrEmpty(s))
                return item;
            string[] p = s.Split(',');
            if (p.Length < 3 || !int.TryParse(p[0], out int type) || type <= 0)
                return item;
            item.SetDefaults(type);
            if (int.TryParse(p[1], out int stack)) item.stack = stack;
            if (byte.TryParse(p[2], out byte prefix)) item.prefix = prefix;
            return item;
        }


        private static void CheckBurst(TSPlayer player, InfChest chest, Item taken)
        {
            long now = Environment.TickCount64;
            int count;
            bool alert = false;

            lock (StateLock)
            {
                if (!Bursts.TryGetValue(player.Index, out BurstState st))
                    Bursts[player.Index] = st = new BurstState();

                st.Takes.Enqueue(now);
                while (st.Takes.Count > 0 && now - st.Takes.Peek() > BurstWindowMs)
                    st.Takes.Dequeue();

                count = st.Takes.Count;
                if (count >= BurstThreshold && now - st.LastAlert > BurstCooldownMs)
                {
                    st.LastAlert = now;
                    alert = true;
                }
            }

            if (!alert)
                return;

            string line = $"!!! SOSPECHOSO: '{player.Name}' (IP {player.IP}) hizo {count} retiradas de cofres ajenos/públicos " +
                          $"en {BurstWindowMs / 1000} s. Último: cofre ID {chest.id} en ({chest.x}, {chest.y})";
            Write(line);
            if (InfMain.Debug)
                TShock.Log.ConsoleInfo("[HC][AUDIT] " + line);
        }

        public static void ForgetPlayer(int index)
        {
            lock (StateLock)
                Bursts.Remove(index);
        }


        private static bool IsEmpty(Item it) => it == null || it.type <= 0 || it.stack <= 0;

        private static Item Clone(Item src, int stack)
        {
            Item c = src.Clone();
            c.stack = stack;
            return c;
        }

        private static string Fmt(Item it)
        {
            string name = ItemName(it.type);
            string prefix = null;
            try
            {
                if (it.prefix > 0 && it.prefix < Lang.prefix.Length)
                    prefix = Lang.prefix[it.prefix].Value;
            }
            catch { }
            return "[" + name + " x" + it.stack + (string.IsNullOrEmpty(prefix) ? "" : " (" + prefix + ")") + "]";
        }

        private static string ItemName(int type)
        {
            if (catalogNames != null && catalogNames.TryGetValue(type, out string n) && !string.IsNullOrEmpty(n))
                return n;
            try
            {
                string v = Lang.GetItemNameValue(type);
                if (!string.IsNullOrEmpty(v))
                    return v;
            }
            catch { }
            return "Item#" + type;
        }

        private static string OwnerName(int userId)
        {
            if (userId == -1)
                return "ninguno";
            lock (StateLock)
            {
                if (OwnerNames.TryGetValue(userId, out string cached))
                    return cached;
            }

            string name;
            try { name = TShock.UserAccounts.GetUserAccountByID(userId)?.Name ?? "(cuenta borrada #" + userId + ")"; }
            catch { name = "#" + userId; }

            lock (StateLock)
                OwnerNames[userId] = name;
            return name;
        }

        private static void LoadCatalog()
        {
            string[] candidates =
            {
                Path.Combine(TShock.SavePath, CatalogFile),
                Path.Combine(TShock.SavePath, FolderName, CatalogFile),
                Path.Combine(AppContext.BaseDirectory, CatalogFile),
                Path.Combine(AppContext.BaseDirectory, "ServerPlugins", CatalogFile),
            };

            foreach (string path in candidates)
            {
                if (!File.Exists(path))
                    continue;
                try
                {
                    using (FileStream fs = File.OpenRead(path))
                    using (JsonDocument doc = JsonDocument.Parse(fs))
                    {
                        var map = new Dictionary<int, string>();
                        if (doc.RootElement.TryGetProperty("items", out JsonElement items))
                        {
                            foreach (JsonElement it in items.EnumerateArray())
                            {
                                if (!it.TryGetProperty("id", out JsonElement idEl) || !it.TryGetProperty("name", out JsonElement nameEl))
                                    continue;
                                string nm = null;
                                if (nameEl.TryGetProperty(CatalogLang, out JsonElement l)) nm = l.GetString();
                                if (string.IsNullOrEmpty(nm) && nameEl.TryGetProperty("en", out JsonElement en)) nm = en.GetString();
                                if (!string.IsNullOrEmpty(nm))
                                    map[idEl.GetInt32()] = nm;
                            }
                        }
                        catalogNames = map;
                        InfMain.Log($"Catálogo de objetos cargado ({map.Count} nombres) desde {path}");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError("[HorizonChests] No se pudo leer el catálogo: " + ex.Message);
                }
            }
            InfMain.Log($"Catálogo {CatalogFile} no encontrado: los nombres de objeto saldrán en el idioma del servidor.");
        }
    }
}
