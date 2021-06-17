using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;
using System.Collections.Generic;

// TODO: Permissions / config to determine what preferences players can manage
// TODO: Commands to manage player-specific preferences
// TODO: Aliases like "c" for "cupboard"
// TODO: UI

namespace Oxide.Plugins
{
    [Info("Sharing API", "WhiteThunder", "0.1.0")]
    [Description("Central API for players to manage sharing preferences.")]
    internal class SharingAPI : CovalencePlugin
    {
        #region Fields

        private static SharingAPI _instance;
        private Dictionary<string, ShareType> _shareTypes = new Dictionary<string, ShareType>();

        #endregion

        #region Hooks

        private void Init()
        {
            _instance = this;
        }

        private void Unload()
        {
            _instance = null;
        }

        #endregion

        #region API

        private void RegisterShareType(Plugin plugin, string typeName, string langKey, Dictionary<string, bool> defaultSettings)
        {
            _shareTypes[typeName] = new ShareType()
            {
                Plugin = plugin,
                TypeName = typeName,
                LangKey = langKey,
                DefaultSettings = defaultSettings,
            };
        }

        private Dictionary<string, bool> GetSharePreferences(string typeName, string userId)
        {
            var sharingType = GetShareType(typeName);
            if (sharingType == null || sharingType.Plugin == null)
                return null;

            return PlayerPreferences.Load(userId).Settings[typeName];
        }

        #endregion

        #region Share Types

        private ShareType GetShareType(string typeName)
        {
            ShareType sharingType;
            return _shareTypes.TryGetValue(typeName, out sharingType)
                ? sharingType
                : null;
        }

        private class ShareType
        {
            public Plugin Plugin;
            public string TypeName;
            public string LangKey;
            public Dictionary<string, bool> DefaultSettings;

            public bool GetDefault(string recipientType)
            {
                if (DefaultSettings == null)
                    return false;

                bool value;
                return DefaultSettings.TryGetValue(recipientType, out value)
                    ? value
                    : false;
            }
        }

        #endregion

        #region Commands

        // Really basic implementation for initial testing.
        [Command("listsharetypes")]
        private void CommandShare(IPlayer player, string cmd, string[] args)
        {
            if (player.IsServer)
                return;

            var shareTypeLabels = new List<string>();
            foreach (var shareType in _shareTypes.Values)
            {
                if (shareType.Plugin.IsLoaded)
                    shareTypeLabels.Add(GetShareTypeLabel(player.Id, shareType));
            }

            if (shareTypeLabels.Count == 0)
                player.Reply(GetMessage(player, "Error.NoShareTypes"));
            else
                player.Reply(string.Join("\n", shareTypeLabels));
        }

        // Really basic implementation for initial testing.
        [Command("toggleshare")]
        private void CommandToggleShare(IPlayer player, string cmd, string[] args)
        {
            if (args.Length < 2)
            {
                player.Reply(GetMessage(player, "Error.ToggleShareSyntax"));
                return;
            }

            // Match share type using translated name.
            var shareType = ParseShareType(player, args[0]);
            if (shareType == null)
            {
                player.Reply(GetMessage(player, "Error.UnrecognizedShareType", args[0]));
                return;
            }

            var recipientType = ParseRecipientType(player, args[1]);
            if (recipientType == null)
            {
                player.Reply(GetMessage(player, "Error.UnrecognizedRecipientType", args[1]));
                return;
            }

            PlayerPreferences.Load(player.Id).TogglePreference(shareType, recipientType);
        }

        #endregion

        #region Helper Methods

        private ShareType ParseShareType(IPlayer player, string rawName)
        {
            foreach (var entry in _shareTypes)
            {
                var localizedName = GetShareTypeLabel(player.Id, entry.Value);
                if (localizedName.ToLowerInvariant() == rawName.ToLowerInvariant())
                    return entry.Value;
            }

            return null;
        }

        private string ParseRecipientType(IPlayer player, string rawName)
        {
            if (rawName.ToLowerInvariant() == GetMessage(player, "Team").ToLowerInvariant())
                return "Team";

            if (rawName.ToLowerInvariant() == GetMessage(player, "Friends").ToLowerInvariant())
                return "Friends";

            if (rawName.ToLowerInvariant() == GetMessage(player, "Clan").ToLowerInvariant())
                return "Clan";

            if (rawName.ToLowerInvariant() == GetMessage(player, "Allies").ToLowerInvariant())
                return "Allies";

            return null;
        }

        #endregion

        #region Data

        private class PlayerPreferences
        {
            private static string GetFilename(string userId) => $"{_instance.Name}/{userId}";

            public static PlayerPreferences Load(string userId)
            {
                var filename = GetFilename(userId);

                PlayerPreferences data = null;
                if (Interface.Oxide.DataFileSystem.ExistsDatafile(filename))
                    data = Interface.Oxide.DataFileSystem.ReadObject<PlayerPreferences>(filename);

                // Either there was no file, or ReadObject returned null (happens in some cases due to corruption).
                if (data == null)
                    data = new PlayerPreferences() { UserId = userId };

                return data;
            }

            public void Save() =>
                Interface.Oxide.DataFileSystem.WriteObject(GetFilename(UserId), this);

            public bool TogglePreference(ShareType shareType, string recipientType)
            {
                Dictionary<string, bool> typeSettings;
                if (!Settings.TryGetValue(shareType.TypeName, out typeSettings))
                {
                    typeSettings = new Dictionary<string, bool>();
                    Settings[shareType.TypeName] = typeSettings;
                }

                bool currentValue;
                if (!typeSettings.TryGetValue(recipientType, out currentValue))
                    currentValue = shareType.GetDefault(recipientType);

                var newValue = !currentValue;
                typeSettings[recipientType] = newValue;
                Save();

                return newValue;
            }

            [JsonProperty("UserId")]
            public string UserId;

            [JsonProperty("SharingSettings")]
            public Dictionary<string, Dictionary<string, bool>> Settings = new Dictionary<string, Dictionary<string, bool>>();
        }

        #endregion

        #region Localization

        private string GetMessage(BasePlayer player, string messageName, params object[] args) =>
            GetMessage(player.UserIDString, messageName, args);

        private string GetMessage(IPlayer player, string messageName, params object[] args) =>
            GetMessage(player.Id, messageName, args);

        private string GetMessage(string playerId, string messageName, params object[] args)
        {
            var message = lang.GetMessage(messageName, this, playerId);
            return args.Length > 0 ? string.Format(message, args) : message;
        }

        private string GetShareTypeLabel(string userId, ShareType shareType) =>
            lang.GetMessage(shareType.LangKey, shareType.Plugin, userId);

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Team"] = "Team",
                ["Friends"] = "Friends",
                ["Clan"] = "Clan",
                ["Allies"] = "Allies",
                ["Enabled"] = "Enabled",
                ["Disabled"] = "Disabled",
                ["Error.ToggleShareSyntax"] = "Syntax: toggleshare <cupboard|box|etc> <team|friends|clan|allies>",
                ["Error.UnrecognizedShareType"] = "Error: Unrecognized share type: {0}",
                ["Error.UnrecognizedRecipientType"] = "Error: Unrecognized recipient type: {0}",
                ["Error.NoShareTypes"] = "No share types available",
            }, this, "en");
        }

        #endregion
    }
}
