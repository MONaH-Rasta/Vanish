using Network;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Cui;
using Rust;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Vanish", "Whispers88", "1.5.0")]
    [Description("Allows players with permission to become invisible")]
    public class Vanish : RustPlugin
    {
        #region Configuration
        private readonly List<BasePlayer> _hiddenPlayers = new List<BasePlayer>();
        private readonly List<BasePlayer> _hiddenOffline = new List<BasePlayer>();
        private static List<string> _registeredhooks = new List<string> { "CanUseLockedEntity", "OnPlayerDisconnected", "OnEntityTakeDamage" };
        private static readonly DamageTypeList _EmptyDmgList = new DamageTypeList();
        CuiElementContainer cachedVanishUI = null;
        CuiElementContainer cachedVanishColliderUI = null;

        private Configuration config;

        public class Configuration
        {
            [JsonProperty("NoClip on Vanish (runs noclip command)")]
            public bool NoClipOnVanish = true;

            [JsonProperty("Use OnEntityTakeDamage hook (Set to true to enable use of vanish.damage perm. Set to false for better performance)")]
            public bool UseOnEntityTakeDamage = false;

            [JsonProperty("Use CanUseLockedEntity hook (Allows vanished players with the perm vanish.unlock to bypass locks. Set to false for better performance)")]
            public bool UseCanUseLockedEntity = true;

            [JsonProperty("Hide an invisible players body under the terrain after disconnect")]
            public bool HideOnDisconnect = true;

            [JsonProperty("If a player was vanished on disconnection keep them vanished on reconnect")]
            public bool HideOnReconnect = true;

            [JsonProperty("Turn off fly hack detection for players in vanish")]
            public bool AntiHack = true;

            [JsonProperty("Enable vanishing and reappearing sound effects")]
            public bool EnableSound = true;

            [JsonProperty("Make sound effects public")]
            public bool PublicSound = false;

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

            [JsonProperty("Collider Toggle Icon URL (.png or .jpg)")]
            public string ImageUrlColliderIcon = "https://i.imgur.com/9pLtRiI.png";

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
                ["CollisionToggle"] = "collider",
                ["Vanished"] = "Vanish: <color=orange> Enabled </color>",
                ["Reappear"] = "Vanish: <color=orange> Disabled </color>",
                ["NoPerms"] = "You do not have permission to do this",
                ["PermanentVanish"] = "You are in a permanent vanish mode",
                ["ColliderEnbabled"] = "Player Collider: <color=orange> Enabled </color>",
                ["ColliderDisabled"] = "Player Collider: <color=orange> Disabled </color>"

            }, this);
        }

        #endregion Localization

        #region Initialization

        private const string permallow = "vanish.allow";
        private const string permunlock = "vanish.unlock";
        private const string permdamage = "vanish.damage";
        private const string permavanish = "vanish.permanent";
        private const string permcollision = "vanish.collision";

        private void Init()
        {
            cachedVanishUI = CreateVanishUI();
            cachedVanishColliderUI = CreateVanishColliderUI();

            // Register univeral chat/console commands
            AddLocalizedCommand(nameof(VanishCommand));
            AddLocalizedCommand(nameof(CollisionToggle));

            // Register permissions for commands
            permission.RegisterPermission(permallow, this);
            permission.RegisterPermission(permunlock, this);
            permission.RegisterPermission(permdamage, this);
            permission.RegisterPermission(permavanish, this);
            permission.RegisterPermission(permcollision, this);

            //Unsubscribe from hooks
            UnSubscribeFromHooks();

            if (!config.UseOnEntityTakeDamage)
            {
                _registeredhooks.Remove("OnEntityTakeDamage");
            }

            if (!config.UseCanUseLockedEntity)
            {
                _registeredhooks.Remove("CanUseLockedEntity");
            }

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (!HasPerm(player.UserIDString, permavanish) || IsInvisible(player)) continue;
                Disappear(player);
            }
        }

        private void Unload()
        {
            foreach (var player in _hiddenPlayers.ToList())
            {
                if (player == null) continue;
                Reappear(player);
            }

            foreach (var player in BasePlayer.activePlayerList)
            {
                VanishPositionUpdate t;
                if (!player.TryGetComponent<VanishPositionUpdate>(out t)) continue;
                UnityEngine.Object.Destroy(t);
            }
        }

        #endregion Initialization

        #region Commands
        private void VanishCommand(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer player = (BasePlayer)iplayer.Object;
            if (player == null) return;
            if (!HasPerm(player.UserIDString, permallow))
            {
                if (config.EnableNotifications) Message(player.IPlayer, "NoPerms");
                return;
            }
            if (HasPerm(player.UserIDString, permavanish))
            {
                if (config.EnableNotifications) Message(player.IPlayer, "PermanentVanish");
                return;
            }
            if (IsInvisible(player)) Reappear(player);
            else Disappear(player);

        }

        private HashSet<ulong> collideroff = new HashSet<ulong>();

        private void CollisionToggle(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer player = (BasePlayer)iplayer.Object;
            if (player == null) return;
            if (!IsInvisible(player)) return;
            if (!HasPerm(player.UserIDString, permcollision))
            {
                if (config.EnableNotifications) Message(player.IPlayer, "NoPerms");
                return;
            }
            VanishPositionUpdate t;
            Collider col;
            if (!player.gameObject.TryGetComponent<Collider>(out col)) return;
            if (!col.enabled)
            {
                if (collideroff.Count == 0)
                    Subscribe("OnEntityTakeDamage");

                collideroff.Add(player.userID);
                player.EnablePlayerCollider();

                if (player.TryGetComponent<VanishPositionUpdate>(out t))
                    t.collider = true;

                CuiHelper.DestroyUi(player, "VanishColliderUI");
                Message(player.IPlayer, "ColliderEnbabled");
                return;
            }

            CuiHelper.AddUi(player, cachedVanishColliderUI);
            player.DisablePlayerCollider();
            collideroff.Remove(player.userID);
            if (collideroff.Count == 0)
                Unsubscribe("OnEntityTakeDamage");

            if (player.TryGetComponent<VanishPositionUpdate>(out t))
                t.collider = false;

            Message(player.IPlayer, "ColliderDisabled");
        }

        private void Reappear(BasePlayer player)
        {
            if (Interface.CallHook("OnVanishReappear", player) != null) return;
            if (config.AntiHack) player.ResetAntiHack();

            player.syncPosition = true;
            UnityEngine.Object.Destroy(player.GetComponent<VanishPositionUpdate>());

            player.metabolism.Reset();

            player._limitedNetworking = false;

            player.playerCollider.enabled = true;
            _hiddenPlayers.Remove(player);
            player.UpdateNetworkGroup();
            player.SendNetworkUpdate();
            player.GetHeldEntity()?.SendNetworkUpdate();

            //Un-Mute Player Effects
            player.drownEffect.guid = "28ad47c8e6d313742a7a2740674a25b5";
            player.fallDamageEffect.guid = "ca14ed027d5924003b1c5d9e523a5fce";

            if (_hiddenPlayers.Count == 0) UnSubscribeFromHooks();

            if (config.EnableSound)
            {
                if (config.PublicSound)
                {
                    Effect.server.Run(config.ReappearSoundEffect, player.transform.position);
                }
                else
                {
                    SendEffect(player, config.ReappearSoundEffect);
                }
            }
            CuiHelper.DestroyUi(player, "VanishUI");
            CuiHelper.DestroyUi(player, "VanishColliderUI");

            collideroff.Remove(player.userID);

            if (collideroff.Count == 0)
            {
                Unsubscribe("OnEntityTakeDamage");
            }

            if (config.NoClipOnVanish && player.IsFlying) player.SendConsoleCommand("noclip");

            if (config.EnableNotifications) Message(player.IPlayer, "Reappear");
        }

        private void Disappear(BasePlayer player)
        {
            if (Interface.CallHook("OnVanishDisappear", player) != null) return;
            if (config.AntiHack) player.PauseFlyHackDetection(9000000f);

            player.CancelInvoke("MetabolismUpdate");

            var connections = Net.sv.connections.Where(con => con.connected && con.isAuthenticated && con.player is BasePlayer && con.player != player).ToList();
            player.OnNetworkSubscribersLeave(connections);
            player.DisablePlayerCollider();
            player.syncPosition = false;

            player._limitedNetworking = true;

            player.gameObject.AddComponent<VanishPositionUpdate>();

            //Mute Player Effects
            player.fallDamageEffect = new GameObjectRef();
            player.drownEffect = new GameObjectRef();

            if (_hiddenPlayers.Count == 0) SubscribeToHooks();
            _hiddenPlayers.Add(player);

            if (config.EnableSound)
            {
                if (config.PublicSound)
                {
                    Effect.server.Run(config.VanishSoundEffect, player.transform.position);
                }
                else
                {
                    SendEffect(player, config.VanishSoundEffect);
                }
            }

            if (config.NoClipOnVanish && !player.IsFlying && !player.isMounted) player.SendConsoleCommand("noclip");

            if (config.EnableGUI)
            {
                CuiHelper.AddUi(player, cachedVanishUI);
                CuiHelper.AddUi(player, cachedVanishColliderUI);
            }

            if (config.EnableNotifications) Message(player.IPlayer, "Vanished");
        }

        #endregion Commands

        #region Hooks
        private void OnPlayerConnected(BasePlayer player)
        {
            if (_hiddenOffline.Contains(player))
            {
                _hiddenOffline.Remove(player);
                if (HasPerm(player.UserIDString, permallow))
                    Disappear(player);
                return;
            }
            if (HasPerm(player.UserIDString, permavanish))
            {
                Disappear(player);
            }
        }

        private object CanUseLockedEntity(BasePlayer player, BaseLock baseLock)
        {
            if (player.limitNetworking)
            {
                if (HasPerm(player.UserIDString, permunlock)) return true;
                if (config.EnableNotifications) Message(player.IPlayer, "NoPerms");
            }
            return null;
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var attacker = info?.InitiatorPlayer;
            var victim = entity?.ToPlayer();
            if (!IsInvisible(victim) && !IsInvisible(attacker)) return null;
            if (IsInvisible(attacker) && HasPerm(attacker.UserIDString, permdamage)) return null;
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
            if (!IsInvisible(player)) return;

            Reappear(player);

            if (_hiddenPlayers.Count == 0) UnSubscribeFromHooks();

            if (config.HideOnDisconnect)
            {
                var pos = player.transform.position;
                var underTerrainPos = new Vector3(pos.x, TerrainMeta.HeightMap.GetHeight(pos) - 5, pos.z);
                player.Teleport(underTerrainPos);
                player.DisablePlayerCollider();
                player.limitNetworking = true;
            }

            if (config.HideOnReconnect)
                _hiddenOffline.Add(player);

            CuiHelper.DestroyUi(player, "VanishUI");
            CuiHelper.DestroyUi(player, "VanishColliderUI");
        }
        #endregion Hooks

        #region GUI

        private CuiElementContainer CreateVanishUI()
        {
            CuiElementContainer elements = new CuiElementContainer();
            string panel = elements.Add(new CuiPanel
            {
                Image = { Color = "0.5 0.5 0.5 0.0" },
                RectTransform = { AnchorMin = config.ImageAnchorMin, AnchorMax = config.ImageAnchorMax }
            }, "Hud.Menu", "VanishUI");
            elements.Add(new CuiElement
            {
                Parent = panel,
                Components =
                {
                    new CuiRawImageComponent {Color = config.ImageColor, Url = config.ImageUrlIcon},
                    new CuiRectTransformComponent {AnchorMin = "0 0", AnchorMax = "1 1"}
                }
            });
            return elements;
        }

        private CuiElementContainer CreateVanishColliderUI()
        {
            CuiElementContainer elements = new CuiElementContainer();
            string panel = elements.Add(new CuiPanel
            {
                Image = { Color = "0.5 0.5 0.5 0.0" },
                RectTransform = { AnchorMin = config.ImageAnchorMin, AnchorMax = config.ImageAnchorMax }
            }, "Hud", "VanishColliderUI");
            elements.Add(new CuiElement
            {
                Parent = panel,
                Components =
                {
                    new CuiRawImageComponent {Color = config.ImageColor, Url = config.ImageUrlColliderIcon},
                    new CuiRectTransformComponent {AnchorMin = "0 0", AnchorMax = "1 1"}
                }
            });
            return elements;
        }

        #endregion GUI

        #region Monobehaviour
        public class VanishPositionUpdate : FacepunchBehaviour
        {
            private BasePlayer player;
            public bool collider;
            private void Awake()
            {
                player = GetComponent<BasePlayer>();
                InvokeRepeating("UpdatePos", 1f, 5f);
                player.transform.localScale = Vector3.zero;
            }

            private void UpdatePos()
            {
                using (TimeWarning.New("UpdateVanishGroup"))
                    player.net.UpdateGroups(player.transform.position);
                //until a collider hook is added
                if (player.playerCollider.enabled)
                    CuiHelper.DestroyUi(player, "VanishColliderUI");
                player.transform.localScale = Vector3.zero;
            }
            private void OnDestroy()
            {
                player.transform.localScale = new Vector3(1, 1, 1);
                CancelInvoke();
                player = null;
            }
        }

        #endregion Monobehaviour

        #region Helpers

        private void AddLocalizedCommand(string command)
        {
            foreach (string language in lang.GetLanguages(this))
            {
                Dictionary<string, string> messages = lang.GetMessages(language, this);
                foreach (KeyValuePair<string, string> message in messages)
                {
                    if (!message.Key.Equals(command)) continue;

                    if (string.IsNullOrEmpty(message.Value)) continue;

                    AddCovalenceCommand(message.Value, command);
                }
            }
        }

        private bool HasPerm(string id, string perm) => permission.UserHasPermission(id, perm);

        private string GetLang(string langKey, string playerId = null, params object[] args) => string.Format(lang.GetMessage(langKey, this, playerId), args);

        private void Message(IPlayer player, string langKey, params object[] args)
        {
            if (player.IsConnected) player.Message(GetLang(langKey, player.Id, args));
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

        #endregion Helpers

        #region Public Helpers
        public void _Disappear(BasePlayer basePlayer) => Disappear(basePlayer);
        public void _Reappear(BasePlayer basePlayer) => Reappear(basePlayer);
        public bool _IsInvisible(BasePlayer basePlayer) => IsInvisible(basePlayer);
        #endregion
    }
}