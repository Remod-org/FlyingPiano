#region License (GPL v3)
/*
    DESCRIPTION
    Copyright (c) RFC1920 <desolationoutpostpve@gmail.com>

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/
#endregion License Information (GPL v3)
using Oxide.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Rust;
using System.Linq;
using Oxide.Core.Libraries.Covalence;
using System.Globalization;
using Oxide.Core.Plugins;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("FlyingPiano", "RFC1920", "1.0.5")]
    [Description("Fly a piano!")]
    internal class FlyingPiano : RustPlugin
    {
        #region Load
        private ConfigData configData;
        private static LayerMask layerMask = LayerMask.GetMask("Construction", "Prevent Building", "Deployed", "Terrain", "Water");//, "World");
        //buildingMask = LayerMask.GetMask("Construction", "Prevent Building", "Deployed");//, "World");
        private static LayerMask buildingMask = LayerMask.GetMask("Construction", "Prevent Building", "Deployed", "World", "Terrain", "Tree", "Invisible", "Default");
        private static LayerMask strongerMask = LayerMask.GetMask("Construction", "Prevent Building", "Deployed", "World", "Default");

        private static Dictionary<ulong, PlayerPianoData> pianoplayer = new Dictionary<ulong, PlayerPianoData>();
        private static List<ulong> pilotslist = new List<ulong>();

        public static FlyingPiano Instance;

        [PluginReference]
        private readonly Plugin SignArtist;

        public class PlayerPianoData
        {
            public BasePlayer player;
            public int pianocount;
        }

        private void Init()
        {
            LoadConfigVariables();

            AddCovalenceCommand("fp", "cmdPianoBuild");
            AddCovalenceCommand("fpc", "cmdPianoCount");
            AddCovalenceCommand("fpd", "cmdPianoDestroy");
            AddCovalenceCommand("fpg", "cmdPianoGiveChat");
            AddCovalenceCommand("fphelp", "cmdPianoHelp");

            permission.RegisterPermission("flyingpiano.use", this);
            permission.RegisterPermission("flyingpiano.vip", this);
            permission.RegisterPermission("flyingpiano.admin", this);
            permission.RegisterPermission("flyingpiano.unlimited", this);

            Instance = this;
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["helptext1"] = "Flying Piano instructions:",
                ["helptext2"] = "  type /fp to spawn a Flying Piano",
                ["helptext3"] = "  type /fpd to destroy your flyingpiano.",
                ["helptext4"] = "  type /fpc to show a count of your pianos",
                ["notauthorized"] = "You don't have permission to do that !!",
                ["nospawnhere"] = "You cannot spawn a piano here !!",
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

        private bool isAllowed(BasePlayer player, string perm) => permission.UserHasPermission(player.UserIDString, perm);
        private bool HasPermission(ConsoleSystem.Arg arg, string permname) => !(arg.Connection.player is BasePlayer) || permission.UserHasPermission((arg.Connection.player as BasePlayer)?.UserIDString, permname);

        private static HashSet<BasePlayer> FindPlayers(string nameOrIdOrIp)
        {
            HashSet<BasePlayer> players = new HashSet<BasePlayer>();
            if (string.IsNullOrEmpty(nameOrIdOrIp)) return players;
            foreach (BasePlayer activePlayer in BasePlayer.activePlayerList)
            {
                if (activePlayer.UserIDString.Equals(nameOrIdOrIp))
                {
                    players.Add(activePlayer);
                }
                else if (!string.IsNullOrEmpty(activePlayer.displayName) && activePlayer.displayName.Contains(nameOrIdOrIp, CompareOptions.IgnoreCase))
                {
                    players.Add(activePlayer);
                }
                else if (activePlayer.net?.connection != null && activePlayer.net.connection.ipaddress.Equals(nameOrIdOrIp))
                {
                    players.Add(activePlayer);
                }
            }
            foreach (BasePlayer sleepingPlayer in BasePlayer.sleepingPlayerList)
            {
                if (sleepingPlayer.UserIDString.Equals(nameOrIdOrIp))
                {
                    players.Add(sleepingPlayer);
                }
                else if (!string.IsNullOrEmpty(sleepingPlayer.displayName) && sleepingPlayer.displayName.Contains(nameOrIdOrIp, CompareOptions.IgnoreCase))
                {
                    players.Add(sleepingPlayer);
                }
            }
            return players;
        }
        #endregion

        #region Configuration
        protected override void LoadDefaultConfig()
        {
            Puts("Creating new config file.");
            ConfigData config = new ConfigData
            {
                UseMaxPianoChecks = true,
                DoubleFuel = false,
                PlayEmptySound = false,
                RequireFuel = true,
                debug = false,
                MaxPianos = 1,
                VIPMaxPianos = 2,
                MinDistance = 10,
                MinAltitude = 5,
                NormalSpeed = 12,
                SprintSpeed = 25,
                Version = Version
            };
            SaveConfig(config);
        }

        private void LoadConfigVariables()
        {
            configData = Config.ReadObject<ConfigData>();
            configData.Version = Version;
            SaveConfig(configData);
        }

        private void SaveConfig(ConfigData config)
        {
            Config.WriteObject(config, true);
        }

        private class ConfigData
        {
            //public bool AllowDamage;

            [JsonProperty(PropertyName = "Deploy - Enable limited FlyingPianos per person : ")]
            public bool UseMaxPianoChecks;

            [JsonProperty(PropertyName = "Double Fuel Consumption: ")]
            public bool DoubleFuel;

            [JsonProperty(PropertyName = "Play low fuel sound : ")]
            public bool PlayEmptySound;

            [JsonProperty(PropertyName = "Require Fuel to Operate : ")]
            public bool RequireFuel;

            [JsonProperty(PropertyName = "Deploy - Limit of Pianos players can build : ")]
            public int MaxPianos;

            [JsonProperty(PropertyName = "Deploy - Limit of Pianos VIP players can build : ")]
            public int VIPMaxPianos;

            //public float InitialHealth;
            [JsonProperty(PropertyName = "Minimum Distance for FPD: ")]
            public float MinDistance;

            [JsonProperty(PropertyName = "Minimum Flight Altitude : ")]
            public float MinAltitude;

            [JsonProperty(PropertyName = "Speed - Normal Flight Speed is : ")]
            public float NormalSpeed;

            [JsonProperty(PropertyName = "Speed - Sprint Flight Speed is : ")]
            public float SprintSpeed;

            public bool debug;
            public VersionNumber Version;
        }
        #endregion

        #region Message
        private string _(string msgId, BasePlayer player, params object[] args)
        {
            string msg = lang.GetMessage(msgId, this, player?.UserIDString);
            return args.Length > 0 ? string.Format(msg, args) : msg;
        }

        private void PrintMsgL(BasePlayer player, string msgId, params object[] args)
        {
            if (player == null) return;
            PrintMsg(player, _(msgId, player, args));
        }

        private void PrintMsg(BasePlayer player, string msg)
        {
            if (player == null) return;
            SendReply(player, $"{msg}");
        }
        #endregion

        #region Chat Commands
        [Command("fp"), Permission("flyingpiano.use")]
        private void cmdPianoBuild(IPlayer iplayer, string command, string[] args)
        {
            bool vip = false;
            BasePlayer player = iplayer.Object as BasePlayer;
            if (!iplayer.HasPermission("flyingpiano.use")) { PrintMsgL(player, "notauthorized"); return; }
            if (iplayer.HasPermission("flyingpiano.vip"))
            {
                vip = true;
            }
            if (PianoLimitReached(player, vip)) { PrintMsgL(player, "maxpianos"); return; }
            AddPiano(player, player.transform.position);
        }

        [Command("fpg"), Permission("flyingpiano.admin")]
        private void cmdPianoGiveChat(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer player = iplayer.Object as BasePlayer;
            if (args.Length == 0)
            {
                PrintMsgL(player, "giveusage");
                return;
            }
            bool vip = false;
            string pname = args[0] ?? null;

            if (!iplayer.HasPermission("flyingpiano.admin")) { PrintMsgL(player, "notauthorized"); return; }
            if (pname == null) { PrintMsgL(player, "noplayer", "NAME_OR_ID"); return; }

            BasePlayer Bplayer = BasePlayer.Find(pname);
            if (Bplayer == null)
            {
                PrintMsgL(player, "noplayer", pname);
                return;
            }

            IPlayer Iplayer = Bplayer.IPlayer;
            if (Iplayer.HasPermission("flyingpiano.vip"))
            {
                vip = true;
            }
            if (PianoLimitReached(Bplayer, vip)) { PrintMsgL(player, "maxpianos"); return; }
            AddPiano(Bplayer, Bplayer.transform.position);
            PrintMsgL(player, "gaveplayer", pname);
        }

        [ConsoleCommand("fpgive")]
        private void cmdPianoGive(ConsoleSystem.Arg arg)
        {
            if (arg.IsRcon)
            {
                if (arg.Args == null)
                {
                    Puts("You need to supply a valid SteamId.");
                    return;
                }
            }
            else if (!HasPermission(arg, "flyingpiano.admin"))
            {
                SendReply(arg, _("notauthorized", arg.Connection.player as BasePlayer));
                return;
            }
            else if (arg.Args == null)
            {
                SendReply(arg, _("giveusage", arg.Connection.player as BasePlayer));
                return;
            }

            bool vip = false;
            string pname = arg.GetString(0);

            if (pname.Length < 1) { Puts("Player name or id cannot be null"); return; }

            BasePlayer Bplayer = BasePlayer.Find(pname);
            if (Bplayer == null) { Puts($"Unable to find player '{pname}'"); return; }

            IPlayer Iplayer = Bplayer.IPlayer;
            if (Iplayer.HasPermission("flyingpiano.vip")) { vip = true; }
            if (PianoLimitReached(Bplayer, vip))
            {
                Puts($"Player '{pname}' has reached maxpianos"); return;
            }
            AddPiano(Bplayer, Bplayer.transform.position);
            Puts($"Gave piano to '{Bplayer.displayName}'");
        }

        [Command("fpc"), Permission("flyingpiano.use")]
        private void cmdPianoCount(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer player = iplayer.Object as BasePlayer;
            if (!iplayer.HasPermission("flyingpiano.use")) { PrintMsgL(player, "notauthorized"); return; }
            if (!pianoplayer.ContainsKey(player.userID))
            {
                PrintMsgL(player, "nopianos");
                return;
            }
            string ccount = pianoplayer[player.userID].pianocount.ToString();
            if (configData.debug) Puts("PianoCount: " + ccount);
            PrintMsgL(player, "currpianos", ccount);
        }

        [Command("fpd"), Permission("flyingpiano.use")]
        private void cmdPianoDestroy(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer player = iplayer.Object as BasePlayer;
            if (!iplayer.HasPermission("flyingpiano.use")) { PrintMsgL(player, "notauthorized"); return; }

            string target = null;
            if (args.Length > 0)
            {
                target = args[0];
            }
            if (iplayer.HasPermission("flyingpiano.admin") && target != null)
            {
                if (target == "all")
                {
                    DestroyAllPianos(player);
                    return;
                }
                HashSet<BasePlayer> players = FindPlayers(target);
                if (players.Count == 0)
                {
                    PrintMsgL(player, "PlayerNotFound", target);
                    return;
                }
                if (players.Count > 1)
                {
                    PrintMsgL(player, "MultiplePlayers", target, string.Join(", ", players.Select(p => p.displayName).ToArray()));
                    return;
                }
                BasePlayer targetPlayer = players.First();
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
        private void cmdPianoHelp(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer player = iplayer.Object as BasePlayer;
            if (!iplayer.HasPermission("flyingpiano.use")) { PrintMsgL(player, "notauthorized"); return; }
            PrintMsgL(player, "helptext1");
            PrintMsgL(player, "helptext2");
            PrintMsgL(player, "helptext3");
            PrintMsgL(player, "helptext4");
        }
        #endregion

        #region Hooks
        // This is how we take off or land the piano!
        private object OnOvenToggle(BaseOven oven, BasePlayer player)
        {
            const bool rtrn = false; // Must match other plugins with this call to avoid conflicts. QuickSmelt uses false

            PianoEntity activepiano;

            try
            {
                activepiano = player.GetMounted().GetComponentInParent<PianoEntity>() ?? null;
                if (activepiano == null)
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

            if (activepiano.pianolock?.IsLocked() == true) { PrintMsgL(player, "pianolocked"); return rtrn; }
            if (!player.isMounted) return rtrn; // player offline, does not mean ismounted on piano

            if (player.GetMounted() != activepiano.entity) return rtrn; // online player not in seat on piano
            if (configData.debug) Puts("OnOvenToggle: Player cycled lantern!");
            if (oven.IsOn())
            {
                oven.StopCooking();
            }
            else
            {
                oven.StartCooking();
            }
            if (!activepiano.FuelCheck() && activepiano.needfuel)
            {
                PrintMsgL(player, "nofuel");
                PrintMsgL(player, "landingpiano");
                activepiano.engineon = false;
            }
            bool ison = activepiano.engineon;
            if (ison) { activepiano.islanding = true; PrintMsgL(player, "landingpiano"); return null; }
            if (!ison)
            {
                AddPlayerToPilotsList(player);
                activepiano.StartEngine();
                return null;
            }

            return rtrn;
        }

        // Check for piano lantern fuel
        private void OnFuelConsume(BaseOven oven, Item fuel, ItemModBurnable burnable)
        {
            // Only work on lanterns
            if (oven.ShortPrefabName != "lantern.deployed") return;
            int dbl = configData.DoubleFuel ? 4 : 2;

            BaseEntity lantern = oven as BaseEntity;
            // Only work on lanterns attached to a Piano
            PianoEntity activepiano = lantern.GetComponentInParent<PianoEntity>() ?? null;
            if (activepiano == null) return;
            if (configData.debug) Puts("OnConsumeFuel: found a piano lantern!");
            if (activepiano.needfuel)
            {
                if (configData.debug) Puts("OnConsumeFuel: piano requires fuel!");
            }
            else
            {
                if (configData.debug) Puts("OnConsumeFuel: piano does not require fuel!");
                fuel.amount++; // Required to keep it from decrementing
                return;
            }
            BasePlayer player = activepiano.GetComponent<BaseMountable>().GetMounted() as BasePlayer;
            if (!player) return;
            if (configData.debug) Puts("OnConsumeFuel: checking fuel level...");
            // Before it drops to 1 (3 for configData.DoubleFuel) AFTER this hook call is complete, warn them that the fuel is low (1) - ikr
            if (fuel.amount == dbl)
            {
                if (configData.debug) Puts("OnConsumeFuel: sending low fuel warning...");
                if (configData.PlayEmptySound)
                {
                    Effect.server.Run("assets/bundled/prefabs/fx/well/pump_down.prefab", player.transform.position);
                }
                PrintMsgL(player, "lowfuel");
            }

            if (configData.DoubleFuel)
            {
                fuel.amount--;
            }

            if (fuel.amount == 0)
            {
                if (configData.debug) Puts("OnConsumeFuel: out of fuel.");
                PrintMsgL(player, "lowfuel");
                bool ison = activepiano.engineon;
                if (ison)
                {
                    activepiano.islanding = true;
                    activepiano.engineon = false;
                    PrintMsgL(player, "landingpiano");
                    OnOvenToggle(oven, player);
                }
            }
        }

        // To skip cycling our lantern (thanks, k11l0u)
        private object OnNightLanternToggle(BaseEntity entity, bool status)
        {
            // Only work on lanterns
            if (entity.ShortPrefabName != "lantern.deployed") return null;
            if (configData.debug) Puts("OnNightLanternToggle: Called on a lantern.  Checking for piano...");

            // Only work on lanterns attached to a Piano
            PianoEntity activepiano = entity.GetComponentInParent<PianoEntity>() ?? null;
            if (activepiano != null)
            {
                if (configData.debug) Puts("OnNightLanternToggle: Do not cycle this lantern!");
                return true;
            }
            if (configData.debug) Puts("OnNightLanternToggle: Not a piano lantern.");
            return null;
        }
        #endregion

        #region Primary
        private void AddPiano(BasePlayer player, Vector3 location)
        {
            if (player == null && location == default(Vector3)) return;
            if (location == default(Vector3) && player != null) location = player.transform.position;

            List<BaseEntity> blocks = new List<BaseEntity>();
            Vis.Entities(player.transform.position, 10f, blocks, buildingMask);
            if (blocks.Count > 0)
            {
                PrintMsgL(player, "nospawnhere");
            }

            Vector3 spawnpos = new Vector3();

            // Set initial default for fuel requirement based on config
            bool needfuel = configData.RequireFuel;
            if (isAllowed(player, "flyingpiano.unlimited"))
            {
                // User granted unlimited fly time without fuel
                needfuel = false;
                if (configData.debug) Puts("AddPiano: Unlimited fuel granted!");
            }

            Vector3 rot = player.transform.rotation.eulerAngles;
            //rot.y += 180;
            if (needfuel)
            {
                // Don't put them on the piano since they need to fuel up first
                spawnpos = player.transform.position + (-player.transform.forward * 2f);
            }
            else
            {
                // Spawn at point of player
                spawnpos = new Vector3(location.x, location.y + 0.5f, location.z);
            }

            const string staticprefab = "assets/prefabs/instruments/piano/piano.deployed.prefab";
            BaseEntity newPiano = GameManager.server.CreateEntity(staticprefab, spawnpos, Quaternion.Euler(rot), true);
            newPiano.name = "FlyingPiano";
            UnityEngine.Object.Destroy(newPiano.GetComponent<DestroyOnGroundMissing>());
            UnityEngine.Object.Destroy(newPiano.GetComponent<GroundWatch>());
            BaseMountable chairmount = newPiano.GetComponent<BaseMountable>();
            chairmount.isMobile = true;
            newPiano.enableSaving = false;
            newPiano.OwnerID = player.userID;
            newPiano.Spawn();
            PianoEntity piano = newPiano.gameObject.AddComponent<PianoEntity>();
            piano.needfuel = needfuel;
            // Unlock the tank if they need fuel.
            piano.lantern1.SetFlag(BaseEntity.Flags.Locked, !needfuel);
            if (needfuel)
            {
                // We have to set this after the spawn.
                if (configData.debug) Puts("AddPiano: Emptying the tank!");
                piano.SetFuel(0);
            }

            AddPlayerID(player.userID);

            if (chairmount != null && player != null)
            {
                PrintMsgL(player, "pianospawned");
                if (piano.needfuel)
                {
                    PrintMsgL(player, "pianofuel");
                }
                else
                {
                    // Put them in the chair.  They will still need to unlock it.
                    PrintMsgL(player, "pianonofuel");
                    chairmount.MountPlayer(player);
                    if (configData.debug) Puts($"Mounted player: {chairmount._mounted.UserIDString}");
                }
            }
        }

        public bool PilotListContainsPlayer(BasePlayer player)
        {
            return pilotslist.Contains(player.userID);
        }

        private void AddPlayerToPilotsList(BasePlayer player)
        {
            if (PilotListContainsPlayer(player)) return;
            pilotslist.Add(player.userID);
        }

        public void RemovePlayerFromPilotsList(BasePlayer player)
        {
            if (PilotListContainsPlayer(player))
            {
                pilotslist.Remove(player.userID);
            }
        }

        private void DestroyLocalPiano(BasePlayer player)
        {
            if (player == null) return;
            List<BaseEntity> pianolist = new List<BaseEntity>();
            Vis.Entities<BaseEntity>(player.transform.position, configData.MinDistance, pianolist);
            bool foundpiano = false;

            foreach (BaseEntity p in pianolist)
            {
                PianoEntity foundent = p.GetComponentInParent<PianoEntity>() ?? null;
                if (foundent != null)
                {
                    foundpiano = true;
                    if (foundent.ownerid != player.userID) return;
                    foundent.entity.Kill(BaseNetworkable.DestroyMode.Gib);
                    PrintMsgL(player, "pianodestroyed");
                }
            }
            if (!foundpiano)
            {
                PrintMsgL(player, "notfound", configData.MinDistance.ToString());
            }
        }

        private void DestroyAllPianos(BasePlayer player)
        {
            List<BaseEntity> pianolist = new List<BaseEntity>();
            Vis.Entities(new Vector3(0,0,0), 3500f, pianolist);
            bool foundpiano = false;

            foreach (BaseEntity p in pianolist)
            {
                PianoEntity foundent = p.GetComponentInParent<PianoEntity>() ?? null;
                if (foundent != null)
                {
                    foundpiano = true;
                    foundent.entity.Kill(BaseNetworkable.DestroyMode.Gib);
                    PrintMsgL(player, "pianodestroyed");
                }
            }
            if (!foundpiano)
            {
                PrintMsgL(player, "notfound", configData.MinDistance.ToString());
            }
        }

        private void DestroyRemotePiano(BasePlayer player)
        {
            if (player == null) return;
            List<BaseEntity> pianolist = new List<BaseEntity>();
            Vis.Entities<BaseEntity>(new Vector3(0,0,0), 3500f, pianolist);
            bool foundpiano = false;

            foreach (BaseEntity p in pianolist)
            {
                PianoEntity foundent = p.GetComponentInParent<PianoEntity>() ?? null;
                if (foundent != null)
                {
                    foundpiano = true;
                    if (foundent.ownerid != player.userID) return;
                    foundent.entity.Kill(BaseNetworkable.DestroyMode.Gib);
                    PrintMsgL(player, "pianodestroyed");
                }
            }
            if (!foundpiano)
            {
                PrintMsgL(player, "notfound", configData.MinDistance.ToString());
            }
        }

        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null || input == null) return;
            if (!player.isMounted) return;
            PianoEntity activepiano = player.GetMounted().GetComponentInParent<PianoEntity>() ?? null;
            if (activepiano == null) return;
            if (player.GetMounted() != activepiano.entity) return;
            if (input != null)
            {
                activepiano.PianoInput(input, player);
            }
            return;
        }

        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo hitInfo)
        {
            if (entity == null || hitInfo == null) return;
            PianoEntity ispiano = entity.GetComponentInParent<PianoEntity>() ?? null;
            if (ispiano != null) hitInfo.damageTypes.ScaleAll(0);
        }

        private object OnEntityGroundMissing(BaseEntity entity)
        {
            PianoEntity ispiano = entity.GetComponentInParent<PianoEntity>() ?? null;
            if (ispiano != null) return false;
            return null;
        }

        private bool PianoLimitReached(BasePlayer player, bool vip=false)
        {
            if (configData.UseMaxPianoChecks)
            {
                if (pianoplayer.ContainsKey(player.userID))
                {
                    int currentcount = pianoplayer[player.userID].pianocount;
                    int maxallowed = configData.MaxPianos;
                    if (vip)
                    {
                        maxallowed = configData.VIPMaxPianos;
                    }
                    if (currentcount >= maxallowed) return true;
                }
            }
            return false;
        }

        private object OnPlayerWantsDismount(BasePlayer player, BaseMountable entity)
        {
//            Puts("OnPlayerWantsDismount!");
            return null;
        }

//        object OnPlayerWantsMount(BasePlayer player, BaseMountable entity)
//        {
////            Puts("OnPlayerWantsMount!");
//            var activepiano = entity.GetComponentInParent<PianoEntity>() ?? null;
//            if (activepiano != null)
//            {
//                Puts("Found a flying piano!");
//                return true;
//            }
//            return null;
//        }

        private object CanMountEntity(BasePlayer player, BaseMountable entity)
        {
//            Puts("CanMountEntity!");
            return null;
        }

        private object CanDismountEntity(BasePlayer player, BaseMountable entity)
        {
            Puts("CanDismountEntity!");
            if (player == null)
            {
                if (configData.debug) Puts("Player null!");
                return null;
            }
            if (PilotListContainsPlayer(player))
            {
                if (configData.debug) Puts("Unable to dismount!");
                return false;
            }
            return null;
        }

        private void OnEntityMounted(BaseMountable mountable, BasePlayer player)
        {
            PianoEntity activepiano = mountable.GetComponentInParent<PianoEntity>() ?? null;
            if (activepiano != null)
            {
                if (configData.debug) Puts("OnEntityMounted: player mounted copter!");
                if (mountable.GetComponent<BaseEntity>() != activepiano.entity) return;
                activepiano.lantern1.SetFlag(BaseEntity.Flags.On, false);
            }
        }

        private void OnEntityDismounted(BaseMountable mountable, BasePlayer player)
        {
            PianoEntity activepiano = mountable.GetComponentInParent<PianoEntity>() ?? null;
            if (activepiano != null)
            {
                if (configData.debug) Puts("OnEntityMounted: player dismounted copter!");
                if (mountable.GetComponent<BaseEntity>() != activepiano.entity) return;
            }
        }

        private object CanLootEntity(BasePlayer player, StorageContainer container)
        {
            if (container == null || player == null) return null;
            PianoEntity ispiano = container.GetComponentInParent<PianoEntity>();
            if (ispiano?.pianolock?.IsLocked() == true)
            {
                return false;
            }
            return null;
        }

        private object CanPickupEntity(BasePlayer player, BaseCombatEntity entity)
        {
            if (entity == null || player == null) return null;

            BaseEntity myent = entity as BaseEntity;
            string myparent = null;
            try
            {
                myparent = myent.GetParentEntity().name;
            }
            catch {}

            if (myparent == "FlyingPiano" || myent.name == "FlyingPiano")
            {
                if (configData.debug)
                {
                    if (myent.name == "FlyingPiano")
                    {
                        Puts("CanPickupEntity: player trying to pickup the piano!");
                    }
                    else if (myparent == "FlyingPiano")
                    {
                        string entity_name = myent.LookupPrefab().name;
                        Puts($"CanPickupEntity: player trying to remove {entity_name} from a piano!");
                    }
                }
                PrintMsgL(player, "notauthorized");
                return false;
            }
            return null;
        }

        private object CanPickupLock(BasePlayer player, BaseLock baseLock)
        {
            if (baseLock == null || player == null) return null;

            BaseEntity myent = baseLock as BaseEntity;
            string myparent = null;
            try
            {
                myparent = myent.GetParentEntity().name;
            }
            catch {}

            if (myparent == "FlyingPiano")
            {
                if (configData.debug)Puts("CanPickupLock: player trying to remove lock from a piano!");
                PrintMsgL(player, "notauthorized");
                return false;
            }
            return null;
        }

        private void AddPlayerID(ulong ownerid)
        {
            if (!pianoplayer.ContainsKey(ownerid))
            {
                pianoplayer.Add(ownerid, new PlayerPianoData
                {
                    pianocount = 1,
                });
                return;
            }
            pianoplayer[ownerid].pianocount++;
        }

        private void RemovePlayerID(ulong ownerid)
        {
            if (pianoplayer.ContainsKey(ownerid)) pianoplayer[ownerid].pianocount--;
        }

        private object OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            RemovePlayerFromPilotsList(player);
            return null;
        }

        private void RemovePiano(BasePlayer player)
        {
            RemovePlayerFromPilotsList(player);
            return;
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            RemovePiano(player);
        }

        private void OnPlayerRespawned(BasePlayer player)
        {
            RemovePiano(player);
        }

        private void DestroyAll<T>()
        {
            UnityEngine.Object[] objects = UnityEngine.Object.FindObjectsOfType(typeof(T));
            if (objects != null)
            {
                foreach (UnityEngine.Object gameObj in objects)
                {
                    UnityEngine.Object.Destroy(gameObj);
                }
            }
        }

        private void Unload()
        {
            DestroyAll<PianoEntity>();
        }
        #endregion

        #region Piano Antihack check
        private static List<BasePlayer> pianoantihack = new List<BasePlayer>();

        private object OnPlayerViolation(BasePlayer player, AntiHackType type, float amount)
        {
            if (player == null) return null;
            if (pianoantihack.Contains(player)) return false;
            return null;
        }
        #endregion

        #region Piano Entity
        private class PianoEntity : BaseEntity
        {
            public BaseEntity entity;
            public BasePlayer player;
            public BaseEntity piano1;
            public BaseEntity lantern1;
            public BaseEntity pianolock;

            public string entname = "FlyingPiano";

            private Quaternion entityrot;
            private Vector3 entitypos;

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
            private bool zmTrigger;

            public ulong skinid = 1;
            public ulong ownerid;
            //int count;
            private float minaltitude;
            private FlyingPiano instance;
            public bool throttleup;
            private float sprintspeed;
            private float normalspeed;
            //bool isenabled = true;
            private SphereCollider sphereCollider;

            private readonly string prefablamp = "assets/prefabs/deployable/lantern/lantern.deployed.prefab";
            private readonly string prefablock = "assets/prefabs/locks/keypad/lock.code.prefab";

            private void Awake()
            {
                entity = GetComponentInParent<BaseEntity>();
                entityrot = Quaternion.identity;
                entitypos = entity.transform.position;
                minaltitude = Instance.configData.MinAltitude;
                instance = new FlyingPiano();
                ownerid = entity.OwnerID;
                gameObject.name = "FlyingPiano";

                engineon = false;
                hasFuel = false;
                //needfuel = requirefuel;
                if (!needfuel)
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
                sprintspeed = Instance.configData.SprintSpeed;
                normalspeed = Instance.configData.NormalSpeed;
                //isenabled = false;
                SpawnPiano();
                lantern1.OwnerID = entity.OwnerID;

                sphereCollider = entity.gameObject.AddComponent<SphereCollider>();
                sphereCollider.gameObject.layer = (int)Layer.Reserved1;
                sphereCollider.isTrigger = true;
                sphereCollider.radius = 6f;
            }

            private BaseEntity SpawnPart(string prefab, BaseEntity entitypart, bool setactive, int eulangx, int eulangy, int eulangz, float locposx, float locposy, float locposz, BaseEntity parent, ulong skinid)
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

            private void SpawnRefresh(BaseEntity entity)
            {
                StabilityEntity hasstab = entity.GetComponent<StabilityEntity>();
                if (hasstab != null)
                {
                    hasstab.grounded = true;
                }
                BaseMountable hasmount = entity.GetComponent<BaseMountable>();
                if (hasmount != null)
                {
                    hasmount.isMobile = true;
                }
            }

            public void SetFuel(int amount = 0)
            {
                BaseOven lanternCont = lantern1 as BaseOven;
                ItemContainer container1 = lanternCont.inventory;

                if (amount == 0)
                {
                    while (container1.itemList.Count > 0)
                    {
                        Item item = container1.itemList[0];
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
                lantern1.SetFlag(Flags.On, false);
                pianolock = SpawnPart(prefablock, pianolock, true, 0, 0, 0, 0.6f, 0.8f, 0.1f, entity, 1);

                if (needfuel)
                {
                    // Empty tank
                    SetFuel(0);
                }
                else
                {
                    // Cannot be looted
                    lantern1.SetFlag(Flags.Locked, true);
                    // Add some fuel (1 lgf) so it lights up anyway.  It should always stay at 1.
                    SetFuel(1);
                }
            }

            private void OnTriggerEnter(Collider col)
            {
                if (col.gameObject.name == "ZoneManager")
                {
                    if (Instance.configData.debug) Instance.Puts("Entering ZoneManager zone.");
                    zmTrigger = true;
                }
                else if (col.GetComponentInParent<BasePlayer>() != null)
                {
                    pianoantihack.Add(col.GetComponentInParent<BasePlayer>());
                }
            }

            private void OnTriggerExit(Collider col)
            {
                if (col.gameObject.name == "ZoneManager")
                {
                    if (Instance.configData.debug) Instance.Puts("Exiting ZoneManager zone.");
                    zmTrigger = false;
                }
                else if (col.GetComponentInParent<BasePlayer>() != null)
                {
                    pianoantihack.Remove(col.GetComponentInParent<BasePlayer>());
                }
            }

            public BasePlayer GetPilot()
            {
                player = entity.GetComponent<BaseMountable>().GetMounted() as BasePlayer;
                return player;
            }

            public void PianoInput(InputState input, BasePlayer player)
            {
                if (input == null || player == null) return;

                if (input.WasJustPressed(BUTTON.FORWARD)) moveforward = true;
                if (input.WasJustReleased(BUTTON.FORWARD)) moveforward = false;
                if (input.WasJustPressed(BUTTON.BACKWARD)) movebackward = true;
                if (input.WasJustReleased(BUTTON.BACKWARD)) movebackward = false;
                if (input.WasJustPressed(BUTTON.RIGHT)) rotright = true;
                if (input.WasJustReleased(BUTTON.RIGHT)) rotright = false;
                if (input.WasJustPressed(BUTTON.LEFT)) rotleft = true;
                if (input.WasJustReleased(BUTTON.LEFT)) rotleft = false;
                if (input.IsDown(BUTTON.SPRINT)) throttleup = true;
                if (input.WasJustReleased(BUTTON.SPRINT)) throttleup = false;
                if (input.WasJustPressed(BUTTON.JUMP)) moveup = true;
                if (input.WasJustReleased(BUTTON.JUMP)) moveup = false;
                if (input.WasJustPressed(BUTTON.DUCK)) movedown = true;
                if (input.WasJustReleased(BUTTON.DUCK)) movedown = false;
            }

            public bool FuelCheck()
            {
                if (!needfuel)
                {
                    return true;
                }
                BaseOven lantern = lantern1 as BaseOven;
                Item slot = lantern.inventory.GetSlot(0);
                if (slot == null)
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

            public void StartEngine()
            {
                engineon = true;
                zmTrigger = false;
                sphereCollider.enabled = false;
                if (Instance.configData.debug) Instance.Puts("sphereCollider disabled for engine start");
                instance.timer.Once(2, EngineStarted);
            }

            private void EngineStarted()
            {
                sphereCollider.enabled = true;
                if (Instance.configData.debug) Instance.Puts("sphereCollider enabled after engine start");
            }

            private void Update()
            {
                if (engineon)
                {
                    if (!GetPilot()) islanding = true;
                    float currentspeed = normalspeed;
                    if (throttleup) { currentspeed = sprintspeed; }
                    RaycastHit hit;

                    // This is a little weird.  Fortunately, some of the hooks determine fuel status...
                    if (!hasFuel && needfuel)
                    {
                        islanding = false;
                        engineon = false;
                        return;
                    }
                    if (islanding)
                    {
                        // LANDING
                        if (Instance.configData.debug) Interface.Oxide.LogWarning($"Trying to land, current pos = {entity.transform.position}");
                        if (!Physics.Raycast(new Ray(entity.transform.position, Vector3.down), out hit, 1.5f, layerMask))
                        {
                            // Drop fast, it's a piano
                            entity.transform.localPosition += transform.up * -15f * Time.deltaTime;
                        }
                        else
                        {
                            // Slow down
                            entity.transform.localPosition += transform.up * -1f * Time.deltaTime;
                        }
                        if (Physics.Raycast(new Ray(entity.transform.position, Vector3.down), out hit, 0.5f, layerMask) && hit.collider?.name != "ZoneManager")
                        {
                            // Stop
                            if (Instance.configData.debug) Interface.Oxide.LogWarning($"Landing, current pos = {entity.transform.position}");
                            islanding = false;
                            engineon = false;
                            if (pilotslist.Contains(player.userID))
                            {
                                if (Instance.configData.debug) Interface.Oxide.LogWarning("Landed!");
                                pilotslist.Remove(player.userID);
                            }
                        }
                        ResetMovement();
                        ServerMgr.Instance.StartCoroutine(RefreshTrain());
                        return;
                    }

                    // Maintain minimum height
                    if (Physics.Raycast(entity.transform.position, entity.transform.TransformDirection(Vector3.down), out hit, minaltitude, layerMask) && !zmTrigger)
                    {
                        entity.transform.localPosition += transform.up * minaltitude * Time.deltaTime * 2;
                        ServerMgr.Instance.StartCoroutine(RefreshTrain());
                        return;
                    }
                    // Disallow flying forward into buildings, etc.
                    if (Physics.Raycast(entity.transform.position, entity.transform.TransformDirection(Vector3.forward), out hit, 10f, buildingMask))
                    {
                        if (!zmTrigger)
                        {
                            entity.transform.localPosition += transform.forward * -5f * Time.deltaTime;
                            moveforward = false;
                        }
                    }
                    // Disallow flying backward into buildings, etc.
                    else if (Physics.Raycast(new Ray(entity.transform.position, Vector3.forward * -1f), out hit, 10f, buildingMask))
                    {
                        if (!zmTrigger)
                        {
                            entity.transform.localPosition += transform.forward * 5f * Time.deltaTime;
                            movebackward = false;
                        }
                    }

                    float rotspeed = 0.1f;
                    if (throttleup) rotspeed += 0.25f;
                    if (rotright) entity.transform.eulerAngles += new Vector3(0, rotspeed, 0);
                    else if (rotleft) entity.transform.eulerAngles += new Vector3(0, -rotspeed, 0);

                    if (moveforward) entity.transform.localPosition += transform.forward * currentspeed * Time.deltaTime;
                    else if (movebackward) entity.transform.localPosition -= transform.forward * currentspeed * Time.deltaTime;

                    if (moveup) entity.transform.localPosition += transform.up * currentspeed * Time.deltaTime;
                    else if (movedown) entity.transform.localPosition += transform.up * -currentspeed * Time.deltaTime;

                    ServerMgr.Instance.StartCoroutine(RefreshTrain());
                }
            }

            private IEnumerator RefreshTrain()
            {
                entity.transform.hasChanged = true;
                for (int i = 0; i < entity.children.Count; i++)
                {
                    entity.children[i].transform.hasChanged = true;
                    entity.children[i].SendNetworkUpdateImmediate();
                    entity.children[i].UpdateNetworkGroup();
                }
                entity.SendNetworkUpdateImmediate();
                entity.UpdateNetworkGroup();
                yield return new WaitForEndOfFrame();
            }

            private void ResetMovement()
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
                if (pianoplayer.ContainsKey(ownerid)) pianoplayer[ownerid].pianocount--;
                entity?.Invoke("KillMessage", 0.1f);
            }
        }
        #endregion
    }
}
