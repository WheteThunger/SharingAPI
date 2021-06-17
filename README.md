## Features

- Allows players to manage sharing preferences
- Allows plugins to register custom sharing types

## Developer API

```csharp
void RegisterShareType(Plugin plugin, string typeName, string langKey, Dictionary<string, bool> defaultSettings)
```

```csharp
Dictionary<string, bool> GetSharePreferences(string typeName, string userId)
```

### Example usage

```csharp
[PluginReference]
private Plugin SharingAPI;

private void OnServerInitialized()
{
    RegisterShareType();
}

private void OnPluginLoaded(Plugin plugin)
{
    if (plugin == SharingAPI)
        RegisterShareType();
}

private void RegisterShareType()
{
    SharingAPI?.Call("RegisterShareType", this, "myshare", "ExampleLangKey", new Dictionary()
    {
        // These should probably be managed in the config of the plugin making this API call.
        ["Team"] = true,
        ["Friends"] = false,
        ["Clan"] = false,
        ["Allies"] = false,
    });
}

protected override void LoadDefaultMessages()
{
    lang.RegisterMessages(new Dictionary<string, string>
    {
        ["ExampleLangKey"] = "MyShareType",
    }, this, "en");
}
```

### Command usage

This is just an example command for testing.
- `togglesharetype mysharetype team`
- `togglesharetype mysharetype friends`
- `togglesharetype mysharetype clan`
- `togglesharetype mysharetype allies`

### Example data file

`oxide/data/SharingAPI/76561197960287930.json`:
```json
{
  "UserId": "76561197960287930",
  "SharingSettings": {
    "myshare": {
      "Team": false,
      "Friends": false,
      "Clan": false,
      "Allies": false
    }
  }
}
```
