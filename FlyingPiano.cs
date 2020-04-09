//#define DEBUG
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ProtoBuf;
using Rust;
using System.Linq;
using Network;
using Oxide.Core.Libraries.Covalence;
using System;
using System.Globalization;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("FlyingPiano", "RFC1920", "1.0.1")]
    [Description("Fly a piano!")]
    // Thanks to Colon Blow for his fine work on GyroCopter, upon which this was originally based.
    class FlyingPiano : RustPlugin
    {
        #region Load
        static LayerMask layerMask;
        BaseEntity newPiano;

        static Dictionary<ulong, PlayerPianoData> pianoplayer = new Dictionary<ulong, PlayerPianoData>();
        static List<ulong> pilotslist = new List<ulong>();

        // SignArtist plugin
        [PluginReference]
        Plugin SignArtist;

        public class PlayerPianoData
        {
            public BasePlayer player;
            public int pianocount;
        }

        void Init()
        {
            LoadVariables();
            layerMask = LayerMask.GetMask("Terrain", "World", "Construction", "Deployed");

            AddCovalenceCommand("fp", "cmdPianoBuild");
            AddCovalenceCommand("fpc", "cmdPianoCount");
            AddCovalenceCommand("fpd", "cmdPianoDestroy");
            AddCovalenceCommand("fpg", "cmdPianoGiveChat");
            AddCovalenceCommand("fphelp", "cmdPianoHelp");

            permission.RegisterPermission("flyingpiano.use", this);
            permission.RegisterPermission("flyingpiano.vip", this);
            permission.RegisterPermission("flyingpiano.admin", this);
            permission.RegisterPermission("flyingpiano.unlimited", this);

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["helptext1"] = "Flying Piano instructions:",
                ["helptext2"] = "  type /fp to spawn a Flying Piano",
                ["helptext3"] = "  type /fpd to destroy your flyingpiano.",
                ["helptext4"] = "  type /fpc to show a count of your pianos",
                ["notauthorized"] = "You don't have permission to do that !!",
                ["notfound"] = "Could not locate a piano.  You must be within {0} meters for this!!",
                ["notflyingpiano"] = "You are not piloting a flying piano !!",
                ["maxpianos"] = "You have reached the maximum allowed pianos",
                ["landingpiano"] = "Piano landing sequence started !!",
                ["risingpiano"] = "Piano takeoff sequence started !!",
                ["pianolocked"] = "You must unlock the Piano first !!",
                ["pianospawned"] = "Flying Piano spawned!  Don't forget to lock it !!",
                ["pianodestroyed"] = "Flying Piano destroyed !!",
                ["pianofuel"] = "You will need fuel to fly.  Do not start without fuel !!",
                ["pianonofuel"] = "You have been granted unlimited fly time, no fuel required !!",
                ["nofuel"] = "You're out of fuel !!",
                ["noplayer"] = "Unable to find player {0}!",
                ["gaveplayer"] = "Gave piano to player {0}!",
                ["lowfuel"] = "You're low on fuel !!",
                ["nopianos"] = "You have no Pianos",
                ["currpianos"] = "Current Pianos : {0}",
                ["giveusage"] = "You need to supply a valid SteamId."
            }, this);
        }

        bool isAllowed(BasePlayer player, string perm) => permission.UserHasPermission(player.UserIDString, perm);
        private bool HasPermission(ConsoleSystem.Arg arg, string permname) => (arg.Connection.player as BasePlayer) == null ? true : permission.UserHasPermission((arg.Connection.player as BasePlayer).UserIDString, permname);

        private static HashSet<BasePlayer> FindPlayers(string nameOrIdOrIp)
        {
            var players = new HashSet<BasePlayer>();
            if (string.IsNullOrEmpty(nameOrIdOrIp)) return players;
            foreach (var activePlayer in BasePlayer.activePlayerList)
            {
                if (activePlayer.UserIDString.Equals(nameOrIdOrIp))
                    players.Add(activePlayer);
                else if (!string.IsNullOrEmpty(activePlayer.displayName) && activePlayer.displayName.Contains(nameOrIdOrIp, CompareOptions.IgnoreCase))
                    players.Add(activePlayer);
                else if (activePlayer.net?.connection != null && activePlayer.net.connection.ipaddress.Equals(nameOrIdOrIp))
                    players.Add(activePlayer);
            }
            foreach (var sleepingPlayer in BasePlayer.sleepingPlayerList)
            {
                if (sleepingPlayer.UserIDString.Equals(nameOrIdOrIp))
                    players.Add(sleepingPlayer);
                else if (!string.IsNullOrEmpty(sleepingPlayer.displayName) && sleepingPlayer.displayName.Contains(nameOrIdOrIp, CompareOptions.IgnoreCase))
                    players.Add(sleepingPlayer);
            }
            return players;
        }
        #endregion

        #region Configuration
        bool UseMaxPianoChecks = true;
        bool playemptysound = true;
        public int maxpianos = 1;
        public int vipmaxpianos = 2;

        static float MinAltitude = 2f;
        static float MinDistance = 10f;

        static float NormalSpeed = 12f;
        static float SprintSpeed = 25f;
        static bool requirefuel = true;
        static bool doublefuel = false;

        //bool Changed = false;

        protected override void LoadDefaultConfig()
        {
#if DEBUG
            Puts("Creating a new config file...");
#endif
            Config.Clear();
            LoadVariables();
        }

        private void LoadConfigVariables()
        {
            CheckCfgFloat("Minimum Flight Altitude : ", ref MinAltitude);
            CheckCfgFloat("Minimum Distance for FPD: ", ref MinDistance);
            CheckCfgFloat("Speed - Normal Flight Speed is : ", ref NormalSpeed);
            CheckCfgFloat("Speed - Sprint Flight Speed is : ", ref SprintSpeed);

            CheckCfg("Deploy - Enable limited FlyingPianos per person : ", ref UseMaxPianoChecks);
            CheckCfg("Deploy - Limit of Pianos players can build : ", ref maxpianos);
            CheckCfg("Deploy - Limit of Pianos VIP players can build : ", ref vipmaxpianos);
            CheckCfg("Require Fuel to Operate : ", ref requirefuel);
            CheckCfg("Play low fuel sound : ", ref playemptysound);
            CheckCfg("Double Fuel Consumption: ", ref doublefuel);
        }

        private void LoadVariables()
        {
            LoadConfigVariables();
            SaveConfig();
        }

        private void CheckCfg<T>(string Key, ref T var)
        {
            if(Config[Key] is T)
            {
                var = (T)Config[Key];
            }
            else
            {
                Config[Key] = var;
            }
        }

        private void CheckCfgFloat(string Key, ref float var)
        {
            if(Config[Key] != null)
            {
                var = Convert.ToSingle(Config[Key]);
            }
            else
            {
                Config[Key] = var;
            }
        }

        object GetConfig(string menu, string datavalue, object defaultValue)
        {
            var data = Config[menu] as Dictionary<string, object>;
            if(data == null)
            {
                data = new Dictionary<string, object>();
                Config[menu] = data;
                //Changed = true;
            }

            object value;
            if(!data.TryGetValue(datavalue, out value))
            {
                value = defaultValue;
                data[datavalue] = value;
                //Changed = true;
            }
            return value;
        }
        #endregion

        #region Message
        private string _(string msgId, BasePlayer player, params object[] args)
        {
            var msg = lang.GetMessage(msgId, this, player?.UserIDString);
            return args.Length > 0 ? string.Format(msg, args) : msg;
        }

        private void PrintMsgL(BasePlayer player, string msgId, params object[] args)
        {
            if(player == null) return;
            PrintMsg(player, _(msgId, player, args));
        }

        private void PrintMsg(BasePlayer player, string msg)
        {
            if(player == null) return;
            SendReply(player, $"{msg}");
        }
        #endregion

        #region Chat Commands
        [Command("fp"), Permission("flyingpiano.use")]
        void cmdPianoBuild(IPlayer iplayer, string command, string[] args)
        {
            bool vip = false;
            var player = iplayer.Object as BasePlayer;
            if(!iplayer.HasPermission("flyingpiano.use")) { PrintMsgL(player, "notauthorized"); return; }
            if(iplayer.HasPermission("flyingpiano.vip"))
            {
                vip = true;
            }
            if(PianoLimitReached(player, vip)) { PrintMsgL(player, "maxpianos"); return; }
            AddPiano(player, player.transform.position);
        }

        [Command("fpg"), Permission("flyingpiano.admin")]
        void cmdPianoGiveChat(IPlayer iplayer, string command, string[] args)
        {
            var player = iplayer.Object as BasePlayer;
            if(args.Length == 0)
            {
                PrintMsgL(player, "giveusage");
                return;
            }
            bool vip = false;
            string pname = args[0] == null ? null : args[0];

            if(!iplayer.HasPermission("flyingpiano.admin")) { PrintMsgL(player, "notauthorized"); return; }
            if(pname == null) { PrintMsgL(player, "noplayer", "NAME_OR_ID"); return; }

            BasePlayer Bplayer = BasePlayer.Find(pname);
            if(Bplayer == null)
            {
                PrintMsgL(player, "noplayer", pname);
                return;
            }

            var Iplayer = Bplayer.IPlayer;
            if(Iplayer.HasPermission("flyingpiano.vip"))
            {
                vip = true;
            }
            if(PianoLimitReached(Bplayer, vip)) { PrintMsgL(player, "maxpianos"); return; }
            AddPiano(Bplayer, Bplayer.transform.position);
            PrintMsgL(player, "gaveplayer", pname);
        }

        [ConsoleCommand("fpgive")]
        void cmdPianoGive(ConsoleSystem.Arg arg)
        {
            if(arg.IsRcon)
            {
                if(arg.Args == null)
                {
                    Puts("You need to supply a valid SteamId.");
                    return;
                }
            }
            else if(!HasPermission(arg, "flyingpiano.admin"))
            {
                SendReply(arg, _("notauthorized", arg.Connection.player as BasePlayer));
                return;
            }
            else if(arg.Args == null)
            {
                SendReply(arg, _("giveusage", arg.Connection.player as BasePlayer));
                return;
            }

            bool vip = false;
            string pname = arg.GetString(0);

            if(pname.Length < 1) { Puts("Player name or id cannot be null"); return; }

            BasePlayer Bplayer = BasePlayer.Find(pname);
            if(Bplayer == null) { Puts($"Unable to find player '{pname}'"); return; }

            var Iplayer = Bplayer.IPlayer;
            if(Iplayer.HasPermission("flyingpiano.vip")) { vip = true; }
            if(PianoLimitReached(Bplayer, vip))
            {
                Puts($"Player '{pname}' has reached maxpianos"); return;
            }
            AddPiano(Bplayer, Bplayer.transform.position);
            Puts($"Gave piano to '{Bplayer.displayName}'");
        }

        [Command("fpc"), Permission("flyingpiano.use")]
        void cmdPianoCount(IPlayer iplayer, string command, string[] args)
        {
            var player = iplayer.Object as BasePlayer;
            if(!iplayer.HasPermission("flyingpiano.use")) { PrintMsgL(player, "notauthorized"); return; }
            if(!pianoplayer.ContainsKey(player.userID))
            {
                PrintMsgL(player, "nopianos");
                return;
            }
            string ccount = pianoplayer[player.userID].pianocount.ToString();
#if DEBUG
            Puts("PianoCount: " + ccount);
#endif
            PrintMsgL(player, "currpianos", ccount);
        }

        [Command("fpd"), Permission("flyingpiano.use")]
        void cmdPianoDestroy(IPlayer iplayer, string command, string[] args)
        {
            var player = iplayer.Object as BasePlayer;
            if(!iplayer.HasPermission("flyingpiano.use")) { PrintMsgL(player, "notauthorized"); return; }

            string target = null;
            if(args.Length > 0)
            {
                target = args[0];
            }
            if(iplayer.HasPermission("flyingpiano.admin") && target != null)
            {
                if(target == "all")
                {
                    DestroyAllPianos(player);
                    return;
                }
                var players = FindPlayers(target);
                if (players.Count <= 0)
                {
                    PrintMsgL(player, "PlayerNotFound", target);
                    return;
                }
                if (players.Count > 1)
                {
                    PrintMsgL(player, "MultiplePlayers", target, string.Join(", ", players.Select(p => p.displayName).ToArray()));
                    return;
                }
                var targetPlayer = players.First();
                RemovePiano(targetPlayer);
                DestroyRemotePiano(targetPlayer);
            }
            else
            {
                RemovePiano(player);
                DestroyLocalPiano(player);
            }
        }

        [Command("fphelp"), Permission("flyingpiano.use")]
        void cmdPianoHelp(IPlayer iplayer, string command, string[] args)
        {
            var player = iplayer.Object as BasePlayer;
            if(!iplayer.HasPermission("flyingpiano.use")) { PrintMsgL(player, "notauthorized"); return; }
            PrintMsgL(player, "helptext1");
            PrintMsgL(player, "helptext2");
            PrintMsgL(player, "helptext3");
            PrintMsgL(player, "helptext4");
        }
        #endregion

        #region Hooks
        // This is how we take off or land the piano!
        object OnOvenToggle(BaseOven oven, BasePlayer player)
        {
            bool rtrn = false; // Must match other plugins with this call to avoid conflicts. QuickSmelt uses false

            PianoEntity activepiano;

            try
            {
                activepiano = player.GetMounted().GetComponentInParent<PianoEntity>() ?? null;
                if(activepiano == null)
                {
                    return null;
                    //oven.StopCooking();
                    //return rtrn;
                }
            }
            catch
            {
                return null;
            }

            if(activepiano.pianolock != null && activepiano.pianolock.IsLocked()) { PrintMsgL(player, "pianolocked"); return rtrn; }
            if(!player.isMounted) return rtrn; // player offline, does not mean ismounted on piano

            if(player.GetMounted() != activepiano.entity) return rtrn; // online player not in seat on piano
#if DEBUG
            Puts("OnOvenToggle: Player cycled lantern!");
#endif
            if(oven.IsOn())
            {
                oven.StopCooking();
            }
            else
            {
                oven.StartCooking();
            }
            if(!activepiano.FuelCheck())
            {
                if(activepiano.needfuel)
                {
                    PrintMsgL(player, "nofuel");
                    PrintMsgL(player, "landingpiano");
                    activepiano.engineon = false;
                }
            }
            var ison = activepiano.engineon;
            if(ison) { activepiano.islanding = true; PrintMsgL(player, "landingpiano"); return null; }
            if(!ison) { AddPlayerToPilotsList(player); activepiano.engineon = true; return null; }

            return rtrn;
        }

        // Check for piano lantern fuel
        void OnConsumeFuel(BaseOven oven, Item fuel, ItemModBurnable burnable)
        {
            // Only work on lanterns
            if(oven.ShortPrefabName != "lantern.deployed") return;
            int dbl = doublefuel ? 4 : 2;

            BaseEntity lantern = oven as BaseEntity;
            // Only work on lanterns attached to a Piano
            var activepiano = lantern.GetComponentInParent<PianoEntity>() ?? null;
            if(activepiano == null) return;
#if DEBUG
            Puts("OnConsumeFuel: found a piano lantern!");
#endif
            if(activepiano.needfuel)
            {
#if DEBUG
                Puts("OnConsumeFuel: piano requires fuel!");
#endif
            }
            else
            {
#if DEBUG
                Puts("OnConsumeFuel: piano does not require fuel!");
#endif
                fuel.amount++; // Required to keep it from decrementing
                return;
            }
            BasePlayer player = activepiano.GetComponent<BaseMountable>().GetMounted() as BasePlayer;
            if(!player) return;
#if DEBUG
            Puts("OnConsumeFuel: checking fuel level...");
#endif
            // Before it drops to 1 (3 for doublefuel) AFTER this hook call is complete, warn them that the fuel is low (1) - ikr
            if(fuel.amount == dbl)
            {
#if DEBUG
                Puts("OnConsumeFuel: sending low fuel warning...");
#endif
                if(playemptysound)
                {
                    Effect.server.Run("assets/bundled/prefabs/fx/well/pump_down.prefab", player.transform.position);
                }
                PrintMsgL(player, "lowfuel");
            }

            if(doublefuel)
            {
                fuel.amount--;
            }

            if(fuel.amount == 0)
            {
#if DEBUG
                Puts("OnConsumeFuel: out of fuel.");
#endif
                PrintMsgL(player, "lowfuel");
                var ison = activepiano.engineon;
                if(ison)
                {
                    activepiano.islanding = true;
                    activepiano.engineon = false;
                    PrintMsgL(player, "landingpiano");
                    OnOvenToggle(oven, player);
                    return;
                }
            }
        }

        // To skip cycling our lantern (thanks, k11l0u)
        private object OnNightLanternToggle(BaseEntity entity, bool status)
        {
            // Only work on lanterns
            if(entity.ShortPrefabName != "lantern.deployed") return null;
#if DEBUG
            Puts("OnNightLanternToggle: Called on a lantern.  Checking for piano...");
#endif

            // Only work on lanterns attached to a Piano
            var activepiano = entity.GetComponentInParent<PianoEntity>() ?? null;
            if(activepiano != null)
            {
#if DEBUG
                Puts("OnNightLanternToggle: Do not cycle this lantern!");
#endif
                return true;
            }
#if DEBUG
            Puts("OnNightLanternToggle: Not a piano lantern.");
#endif
            return null;
        }
        #endregion

        #region Primary
        private void AddPiano(BasePlayer player, Vector3 location)
        {
            if(player == null && location == null) return;
            if(location == null && player != null) location = player.transform.position;
            Vector3 spawnpos = new Vector3();

            // Set initial default for fuel requirement based on config
            bool needfuel = requirefuel;
            if(isAllowed(player, "flyingpiano.unlimited"))
            {
                // User granted unlimited fly time without fuel
                needfuel = false;
#if DEBUG
                Puts("AddPiano: Unlimited fuel granted!");
#endif
            }

            if(needfuel)
            {
                // Don't put them on the piano since they need to fuel up first
                spawnpos = player.transform.position + -player.transform.forward * 2f;
            }
            else
            {
                // Spawn at point of player
                spawnpos = new Vector3(location.x, location.y + 0.5f, location.z);
            }

            string staticprefab = "assets/prefabs/instruments/piano/piano.deployed.prefab";
            newPiano = GameManager.server.CreateEntity(staticprefab, spawnpos, new Quaternion(), true);
            newPiano.name = "FlyingPiano";
            var chairmount = newPiano.GetComponent<BaseMountable>();
            chairmount.isMobile = true;
            newPiano.enableSaving = false;
            newPiano.OwnerID = player.userID;
            newPiano.Spawn();
            var piano = newPiano.gameObject.AddComponent<PianoEntity>();
            piano.needfuel = needfuel;
            // Unlock the tank if they need fuel.
            piano.lantern1.SetFlag(BaseEntity.Flags.Locked, !needfuel);
            if(needfuel)
            {
#if DEBUG
                // We have to set this after the spawn.
                Puts("AddPiano: Emptying the tank!");
#endif
                piano.SetFuel(0);
            }

            AddPlayerID(player.userID);

            if(chairmount != null && player != null)
            {
                PrintMsgL(player, "pianospawned");
                if(piano.needfuel)
                {
                    PrintMsgL(player, "pianofuel");
                }
                else
                {
                    // Put them in the chair.  They will still need to unlock it.
                    PrintMsgL(player, "pianonofuel");
                    chairmount.MountPlayer(player);
                }
                return;
            }
        }

        public bool PilotListContainsPlayer(BasePlayer player)
        {
            if(pilotslist.Contains(player.userID)) return true;
            return false;
        }

        void AddPlayerToPilotsList(BasePlayer player)
        {
            if(PilotListContainsPlayer(player)) return;
            pilotslist.Add(player.userID);
        }

        public void RemovePlayerFromPilotsList(BasePlayer player)
        {
            if(PilotListContainsPlayer(player))
            {
                pilotslist.Remove(player.userID);
                return;
            }
        }

        void DestroyLocalPiano(BasePlayer player)
        {
            if(player == null) return;
            List<BaseEntity> pianolist = new List<BaseEntity>();
            Vis.Entities<BaseEntity>(player.transform.position, MinDistance, pianolist);
            bool foundpiano = false;

            foreach(BaseEntity p in pianolist)
            {
                var foundent = p.GetComponentInParent<PianoEntity>() ?? null;
                if(foundent != null)
                {
                    foundpiano = true;
                    if(foundent.ownerid != player.userID) return;
                    foundent.entity.Kill(BaseNetworkable.DestroyMode.Gib);
                    PrintMsgL(player, "pianodestroyed");
                }
            }
            if(!foundpiano)
            {
                PrintMsgL(player, "notfound", MinDistance.ToString());
            }
        }

        void DestroyAllPianos(BasePlayer player)
        {
            List<BaseEntity> pianolist = new List<BaseEntity>();
            Vis.Entities<BaseEntity>(new Vector3(0,0,0), 3500f, pianolist);
            bool foundpiano = false;

            foreach(BaseEntity p in pianolist)
            {
                var foundent = p.GetComponentInParent<PianoEntity>() ?? null;
                if(foundent != null)
                {
                    foundpiano = true;
                    foundent.entity.Kill(BaseNetworkable.DestroyMode.Gib);
                    PrintMsgL(player, "pianodestroyed");
                }
            }
            if(!foundpiano)
            {
                PrintMsgL(player, "notfound", MinDistance.ToString());
            }
        }

        void DestroyRemotePiano(BasePlayer player)
        {
            if(player == null) return;
            List<BaseEntity> pianolist = new List<BaseEntity>();
            Vis.Entities<BaseEntity>(new Vector3(0,0,0), 3500f, pianolist);
            bool foundpiano = false;

            foreach(BaseEntity p in pianolist)
            {
                var foundent = p.GetComponentInParent<PianoEntity>() ?? null;
                if(foundent != null)
                {
                    foundpiano = true;
                    if(foundent.ownerid != player.userID) return;
                    foundent.entity.Kill(BaseNetworkable.DestroyMode.Gib);
                    PrintMsgL(player, "pianodestroyed");
                }
            }
            if(!foundpiano)
            {
                PrintMsgL(player, "notfound", MinDistance.ToString());
            }
        }

        void OnPlayerInput(BasePlayer player, InputState input)
        {
            if(player == null || input == null) return;
            if(!player.isMounted) return;
            var activepiano = player.GetMounted().GetComponentInParent<PianoEntity>() ?? null;
            if(activepiano == null) return;
            if(player.GetMounted() != activepiano.entity) return;
            if(input != null)
            {
                activepiano.PianoInput(input, player);
            }
            return;
        }

        void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo hitInfo)
        {
            if(entity == null || hitInfo == null) return;
            var ispiano = entity.GetComponentInParent<PianoEntity>() ?? null;
            if(ispiano != null) hitInfo.damageTypes.ScaleAll(0);
            return;
        }

        object OnEntityGroundMissing(BaseEntity entity)
        {
            var ispiano = entity.GetComponentInParent<PianoEntity>() ?? null;
            if(ispiano != null) return false;
            return null;
        }

        bool PianoLimitReached(BasePlayer player, bool vip=false)
        {
            if(UseMaxPianoChecks)
            {
                if(pianoplayer.ContainsKey(player.userID))
                {
                    var currentcount = pianoplayer[player.userID].pianocount;
                    int maxallowed = maxpianos;
                    if(vip)
                    {
                        maxallowed = vipmaxpianos;
                    }
                    if(currentcount >= maxallowed) return true;
                }
            }
            return false;
        }

        object CanDismountEntity(BasePlayer player, BaseMountable entity)
        {
            if(player == null) return null;
            if(PilotListContainsPlayer(player)) return false;
            return null;
        }

        void OnEntityMounted(BaseMountable mountable, BasePlayer player)
        {
            var activepiano = mountable.GetComponentInParent<PianoEntity>() ?? null;
            if(activepiano != null)
            {
#if DEBUG
                Puts("OnEntityMounted: player mounted copter!");
#endif
                if(mountable.GetComponent<BaseEntity>() != activepiano.entity) return;
                activepiano.lantern1.SetFlag(BaseEntity.Flags.On, false);
            }
        }

        void OnEntityDismounted(BaseMountable mountable, BasePlayer player)
        {
            var activepiano = mountable.GetComponentInParent<PianoEntity>() ?? null;
            if(activepiano != null)
            {
#if DEBUG
                Puts("OnEntityMounted: player dismounted copter!");
#endif
                if(mountable.GetComponent<BaseEntity>() != activepiano.entity) return;
            }
        }

        private object CanLootEntity(BasePlayer player, StorageContainer container)
        {
            if(container == null || player == null) return null;
            var ispiano = container.GetComponentInParent<PianoEntity>() ?? null;
            if(ispiano != null)
            {
                if(ispiano.pianolock != null && ispiano.pianolock.IsLocked()) return false;
            }
            return null;
        }

        private object CanPickupEntity(BasePlayer player, BaseCombatEntity entity)
        {
            if(entity == null || player == null) return null;

            BaseEntity myent = entity as BaseEntity;
            string myparent = null;
            try
            {
                myparent = myent.GetParentEntity().name;
            }
            catch {}

            if(myparent == "FlyingPiano" || myent.name == "FlyingPiano")
            {
#if DEBUG
                if(myent.name == "FlyingPiano")
                {
                    Puts("CanPickupEntity: player trying to pickup the piano!");
                }
                else if(myparent == "FlyingPiano")
                {
                    string entity_name = myent.LookupPrefab().name;
                    Puts($"CanPickupEntity: player trying to remove {entity_name} from a piano!");
                }
#endif
                PrintMsgL(player, "notauthorized");
                return false;
            }
            return null;
        }

        private object CanPickupLock(BasePlayer player, BaseLock baseLock)
        {
            if(baseLock == null || player == null) return null;

            BaseEntity myent = baseLock as BaseEntity;
            string myparent = null;
            try
            {
                myparent = myent.GetParentEntity().name;
            }
            catch {}

            if(myparent == "FlyingPiano")
            {
#if DEBUG
                Puts("CanPickupLock: player trying to remove lock from a piano!");
#endif
                PrintMsgL(player, "notauthorized");
                return false;
            }
            return null;
        }

        void AddPlayerID(ulong ownerid)
        {
            if(!pianoplayer.ContainsKey(ownerid))
            {
                pianoplayer.Add(ownerid, new PlayerPianoData
                {
                    pianocount = 1,
                });
                return;
            }
            pianoplayer[ownerid].pianocount = pianoplayer[ownerid].pianocount + 1;
        }

        void RemovePlayerID(ulong ownerid)
        {
            if(pianoplayer.ContainsKey(ownerid)) pianoplayer[ownerid].pianocount = pianoplayer[ownerid].pianocount - 1;
            return;
        }

        object OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            RemovePlayerFromPilotsList(player);
            return null;
        }

        void RemovePiano(BasePlayer player)
        {
            RemovePlayerFromPilotsList(player);
            return;
        }

        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            RemovePiano(player);
        }

        void OnPlayerRespawned(BasePlayer player)
        {
            RemovePiano(player);
        }

        void DestroyAll<T>()
        {
            var objects = GameObject.FindObjectsOfType(typeof(T));
            if(objects != null)
            {
                foreach(var gameObj in objects)
                {
                    GameObject.Destroy(gameObj);
                }
            }
        }

        void Unload()
        {
            DestroyAll<PianoEntity>();
        }
        #endregion

        #region Piano Antihack check
        static List<BasePlayer> pianoantihack = new List<BasePlayer>();

        object OnPlayerViolation(BasePlayer player, AntiHackType type, float amount)
        {
            if(player == null) return null;
            if(pianoantihack.Contains(player)) return false;
            return null;
        }
        #endregion

        #region Piano Entity
        class PianoEntity : BaseEntity
        {
            public BaseEntity entity;
            public BasePlayer player;
            public BaseEntity piano1;
            public BaseEntity lantern1;
            public BaseEntity pianolock;

            public string entname = "FlyingPiano";

            Quaternion entityrot;
            Vector3 entitypos;

            public bool moveforward;
            public bool movebackward;
            public bool moveup;
            public bool movedown;
            public bool rotright;
            public bool rotleft;
            public bool sprinting;
            public bool islanding;
            public bool mounted;

            public bool engineon;
            public bool hasFuel;
            public bool needfuel;

            public ulong skinid = 1;
            public ulong ownerid;
            //int count;
            float minaltitude;
            FlyingPiano instance;
            public bool throttleup;
            float sprintspeed;
            float normalspeed;
            //bool isenabled = true;
            SphereCollider sphereCollider;

            string prefablamp = "assets/prefabs/deployable/lantern/lantern.deployed.prefab";
            string prefablock = "assets/prefabs/locks/keypad/lock.code.prefab";

            void Awake()
            {
                entity = GetComponentInParent<BaseEntity>();
                entityrot = Quaternion.identity;
                entitypos = entity.transform.position;
                minaltitude = MinAltitude;
                instance = new FlyingPiano();
                ownerid = entity.OwnerID;
                gameObject.name = "FlyingPiano";

                engineon = false;
                hasFuel = false;
                //needfuel = requirefuel;
                if(!needfuel)
                {
                    hasFuel = true;
                }
                moveforward = false;
                movebackward = false;
                moveup = false;
                movedown = false;
                rotright = false;
                rotleft = false;
                sprinting = false;
                islanding = false;
                mounted = false;
                throttleup = false;
                sprintspeed = SprintSpeed;
                normalspeed = NormalSpeed;
                //isenabled = false;
                SpawnPiano();
                lantern1.OwnerID = entity.OwnerID;

                sphereCollider = entity.gameObject.AddComponent<SphereCollider>();
                sphereCollider.gameObject.layer = (int)Layer.Reserved1;
                sphereCollider.isTrigger = true;
                sphereCollider.radius = 6f;
            }

            BaseEntity SpawnPart(string prefab, BaseEntity entitypart, bool setactive, int eulangx, int eulangy, int eulangz, float locposx, float locposy, float locposz, BaseEntity parent, ulong skinid)
            {
                entitypart = new BaseEntity();
                entitypart = GameManager.server.CreateEntity(prefab, entitypos, entityrot, setactive);
                entitypart.transform.localEulerAngles = new Vector3(eulangx, eulangy, eulangz);
                entitypart.transform.localPosition = new Vector3(locposx, locposy, locposz);

                entitypart.SetParent(parent, 0);
                entitypart.skinID = skinid;
                entitypart?.Spawn();
                SpawnRefresh(entitypart);
                return entitypart;
            }

            void SpawnRefresh(BaseEntity entity)
            {
                var hasstab = entity.GetComponent<StabilityEntity>() ?? null;
                if(hasstab != null)
                {
                    hasstab.grounded = true;
                }
                var hasmount = entity.GetComponent<BaseMountable>() ?? null;
                if(hasmount != null)
                {
                    hasmount.isMobile = true;
                }
            }

            public void SetFuel(int amount = 0)
            {
                BaseOven lanternCont = lantern1 as BaseOven;
                ItemContainer container1 = lanternCont.inventory;

                if(amount == 0)
                {
                    while(container1.itemList.Count > 0)
                    {
                        var item = container1.itemList[0];
                        item.RemoveFromContainer();
                        item.Remove(0f);
                    }
                }
                else
                {
                    Item addfuel = ItemManager.CreateByItemID(-946369541, amount);
                    container1.itemList.Add(addfuel);
                    addfuel.parent = container1;
                    addfuel.MarkDirty();
                }
            }

            public void SpawnPiano()
            {
                lantern1 = SpawnPart(prefablamp, lantern1, true, 0, 0, 0, 0f, 0.83f, 0f, entity, 1);
                lantern1.SetFlag(BaseEntity.Flags.On, false);
                pianolock = SpawnPart(prefablock, pianolock, true, 0, 0, 0, 0.6f, 0.8f, 0.1f, entity, 1);

                if(needfuel)
                {
                    // Empty tank
                    SetFuel(0);
                }
                else
                {
                    // Cannot be looted
                    lantern1.SetFlag(BaseEntity.Flags.Locked, true);
                    // Add some fuel (1 lgf) so it lights up anyway.  It should always stay at 1.
                    SetFuel(1);
                }
            }

            private void OnTriggerEnter(Collider col)
            {
                var target = col.GetComponentInParent<BasePlayer>();
                if(target != null)
                {
                    pianoantihack.Add(target);
                }
            }

            private void OnTriggerExit(Collider col)
            {
                var target = col.GetComponentInParent<BasePlayer>();
                if(target != null)
                {
                    pianoantihack.Remove(target);
                }
            }

            BasePlayer GetPilot()
            {
                player = entity.GetComponent<BaseMountable>().GetMounted() as BasePlayer;
                return player;
            }

            public void PianoInput(InputState input, BasePlayer player)
            {
                if(input == null || player == null) return;
                if(input.WasJustPressed(BUTTON.FORWARD)) moveforward = true;
                if(input.WasJustReleased(BUTTON.FORWARD)) moveforward = false;
                if(input.WasJustPressed(BUTTON.BACKWARD)) movebackward = true;
                if(input.WasJustReleased(BUTTON.BACKWARD)) movebackward = false;
                if(input.WasJustPressed(BUTTON.RIGHT)) rotright = true;
                if(input.WasJustReleased(BUTTON.RIGHT)) rotright = false;
                if(input.WasJustPressed(BUTTON.LEFT)) rotleft = true;
                if(input.WasJustReleased(BUTTON.LEFT)) rotleft = false;
                if(input.IsDown(BUTTON.SPRINT)) throttleup = true;
                if(input.WasJustReleased(BUTTON.SPRINT)) throttleup = false;
                if(input.WasJustPressed(BUTTON.JUMP)) moveup = true;
                if(input.WasJustReleased(BUTTON.JUMP)) moveup = false;
                if(input.WasJustPressed(BUTTON.DUCK)) movedown = true;
                if(input.WasJustReleased(BUTTON.DUCK)) movedown = false;
            }

            public bool FuelCheck()
            {
                if(!needfuel)
                {
                    return true;
                }
                BaseOven lantern = lantern1 as BaseOven;
                Item slot = lantern.inventory.GetSlot(0);
                if(slot == null)
                {
                    islanding = true;
                    hasFuel = false;
                    return false;
                }
                else
                {
                    hasFuel = true;
                    return true;
                }
            }

            void FixedUpdate()
            {
                if(engineon)
                {
                    if(!GetPilot()) islanding = true;
                    var currentspeed = normalspeed;
                    if(throttleup) { currentspeed = sprintspeed; }

                    // This is a little weird.  Fortunately, some of the hooks determine fuel status...
                    if(!hasFuel)
                    {
                        if(needfuel)
                        {
                            islanding = false;
                            engineon = false;
                            return;
                        }
                    }
                    if(islanding)
                    {
#if DEBUG
                        Interface.Oxide.LogWarning($"Trying to land, current pos = {entity.transform.position}");
#endif
                        entity.transform.localPosition += (transform.up * -2.5f) * Time.deltaTime;
                        RaycastHit hit;
                        if(Physics.Raycast(new Ray(entity.transform.position, Vector3.down), out hit, 0.6f, layerMask))
                        {
#if DEBUG
                            Interface.Oxide.LogWarning($"Landing, current pos = {entity.transform.position}");
#endif
                            islanding = false;
                            engineon = false;
                            if(pilotslist.Contains(player.userID))
                            {
                                pilotslist.Remove(player.userID);
                            }
                        }
                        ResetMovement();
                        ServerMgr.Instance.StartCoroutine(RefreshTrain());
                        return;
                    }

                    if(Physics.Raycast(new Ray(entity.transform.position, Vector3.down), minaltitude, layerMask))
                    {
                        entity.transform.localPosition += transform.up * minaltitude * Time.deltaTime;
                        ServerMgr.Instance.StartCoroutine(RefreshTrain());
                        return;
                    }

                    if(rotright) entity.transform.eulerAngles += new Vector3(0, 2, 0);
                    else if(rotleft) entity.transform.eulerAngles += new Vector3(0, -2, 0);

                    if(moveforward) entity.transform.localPosition += ((transform.forward * currentspeed) * Time.deltaTime);
                    else if(movebackward) entity.transform.localPosition = entity.transform.localPosition - ((transform.forward * currentspeed) * Time.deltaTime);

                    if(moveup) entity.transform.localPosition += ((transform.up * currentspeed) * Time.deltaTime);
                    else if(movedown) entity.transform.localPosition += ((transform.up * -currentspeed) * Time.deltaTime);

                    ServerMgr.Instance.StartCoroutine(RefreshTrain());
                }
            }

            private IEnumerator RefreshTrain()
            {
                entity.transform.hasChanged = true;
                for(int i = 0; i < entity.children.Count; i++)
                {
                    entity.children[i].transform.hasChanged = true;
                    entity.children[i].SendNetworkUpdateImmediate();
                    entity.children[i].UpdateNetworkGroup();
                }
                entity.SendNetworkUpdateImmediate();
                entity.UpdateNetworkGroup();
                yield return new WaitForEndOfFrame();
            }

            void ResetMovement()
            {
                moveforward = false;
                movebackward = false;
                moveup = false;
                movedown = false;
                rotright = false;
                rotleft = false;
                throttleup = false;
            }

            public void OnDestroy()
            {
                if(pianoplayer.ContainsKey(ownerid)) pianoplayer[ownerid].pianocount = pianoplayer[ownerid].pianocount - 1;
                if(entity != null) { entity.Invoke("KillMessage", 0.1f); }
            }
        }
        #endregion
    }
}
