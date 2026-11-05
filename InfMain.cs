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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using TerrariaApi.Server;
using TShockAPI;

namespace HorizonChests
{
    [ApiVersion(2, 1)]
    public class InfMain : TerrariaPlugin
    {
        public const string PIString = "HorizonChests.PlayerInfo";
        private const int MaxTempChestId = 998;

        public static readonly List<RefillChestInfo> RCInfos = new List<RefillChestInfo>();
        public static int nextChestId = 2;
        public static volatile bool lockChests = false;
        public static bool usingInfChests = true;

        // Trazas [HC] en la consola. Déjalo en false en las versiones publicadas.
        // (static readonly en lugar de const para no generar avisos de código inalcanzable.)
        internal static readonly bool Debug = false;

        // Último paquete procesado (el hook se registra dos veces; ver Initialize).
        private static GetDataEventArgs lastProcessed;

        // Último aviso de "cofre fantasma" por jugador (índice -> unix segundos).
        private static readonly Dictionary<int, long> GhostWarnedAt = new Dictionary<int, long>();

        internal static void Log(string msg)
        {
            if (Debug)
                TShock.Log.ConsoleInfo("[HC] " + msg);
        }

        public override string Author => "Riburitu: based on Zaicon/MarioE's InfiniteChests";
        public override string Description => "HorizonChests: fork of InfiniteChests V3 ported to Terraria 1.4.5.X with improvements.";
        public override string Name => "HorizonChests";
        public override Version Version => Assembly.GetExecutingAssembly().GetName().Version;

        public InfMain(Main game) : base(game) { }

        public override void Initialize()
        {
            ServerApi.Hooks.GameInitialize.Register(this, OnGameInitialize);
            // TShock tiene sus propios manejadores para los paquetes 31/32/33/34 de cofres y, si corren antes
            // que este plugin, pueden descartar los paquetes (el cofre temporal no existe en Main.chest).
            // Se registra el mismo hook con la prioridad más alta y la más baja: así corre antes que TShock
            // sea cual sea el sentido en que se ordenen las prioridades. El segundo registro lo ignora
            // OnGetData mediante lastProcessed.
            ServerApi.Hooks.NetGetData.Register(this, OnGetDataFirstA, int.MaxValue);
            ServerApi.Hooks.NetGetData.Register(this, OnGetDataFirstB, int.MinValue);
            ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreet);
            ServerApi.Hooks.ServerLeave.Register(this, OnLeaveAudit);
            ServerApi.Hooks.GamePostInitialize.Register(this, OnWorldLoad);
            ServerApi.Hooks.GameUpdate.Register(this, OnUpdate);
        }

        private void OnLeaveAudit(LeaveEventArgs args) => ChestAudit.ForgetPlayer(args.Who);
        private void OnGetDataFirstA(GetDataEventArgs args) => OnGetData(args);
        private void OnGetDataFirstB(GetDataEventArgs args) => OnGetData(args);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.GameInitialize.Deregister(this, OnGameInitialize);
                ServerApi.Hooks.NetGetData.Deregister(this, OnGetDataFirstA);
                ServerApi.Hooks.NetGetData.Deregister(this, OnGetDataFirstB);
                ServerApi.Hooks.NetGreetPlayer.Deregister(this, OnGreet);
                ServerApi.Hooks.ServerLeave.Deregister(this, OnLeaveAudit);
                ServerApi.Hooks.GamePostInitialize.Deregister(this, OnWorldLoad);
                ServerApi.Hooks.GameUpdate.Deregister(this, OnUpdate);
                ChestAudit.Shutdown();
            }
            base.Dispose(disposing);
        }

        private void OnGameInitialize(EventArgs args)
        {
            DB.Connect();
            ChestAudit.Init();
            Commands.ChatCommands.Add(new Command("hc.use", ChestCMD, "chest") { AllowServer = false });
            Commands.ChatCommands.Add(new Command("hc.convert", ConvChests, "convchests"));
            Commands.ChatCommands.Add(new Command("hc.prune", PruneChests, "prunechests"));
            Commands.ChatCommands.Add(new Command("hc.transfer", Transfer, "transferchests"));
        }

        // RECONSTRUIDO: el decompilado no incluía el cuerpo (state machine async).
        private void OnWorldLoad(EventArgs args)
        {
            int real = Main.chest.Count(c => c != null);
            Log($"Mundo cargado. WorldID={Main.worldID}, cofres vanilla en el mundo={real}, cofres en la base de datos={DB.GetCount()}.");
            if (real == 0)
            {
                usingInfChests = true;
                Log("HorizonChests ACTIVO.");
                return;
            }

            usingInfChests = false;
            Log("HorizonChests DESACTIVADO: hay cofres vanilla en el mundo.");
            if (DB.GetCount() == 0)
                TShock.Log.ConsoleError($"[HorizonChests] Se encontraron {real} cofres normales en el mundo. Usa /convchests para pasarlos a la base de datos.");
            else
                TShock.Log.ConsoleError("[HorizonChests] Hay cofres normales Y cofres en la base de datos. Plugin desactivado; revisa el estado manualmente.");
        }

        private void OnGreet(GreetPlayerEventArgs args)
        {
            TSPlayer player = TShock.Players[args.Who];
            if (player != null)
                player.SetData(PIString, new PlayerInfo());
        }

        public static void AddAllChest()
        {
            List<Chest> all = DB.GetAllChests();
            for (int i = 0; i < Main.chest.Length; i++)
                Chest.RemoveChest(i);
            // Chest.Assign usa chest.index (fijado en GetAllChests) y registra las coordenadas.
            for (int j = 0; j < all.Count && j < Main.chest.Length; j++)
                Chest.Assign(all[j]);
        }

        // ------------------------------------------------------------------ helpers

        private static PlayerInfo GetInfo(TSPlayer p)
        {
            var info = p.GetData<PlayerInfo>(PIString);
            if (info == null)
            {
                info = new PlayerInfo();
                p.SetData(PIString, info);
            }
            return info;
        }

        private static int AccountId(TSPlayer p) => p.IsLoggedIn && p.Account != null ? p.Account.ID : -1;

        private static bool CanManage(TSPlayer p, InfChest c) =>
            p.HasPermission("hc.edit") || (c.userid != -1 && c.userid == AccountId(p));

        private static bool CanOpen(TSPlayer p, InfChest c)
        {
            if (c.userid == -1 || c.isPublic || p.HasPermission("hc.edit"))
                return true;
            if (c.groups.Contains(p.Group.Name))
                return true;
            int id = AccountId(p);
            return id != -1 && (c.userid == id || c.users.Contains(id));
        }

        private static bool CheckManage(TSPlayer p, InfChest c)
        {
            if (!CanManage(p, c)) { p.SendErrorMessage("This chest is not yours!"); return false; }
            if (c.userid == -1) { p.SendErrorMessage("This chest is not claimed!"); return false; }
            return true;
        }

        // Clave del jugador para sus copias personales de cofres.
        private static string PlayerKey(TSPlayer p) =>
            p.IsLoggedIn && p.Account != null ? "a:" + p.Account.ID : "u:" + p.UUID;

        private static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Cada jugador ve su propia copia: en los cofres del mundo siempre, y en los de jugador si tienen refill.
        private static bool IsInstanced(InfChest c) => !c.playerPlaced || c.isRefill;

        // 85 = el cliente pide guardar rápido un hueco del inventario en los cofres cercanos.
        private const PacketTypes QuickStackPacket = (PacketTypes)85;
        private static int GetNextChestId()
        {
            nextChestId++;
            if (nextChestId >= MaxTempChestId)
                nextChestId = 2;
            return nextChestId;
        }
        // Ranura fija de Main.chest en el cliente para este cofre. Se evitan 0 y 1 (como hacía el id rotativo).
        // Solo si el id no cabe en el array se usa el id rotativo de siempre.
        private static int ClientSlot(InfChest c)
        {
            int slot = c.id + 2;
            return slot < Main.chest.Length ? slot : GetNextChestId();
        }
        
        // ------------------------------------------------------------------ 1.4.5: cofres cercanos en el cliente
        //
        // Desde 1.4.5 el cliente craftea con los objetos de los cofres cercanos SIN abrirlos: lee su propio
        // Main.chest. Como aquí el Main.chest del servidor está vacío (todo vive en la base de datos), el servidor
        // tiene que enviar al cliente el contenido de los cofres de alrededor y mantenerlo actualizado.

        // Chest.chestStackRange en 1.4.5 = 600 px (37,5 casillas). Es también el radio del crafteo desde cofres.
        private const float StackRange = 600f;
        // El almacenamiento portátil cuenta en un cuadrado de 79x79 casillas: se sincroniza ±40.
        private const int SyncRangeTiles = 40;
        // Máximo de cofres nuevos enviados por jugador y ciclo (cada ~1 s) para no saturar la conexión.
        // Bajado de 10 a 4 para repartir las ráfagas de paquetes (~42 por cofre) y descartar saturación como causa de NPCs fantasma.
        private const int ChestsPerCycle = 4;
        // Main.chest[slot] se rellena y se vacía de forma temporal; se serializa para que no choquen hilos.
        private static readonly object ClientSyncLock = new object();
        private static int updateTick;

        // Envía al cliente el contenido de "items" en la ranura indicada, SIN abrir la ventana del cofre.
        private static void SyncChestToClient(int to, InfChest chest, Item[] items, int slot)
        {
            lock (ClientSyncLock)
            {
                Chest prevChest = Main.chest[slot];
                Main.chest[slot] = ChestCompat.Create(items, chest.x, chest.y, slot);
                try
                {
                    NetMessage.SendData(69, to, -1, NetworkText.Empty, slot, chest.x, chest.y);
                    NetMessage.SendData((int)PacketTypes.SyncChestSize, to, -1, NetworkText.Empty, slot, items.Length);
                    for (int i = 0; i < items.Length; i++)
                        NetMessage.SendData((int)PacketTypes.ChestItem, to, -1, NetworkText.Empty, slot, i);
                }
                finally
                {
                    Main.chest[slot] = prevChest;
                }
            }
        }

        // Contenido que ve ESTE jugador de un cofre instanciado (su copia, o el contenido maestro si no tiene).
        private static Item[] GetPlayerItems(TSPlayer p, InfChest chest, out DB.InstanceData copy)
        {
            string key = PlayerKey(p);
            copy = DB.GetInstance(chest.id, key);
            if (copy != null && chest.isRefill && NowUnix() - copy.Opened >= chest.refill)
            {
                DB.DeleteInstance(chest.id, key);
                copy = null;
            }
            return InfChest.ParseItems(copy != null ? copy.Items : chest.ItemsToString());
        }

        private static void ResyncChest(TSPlayer p, InfChest c)
        {
            Item[] items = IsInstanced(c) ? GetPlayerItems(p, c, out _) : c.items;
            SyncChestToClient(p.Index, c, items, ClientSlot(c));
        }

        private static bool InChestRange(TSPlayer p, InfChest c)
        {
            var center = new Microsoft.Xna.Framework.Vector2(c.x * 16 + 16, c.y * 16 + 16);
            return (center - p.TPlayer.Center).Length() < StackRange + 64f;
        }

        // Un cofre COMPARTIDO ha cambiado: se reenvía a todos los jugadores que ya tienen copia suya en el cliente
        // (si no, esos clientes siguen viendo objetos que ya no existen => "objetos fantasma" al craftear).
        private static void NotifyChestChanged(InfChest chest, int exceptIndex)
        {
            if (IsInstanced(chest))
                return;

            foreach (TSPlayer p in TShock.Players)
            {
                if (p == null || !p.Active || p.Index == exceptIndex)
                    continue;
                if (!GetInfo(p).KnownChests.Contains(chest.id))
                    continue;
                SyncChestToClient(p.Index, chest, chest.items, ClientSlot(chest));
            }
        }

        private void OnUpdate(EventArgs args)
        {
            FinishCrafts();
            if (!usingInfChests || lockChests)
                return;
            if (++updateTick % 60 != 0)
                return;

            foreach (TSPlayer p in TShock.Players)
            {
                if (p == null || !p.Active || p.TPlayer == null)
                    continue;
                if (Netplay.Clients[p.Index].State != 10)   // 10 = ya dentro del mundo
                    continue;
                try { PushNearbyChests(p); }
                catch (Exception ex) { TShock.Log.ConsoleError("[HorizonChests] " + ex); }
            }
        }

        // Envía al cliente los cofres cercanos que aún no conoce, los más próximos primero.
        private static void PushNearbyChests(TSPlayer p)
        {
            PlayerInfo info = GetInfo(p);
            var pos = p.TPlayer.position;   // el crafteo mide desde la esquina superior izquierda del jugador
            int tx = (int)(pos.X / 16f), ty = (int)(pos.Y / 16f);
            long now = NowUnix();

            bool moved = info.PushX == int.MinValue || Math.Abs(tx - info.PushX) >= 6 || Math.Abs(ty - info.PushY) >= 6;
            if (moved || now >= info.NextScan)
            {
                info.PushX = tx;
                info.PushY = ty;
                info.NextScan = now + 5;

                var area = DB.GetChestsInArea(tx - SyncRangeTiles, ty - SyncRangeTiles, tx + SyncRangeTiles, ty + SyncRangeTiles);
                info.PendingChests.Clear();
                foreach (InfChest c in area.OrderBy(k => (k.x - tx) * (k.x - tx) + (k.y - ty) * (k.y - ty)))
                {
                    if (info.KnownChests.Contains(c.id) || !CanOpen(p, c) || Chest.IsLocked(c.x, c.y))
                        continue;
                    info.PendingChests.Add(c.id);
                }
            }

            int sent = 0;
            while (info.PendingChests.Count > 0 && sent < ChestsPerCycle)
            {
                int id = info.PendingChests[0];
                info.PendingChests.RemoveAt(0);
                if (info.KnownChests.Contains(id))
                    continue;

                InfChest c = DB.GetChest(id);
                if (c == null || !CanOpen(p, c) || Chest.IsLocked(c.x, c.y))
                    continue;

                int slot = ClientSlot(c);
                SyncChestToClient(p.Index, c, IsInstanced(c) ? GetPlayerItems(p, c, out _) : c.items, slot);
                info.KnownChests.Add(c.id);
                sent++;
            }

            if (sent > 0)
                Log($"Cofres cercanos enviados a {p.Name}: {sent} (pendientes: {info.PendingChests.Count}, conocidos: {info.KnownChests.Count}).");
        }


private static void ClosePlayerChest(TSPlayer p)
        {
            NetMessage.SendData((int)PacketTypes.SyncPlayerChestIndex, -1, p.Index, NetworkText.Empty, p.Index, -1f, 0f, 0f, 0, 0, 0);
        }

        // ------------------------------------------------------------------ paquetes

        private void OnGetData(GetDataEventArgs args)
        {
            PacketTypes type = args.MsgID;
            if (type == PacketTypes.Tile)
            {
                GuardWorldChestTile(args);
                return;
            }
            if (type == PacketTypes.LoadNetModule)
            {
                HandleCraftModule(args);   // nunca marca el paquete como manejado: lo procesa el servidor vanilla
                return;
            }
            if (type != PacketTypes.ChestGetContents && type != PacketTypes.ChestItem &&
                type != PacketTypes.ChestOpen && type != PacketTypes.PlaceChest && type != QuickStackPacket)
                return;

            // El hook está registrado dos veces: el segundo aviso del mismo paquete se ignora.
            if (ReferenceEquals(args, lastProcessed))
                return;

            Log($"Paquete {(int)type} ({type}) de jugador {args.Msg.whoAmI}: Handled antes={args.Handled}, usingInfChests={usingInfChests}");

            if (args.Handled || !usingInfChests)
                return;

            lastProcessed = args;

            TSPlayer player = TShock.Players[args.Msg.whoAmI];
            if (player == null)
                return;

            try
            {
                using (var reader = new BinaryReader(new MemoryStream(args.Msg.readBuffer, args.Index, args.Length)))
                {
                    switch (type)
                    {
                        case PacketTypes.ChestGetContents: HandleGetContents(args, reader, player); break;
                        case PacketTypes.ChestItem: HandleChestItem(args, reader, player); break;
                        case PacketTypes.ChestOpen: HandleChestOpen(args, reader, player); break;
                        case PacketTypes.PlaceChest: HandlePlaceChest(args, reader, player); break;
                        case QuickStackPacket: HandleQuickStack(args, reader, player); break;
                    }
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError("[HorizonChests] " + ex);
            }
        }

        // 82 = LoadNetModule. En 1.4.5 el módulo 12 es NetCraftingRequestsModule (petición de crafteo).
        //
        // Formato observado: tras el id del módulo (UInt16 = 12) vienen enteros de 7 bits (como
        // BinaryWriter.Write7BitEncodedInt; -2 se escribe FE-FF-FF-FF-0F). Ejemplo real:
        //     1, 829, 0, 0, 10, 5, -2, 604, 605, 606, 607, 0
        // Los cuatro últimos (604..607) son las ranuras de cliente (id de la BD + 2) de los 4 cofres cercanos.
        // El significado exacto del resto no está confirmado.
        //
        // El servidor vanilla resuelve el crafteo leyendo Main.chest, que aquí está vacío (todo vive en la BD): por eso
        // rechazaba la petición. Solución: justo antes de que el servidor vanilla procese el paquete se cargan en
        // Main.chest los cofres que indica el cliente; unos milisegundos después (FinishCrafts, en GameUpdate) se
        // guardan en la BD los huecos que hayan cambiado y Main.chest se vuelve a vaciar.
        private static GetDataEventArgs lastModuleLogged;

        private sealed class CraftChest
        {
            public int Slot;
            public int ChestId;
            public bool Instanced;
            public Item[] Items;
            public string[] Before;
        }

        private sealed class PendingCraft
        {
            public TSPlayer Player;
            public long At;
            public List<CraftChest> Chests = new List<CraftChest>();
        }

        private static readonly List<PendingCraft> pendingCrafts = new List<PendingCraft>();

        private static string Describe(Item it) => it == null ? "0,0,0" : it.type + "," + it.stack + "," + it.prefix;

        private static List<int> ReadVarInts(byte[] buf, int start, int end)
        {
            var list = new List<int>();
            int pos = start;
            while (pos < end && list.Count < 256)
            {
                int result = 0, shift = 0;
                bool ok = false;
                while (pos < end && shift < 35)
                {
                    byte b = buf[pos++];
                    result |= (b & 0x7F) << shift;
                    shift += 7;
                    if ((b & 0x80) == 0) { ok = true; break; }
                }
                if (!ok)
                    break;
                list.Add(result);
            }
            return list;
        }

        private static void HandleCraftModule(GetDataEventArgs args)
        {
            if (args.Length < 2 || ReferenceEquals(args, lastModuleLogged))
                return;
            ushort moduleId = BitConverter.ToUInt16(args.Msg.readBuffer, args.Index);
            if (moduleId != 12)
                return;
            lastModuleLogged = args;

            TSPlayer player = TShock.Players[args.Msg.whoAmI];
            byte[] raw = new byte[args.Length];
            Array.Copy(args.Msg.readBuffer, args.Index, raw, 0, args.Length);
            List<int> ints = ReadVarInts(raw, 2, raw.Length);
            Log($"Crafteo (módulo 12) de {player?.Name}: enteros=[{string.Join(", ", ints)}]");

            foreach (int v in ints.Take(2))
            {
                if (v > 0 && v < Main.recipe.Length && Main.recipe[v] != null && Main.recipe[v].createItem.type > 0)
                    Log($"   Main.recipe[{v}] crea {Main.recipe[v].createItem.Name} x{Main.recipe[v].createItem.stack}");
            }

            if (player == null || !usingInfChests || lockChests)
                return;

            PlayerInfo info = GetInfo(player);
            var craft = new PendingCraft { Player = player, At = Environment.TickCount64 };

            lock (ClientSyncLock)
            {
                foreach (int v in ints.Distinct())
                {
                    int id = v - 2;
                    if (id < 0 || v >= Main.chest.Length || !info.KnownChests.Contains(id))
                        continue;

                    // ¿Ya cargado por una petición anterior que aún no se ha cerrado? Solo se retrasa su cierre.
                    if (Main.chest[v] != null)
                    {
                        lock (pendingCrafts)
                        {
                            foreach (PendingCraft pc in pendingCrafts)
                                if (pc.Player == player && pc.Chests.Any(x => x.Slot == v))
                                    pc.At = Environment.TickCount64;
                        }
                        continue;
                    }

                    InfChest c = DB.GetChest(id);
                    if (c == null || !CanOpen(player, c) || Chest.IsLocked(c.x, c.y) || !InChestRange(player, c))
                        continue;

                    bool instanced = IsInstanced(c);
                    Item[] items = instanced ? GetPlayerItems(player, c, out _) : c.items;

                    Main.chest[v] = ChestCompat.Create(items, c.x, c.y, v);
                    craft.Chests.Add(new CraftChest
                    {
                        Slot = v,
                        ChestId = c.id,
                        Instanced = instanced,
                        Items = items,
                        Before = items.Select(Describe).ToArray()
                    });
                }
            }

            if (craft.Chests.Count > 0)
            {
                lock (pendingCrafts)
                    pendingCrafts.Add(craft);
                Log($"   Cofres cargados en el servidor para este crafteo: {string.Join(", ", craft.Chests.Select(x => x.ChestId))}.");
            }
        }

        // Cierra los crafteos pendientes (se llama en cada tick de GameUpdate).
        private static void FinishCrafts()
        {
            List<PendingCraft> due;
            lock (pendingCrafts)
            {
                if (pendingCrafts.Count == 0)
                    return;
                long now = Environment.TickCount64;
                due = pendingCrafts.Where(pc => now - pc.At >= 40).ToList();
                foreach (PendingCraft pc in due)
                    pendingCrafts.Remove(pc);
            }

            foreach (PendingCraft craft in due)
            {
                foreach (CraftChest cc in craft.Chests)
                {
                    try { FinishCraftChest(craft.Player, cc); }
                    catch (Exception ex) { TShock.Log.ConsoleError("[HorizonChests] " + ex); }
                }
            }
        }

        private static void FinishCraftChest(TSPlayer player, CraftChest cc)
        {
            Item[] after;
            lock (ClientSyncLock)
            {
                after = Main.chest[cc.Slot]?.item ?? cc.Items;
                Main.chest[cc.Slot] = null;
            }

            var changed = new List<int>();
            for (int i = 0; i < InfChest.Slots && i < after.Length; i++)
            {
                if (Describe(after[i]) != cc.Before[i])
                    changed.Add(i);
            }

            if (changed.Count == 0)
            {
                Log($"   Crafteo: el cofre {cc.ChestId} no cambió.");
                return;
            }
            Log($"   Crafteo: el cofre {cc.ChestId} cambió en {changed.Count} hueco(s): {string.Join(", ", changed)}.");

            InfChest chest = DB.GetChest(cc.ChestId);
            if (chest == null)
                return;

            if (cc.Instanced)
            {
                Item[] items = GetPlayerItems(player, chest, out DB.InstanceData copy);
                foreach (int i in changed)
                    items[i] = after[i] == null ? new Item() : after[i].Clone();
                long opened = copy != null ? copy.Opened : NowUnix();
                DB.SaveInstance(chest.id, PlayerKey(player), InfChest.SerializeItems(items), opened);

                PlayerInfo pi = GetInfo(player);
                if (pi.ChestIdInUse == chest.id && pi.InstanceItems != null)
                {
                    foreach (int i in changed)
                        pi.InstanceItems[i] = items[i].Clone();
                    if (pi.InstanceOpened == 0)
                        pi.InstanceOpened = opened;
                }
                if (player.Active)
                    SyncChestToClient(player.Index, chest, items, ClientSlot(chest));
            }
            else
            {
                foreach (int i in changed)
                {
                    ChestAudit.LogSlotChange(player, chest, i, ChestAudit.FromDescribed(cc.Before[i]), after[i], false, "crafteo");
                    chest.items[i] = after[i] == null ? new Item() : after[i].Clone();
                }
                DB.UpdateItems(chest);
                NotifyChestChanged(chest, -1);
            }
        }

private static void WarnLocked(TSPlayer p) =>
            p.SendWarningMessage("Chests are currently being converted. Please wait for a few moments.");

        // 31: el cliente quiere abrir un cofre
        private void HandleGetContents(GetDataEventArgs args, BinaryReader r, TSPlayer player)
        {
            args.Handled = true;
            if (lockChests) { WarnLocked(player); return; }

            short x = r.ReadInt16();
            short y = r.ReadInt16();

            InfChest chest = DB.GetChest(x, y);
            Log($"Jugador {player.Name} pide abrir cofre en ({x},{y}): {(chest == null ? "NO existe en la base de datos" : "id=" + chest.id)}");
            if (chest == null)
            {
                // Hay tile de cofre pero no fila en la base de datos. ANTES se destruía el tile (el cofre "desaparecía").
                // Ahora se registra un cofre vacío y público en esa posición (autorreparación).
                if (!TryGetChestOrigin(x, y, out int ox, out int oy))
                {
                    // Cofre fantasma: el cliente lo dibuja pero en el servidor ya no hay tile. Se reenvía la zona.
                    var gt = Main.tile[x, y];
                    Log($"Cofre fantasma en ({x},{y}) para {player.Name}: servidor activo={gt.active()} tipo={gt.type} frameX={gt.frameX} frameY={gt.frameY}. " + DescribePlaceArea(x, y + 1));
                    int fragType = 0;
                    for (int dx = 0; dx < 3 && fragType == 0; dx++)
                        for (int dy = 0; dy < 2 && fragType == 0; dy++)
                            if (x + dx < Main.maxTilesX && y + dy < Main.maxTilesY &&
                                TryGetChestOrigin(x + dx, y + dy, out int fx, out int fy) && fx == x && fy == y)
                                fragType = Main.tile[x + dx, y + dy].type;
                    if (fragType != 0)
                    {
                        int n = ClearChestFootprint(x, y, fragType);
                        Log($"Restos de cofre sin registro en ({x},{y}): {n} casilla(s) eliminadas.");
                    }
                    player.SendTileSquareCentered(x, y, 7);
                    TSPlayer.All.SendTileSquareCentered(x + 1, y, 5);

                    // Aviso al jugador (máximo uno cada 5 segundos para no llenar el chat si sigue pulsando).
                    long now = NowUnix();
                    if (!GhostWarnedAt.TryGetValue(player.Index, out long last) || now - last >= 5)
                    {
                        GhostWarnedAt[player.Index] = now;
                        player.SendWarningMessage("Este cofre no existe en el servidor. Sal del servidor y entra nuevamente.");
                    }
                    return;
                }
                chest = DB.GetChest(ox, oy);
                if (chest == null)
                {
                    DB.AddChest(new InfChest(-1, ox, oy, Main.worldID) { playerPlaced = true });
                    chest = DB.GetChest(ox, oy);
                    TShock.Log.ConsoleError($"[HorizonChests] Cofre sin registro en ({ox},{oy}); se creó una fila vacía (reparado).");
                }
                if (chest == null)
                    return;
            }

            PlayerInfo info = GetInfo(player);
            if (info.Action == ChestAction.None)
                OpenChest(player, info, chest);
            else
                ApplyAction(player, info, chest);

            info.Action = ChestAction.None;
            info.ExtraInfo = "";
        }

        private static void OpenChest(TSPlayer player, PlayerInfo info, InfChest chest, bool master = false)
        {
            if (!CanOpen(player, chest))
            {
                player.SendErrorMessage("This chest is protected.");
                return;
            }

            info.ChestIdInUse = chest.id;
            info.InstanceItems = null;

            Item[] items;
            if (IsInstanced(chest) && !master)
            {
                // Copia personal: si el jugador aún no ha tocado el cofre (o su copia caducó) ve el contenido
                // maestro; en cuanto cambia algo, su copia se guarda en la base de datos.
                string key = PlayerKey(player);
                DB.InstanceData copy = DB.GetInstance(chest.id, key);
                if (copy != null && chest.isRefill && NowUnix() - copy.Opened >= chest.refill)
                {
                    DB.DeleteInstance(chest.id, key);
                    copy = null;
                }

                items = InfChest.ParseItems(copy != null ? copy.Items : chest.ItemsToString());
                info.InstanceItems = items;
                info.InstanceKey = key;
                info.InstanceOpened = copy != null ? copy.Opened : 0;
                Log($"Copia personal {key} del cofre {chest.id}: {(copy != null ? "existente" : "nueva (contenido maestro)")}.");
            }
            else
            {
                items = chest.items;
            }

            int tmp = ClientSlot(chest);
            int to = player.Index;
            
            SyncChestToClient(to, chest, items, tmp);   // 69 + 155 + 32 (copia del cliente siempre actualizada)
            info.KnownChests.Add(chest.id);
            
            Monitor.Enter(ClientSyncLock);
            Chest prevChest = Main.chest[tmp];
            try
            {
                Main.chest[tmp] = ChestCompat.Create(items, chest.x, chest.y, tmp);
                if (Debug)
                {
                    int filled = items.Count(it => it != null && it.type != 0);
                    Log($"Abriendo cofre DB id={chest.id} en ({chest.x},{chest.y}) -> ranura cliente {tmp}, {filled} hueco(s) con objetos.");
                }
            
                // 33: abre la ventana del cofre.
                NetMessage.SendData((int)PacketTypes.ChestOpen, to, -1, NetworkText.Empty, tmp);
                // 80: avisa a los demás de qué cofre usa este jugador.
                NetMessage.SendData((int)PacketTypes.SyncPlayerChestIndex, -1, to, NetworkText.Empty, to, tmp, 0f, 0f, 0, 0, 0);
            }
            finally
            {
                Main.chest[tmp] = prevChest;
                Monitor.Exit(ClientSyncLock);
            }
        }

        private static void ApplyAction(TSPlayer p, PlayerInfo info, InfChest chest)
        {
            switch (info.Action)
            {
                case ChestAction.GetInfo:
                    ShowInfo(p, chest);
                    break;

                case ChestAction.EditMaster:
                    if (!p.HasPermission("hc.edit")) { p.SendErrorMessage("You do not have permission to change this."); break; }
                    p.SendInfoMessage("Editing the MASTER contents of this chest (what players see until they change it).");
                    OpenChest(p, info, chest, true);
                    break;

                case ChestAction.ToggleBreakable:
                    if (!p.HasPermission("hc.edit")) { p.SendErrorMessage("You do not have permission to change this."); break; }
                    chest.playerPlaced = !chest.playerPlaced;
                    DB.UpdatePlayerPlaced(chest);
                    p.SendSuccessMessage(chest.playerPlaced
                        ? "This chest can now be destroyed (treated as player-placed)."
                        : "This chest is now indestructible (world chest).");
                    break;

                case ChestAction.Protect:
                    if (!p.IsLoggedIn) { p.SendErrorMessage("You must be logged in to claim a chest."); break; }
                    if (chest.userid == AccountId(p)) { p.SendErrorMessage("This chest is already claimed by you!"); break; }
                    if (chest.userid != -1 && !p.HasPermission("hc.edit")) { p.SendErrorMessage("This chest is already claimed by someone else!"); break; }
                    chest.userid = AccountId(p);
                    DB.UpdateUser(chest);
                    p.SendSuccessMessage("This chest is now claimed by you!");
                    break;

                case ChestAction.TogglePublic:
                case ChestAction.SetGroup:
                case ChestAction.SetUser:
                case ChestAction.SetRefill:
                case ChestAction.Unprotect:
                    if (CheckManage(p, chest))
                        ManageChest(p, info, chest);
                    break;
            }
        }

        private static void ManageChest(TSPlayer p, PlayerInfo info, InfChest chest)
        {
            switch (info.Action)
            {
                case ChestAction.TogglePublic:
                    chest.isPublic = !chest.isPublic;
                    DB.UpdatePublic(chest);
                    p.SendSuccessMessage(chest.isPublic ? "Successfully set chest as public." : "Successfully set chest as private.");
                    break;

                case ChestAction.SetGroup:
                    if (chest.groups.Remove(info.ExtraInfo))
                        p.SendSuccessMessage("Successfully removed group access from chest.");
                    else
                    {
                        chest.groups.Add(info.ExtraInfo);
                        p.SendSuccessMessage("Successfully added group access to chest.");
                    }
                    DB.UpdateGroups(chest);
                    break;

                case ChestAction.SetUser:
                    int uid = int.Parse(info.ExtraInfo);
                    if (chest.users.Remove(uid))
                        p.SendSuccessMessage("Successfully removed user access from chest.");
                    else
                    {
                        chest.users.Add(uid);
                        p.SendSuccessMessage("Successfully added user access to chest.");
                    }
                    DB.UpdateUsers(chest);
                    break;

                case ChestAction.SetRefill:
                    int secs = int.Parse(info.ExtraInfo);
                    chest.refill = secs;
                    DB.UpdateRefill(chest);
                    p.SendSuccessMessage("Successfully set refill time to " + (secs == -1 ? "(none)." : secs + "."));
                    break;

                case ChestAction.Unprotect:
                    chest.userid = -1;
                    DB.UpdateUser(chest);
                    p.SendSuccessMessage("This chest is no longer claimed.");
                    break;
            }
        }

        private static void ShowInfo(TSPlayer p, InfChest chest)
        {
            p.SendInfoMessage($"X: {chest.x} | Y: {chest.y} | " + (chest.playerPlaced ? "Player-placed" : "World chest (indestructible, per-player contents)"));

            string owner = chest.userid == -1
                ? "(None)"
                : (TShock.UserAccounts.GetUserAccountByID(chest.userid)?.Name ?? "(Deleted User)");
            string pub = chest.isPublic ? " (Public)" : "";
            string refill = chest.refill <= -1 ? "" : $" (Refill: {chest.refill})";
            p.SendInfoMessage("Chest Owner: " + owner + pub + refill);

            if (chest.groups.Count > 0 && !string.IsNullOrWhiteSpace(chest.groups[0]))
                p.SendInfoMessage("Groups Allowed: " + string.Join(", ", chest.groups));
            else
                p.SendInfoMessage("Groups Allowed: (None)");

            if (chest.users.Count > 0)
                p.SendInfoMessage("Users Allowed: " + string.Join(", ",
                    chest.users.Select(u => TShock.UserAccounts.GetUserAccountByID(u)?.Name ?? "(Deleted User)")));
            else
                p.SendInfoMessage("Users Allowed: (None)");
        }

        // 32: el cliente modifica un slot de un cofre.
        // En 1.4.5 puede ser el cofre abierto O un cofre cercano que el cliente usa para craftear: el id que manda
        // es la ranura de SU Main.chest, que aquí es siempre (id en la BD) + 2 (ver ClientSlot).
        private void HandleChestItem(GetDataEventArgs args, BinaryReader r, TSPlayer player)
        {
            // Se marca como handled para que el servidor vanilla no cree cofres "fantasma" en Main.chest.
            args.Handled = true;
            if (lockChests) { WarnLocked(player); return; }

            short clientChest = r.ReadInt16();
            byte slot = r.ReadByte();
            short stack = r.ReadInt16();
            byte prefix = r.ReadByte();
            short netId = r.ReadInt16();

            PlayerInfo info = GetInfo(player);

            int targetId = info.ChestIdInUse;
            bool otherChest = false;
            int mapped = clientChest - 2;
            if (mapped >= 0 && mapped != info.ChestIdInUse && info.KnownChests.Contains(mapped))
            {
                targetId = mapped;
                otherChest = true;
            }

            Log($"Hueco {slot} del cofre modificado por {player.Name}: tipo={netId}, cantidad={stack}, idCliente={clientChest}, " +
                $"objetivo={(otherChest ? "cofre cercano " : "cofre abierto ")}{targetId}");
            if (targetId == -1 || slot >= InfChest.Slots)
                return;

            InfChest chest = DB.GetChest(targetId);
            if (chest == null)
            {
                player.SendWarningMessage("This chest is corrupted. Please remove it.");
                return;
            }

            if (otherChest && (!CanOpen(player, chest) || Chest.IsLocked(chest.x, chest.y) || !InChestRange(player, chest)))
            {
                Log($"   cofre {chest.id}: modificación rechazada (sin permiso, con llave o fuera de rango). Se reenvía su contenido.");
                ChestAudit.LogDenied(player, chest, slot, netId, stack, "sin permiso, con llave o fuera de rango");
                ResyncChest(player, chest);
                return;
            }

            var item = new Item();
            item.SetDefaults(netId);
            item.stack = stack;
            item.prefix = prefix;

            bool useOpenCopy = !otherChest && info.InstanceItems != null;
            if (useOpenCopy || (otherChest && IsInstanced(chest)))
            {
                // Copia personal: solo cambia lo que ve ESTE jugador.
                Item[] items;
                string key;
                long opened;
                if (useOpenCopy)
                {
                    items = info.InstanceItems;
                    key = info.InstanceKey;
                    opened = info.InstanceOpened;
                }
                else
                {
                    items = GetPlayerItems(player, chest, out DB.InstanceData copy);
                    key = PlayerKey(player);
                    opened = copy != null ? copy.Opened : 0;
                }

                ChestAudit.LogSlotChange(player, chest, slot, items[slot], item, true);
                items[slot] = item;
                if (opened == 0)
                    opened = NowUnix();
                if (useOpenCopy)
                    info.InstanceOpened = opened;
                DB.SaveInstance(chest.id, key, InfChest.SerializeItems(items), opened);
                return;
            }

            if (!otherChest && TryKeyMimic(player, info, chest, item))
                return;

            ChestAudit.LogSlotChange(player, chest, slot, chest.items[slot], item, false);
            chest.items[slot] = item;
            DB.UpdateItems(chest);
            NotifyChestChanged(chest, player.Index);   // el resto de jugadores que tienen este cofre en su cliente
        }

// Key of Night / Key of Light en un cofre vacío -> mimic del bioma
        private static bool TryKeyMimic(TSPlayer player, PlayerInfo info, InfChest chest, Item item)
        {
            // Un cofre del mundo nunca se destruye con la llave (rompería el cofre para todos).
            if (!chest.playerPlaced || !Main.hardMode || !player.HasPermission(Permissions.spawnmob) || !chest.isEmpty)
                return false;

            bool night = item.type == ItemID.NightKey && (player.TPlayer.ZoneCrimson || player.TPlayer.ZoneCorrupt);
            bool light = item.type == ItemID.LightKey && player.TPlayer.ZoneHallow;
            if (!night && !light)
                return false;

            info.ChestIdInUse = -1;
            ClosePlayerChest(player);
            DB.DeleteChest(chest.id);
            WorldGen.KillTile(chest.x, chest.y, false, false, true);
            TSPlayer.All.SendTileSquareCentered(chest.x, chest.y, 3);

            int npcId = light ? NPCID.BigMimicHallow
                      : player.TPlayer.ZoneCrimson ? NPCID.BigMimicCrimson
                      : NPCID.BigMimicCorruption;
            NPC npc = TShock.Utils.GetNPCById(npcId);
            TSPlayer.Server.SpawnNPC(npc.type, npc.FullName, 1, player.TileX, player.TileY, 10, 10);
            return true;
        }

        // 33: el cliente cierra el cofre (id = -1)
        private void HandleChestOpen(GetDataEventArgs args, BinaryReader r, TSPlayer player)
        {
            // El servidor vanilla no conoce el cofre temporal (Player.chest nunca se fija en el servidor): si dejara
            // pasar este paquete, al renombrar/cerrar haría Main.chest[-1]. Lo gestionamos todo aquí.
            args.Handled = true;

            short chestId = r.ReadInt16();
            r.ReadInt16();
            r.ReadInt16();
            byte nameLength = r.ReadByte();

            if (chestId < -1)
                Log($"33: {player.Name} usa el banco {chestId} (-2 hucha, -3 caja fuerte, -4 forja, -5 Void Vault).");

            if (nameLength == 0 && chestId == -1)
            {
                PlayerInfo pi = GetInfo(player);
                pi.ChestIdInUse = -1;
                pi.InstanceItems = null;
                ClosePlayerChest(player);
            }
        }
        // ------------------------------------------------------------------ 85: acumulación rápida (1.4.5)
        //
        // Formato del paquete 85 en 1.4.5 (deducido de bytes reales):
        //     Int32 n | n x Int16 (número de hueco, el mismo que usa el paquete 5) | Int16 modo
        // El Int16 final vale 0 en el modo normal ("Quick Stack"). Se supone que 1 = "Similar Stack" (NO confirmado: si no
        // se activa, el log muestra "85: bytes finales" con el valor real que manda el cliente).
        // Antes de 1.4.5 era un único byte con el hueco del inventario.
        //
        // Huecos que se procesan: 10..49 = inventario principal y 220..259 = Void Vault (la Bolsa del Vacío abierta actúa
        // como un segundo inventario y el cliente envía también sus huecos). El resto (hotbar, monedas, munición,
        // bancos...) no se toca.
        //
        // IMPORTANTE: el paquete NO se marca como manejado. Tras guardar en la BD, el servidor vanilla lo procesa igualmente
        // (no encuentra cofres en Main.chest porque no hay ninguno, así que no mueve nada) y envía al cliente la
        // confirmación que éste espera. Cuando el paquete se marcaba como manejado y quedaban huecos sin responder
        // (p. ej. los del Void Vault), el cliente se quedaba "ocupado" y bloqueaba crafteo, ordenar y guardado rápido.
        private const int QuickStackMaxSlots = 300;

        private sealed class StackTarget
        {
            public InfChest Chest;
            public Item[] Items;
            public bool Instanced;
            public DB.InstanceData Copy;
            public bool Dirty;
        }

        private static bool IsQuickStackSlot(int s) => (s >= 10 && s < 50) || (s >= 220 && s < 260);

        // Resuelve un número de hueco del protocolo (el de los paquetes 5) a su objeto en el servidor.
        private static Item GetSlotItem(TSPlayer p, int slot)
        {
            Player tp = p.TPlayer;
            if (slot >= 0 && slot <= 58)
                return tp.inventory[slot];
            if (slot >= 220 && slot < 260)
                return tp.bank4.item[slot - 220];
            return null;
        }

        // Reenvía un hueco (paquete 5) para que el cliente muestre exactamente lo que ve el servidor.
        private static void ResyncAny(TSPlayer p, int slot)
        {
            Item it = GetSlotItem(p, slot);
            if (it == null)
                return;
            NetMessage.SendData(5, -1, -1, NetworkText.Empty, p.Index, slot, it.prefix);
        }

        private void HandleQuickStack(GetDataEventArgs args, BinaryReader r, TSPlayer player)
        {
            // No se marca nunca como manejado (ver arriba).
            byte[] raw = new byte[args.Length];
            Array.Copy(args.Msg.readBuffer, args.Index, raw, 0, args.Length);
            Log($"85 de {player.Name}: longitud={args.Length}, bytes={BitConverter.ToString(raw)}");

            var requested = new List<int>();
            int mode = 0;
            if (args.Length == 1)
            {
                requested.Add(raw[0]);
            }
            else if (args.Length >= 4)
            {
                int count = BitConverter.ToInt32(raw, 0);
                if (count < 0 || count > QuickStackMaxSlots || 4 + count * 2 > args.Length)
                {
                    Log("85: formato inesperado. Se deja pasar sin tocar.");
                    return;
                }
                for (int i = 0; i < count; i++)
                    requested.Add(BitConverter.ToInt16(raw, 4 + i * 2));

                int tailAt = 4 + count * 2;
                if (args.Length - tailAt >= 2)
                    mode = BitConverter.ToInt16(raw, tailAt);
                if (args.Length > tailAt)
                    Log($"85: bytes finales = {BitConverter.ToString(raw, tailAt)} -> modo {mode}");
            }
            else
            {
                Log("85: formato inesperado (demasiado corto). Se deja pasar sin tocar.");
                return;
            }

            bool similar = mode == 1;
            List<int> distinct = requested.Distinct().ToList();
            List<int> slots = distinct.Where(IsQuickStackSlot).ToList();
            List<int> ignored = distinct.Where(s => !IsQuickStackSlot(s)).ToList();
            if (ignored.Count > 0)
                Log($"85: huecos ignorados (hotbar, monedas, munición u otros): {string.Join(", ", ignored)}");
            Log($"85: modo {(similar ? "SIMILAR" : "normal")}, {slots.Count} hueco(s) a guardar (del Void Vault: {slots.Count(s => s >= 220)}).");

            try
            {
                if (slots.Count > 0 && usingInfChests && !lockChests)
                    QuickStackToDb(player, slots, similar);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError("[HorizonChests] " + ex);
            }
            finally
            {
                // Pase lo que pase, el cliente recibe el estado real de todos los huecos que pidió.
                foreach (int s in distinct)
                    ResyncAny(player, s);
            }
        }

        private static void QuickStackToDb(TSPlayer player, List<int> slots, bool similar)
        {
            PlayerInfo info = GetInfo(player);
            Microsoft.Xna.Framework.Vector2 pos = player.TPlayer.Center;
            int tx = (int)(pos.X / 16f), ty = (int)(pos.Y / 16f);
            int range = (int)(StackRange / 16f) + 2;

            List<InfChest> nearby = DB.GetChestsInArea(tx - range, ty - range, tx + range, ty + range);
            var targets = new List<StackTarget>();

            // Como en vanilla, si varios cofres admiten el objeto se prefiere el que está más abajo en el mundo.
            foreach (InfChest chest in nearby.OrderByDescending(c => c.y).ThenBy(c => c.x))
            {
                if (chest.id == info.ChestIdInUse) { Log($"   cofre {chest.id}: es el que tiene abierto."); continue; }
                if (!CanOpen(player, chest)) { Log($"   cofre {chest.id}: sin permiso."); continue; }

                var center = new Microsoft.Xna.Framework.Vector2(chest.x * 16 + 16, chest.y * 16 + 16);
                float dist = (center - pos).Length();
                if (dist >= StackRange) { Log($"   cofre {chest.id}: demasiado lejos ({dist:0} px)."); continue; }
                if (Chest.IsLocked(chest.x, chest.y)) { Log($"   cofre {chest.id}: con llave."); continue; }

                bool instanced = IsInstanced(chest);
                if (!instanced && TShock.Players.Any(p => p != null && p.Active && p.Index != player.Index &&
                                                          GetInfo(p).ChestIdInUse == chest.id))
                { Log($"   cofre {chest.id}: lo tiene abierto otro jugador."); continue; }

                var t = new StackTarget { Chest = chest, Instanced = instanced };
                t.Items = instanced ? GetPlayerItems(player, chest, out t.Copy) : chest.items;
                targets.Add(t);
            }

            Log($"85: {targets.Count} cofre(s) válidos de {nearby.Count} en el área, jugador en ({tx},{ty}).");
            if (targets.Count == 0)
                return;

            foreach (int s in slots)
            {
                Item item = GetSlotItem(player, s);
                if (item == null || item.type <= 0 || item.stack <= 0)
                    continue;

                // 1) Quick Stack normal: a los cofres que ya contienen ese objeto.
                foreach (StackTarget t in targets)
                {
                    if (item.stack <= 0)
                        break;
                    if (StackInto(t.Items, item))
                        t.Dirty = true;
                }

                // 2) Similar Stack: lo que sobre (o que no estaba en ningún cofre) va al cofre con más objetos de su
                //    misma categoría que aún tenga un hueco libre.
                if (similar && item.stack > 0)
                {
                    string key = SimilarKey(item);
                    StackTarget best = null;
                    int bestCount = 0;
                    foreach (StackTarget t in targets)
                    {
                        if (!HasFreeSlot(t.Items))
                            continue;
                        int n = t.Items.Count(it => it != null && it.type > 0 && it.stack > 0 && SimilarKey(it) == key);
                        if (n > bestCount)
                        {
                            best = t;
                            bestCount = n;
                        }
                    }
                    if (best != null && PlaceInFreeSlot(best.Items, item))
                    {
                        best.Dirty = true;
                        Log($"   similar: {key} -> cofre {best.Chest.id} ({bestCount} objeto(s) de esa categoría).");
                    }
                }

                if (item.stack <= 0)
                    item.TurnToAir();
            }

            foreach (StackTarget t in targets.Where(x => x.Dirty))
            {
                InfChest chest = t.Chest;
                if (t.Instanced)
                {
                    DB.SaveInstance(chest.id, PlayerKey(player), InfChest.SerializeItems(t.Items), t.Copy != null ? t.Copy.Opened : NowUnix());
                    if (info.KnownChests.Contains(chest.id))
                        SyncChestToClient(player.Index, chest, t.Items, ClientSlot(chest));
                }
                else
                {
                    DB.UpdateItems(chest);
                    NotifyChestChanged(chest, -1);   // también al propio jugador, si ya tiene el cofre en su cliente
                }
                Log($"Guardado rápido de {player.Name}: -> cofre {chest.id} ({chest.x},{chest.y}).");
            }
        }

        private static bool HasFreeSlot(Item[] items)
        {
            foreach (Item c in items)
            {
                if (c == null || c.type == 0 || c.stack == 0)
                    return true;
            }
            return false;
        }

        // Deja "item" entero en el primer hueco libre del cofre.
        private static bool PlaceInFreeSlot(Item[] items, Item item)
        {
            for (int j = 0; j < items.Length; j++)
            {
                Item c = items[j];
                if (c == null || c.type == 0 || c.stack == 0)
                {
                    items[j] = item.Clone();
                    item.TurnToAir();
                    return true;
                }
            }
            return false;
        }

        // Categoría usada por el modo "Similar Stack". En vanilla es la misma clasificación que "Sort Items"
        // (Terraria.UI.ItemSorting); aquí es una APROXIMACIÓN con las propiedades del objeto, así que no coincide
        // al 100 % (el propio wiki avisa de que esa clasificación es amplia: p. ej. fogatas y lápidas son "mobiliario").
        private static string SimilarKey(Item it)
        {
            if (it.IsACoin) return "moneda";
            if (it.headSlot >= 0 || it.bodySlot >= 0 || it.legSlot >= 0) return it.vanity ? "vanidad" : "armadura";
            if (it.accessory) return it.vanity ? "vanidad" : "accesorio";
            if (it.pick > 0 || it.axe > 0 || it.hammer > 0) return "herramienta";
            if (it.ammo > 0) return it.damage > 0 ? "municion" : "municion-util";
            if (it.damage > 0) return "arma";
            if (it.healLife > 0 || it.healMana > 0) return "pocion";
            if (it.buffType > 0 && it.consumable) return "pocion";
            if (it.dye > 0) return "tinte";
            if (it.paint > 0) return "pintura";
            if (it.mountType >= 0) return "equipo";
            if (it.bait > 0 || it.fishingPole > 0) return "pesca";
            if (it.createWall >= 0) return "pared";
            if (it.createTile >= 0) return it.material ? "mineral-material" : "bloque";
            if (it.material) return "material";
            return "otros";
        }

        // Apila "item" en los huecos que ya contienen el mismo objeto; si sobra y hay hueco libre, lo deja en uno vacío.
        // (Vanilla solo guarda en cofres que YA tienen ese objeto.) Devuelve true si el cofre cambió.
        private static bool StackInto(Item[] items, Item item)
        {
            bool hasSame = false, hasEmpty = false, changed = false;

            for (int j = 0; j < items.Length && item.stack > 0; j++)
            {
                Item c = items[j];
                if (c != null && c.type > 0 && c.stack > 0)
                {
                    if (item.type != c.type || item.prefix != c.prefix)
                        continue;
                    hasSame = true;
                    int n = Math.Min(c.maxStack - c.stack, item.stack);
                    if (n > 0)
                    {
                        item.stack -= n;
                        c.stack += n;
                        changed = true;
                    }
                }
                else
                {
                    hasEmpty = true;
                }
            }

            if (hasSame && hasEmpty && item.stack > 0)
            {
                for (int j = 0; j < items.Length; j++)
                {
                    Item c = items[j];
                    if (c == null || c.type == 0 || c.stack == 0)
                    {
                        items[j] = item.Clone();
                        item.TurnToAir();
                        changed = true;
                        break;
                    }
                }
            }

            return changed;
        }

        // ------------------------------------------------------------------ cofres del mundo indestructibles

        private static bool TryGetChestOrigin(int x, int y, out int ox, out int oy)
        {
            ox = x; oy = y;
            var t = Main.tile[x, y];
            if (!t.active())
                return false;
            if (t.type == TileID.Containers || t.type == TileID.Containers2)
            {
                ox = x - t.frameX % 36 / 18;
                oy = y - t.frameY % 36 / 18;
                return true;
            }
            if (t.type == TileID.Dressers)
            {
                ox = x - t.frameX % 54 / 18;
                oy = y - t.frameY % 36 / 18;
                return true;
            }
            return false;
        }

        // true si el cofre es del mundo (no colocado por un jugador) y este jugador no puede destruirlo.
        private static bool IsProtectedWorldChest(TSPlayer p, InfChest c) =>
            c != null && !c.playerPlaced && !p.HasPermission("hc.breakworld");

        private static void DenyBreak(TSPlayer p, int x, int y)
        {
            p.SendTileSquareCentered(x + 1, y, 5);   // 5x5: cubre cofres 2x2 y cómodas 3x2
        }

        // Paquete 17 (editar tile): red de seguridad por si un cliente intenta romper un cofre del mundo
        // directamente en vez de usar el paquete 34.
        private static void GuardWorldChestTile(GetDataEventArgs args)
        {
            if (args.Handled || !usingInfChests)
                return;

            TSPlayer player = TShock.Players[args.Msg.whoAmI];
            if (player == null)
                return;

            try
            {
                using (var reader = new BinaryReader(new MemoryStream(args.Msg.readBuffer, args.Index, args.Length)))
                {
                    byte action = reader.ReadByte();      // 0 = KillTile, 4 = KillTileNoItem
                    short x = reader.ReadInt16();
                    short y = reader.ReadInt16();
                    if (action != 0 && action != 4)
                        return;
                    if (x < 0 || y < 0 || x >= Main.maxTilesX || y >= Main.maxTilesY)
                        return;
                    if (!TryGetChestOrigin(x, y, out int ox, out int oy))
                        return;

                    InfChest chest = DB.GetChest(ox, oy);
                    if (chest == null)
                        return;

                    // Un cliente normal rompe cofres con el paquete 34. Si llega un kill por el paquete 17 con un cofre
                    // registrado, en 1.4.5 el tile se destruiría sin comprobar el contenido y la fila quedaría huérfana.
                    args.Handled = true;
                    Log($"Bloqueado kill directo de cofre registrado en ({ox},{oy}) por {player.Name} (paquete 17).");
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError("[HorizonChests] " + ex);
            }
        }

        // 34: colocar / destruir cofre, cómoda o cofre de bioma (Containers2)
        private void HandlePlaceChest(GetDataEventArgs args, BinaryReader r, TSPlayer player)
        {
            args.Handled = true;
            if (lockChests) { WarnLocked(player); return; }

            byte action = r.ReadByte();
            short tileX = r.ReadInt16();
            short tileY = r.ReadInt16();
            short style = r.ReadInt16();
            short clientChestId = r.ReadInt16();   // id de cofre que conoce el cliente (el servidor vanilla lo devuelve tal cual al destruir)

            if (action > 5 || tileX < 1 || tileY < 1 || tileX >= Main.maxTilesX - 1 || tileY >= Main.maxTilesY - 1)
                return;

            Log($"34: {(action % 2 == 0 ? "COLOCAR" : "DESTRUIR")} accion={action} x={tileX} y={tileY} estilo={style} idCliente={clientChestId} jugador={player.Name}. Antes: " + DescribePlaceArea(tileX, tileY));

            ushort tileType = action <= 1 ? TileID.Containers : action <= 3 ? TileID.Dressers : TileID.Containers2;
            bool placing = action % 2 == 0;

            if (placing)
            {
                if (!player.HasBuildPermission(tileX, tileY))
                {
                    player.SendTileSquareCentered(tileX, tileY, 3);
                    return;
                }

                // Limpia cualquier cofre vanilla "residual" registrado en esa posición (si existiera, CreateChest falla).
                int guessX = tileType == TileID.Dressers ? tileX - 1 : tileX;
                int guessY = tileY - 1;
                for (int i = 0; i < Main.chest.Length; i++)
                {
                    Chest stale = Main.chest[i];
                    if (stale != null && stale.x == guessX && stale.y == guessY)
                    {
                        Log($"Cofre vanilla residual en ({guessX},{guessY}) (ranura {i}): se elimina antes de colocar.");
                        Chest.RemoveChest(i);
                    }
                }

                int idx = WorldGen.PlaceChest(tileX, tileY, tileType, false, style);
                if (idx == -1)
                {
                    Log($"PlaceChest FALLÓ en ({tileX},{tileY}) tipo={tileType} estilo={style}. " + DescribePlaceArea(tileX, tileY));
                    // BUG anterior: el último campo era 0 (= "éxito, cofre id 0"), así que el cliente conservaba el cofre
                    // y quedaba un fantasma. El valor -1 le dice al cliente que la colocación falló y debe quitarlo.
                    NetMessage.TrySendData(34, player.Index, -1, null, action, tileX, tileY, style, -1, 0, 0);
                    player.SendTileSquareCentered(tileX + 1, tileY, 5);
                    int refundItem = ChestCompat.ItemForTile(tileType, style);
                    if (refundItem > 0)
                        // noBroadcast = false: antes era true y el objeto solo aparecía al reentrar en la zona.
                        Item.NewItem(new EntitySource_TileBreak(tileX, tileY), tileX * 16, tileY * 16, 32, 32, refundItem, 1, false, 0);
                    return;
                }

                // Se toman las coordenadas reales del cofre creado y se libera la ranura vanilla.
                Chest placed = Main.chest[idx];
                int cx = placed != null ? placed.x : (action == 2 ? tileX - 1 : tileX);
                int cy = placed != null ? placed.y : tileY - 1;
                Chest.RemoveChest(idx);
                Log($"PlaceChest OK idx={idx} origen=({cx},{cy}); tile en origen: activo={Main.tile[cx, cy].active()} tipo={Main.tile[cx, cy].type}.");

                DB.AddChest(new InfChest(player.HasPermission("hc.protect") ? AccountId(player) : -1, cx, cy, Main.worldID) { playerPlaced = true });
                NetMessage.SendData(34, -1, -1, null, action, tileX, tileY, style, 0, 0, 0);
                return;
            }

            // --- destruir ---
            ushort current = Main.tile[tileX, tileY].type;
            if (current != TileID.Containers && current != TileID.Dressers && current != TileID.Containers2)
                return;

            if (!player.HasBuildPermission(tileX, tileY))
            {
                player.SendTileSquareCentered(tileX, tileY, 3);
                return;
            }

            if (Main.tile[tileX, tileY].frameY % 36 != 0)
                tileY--;
            if ((action == 1 || action == 5) && Main.tile[tileX, tileY].frameX % 36 != 0)
                tileX--;
            if (action == 3)
                tileX -= (short)(Main.tile[tileX, tileY].frameX % 54 / 18);

            InfChest chest = DB.GetChest(tileX, tileY);
            if (chest == null)
            {
                // Tile sin fila en la base de datos (cofre vacío por definición): se elimina igualmente.
                DestroyChestTile(tileX, tileY, action, clientChestId);
                return;
            }

            // Los cofres del mundo no se pueden romper (solo los colocados por jugadores).
            if (IsProtectedWorldChest(player, chest))
            {
                Log($"Bloqueado intento de romper cofre del mundo en ({tileX},{tileY}) por {player.Name} (paquete 34).");
                DenyBreak(player, tileX, tileY);
                return;
            }

            if (chest.userid != -1 && chest.userid != AccountId(player) && !player.HasPermission("hc.edit"))
            {
                player.SendErrorMessage("This chest is protected.");
                player.SendTileSquareCentered(tileX, tileY, 3);
                return;
            }

            if (!chest.isEmpty)
            {
                player.SendTileSquareCentered(tileX, tileY, 3);
                return;
            }

            DB.DeleteChest(chest.id);
            DestroyChestTile(tileX, tileY, action, clientChestId);
        }

        // Elimina todas las casillas del cofre (2x2; 3x2 las cómodas). KillTile sobre la esquina puede dejar restos.
        // Devuelve cuántas casillas quedaron eliminadas.
        private static int ClearChestFootprint(int ox, int oy, int tileType)
        {
            int w = tileType == TileID.Dressers ? 3 : 2;
            int cleared = 0;
            for (int dx = 0; dx < w; dx++)
            {
                for (int dy = 0; dy < 2; dy++)
                {
                    int tx = ox + dx, ty = oy + dy;
                    if (tx < 0 || ty < 0 || tx >= Main.maxTilesX || ty >= Main.maxTilesY) continue;
                    var t = Main.tile[tx, ty];
                    if (!t.active() || t.type != tileType) continue;
                    WorldGen.KillTile(tx, ty, false, false, true);
                    t = Main.tile[tx, ty];
                    if (t.active() && t.type == tileType)
                        t.active(false);
                    cleared++;
                }
            }
            return cleared;
        }

        private static string DescribePlaceArea(int x, int y)
        {
            var sb = new System.Text.StringBuilder();
            for (int dy = -2; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 2; dx++)
                {
                    int tx = x + dx, ty = y + dy;
                    if (tx < 0 || ty < 0 || tx >= Main.maxTilesX || ty >= Main.maxTilesY) continue;
                    var t = Main.tile[tx, ty];
                    sb.Append($"[{tx},{ty}:{(t.active() ? "T" + t.type + (Main.tileSolid[t.type] ? "s" : "") : "-")}{(t.liquid > 0 ? "~" : "")}] ");
                }
            }
            return sb.ToString();
        }

        // Destruye el tile del cofre en el servidor, devuelve el objeto-cofre y sincroniza a TODOS los clientes.
        // Antes solo se mandaba el paquete 34: si el cliente no lo aplicaba, quedaba un cofre "fantasma" visible
        // (sin existir en el servidor) y no se devolvía el objeto.
        private static void DestroyChestTile(int tileX, int tileY, byte action, int clientChestId)
        {
            var t = Main.tile[tileX, tileY];
            int tileType = t.type;
            int style = tileType == TileID.Dressers ? t.frameX / 54 : t.frameX / 36;

            // noItem = true: el objeto se devuelve a mano para que SIEMPRE salga (y una sola vez).
            WorldGen.KillTile(tileX, tileY, false, false, true);
            int leftovers = ClearChestFootprint(tileX, tileY, tileType);
            if (leftovers > 0)
                Log($"Restos del cofre en ({tileX},{tileY}): {leftovers} casilla(s) eliminadas tras KillTile.");

            int refundItem = ChestCompat.ItemForTile(tileType, style);
            if (refundItem > 0)
                Item.NewItem(new EntitySource_TileBreak(tileX, tileY), tileX * 16, tileY * 16, 32, 32, refundItem, 1, false, 0);

            // El primer parámetro del paquete 34 es la ACCIÓN (1/3/5 = destruir). Igual que el servidor vanilla, se devuelve
            // el id de cofre que mandó el cliente (antes -1): así el cliente libera también su registro local del cofre.
            NetMessage.TrySendData(34, -1, -1, null, action, tileX, tileY, 0f, clientChestId, 0, 0);
            // Reenvía el cuadro de tiles para que ningún cliente conserve el cofre.
            TSPlayer.All.SendTileSquareCentered(tileX + 1, tileY, 5);

            // El servidor ya queda correcto (se comprueba en el log), pero el cliente podía seguir mostrando el cofre hasta
            // el siguiente reenvío manual. Se repite el reenvío un poco después, cuando el cliente ya ha procesado todo.
            int cx = tileX, cy = tileY;
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(300);
                    TSPlayer.All.SendTileSquareCentered(cx + 1, cy, 7);
                    await Task.Delay(1200);
                    TSPlayer.All.SendTileSquareCentered(cx + 1, cy, 7);
                }
                catch (Exception ex) { TShock.Log.ConsoleError("[HorizonChests] " + ex.Message); }
            });

            if (Main.tile[tileX, tileY].active() && Main.tile[tileX, tileY].type == tileType)
                TShock.Log.ConsoleError($"[HorizonChests] KillTile no eliminó el cofre en ({tileX},{tileY}).");
        }

        // ------------------------------------------------------------------ /chest

        private void ChestCMD(CommandArgs args)
        {
            TSPlayer p = args.Player;
            if (!usingInfChests)
            {
                p.SendErrorMessage("HorizonChests are currently disabled on this server.");
                return;
            }

            if (args.Parameters.Count == 0 || args.Parameters[0].Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                ShowHelp(args);
                return;
            }

            PlayerInfo info = GetInfo(p);
            string sub = args.Parameters[0].ToLowerInvariant();

            switch (sub)
            {
                case "cancel":
                    info.Action = ChestAction.None;
                    p.SendInfoMessage("Canceled chest action.");
                    return;

                case "info":
                    if (!p.HasPermission("hc.info")) { p.SendErrorMessage("You do not have permission to view chest info."); return; }
                    p.SendInfoMessage("Open a chest to get information about it.");
                    info.Action = ChestAction.GetInfo;
                    return;

                case "master":
                    if (!p.HasPermission("hc.edit")) { p.SendErrorMessage("You do not have permission to change this."); return; }
                    p.SendInfoMessage("Open a chest to edit its master contents.");
                    info.Action = ChestAction.EditMaster;
                    return;

                case "refillall":
                    {
                        if (!p.HasPermission("hc.refill")) { p.SendErrorMessage("You do not have permission to set a chest's refill time."); return; }
                        if (args.Parameters.Count != 2 || !int.TryParse(args.Parameters[1], out int secs) || secs < -1 || secs > 99999)
                        {
                            p.SendErrorMessage("Invalid syntax: /chest refillall <seconds>  (-1 = never reset)");
                            return;
                        }
                        int n = DB.SetWorldChestRefill(secs);
                        p.SendSuccessMessage(secs == -1
                            ? $"World chests ({n}) will never reset players' personal contents."
                            : $"World chests ({n}) now reset each player's contents {secs} seconds after they first change them.");
                        return;
                    }

                case "breakable":
                    if (!p.HasPermission("hc.edit")) { p.SendErrorMessage("You do not have permission to change this."); return; }
                    p.SendInfoMessage("Open a chest to toggle whether it can be destroyed.");
                    info.Action = ChestAction.ToggleBreakable;
                    return;

                case "claim":
                    if (!p.HasPermission("hc.claim")) { p.SendErrorMessage("You do not have permission to claim chests."); return; }
                    p.SendInfoMessage("Open a chest to claim it.");
                    info.Action = ChestAction.Protect;
                    return;

                case "unclaim":
                    if (!p.HasPermission("hc.claim")) { p.SendErrorMessage("You do not have permission to claim chests."); return; }
                    p.SendInfoMessage("Open a chest to unclaim it.");
                    info.Action = ChestAction.Unprotect;
                    return;

                case "public":
                    if (!p.HasPermission("hc.public")) { p.SendErrorMessage("You do not have permission to change a chest's public setting."); return; }
                    info.Action = ChestAction.TogglePublic;
                    p.SendInfoMessage("Open a chest to toggle the chest's public setting.");
                    return;

                case "refill":
                    {
                        if (!p.HasPermission("hc.refill")) { p.SendErrorMessage("You do not have permission to set a chest's refill time."); return; }
                        if (args.Parameters.Count != 2) { p.SendErrorMessage("Invalid syntax: /chest refill <seconds>"); return; }
                        if (!int.TryParse(args.Parameters[1], out int secs) || secs < -1 || secs > 99999)
                        {
                            p.SendErrorMessage("Invalid refill time.");
                            return;
                        }
                        info.Action = ChestAction.SetRefill;
                        info.ExtraInfo = secs.ToString();
                        p.SendInfoMessage(secs != -1
                            ? "Open a chest to set its refill time to " + secs + " seconds."
                            : "Open a chest to remove auto-refill.");
                        return;
                    }

                case "allow":
                case "remove":
                    {
                        if (!p.HasPermission("hc.claim"))
                        {
                            p.SendErrorMessage(sub == "allow"
                                ? "You do not have permission to allow other users to access this chest."
                                : "You do not have permission to remove chest access from other users.");
                            return;
                        }
                        if (args.Parameters.Count < 2) { p.SendErrorMessage($"Invalid syntax: /chest {sub} <player name>"); return; }

                        string name = string.Join(" ", args.Parameters.Skip(1));
                        var account = TShock.UserAccounts.GetUserAccountByName(name);
                        if (account == null) { p.SendErrorMessage("No player found by the name " + name); return; }

                        info.ExtraInfo = account.ID.ToString();
                        info.Action = ChestAction.SetUser;
                        p.SendInfoMessage(sub == "allow"
                            ? "Open a chest to allow " + name + " to access it."
                            : "Open a chest to remove chest access from " + name + ".");
                        return;
                    }

                case "allowgroup":
                case "allowg":
                case "removegroup":
                case "removeg":
                    {
                        bool allow = sub.StartsWith("allow");
                        if (!p.HasPermission("hc.claim"))
                        {
                            p.SendErrorMessage(allow
                                ? "You do not have permission to allow other groups to access this chest."
                                : "You do not have permission to remove chest access from other groups.");
                            return;
                        }
                        if (args.Parameters.Count != 2)
                        {
                            p.SendErrorMessage(allow ? "Invalid syntax: /chest allowgroup <group name>" : "Invalid syntax: /chest removegroup <group name>");
                            return;
                        }

                        var group = TShock.Groups.GetGroupByName(args.Parameters[1]);
                        if (group == null) { p.SendErrorMessage("No group found by the name " + args.Parameters[1]); return; }

                        info.ExtraInfo = group.Name;
                        info.Action = ChestAction.SetGroup;
                        p.SendInfoMessage(allow
                            ? "Open a chest to allow users from the group " + group.Name + " to access it."
                            : "Open a chest to remove chest access from users in the group " + group.Name + ".");
                        return;
                    }

                case "search":
                    SearchCommand(args);
                    return;

                case "verify":
                    VerifyCommand(args, args.Parameters.Count > 1 && args.Parameters[1].Equals("fix", StringComparison.OrdinalIgnoreCase));
                    return;

                default:
                    p.SendErrorMessage("Invalid syntax. Use '/chest help' for help.");
                    return;
            }
        }


        // /chest verify [fix]: compara la base de datos con los tiles del mundo.
        private static void VerifyCommand(CommandArgs args, bool fix)
        {
            TSPlayer p = args.Player;
            if (!p.HasPermission("hc.edit")) { p.SendErrorMessage("You do not have permission to change this."); return; }
            if (lockChests) { WarnLocked(p); return; }

            List<InfChest> rows = DB.GetAllRows();
            var seen = new Dictionary<(int, int), InfChest>();
            int orphans = 0, dups = 0, missing = 0, fixedN = 0;

            foreach (InfChest c in rows)
            {
                bool tileOk = c.x >= 0 && c.y >= 0 && c.x < Main.maxTilesX && c.y < Main.maxTilesY
                              && TryGetChestOrigin(c.x, c.y, out int ox, out int oy) && ox == c.x && oy == c.y;

                if (seen.ContainsKey((c.x, c.y)))
                {
                    dups++;
                    p.SendWarningMessage($"Duplicado: ID {c.id} en ({c.x},{c.y}) (se conserva ID {seen[(c.x, c.y)].id}).");
                    if (fix && c.isEmpty) { DB.DeleteChest(c.id); fixedN++; }
                    continue;
                }
                seen[(c.x, c.y)] = c;

                if (!tileOk)
                {
                    orphans++;
                    int filled = c.items.Count(i => i != null && i.type != 0);
                    p.SendWarningMessage($"Huérfano: ID {c.id} en ({c.x},{c.y}) no tiene tile de cofre; objetos={filled}, dueño={c.userid}.");
                    // Solo se borra automáticamente si está vacío: si tiene objetos se deja para revisarlo a mano.
                    if (fix && filled == 0) { DB.DeleteChest(c.id); fixedN++; }
                }
            }

            // Tiles de cofre sin fila en la base de datos.
            for (int x = 0; x < Main.maxTilesX; x++)
            {
                for (int y = 0; y < Main.maxTilesY; y++)
                {
                    var t = Main.tile[x, y];
                    if (!t.active()) continue;
                    if (t.type != TileID.Containers && t.type != TileID.Containers2 && t.type != TileID.Dressers) continue;
                    if (!TryGetChestOrigin(x, y, out int ox, out int oy) || ox != x || oy != y) continue;
                    if (seen.ContainsKey((x, y))) continue;
                    missing++;
                    if (fix)
                    {
                        DB.AddChest(new InfChest(-1, x, y, Main.worldID) { playerPlaced = true });
                        fixedN++;
                    }
                }
            }

            p.SendInfoMessage($"Verify: filas={rows.Count}, huérfanas={orphans}, duplicadas={dups}, tiles sin fila={missing}." +
                              (fix ? $" Reparados={fixedN}." : " Usa /chest verify fix para reparar."));
        }

        private static void ShowHelp(CommandArgs args)
        {
            TSPlayer p = args.Player;
            var list = new List<string>();
            p.SendErrorMessage("Invalid syntax:");
            if (p.HasPermission("hc.claim")) list.Add("/chest <claim/unclaim>");
            if (p.HasPermission("hc.info")) list.Add("/chest info");
            if (p.HasPermission("hc.search")) list.Add("/chest search <item name>");
            if (p.HasPermission("hc.claim"))
            {
                list.Add("/chest allow <player name>");
                list.Add("/chest remove <player name>");
                list.Add("/chest allowgroup <group name>");
                list.Add("/chest removegroup <group name>");
            }
            if (p.HasPermission("hc.public")) list.Add("/chest public");
            if (p.HasPermission("hc.refill")) { list.Add("/chest refill <seconds>"); list.Add("/chest refillall <seconds>"); }
            if (p.HasPermission("hc.edit")) { list.Add("/chest breakable"); list.Add("/chest master"); list.Add("/chest verify [fix]"); }
            list.Add("/chest cancel");

            if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, p, out int page))
                return;

            PaginationTools.SendPage(p, page, list, new PaginationTools.Settings
            {
                HeaderFormat = "Chest Subcommands ({0}/{1}):",
                FooterFormat = "Type /chest help {0} for more."
            });
        }

        private static void SearchCommand(CommandArgs args)
        {
            TSPlayer p = args.Player;
            if (!p.HasPermission("hc.search")) { p.SendErrorMessage("You do not have permission to search for chest items."); return; }
            if (args.Parameters.Count < 2) { p.SendErrorMessage("Invalid syntax: /chest search <item name>"); return; }

            string text = string.Join(" ", args.Parameters.Skip(1));
            string lower = text.ToLowerInvariant();

            Item exact = null;
            var partial = new List<Item>();
            for (int i = 1; i < ItemID.Count; i++)
            {
                var item = new Item();
                item.SetDefaults(i);
                string n = item.Name.ToLowerInvariant();
                if (n == lower) { exact = item; break; }
                if (n.Contains(lower)) partial.Add(item);
            }

            Item found = exact ?? (partial.Count == 1 ? partial[0] : null);
            if (found != null)
                p.SendSuccessMessage($"There are {DB.SearchChests(found.type)} chest(s) with {found.Name}(s).");
            else if (partial.Count > 1)
                p.SendErrorMessage("Multiple matches found for item '" + text + "'.");
            else
                p.SendErrorMessage("No matches found for item '" + text + "'.");
        }

        // ------------------------------------------------------------------ comandos de administración
        // RECONSTRUIDOS: el decompilado no incluía los cuerpos (state machines async).

        private void ConvChests(CommandArgs args)
        {
            if (lockChests) { args.Player.SendErrorMessage("A chest conversion is already running."); return; }

            bool reverse = args.Parameters.Any(a => a.Equals("-r", StringComparison.OrdinalIgnoreCase));
            if (reverse && !usingInfChests) { args.Player.SendErrorMessage("Chests are already stored in the world file."); return; }

            lockChests = true;
            args.Player.SendInfoMessage("Converting chests, please wait...");

            Task.Run(() =>
            {
                try
                {
                    if (!reverse)
                    {
                        int n = InnerConvChests();
                        usingInfChests = true;
                        args.Player.SendSuccessMessage($"Converted {n} chest(s) to the database.");
                    }
                    else
                    {
                        int n = DB.GetCount();
                        if (n > Main.chest.Length)
                        {
                            args.Player.SendErrorMessage($"There are {n} chests in the database; the world only supports {Main.chest.Length}.");
                            return;
                        }
                        AddAllChest();
                        DB.DeleteAllChests();
                        usingInfChests = false;
                        TShock.Utils.SaveWorld();
                        args.Player.SendSuccessMessage($"Converted {n} chest(s) back into the world. HorizonChests is now disabled.");
                    }
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError("[HorizonChests] " + ex);
                    args.Player.SendErrorMessage("Chest conversion failed. See the server console.");
                }
                finally
                {
                    lockChests = false;
                }
            });
        }

        private static int InnerConvChests()
        {
            int count = 0;
            for (int i = 0; i < Main.chest.Length; i++)
            {
                Chest c = Main.chest[i];
                if (c == null)
                    continue;

                var inf = new InfChest(-1, c.x, c.y, Main.worldID) { items = c.item };
                DB.AddChest(inf);
                Chest.RemoveChest(i);
                count++;
            }
            TShock.Utils.SaveWorld();
            return count;
        }

        private void PruneChests(CommandArgs args)
        {
            if (lockChests) { args.Player.SendErrorMessage("A chest operation is already running."); return; }
            if (!usingInfChests) { args.Player.SendErrorMessage("HorizonChests are currently disabled on this server."); return; }

            lockChests = true;
            args.Player.SendInfoMessage("Pruning empty chests...");

            Task.Run(() =>
            {
                try
                {
                    var removed = DB.DeleteEmptyChests();
                    foreach (var (x, y) in removed)
                    {
                        WorldGen.KillTile(x, y, false, false, true);
                        TSPlayer.All.SendTileSquareCentered(x, y, 3);
                    }
                    TShock.Utils.SaveWorld();
                    args.Player.SendSuccessMessage($"Pruned {removed.Count} empty chest(s).");
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError("[HorizonChests] " + ex);
                    args.Player.SendErrorMessage("Prune failed. See the server console.");
                }
                finally
                {
                    lockChests = false;
                }
            });
        }

        private void Transfer(CommandArgs args)
        {
            if (lockChests) { args.Player.SendErrorMessage("A chest operation is already running."); return; }
            if (args.Parameters.Count != 1 || (args.Parameters[0] != "1" && args.Parameters[0] != "2" && args.Parameters[0] != "3"))
            {
                args.Player.SendErrorMessage("Invalid syntax: /transferchests <1|2|3>  (1 / 2 = old InfiniteChests V1 / V2, 3 = InfiniteChestsV3)");
                return;
            }

            int version = int.Parse(args.Parameters[0]);
            bool v2 = version == 2;
            args.Player.SendInfoMessage("Transferring chests, please wait...");

            Task.Run(() =>
            {
                try
                {
                    int n = version == 3 ? DB.TransferV3() : (v2 ? DB.TransferV2() : DB.TransferV1());
                    args.Player.SendSuccessMessage($"Transferred {n} chest(s) from InfiniteChests {(version == 3 ? "V3" : "V" + version)}.");
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError("[HorizonChests] " + ex);
                    args.Player.SendErrorMessage("Transfer failed. See the server console.");
                }
            });
        }
    }
}