using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Oxide.Plugins
{
    [Info("Sharing API", "WhiteThunder", "0.2.0")]
    [Description("Central API for players to manage sharing preferences.")]
    internal class SharingAPI : CovalencePlugin
    {
        #region Fields

        [PluginReference]
        private Plugin Clans, Friends;

        private static SharingAPI _instance;
        private Dictionary<string, ShareTypeEntry> _shareTypes = new Dictionary<string, ShareTypeEntry>();
        private PreferencesManager _preferencesManager = new PreferencesManager();

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

        private void OnPluginUnloaded(Plugin plugin)
        {
            ShareTypeEntry matchingShareType = null;
            foreach (var shareType in _shareTypes.Values)
            {
                if (shareType.Plugin == plugin)
                    matchingShareType = shareType;
            }

            if (matchingShareType != null)
                _shareTypes.Remove(matchingShareType.TypeName);
        }

        #endregion

        #region API

        private void API_RegisterShareType(Plugin plugin, string shareType, Dictionary<string, bool> shareDefaults)
        {
            _shareTypes[shareType] = ShareTypeEntry.Create(plugin, shareType, shareDefaults);
        }

        private Func<string, string, bool> API_GetIsSharingWithCallback(string typeName)
        {
            var shareTypeEntry = GetShareTypeEntry(typeName);
            return shareTypeEntry != null
                ? GetIsSharingWithCallback(shareTypeEntry)
                : null;
        }

        private Dictionary<string, bool> API_GetSharingPreferences(string shareType, string userId)
        {
            var shareTypeEntry = GetShareTypeEntry(shareType);
            if (shareTypeEntry == null)
                return null;

            // TODO: Consider optimizing performance if this will be called often.
            return _preferencesManager.GetSettingsForRead(shareTypeEntry, userId).ToDictionary();
        }

        private void API_ImportPreferences(string userId, Dictionary<string, Dictionary<string, bool>> sharingSettingsByType)
        {
            _preferencesManager.GetForWrite(userId).Import(sharingSettingsByType);
        }

        private ulong[] API_GetSharedWithIds(ulong userId, string shareType)
        {
            var userIdString = userId.ToString();

            var shareTypeEntry = GetShareTypeEntry(shareType);
            if (shareTypeEntry == null)
                return null;

            var sharingSettings = _preferencesManager.GetSettingsForRead(shareTypeEntry, userIdString);
            if (!shareTypeEntry.AnyEnabled())
                return null;

            var sharedWithUserIds = new HashSet<ulong>();

            if (shareTypeEntry.Enabled(RecipientType.Team) && sharingSettings.Team)
                CollectTeamMembers(userId, sharedWithUserIds);

            if (shareTypeEntry.Enabled(RecipientType.Friends) && sharingSettings.Friends)
                CollectFriends(userId, sharedWithUserIds);

            if (shareTypeEntry.Enabled(RecipientType.Clan) && sharingSettings.Clan)
                CollectClanMembers(userId, sharedWithUserIds);

            if (shareTypeEntry.Enabled(RecipientType.Allies) && sharingSettings.Allies)
                CollectClanAllies(userId, sharedWithUserIds);

            return sharedWithUserIds.ToArray();
        }

        #endregion

        #region Commands

        // Really basic implementation for initial testing.
        [Command("listshares")]
        private void CommandShare(IPlayer player, string cmd, string[] args)
        {
            if (player.IsServer)
                return;

            var outputLines = new List<string>();
            foreach (var shareTypeEntry in _shareTypes.Values)
            {
                if (!shareTypeEntry.Plugin.IsLoaded || !shareTypeEntry.AnyEnabled())
                    continue;

                outputLines.Add(GetShareTypeShortName(player.Id, shareTypeEntry));

                var sharingSettings = _preferencesManager.GetSettingsForRead(shareTypeEntry, player.Id);

                if (shareTypeEntry.Enabled(RecipientType.Team))
                    outputLines.Add(" - " + GetMessage(player, Lang.Team) + ": " + GetMessage(player, sharingSettings.Team ? Lang.Enabled : Lang.Disabled));

                if (shareTypeEntry.Enabled(RecipientType.Friends))
                    outputLines.Add(" - " + GetMessage(player, Lang.Friends) + ": " + GetMessage(player, sharingSettings.Friends ? Lang.Enabled : Lang.Disabled));

                if (shareTypeEntry.Enabled(RecipientType.Clan))
                    outputLines.Add(" - " + GetMessage(player, Lang.Clan) + ": " + GetMessage(player, sharingSettings.Clan ? Lang.Enabled : Lang.Disabled));

                if (shareTypeEntry.Enabled(RecipientType.Allies))
                    outputLines.Add(" - " + GetMessage(player, Lang.Allies) + ": " + GetMessage(player, sharingSettings.Allies ? Lang.Enabled : Lang.Disabled));
            }

            if (outputLines.Count == 0)
                player.Reply(GetMessage(player, Lang.ErrorNoShareTypes));
            else
                player.Reply(string.Join("\n", outputLines));
        }

        // Really basic implementation for initial testing.
        [Command("toggleshare")]
        private void CommandToggleShare(IPlayer player, string cmd, string[] args)
        {
            if (args.Length < 2)
            {
                player.Reply(GetMessage(player, Lang.ErrorToggleShareSyntax));
                return;
            }

            // Match share type using translated name.
            var shareTypeEntry = ParseShareType(player, args[0]);
            if (shareTypeEntry == null)
            {
                player.Reply(GetMessage(player, Lang.ErrorUnrecognizedShareType, args[0]));
                return;
            }

            RecipientType recipientType;
            if (!TryParseRecipientType(player, args[1], out recipientType))
            {
                player.Reply(GetMessage(player, Lang.ErrorUnrecognizedRecipientType, args[1]));
                return;
            }

            var preferences = _preferencesManager.GetForWrite(player.Id);
            if (!shareTypeEntry.Enabled(recipientType))
            {
                player.Reply(GetMessage(player, Lang.ErrorNoEditPermissions, args[2]));
                return;
            }

            preferences.ToggleSharing(shareTypeEntry, recipientType);
        }

        #endregion

        #region Helper Methods

        private static TV GetDictValueOrDefault<TK, TV>(Dictionary<TK, TV> dict, TK key, TV defaultValue = default(TV))
        {
            TV value;
            return dict.TryGetValue(key, out value) ? value : defaultValue;
        }

        private Func<string, string, bool> GetIsSharingWithCallback(ShareTypeEntry shareTypeEntry) =>
            (userId, otherUserId) => IsSharingWith(shareTypeEntry, userId, otherUserId);

        private bool IsSharingWith(ShareTypeEntry shareTypeEntry, string userId, string otherUserId)
        {
            if (userId == otherUserId)
                return true;

            var sharingSettings = _preferencesManager.GetSettingsForRead(shareTypeEntry, userId);

            if (shareTypeEntry.Enabled(RecipientType.Team)
                && sharingSettings.Team
                && SameTeam(userId, otherUserId))
                return true;

            if (shareTypeEntry.Enabled(RecipientType.Friends)
                && sharingSettings.Friends
                && HasFriend(userId, otherUserId))
                return true;

            if (shareTypeEntry.Enabled(RecipientType.Clan)
                && sharingSettings.Clan
                && SameClan(userId, otherUserId))
                return true;

            if (shareTypeEntry.Enabled(RecipientType.Allies)
                && sharingSettings.Allies
                && IsAlly(userId, otherUserId))
                return true;

            return false;
        }

        private bool SameTeam(string userId, string otherUserId) =>
            RelationshipManager.ServerInstance.FindPlayersTeam(Convert.ToUInt64(userId))?.members.Contains(Convert.ToUInt64(otherUserId)) ?? false;

        private bool HasFriend(string userId, string otherUserId)
        {
            var friendsResult = Friends?.Call("HasFriend", userId, otherUserId);
            return friendsResult is bool && (bool)friendsResult;
        }

        private bool SameClan(string userId, string otherUserId)
        {
            var clanResult = Clans?.Call("IsClanMember", userId, otherUserId);
            return clanResult is bool && (bool)clanResult;
        }

        private bool IsAlly(string userId, string otherUserId)
        {
            var clanResult = Clans?.Call("IsAllyPlayer", userId, otherUserId);
            return clanResult is bool && (bool)clanResult;
        }

        private void CollectTeamMembers(ulong userId, HashSet<ulong> collect)
        {
            var team = RelationshipManager.ServerInstance.FindPlayersTeam(userId);
            if (team == null)
                return;

            foreach (var memberId in team.members)
                collect.Add(memberId);
        }

        private void CollectFriends(ulong userId, HashSet<ulong> collect)
        {
            if (Friends == null)
                return;

            var friends = Friends.Call("GetFriends", userId) as ulong[];
            foreach (var friendId in friends)
                collect.Add(friendId);
        }

        private void CollectClanMembers(ulong userId, HashSet<ulong> collect)
        {
            if (Clans == null)
                return;

            var members = Clans.Call("GetClanMembers", userId.ToString()) as List<string>;

            foreach (var memberIdString in members)
                collect.Add(Convert.ToUInt64(memberIdString));
        }

        private void CollectClanAllies(ulong userId, HashSet<ulong> collect)
        {
            if (Clans == null)
                return;

            var clanNames = Clans.Call("GetClanAlliances", userId.ToString()) as List<string>;
            foreach (var name in clanNames)
            {
                var clanObj = Clans.Call("GetClan", name) as JObject;
                var members = (clanObj?.GetValue("members") as JArray).Select(Convert.ToUInt64);

                foreach (var memberId in members)
                    collect.Add(memberId);
            }
        }

        #endregion

        #region Share Types

        private class ShareTypeEntry
        {
            public static ShareTypeEntry Create(Plugin plugin, string typeName, Dictionary<string, bool> defaults)
            {
                var shareType = new ShareTypeEntry()
                {
                    Plugin = plugin,
                    TypeName = typeName,
                    Defaults = new SharingSettings(),
                    CustomizePerimssions = new CustomizePermissions(),
                };

                bool team, friends, clan, allies;

                if (defaults.TryGetValue(RecipientType.Team.ToString(), out team))
                {
                    shareType.Defaults.Team = team;
                    shareType.CustomizePerimssions.Team = true;
                }

                if (defaults.TryGetValue(RecipientType.Friends.ToString(), out friends))
                {
                    shareType.Defaults.Friends = friends;
                    shareType.CustomizePerimssions.Friends = true;
                }

                if (defaults.TryGetValue(RecipientType.Clan.ToString(), out clan))
                {
                    shareType.Defaults.Clan = clan;
                    shareType.CustomizePerimssions.Clan = true;
                }

                if (defaults.TryGetValue(RecipientType.Allies.ToString(), out allies))
                {
                    shareType.Defaults.Allies = allies;
                    shareType.CustomizePerimssions.Allies = true;
                }

                return shareType;
            }

            public Plugin Plugin;
            public string TypeName;
            public SharingSettings Defaults;
            private CustomizePermissions CustomizePerimssions;

            public bool AnyEnabled()
            {
                return Enabled(RecipientType.Team)
                    || Enabled(RecipientType.Friends)
                    || Enabled(RecipientType.Clan)
                    || Enabled(RecipientType.Allies);
            }

            public bool Enabled(RecipientType recipientType)
            {
                switch (recipientType)
                {
                    case RecipientType.Team: return CustomizePerimssions.Team;
                    case RecipientType.Friends: return CustomizePerimssions.Friends;
                    case RecipientType.Clan: return CustomizePerimssions.Clan;
                    case RecipientType.Allies: return CustomizePerimssions.Allies;
                    default: return false;
                }
            }
        }

        private class CustomizePermissions
        {
            public bool Team = false;
            public bool Friends = false;
            public bool Clan = false;
            public bool Allies = false;
        }

        private ShareTypeEntry GetShareTypeEntry(string typeName)
        {
            ShareTypeEntry sharingType;
            return _shareTypes.TryGetValue(typeName, out sharingType)
                ? sharingType
                : null;
        }

        private ShareTypeEntry ParseShareType(IPlayer player, string rawName)
        {
            foreach (var entry in _shareTypes)
            {
                var lowerName = rawName.ToLowerInvariant();
                if (lowerName == GetShareTypeShortName(player.Id, entry.Value).ToLowerInvariant()
                    || lowerName == GetShareTypeAbbreviation(player.Id, entry.Value).ToLowerInvariant())
                    return entry.Value;
            }

            return null;
        }

        // Don't rename since these are exposed by the API and hooks.
        private enum RecipientType { Team, Friends, Clan, Allies }

        private bool TryParseRecipientType(IPlayer player, string rawName, out RecipientType recipientType)
        {
            var lowerName = rawName.ToLowerInvariant();
            if (lowerName == GetMessage(player, Lang.Team).ToLowerInvariant())
            {
                recipientType = RecipientType.Team;
                return true;
            }

            if (lowerName == GetMessage(player, Lang.Friends).ToLowerInvariant())
            {
                recipientType = RecipientType.Friends;
                return true;
            }

            if (lowerName == GetMessage(player, Lang.Clan).ToLowerInvariant())
            {
                recipientType = RecipientType.Clan;
                return true;
            }

            if (lowerName == GetMessage(player, Lang.Allies).ToLowerInvariant())
            {
                recipientType = RecipientType.Allies;
                return true;
            }

            recipientType = RecipientType.Team;
            return false;
        }

        #endregion

        #region Data

        private class SharingSettings
        {
            public static SharingSettings FromDictionary(Dictionary<string, bool> dict)
            {
                var sharingSettings = new SharingSettings();

                bool team, friends, clan, allies;

                if (dict.TryGetValue(RecipientType.Team.ToString(), out team))
                    sharingSettings.Team = team;

                if (dict.TryGetValue(RecipientType.Friends.ToString(), out friends))
                    sharingSettings.Friends = friends;

                if (dict.TryGetValue(RecipientType.Clan.ToString(), out clan))
                    sharingSettings.Clan = clan;

                if (dict.TryGetValue(RecipientType.Allies.ToString(), out allies))
                    sharingSettings.Allies = allies;

                return sharingSettings;
            }

            [JsonProperty("Team")]
            public bool Team = false;

            [JsonProperty("Friends")]
            public bool Friends = false;

            [JsonProperty("Clan")]
            public bool Clan = false;

            [JsonProperty("Allies")]
            public bool Allies = false;

            public SharingSettings Copy()
            {
                return new SharingSettings()
                {
                    Team = Team,
                    Friends = Friends,
                    Clan = Clan,
                    Allies = Allies,
                };
            }

            public Dictionary<string, bool> ToDictionary()
            {
                return new Dictionary<string, bool>()
                {
                    [RecipientType.Team.ToString()] = Team,
                    [RecipientType.Friends.ToString()] = Friends,
                    [RecipientType.Clan.ToString()] = Clan,
                    [RecipientType.Allies.ToString()] = Allies,
                };
            }
        }

        private class PreferencesManager
        {
            private Dictionary<string, PlayerPreferences> _preferencesCache = new Dictionary<string, PlayerPreferences>();

            private PlayerPreferences GetCachedPreferences(string userId) =>
                GetDictValueOrDefault(_preferencesCache, userId);

            public SharingSettings GetSettingsForRead(ShareTypeEntry shareTypeEntry, string userId)
            {
                var preferences = GetCachedPreferences(userId) ?? PlayerPreferences.Load(userId);
                return preferences?.GetSettingsOfType(shareTypeEntry) ?? shareTypeEntry.Defaults;
            }

            public PlayerPreferences GetForWrite(string userId)
            {
                var preferences = GetCachedPreferences(userId) ?? PlayerPreferences.Load(userId, createIfMissing: true);
                _preferencesCache[userId] = preferences;
                return preferences;
            }
        }

        private class PlayerPreferences
        {
            private static string GetFilename(string userId) => $"{_instance.Name}/{userId}";

            public static PlayerPreferences Load(string userId, bool createIfMissing = false)
            {
                var filename = GetFilename(userId);

                PlayerPreferences preferences = null;

                if (Interface.Oxide.DataFileSystem.ExistsDatafile(filename))
                    preferences = Interface.Oxide.DataFileSystem.ReadObject<PlayerPreferences>(filename);

                if (createIfMissing && preferences == null)
                    preferences = new PlayerPreferences() { UserId = userId };

                return preferences;
            }

            public void Save() =>
                Interface.Oxide.DataFileSystem.WriteObject(GetFilename(UserId), this);

            public SharingSettings GetSettingsOfType(ShareTypeEntry shareTypeEntry) =>
                GetDictValueOrDefault(ShareSettingsByType, shareTypeEntry.TypeName);

            public SharingSettings GetOrCreateSettingsOfType(ShareTypeEntry shareTypeEntry)
            {
                var sharingSettings = GetSettingsOfType(shareTypeEntry);
                if (sharingSettings != null)
                    return sharingSettings;

                sharingSettings = shareTypeEntry.Defaults.Copy();
                ShareSettingsByType[shareTypeEntry.TypeName] = sharingSettings;
                return sharingSettings;
            }

            public bool Import(Dictionary<string, Dictionary<string, bool>> importSettings)
            {
                foreach (var entry in importSettings)
                {
                    // Skip if that player already has preferences.
                    if (ShareSettingsByType.ContainsKey(entry.Key))
                        continue;

                    ShareSettingsByType[entry.Key] = SharingSettings.FromDictionary(entry.Value);
                }

                return true;
            }

            public bool ToggleSharing(ShareTypeEntry shareTypeEntry, RecipientType recipientType)
            {
                var sharingSettings = GetOrCreateSettingsOfType(shareTypeEntry);

                bool newValue;
                switch (recipientType)
                {
                    case RecipientType.Team:
                        newValue = sharingSettings.Team = !sharingSettings.Team;
                        break;
                    case RecipientType.Friends:
                        newValue = sharingSettings.Friends = !sharingSettings.Friends;
                        break;
                    case RecipientType.Clan:
                        newValue = sharingSettings.Clan = !sharingSettings.Clan;
                        break;
                    case RecipientType.Allies:
                        newValue = sharingSettings.Allies = !sharingSettings.Allies;
                        break;
                    default:
                        return false;
                }

                Save();
                Interface.CallHook("OnSharingPreferenceChanged", UserId, shareTypeEntry.TypeName, recipientType.ToString(), newValue);
                return newValue;
            }

            [JsonProperty("UserId")]
            public string UserId;

            [JsonProperty("ShareSettingsByType")]
            public Dictionary<string, SharingSettings> ShareSettingsByType = new Dictionary<string, SharingSettings>();
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

        private string GetShareTypeShortName(string userId, ShareTypeEntry shareTypeEntry) =>
            lang.GetMessage(shareTypeEntry.TypeName + ".ShortName", shareTypeEntry.Plugin, userId);

        private string GetShareTypeAbbreviation(string userId, ShareTypeEntry shareTypeEntry) =>
            lang.GetMessage(shareTypeEntry.TypeName + ".Abbreviation", shareTypeEntry.Plugin, userId);

        private string GetShareTypeDescription(string userId, ShareTypeEntry shareTypeEntry) =>
            lang.GetMessage(shareTypeEntry.TypeName + ".Description", shareTypeEntry.Plugin, userId);

        private class Lang
        {
            public const string Team = "Team";
            public const string Friends = "Friends";
            public const string Clan = "Clan";
            public const string Allies = "Allies";

            public const string Enabled = "Enabled";
            public const string Disabled = "Disabled";

            public const string ErrorToggleShareSyntax = "Error.ToggleShareSyntax";
            public const string ErrorUnrecognizedShareType = "Error.UnrecognizedShareType";
            public const string ErrorUnrecognizedRecipientType = "Error.UnrecognizedRecipientType";
            public const string ErrorNoShareTypes = "Error.NoShareTypes";
            public const string ErrorNoEditPermissions = "Error.NoEditPermissions";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.Team] = "Team",
                [Lang.Friends] = "Friends",
                [Lang.Clan] = "Clan",
                [Lang.Allies] = "Allies",
                [Lang.Enabled] = "Enabled",
                [Lang.Disabled] = "Disabled",
                [Lang.ErrorToggleShareSyntax] = "Syntax: toggleshare <cupboard|box|etc> <team|friends|clan|allies>",
                [Lang.ErrorUnrecognizedShareType] = "Error: Unrecognized share type: {0}",
                [Lang.ErrorUnrecognizedRecipientType] = "Error: Unrecognized recipient type: {0}",
                [Lang.ErrorNoShareTypes] = "No share types available",
                [Lang.ErrorNoEditPermissions] = "Error: Not allowed to edit that option.",
            }, this, "en");
        }

        #endregion
    }
}
