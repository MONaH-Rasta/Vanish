using Facepunch;
using Network;
using Newtonsoft.Json;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Cui;
using Rust;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Vanish", "Whispers88", "1.0.5")]
    [Description("Allows players with permission to become invisible. Credits to Nivex & Wulf")]
    public class Vanish : RustPlugin
    {

        #region Configuration
        private readonly List<BasePlayer> _hiddenPlayers = new List<BasePlayer>();
        private static List<string> _registeredhooks = new List<string> { "OnNpcTarget", "CanBeTargeted", "CanHelicopterTarget", "CanBradleyApcTarget", "CanUseLockedEntity", "OnEntityTakeDamage", "OnPlayerDisconnected" };
        private static readonly DamageTypeList _EmptyDmgList = new DamageTypeList();
        private static readonly Dictionary<ulong, string> GuiGuids = new Dictionary<ulong, string>();

        private Configuration config;

        public class Configuration
        {
            [JsonProperty("Hide an invisible players body under the terrain after disconnect")]
            public bool HideOnDisconnect = false;

            [JsonProperty("Disable player collider while in vanish")]
            public bool DisablePlayerCollider = true;

            [JsonProperty("Enable vanishing and reappearing sound effects")]
            public bool EnableSound = true;

            [JsonProperty("Enable chat notifications")]
            public bool EnableNotifications = true;

            [JsonProperty("Sound effect to use when vanishing")]
            public string VanishSoundEffect = "assets/prefabs/npc/patrol helicopter/effects/rocket_fire.prefab";

            [JsonProperty("Sound effect to use when reappearing")]
            public string ReappearSoundEffect = "assets/prefabs/npc/patrol helicopter/effects/rocket_fire.prefab";

            [JsonProperty("Enable GUI")]
            public bool EnableGUI = true;

            [JsonProperty("Icon URL (.png or .jpg)")]
            public string ImageUrlIcon = "http://i.imgur.com/Gr5G3YI.png";

            [JsonProperty("Image Color")]
            public string ImageColor = "1 1 1 0.3";

            [JsonProperty("Image AnchorMin")]
            public string ImageAnchorMin = "0.175 0.017";

            [JsonProperty("Image AnchorMax")]
            public string ImageAnchorMax = "0.22 0.08";

            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonConvert.DeserializeObject<Dictionary<string, object>>(ToJson());
        }

        protected override void LoadDefaultConfig() => config = new Configuration();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null)
                {
                    throw new JsonException();
                }

                if (!config.ToDictionary().Keys.SequenceEqual(Config.ToDictionary(x => x.Key, x => x.Value).Keys))
                {
                    PrintToConsole("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch
            {
                PrintToConsole($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            PrintToConsole($"Configuration changes saved to {Name}.json");
            Config.WriteObject(config, true);
        }

        #endregion Configuration

        #region Localization

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["VanishCommand"] = "vanish",
                ["Vanished"] = "Vanish: <color=orange> Enabled </color>",
                ["Reappear"] = "Vanish: <color=orange> Disabled </color>"
            }, this);
        }

        #endregion Localization

        #region Initialization

        private const string permallow = "vanish.allow";
        private const string permunlock = "vanish.unlock";

        private void Init()
        {
            // Register univeral chat/console commands
            AddLocalizedCommand(nameof(VanishCommand));

            // Register permissions for commands
            permission.RegisterPermission(permallow, this);
            permission.RegisterPermission(permunlock, this);

            //Unsubscribe from hooks
            UnSubscribeFromHooks();
        }

        private void Unload()
        {
            for (int i = _hiddenPlayers.Count - 1; i >= 0; i--)
            {
                var player = _hiddenPlayers[i];
                if (player != null) Reappear(player);
            }

            foreach (var key in GuiGuids.Keys)
            {
                var player = BasePlayer.FindByID(key);
                if (player != null) CuiHelper.DestroyUi(player, GuiGuids[key]);
            }

            _hiddenPlayers.Clear();
            GuiGuids.Clear();
        }

        #endregion Initialization

        #region Commands

        private void VanishCommand(IPlayer iplayer, string command, string[] args)
        {
            var player = (BasePlayer)iplayer.Object;
            if (!HasPerm(player.UserIDString, permallow) && !player.IsAdmin)
                return;
            if (IsInvisible(player)) Reappear(player);
            else Disappear(player);
        }

        private void Reappear(BasePlayer player)
        {
            player._limitedNetworking = false;
            player.UpdatePlayerCollider(true);
            player.SendNetworkUpdate();
            player.GetHeldEntity()?.SendNetworkUpdate();
            _hiddenPlayers.Remove(player);

            if (_hiddenPlayers.Count == 0) UnSubscribeFromHooks();

            if (config.EnableSound) SendEffect(player, config.ReappearSoundEffect);

            if (GuiGuids.ContainsKey(player.userID)) CuiHelper.DestroyUi(player, GuiGuids[player.userID]);

            if(config.EnableNotifications) Message(player.IPlayer, "Reappear");
        }

        private void Disappear(BasePlayer player)
        {
            if (_hiddenPlayers.Count == 0) SubscribeToHooks();
            player._limitedNetworking = true;
            var connections = Net.sv.connections.Where(con => con.connected && con.isAuthenticated && con.player is BasePlayer && con.player != player).ToList();
            player.OnNetworkSubscribersLeave(connections);
            
            if (player.children != null)
            {
                var childs = Pool.GetList<BaseEntity>();
                try
                {
                    foreach (var child in player.children)
                        GetChildren(child, childs);
                    foreach (var childent in childs)
                        childent.OnNetworkSubscribersLeave(connections);
                }
                finally
                {
                    Pool.Free(ref childs);
                }
            }

            if (config.DisablePlayerCollider) player.UpdatePlayerCollider(false);

            _hiddenPlayers.Add(player);

            if (config.EnableSound) SendEffect(player, config.VanishSoundEffect);

            if (config.EnableGUI) VanishGui(player);

            Message(player.IPlayer, "Vanished");
        }

        #endregion Commands

        #region Hooks
        private object OnNpcTarget(BaseEntity npc, BasePlayer player) => IsInvisible(player) ? (object)true : null;
        private object CanBeTargeted(BasePlayer player, MonoBehaviour behaviour) => IsInvisible(player?.ToPlayer()) ? (object)false : null;
        private object CanHelicopterTarget(PatrolHelicopterAI heli, BasePlayer player) => IsInvisible(player) ? (object)false : null;
        private object CanBradleyApcTarget(BradleyAPC apc, BasePlayer player) => IsInvisible(player) ? (object)false : null;
        private object CanUseLockedEntity(BasePlayer player, BaseLock baseLock)
        {
            if (IsInvisible(player) && HasPerm(player.UserIDString, permunlock))
            {
                return true;
            }

            CodeLock codeLock = baseLock as CodeLock;
            if (codeLock != null)
            {
                if (!codeLock.whitelistPlayers.Contains(player.userID) && !codeLock.guestPlayers.Contains(player.userID))
                {
                    return false;
                }
            }
            return null;
        }
        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var attacker = info?.InitiatorPlayer;
            var victim = entity?.ToPlayer();
            if (!IsInvisible(victim) && !IsInvisible(attacker)) return null;
            if (info != null)
            {
                info.damageTypes = _EmptyDmgList;
                info.HitMaterial = 0;
                info.PointStart = Vector3.zero;
                info.HitEntity = null;
            }
            return true;
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (IsInvisible(player))
            {
                if (config.HideOnDisconnect)
                {
                    var pos = player.transform.position;
                    var underTerrainPos = new Vector3(pos.x, TerrainMeta.HeightMap.GetHeight(pos) - 10, pos.z);
                    player.Teleport(underTerrainPos);
                }

                player.syncPosition = true;
                player._limitedNetworking = false;

                if (_hiddenPlayers.Count == 0) UnSubscribeFromHooks();
            }
        }

        #endregion Hooks

        #region GUI

        private void VanishGui(BasePlayer player)
        {
            GuiGuids[player.userID] = CuiHelper.GetGuid();

            string guiId;
            if (GuiGuids.TryGetValue(player.userID, out guiId))
            {
                CuiHelper.DestroyUi(player, guiId);
            }

            CuiElementContainer elements = new CuiElementContainer();
            GuiGuids[player.userID] = CuiHelper.GetGuid();

            elements.Add(new CuiElement
            {
                Name = GuiGuids[player.userID],
                Components =
                {
                    new CuiRawImageComponent
                    {
                        Color = config.ImageColor,
                        Url = config.ImageUrlIcon
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = config.ImageAnchorMin,
                        AnchorMax = config.ImageAnchorMax
                    }
                }
            });

            CuiHelper.AddUi(player, elements);
        }

        #endregion GUI

        #region Helpers

        private void AddLocalizedCommand(string command)
        {
            foreach (string language in lang.GetLanguages(this))
            {
                Dictionary<string, string> messages = lang.GetMessages(language, this);
                foreach (KeyValuePair<string, string> message in messages)
                {
                    if (message.Key.Equals(command))
                    {
                        if (!string.IsNullOrEmpty(message.Value))
                        {
                            AddCovalenceCommand(message.Value, command);
                        }
                    }
                }
            }
        }

        private bool HasPerm(string id, string perm) => permission.UserHasPermission(id, perm);

        private string GetLang(string langKey, string playerId = null, params object[] args)
        {
            return string.Format(lang.GetMessage(langKey, this, playerId), args);
        }

        private void Message(IPlayer player, string langKey, params object[] args)
        {
            if (player.IsConnected)
            {
                player.Message(GetLang(langKey, player.Id, args));
            }
        }

        private bool IsInvisible(BasePlayer player) => player != null && _hiddenPlayers.Contains(player);

        private void UnSubscribeFromHooks()
        {
            foreach (var hook in _registeredhooks)
                Unsubscribe(hook);
        }

        private void SubscribeToHooks()
        {
            foreach (var hook in _registeredhooks)
                Subscribe(hook);
        }

        private void SendEffect(BasePlayer player, string sound)
        {
            var effect = new Effect(sound, player, 0, Vector3.zero, Vector3.forward);
            EffectNetwork.Send(effect, player.net.connection);
        }

        private static void GetChildren(BaseEntity entity, List<BaseEntity> childslist)
        {
            if (entity == null) return;
            if (!childslist.Contains(entity)) childslist.Add(entity);
            if (entity.children == null) return;
            foreach (var subchild in entity.children)
                GetChildren(subchild, childslist);
        }
        #endregion Helpers

        #region Public Helpers
        public void _Disappear(BasePlayer basePlayer) => Disappear(basePlayer);
        public void _Reappear(BasePlayer basePlayer) => Reappear(basePlayer);
        public void _VanishGui(BasePlayer basePlayer) => VanishGui(basePlayer);
        public bool _IsInvisible(BasePlayer basePlayer) => IsInvisible(basePlayer);

        #endregion

    }
}