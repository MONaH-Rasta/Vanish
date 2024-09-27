# Vanish

Oxide plugin for Rust. Allows players with permission to become truly invisible

**Vanish** allows players with permission to become completely invisible. Players, turrets, helicopters, NPCs, etc. will not be able to see, hear, or touch you!

> **Note**: While vanished, you can hear players but they will be unable to hear you. You are invisible to them and essentially do not exist to them. Even if they walk right through you they *DO NOT* know of your existence. With the exception of you making sounds such as shooting a gun, they will hear the bullet - hit effect.

## Permissions

> This plugin uses the permission system. To assign a permission, use `oxide.grant <user or group> <name or steam id> <permission>`. To remove a permission, use `oxide.revoke <user or group> <name or steam id> <permission>`.

* `vanish.allow` -- Required to go invisible
* `vanish.unlock` -- Allows an invisible playe to unlock all locks
* `vanish.damage` -- Allows an invisible player to damage other entities. Requires "Use OnEntityTakeDamage hook" config option to be set to true.
* `vanish.permanent` -- Permanently forces a player to be vanished

## Commands

> This plugin provides both chat and console commands using the same syntax. When using a command in chat, prefix it with a forward slash: `/`.

* `vanish` -- Toggle invisibility on/off for self

## Interaction

*Being authed on toolcupboards and some client bugs can cause issues when interacting with containers and doors. The interaction feature eliviates this issue and adds some additional funcionality.*

**While in vanish a vanished player can use their bound reload key to interact with with various objects including containers, doors, vehicles and players.** This works via ray cast meaning most commonly you will push the R key to interact with the object you are looking at in game.

Interactive Objects:

* Players - Looking at a player and pressing R will view that players inventory
* Doors - Looking at a door and pressing R will toggle it open or closed bypassing all locks in place.
* Containers - This allows all containers to be viewed bypassing all lock and toolcupboard requirments
* Vehicles - Pressing R while looking at a vehicle will mount the vanished player to the closesest mounting position relative to where they are looking bypassing all locks.

## Configuration

> The settings and options can be configured in the `Vanish` file under the `config` directory. The use of an editor and validator is recommended to avoid formatting issues and syntax errors.

```json
{
  "NoClip on Vanish (runs noclip command)": true,
  "Use OnEntityTakeDamage hook (Set to true to enable use of vanish.damage perm. Set to false for better performance)": false,
  "Use CanUseLockedEntity hook (Allows vanished players with the perm vanish.unlock to bypass locks. Set to false for better performance)": true,
  "Keep a vanished player hidden on disconnect": true,
  "Turn off fly hack detection for players in vanish": true,
  "Disable metabolism in vanish": true,
  "Reset hydration and health on un-vanishing (resets to pre-vanished state)": true,
  "Enable vanishing and reappearing sound effects": true,
  "Make sound effects public": false,
  "Enable chat notifications": true,
  "Sound effect to use when vanishing": "assets/prefabs/npc/patrol helicopter/effects/rocket_fire.prefab",
  "Sound effect to use when reappearing": "assets/prefabs/npc/patrol helicopter/effects/rocket_fire.prefab",
  "Enable GUI": true,
  "Icon URL (.png or .jpg)": "http://i.imgur.com/Gr5G3YI.png",
  "Image Color": "1 1 1 0.3",
  "Image AnchorMin": "0.175 0.017",
  "Image AnchorMax": "0.22 0.08"
}
```

## Localization

> The default messages are in the `Vanish` file under the `lang/en` directory. To add support for another language, create a new language folder (e.g. `de` for German) if not already created, copy the default language file to the new folder and then customize the messages.

## For Developers

### API

```cs
void Disappear(BasePlayer player)
```
```cs
void Reappear(BasePlayer player)
```
```cs
bool IsInvisible(BasePlayer player)
```

### Hooks

```cs
void OnVanishReappear(BasePlayer player)
```
```cs
void OnVanishDisappear(BasePlayer player)
```

## Credits
* Wulf, the original author of this plugin
* Nogrod, for all the help along the way. Cheers!
* Jake_Rich and nivex, for helping maintain the plugin
* dcode, for the awesome icon