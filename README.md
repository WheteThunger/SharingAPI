## Features

- Allows players to centrally manage sharing preferences for multiple plugins
- Allows plugins to delegate responsibility for management of sharing preferences
- Allows plugins to register custom sharing types

## Developer API

### API_RegisterShareType

```csharp
void API_RegisterShareType(Plugin plugin, string shareType, Dictionary<string, bool> defaultSettings)
```

- Registers a share type
- If a share type is already registered with the specified name, the existing one will be overriden

#### Example

```csharp
SharingAPI.Call("API_RegisterShareType", this, "turret", new Dictionary<string, bool>
{
    // These defaults apply to players who have not changed any preferences for this share type.
    ["Team"] = true,
    ["Friends"] = false,
    ["Clan"] = false,
    ["Allies"] = false,
});
```

### API_GetIsSharingWithCallback

```csharp
Func<string, string, bool> API_GetIsSharingWithCallback(string shareType)
```

- Returns a function that can be used to determine if one player is sharing with another
- Returns `null` if the specified share type is not registered, or if the plugin that registered that share type is not loaded
- Useful for determining if a SAM Site should target a player

#### Example

```csharp
var isSharingWith = SharingAPI.Call("API_GetIsSharingWithCallback", "samsite") as Func<string, string, bool>;

// Will return true if the first steam id is sharing SAM Sites with the second steam id
isSharingWith("76561197960287930", "76561197960265754")
```

### API_GetSharedWithIds

```csharp
ulong[] API_GetSharedWithIds(ulong userId, string shareType)
```

- Returns an array of steamids that the user is sharing with
- Depending on the user's sharing settings, this may include their teammates, friends, clan and allies
- Useful for determining which steamids to add to a cupboard or auto turret when deployed

### API_GetSharingPreferences

```csharp
Dictionary<string, bool> API_GetSharingPreferences(string shareType, string userId)
```

- Returns a readonly copy of the player's sharing preference for the specified share type

#### Example return value

```csharp
new Dictionary<string, bool>
{
    ["Team"] = true,
    ["Friends"] = false,
    ["Clan"] = false,
    ["Allies"] = false,
}
```

### API_ImportPreferences

```csharp
void API_ImportPreferences(string userId, Dictionary<string, Dictionary<string, bool>> sharingSettingsByType)
```

- Imports sharing preference from another plugin, for a specific user
- Useful if you want to migrate from having your plugin manages preferences, to allow this plugin to manage them for you

#### Example

```csharp
SharingAPI.Call("API_ImportPreferences", "76561197960287930", new Dictionary<string, Dictionary<string, bool>>
{
    ["turret"] = new Dictionary<string, bool>
    {
        ["Team"] = true,
        ["Friends"] = false,
        ["Clan"] = false,
        ["Allies"] = false,
    },
    ["cupboard"] = new Dictionary<string, bool>
    {
        ["Team"] = true,
        ["Friends"] = false,
        ["Clan"] = false,
        ["Allies"] = false,
    },
});
```

## Full example

```csharp
[PluginReference]
private Plugin SharingAPI;

private Func<string, string, bool> _isSharingCupboardWith;
private Func<string, string, bool> _isSharingTurretWith;

private void OnServerInitialized()
{
    RegisterShareTypes();
}

private void OnPluginLoaded(Plugin plugin)
{
    if (plugin == SharingAPI)
        RegisterShareTypes();
}

private void OnPluginUnloaded(Plugin plugin)
{
    if (plugin == SharingAPI)
    {
        _isSharingCupboardWith = null;
        _isSharingTurretWith = null;
    }
}

private void RegisterShareTypes()
{
    SharingAPI?.Call("API_RegisterShareType", this, "cupboard", new Dictionary<string, bool>()
    {
        ["Team"] = true,
        ["Friends"] = false,
        ["Clan"] = false,
        ["Allies"] = false,
    });
    SharingAPI?.Call("API_RegisterShareType", this, "turret", new Dictionary<string, bool>()
    {
        ["Team"] = true,
        ["Friends"] = false,
        ["Clan"] = false,
        ["Allies"] = false,
    });

    _isSharingCupboardWith = SharingAPI?.Call("API_GetIsSharingWithCallback", "cupboard");
    _isSharingTurretWith = SharingAPI?.Call("API_GetIsSharingWithCallback", "turret");
}

protected override void LoadDefaultMessages()
{
    lang.RegisterMessages(new Dictionary<string, string>
    {
        ["cupboard.ShortName"] = "cupboard",
        ["cupboard.Abbreviation"] = "c",
        ["cupboard.Description"] = "Share Cupboard",

        ["turret.ShortName"] = "turret",
        ["turret.Abbreviation"] = "t",
        ["turret.Description"] = "Share Turret",
    }, this, "en");
}

// Auto authorize players to tool cupboards.
private void OnEntityBuilt(Planner plan, GameObject gameObject)
{
    var entity = gameObject.ToBaseEntity();
    if (entity == null)
        return;

    var cupboard = entity as BuildingPrivlidge;
    if (cupboard == null)
        return;

    var sharedWithUsers = SharingAPI?.Call("API_GetSharedWithIds", entity.OwnerID, "cupboard") as ulong[];
    if (sharedWithUsers == null)
        return;

    foreach (var userId in sharedWithUsers)
        cupboard.authorizedPlayers.Add(new ProtoBuf.PlayerNameID { userid = userId, username = string.Empty });
}

// Don't target players that turrets are shared with.
private bool? OnTurretTarget(AutoTurret turret, BasePlayer player)
{
    if (SharingAPI != null
        && _isSharingTurretWith != null
        && _isSharingTurretWith(turret.OwnerId.ToString(), player.UserIdString))
        return false;

    return null;
}
```

## Developer Hooks

### OnSharingPreferenceChanged

```csharp
void OnSharingPreferenceChanged(string userId, string shareType, string recipientType, bool newValue)
```

- Called after a player's sharing preference has changed
- No return behavior
- Possible values for `recipientType`: `"Team"` | `"Friends"` | `"Clan"` | `"Allies"`

## Testing Info

### Command usage

This is just an example command for testing.
- `toggleshare cubpoard team`
- `toggleshare turret friends`
- `toggleshare codelockdoor clan`
- `toggleshare c allies`

### Example data file

`oxide/data/SharingAPI/76561197960287930.json`:
```json
{
  "UserId": "76561197960287930",
  "ShareSettingsByType": {
    "cupboard": {
      "Team": true,
      "Friends": true,
      "Clan": false,
      "Allies": false
    },
    "turret": {
      "Team": false,
      "Friends": false,
      "Clan": false,
      "Allies": false
    }
  }
}
```
