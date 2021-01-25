using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using Rust;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Oxide.Plugins
{
    [Info("Clans", "Reheight", "1.0")]
    [Description("Allow your members to form clans and dynamically manage their members.")]
    public class Clans : RustPlugin
    {
        #region Data Values

        private Dictionary<string, Clan> clans = new Dictionary<string, Clan>();
        private Dictionary<string, string> originalNames = new Dictionary<string, string>();
        private Regex tagRe = new Regex("^[a-zA-Z0-9]{2,6}$");
        private Dictionary<string, string> messages = new Dictionary<string, string>();
        private Dictionary<string, Clan> lookup = new Dictionary<string, Clan>();
        private int limitMembers = -1;
        private int limitOfficers = -1;

        #endregion

        #region Loading Data

        private void loadData()
        {
            clans.Clear();

            var data = Interface.Oxide.DataFileSystem.GetDatafile("clans_rewritten");

            if (data["clans"] != null)
            {
                var clansData = (Dictionary<string, object>)Convert.ChangeType(data["clans"], typeof(Dictionary<string, object>));

                foreach (var iclan in clansData)
                {
                    string tag = iclan.Key;
                    var clanData = iclan.Value as Dictionary<string, object>;
                    bool friendlyfire = (bool)clanData["friendlyfire"];
                    bool open = (bool)clanData["open"];
                    string leader = (string)clanData["leader"];

                    List<string> officers = new List<string>();
                    foreach (var officer in clanData["officers"] as List<object>)
                        officers.Add((string)officer);

                    List<string> members = new List<string>();
                    foreach (var member in clanData["members"] as List<object>)
                        members.Add((string)member);

                    List<string> invited = new List<string>();
                    foreach (var invite in clanData["invited"] as List<object>)
                        invited.Add((string)invite);

                    Clan clan;

                    clans.Add(tag, clan = new Clan()
                    {
                        tag = tag,
                        friendlyfire = friendlyfire,
                        open = open,
                        leader = leader,
                        officers = officers,
                        members = members,
                        invited = invited
                    });

                    lookup[leader] = clan;

                    foreach (var member in members)
                        lookup[member] = clan;
                }
            }
        }

        #endregion

        #region Saving Data

        private void saveData()
        {
            var data = Interface.Oxide.DataFileSystem.GetDatafile("clans_rewritten");

            var clansData = new Dictionary<string, object>();

            foreach (var clan in clans)
            {
                var clanData = new Dictionary<string, object>();

                clanData.Add("tag", clan.Value.tag);
                clanData.Add("leader", clan.Value.leader);
                clanData.Add("friendlyfire", clan.Value.friendlyfire);
                clanData.Add("open", clan.Value.open);

                var officers = new List<object>();
                foreach (var officer in clan.Value.officers)
                    officers.Add(officer);

                var members = new List<object>();
                foreach (var member in clan.Value.members)
                    members.Add(member);

                var invited = new List<object>();
                foreach (var invite in clan.Value.invited)
                    invited.Add(invite);

                clanData.Add("officers", officers);
                clanData.Add("members", members);
                clanData.Add("invited", invited);

                clansData.Add(clan.Value.tag, clanData);
            }

            data["clans"] = clansData;

            Interface.Oxide.DataFileSystem.SaveDatafile("clans_rewritten");
        }

        #endregion

        #region
        private List<string> blacklistedWords = new List<string>()
        {
            "nigger",
            "nig",
            "n1g",
            "n1gger",
            "nigg3r",
            "nlg",
            "nlgger",
            "n1gg3r",
            "nlgg3r",
            "nlgg",
            "n1gg",
            "nigg",
            "fag",
            "faggot",
            "f4g",
            "fagg0t",
            "f4ggot",
            "f4gg0t",
            "feggit",
            "f3gg1t",
            "fegg1t",
            "fa55ot",
            "fa5got",
            "fag5ot",
            "f4gg0t",
            "f4g50t",
            "fa50t",
            "admin",
            "mod",
            "staff"
        };
        #endregion

        #region Messages
        private Dictionary<string, string> texts = new Dictionary<string, string>() {
            { "header", "<size=20><color=#eb4034>Clans Rewritten</color></size> - <size=12><color=#ffcc00>Version 0.1</color></size>\n" },
            { "clanmateConnect", "<color=#eb4034>%NAME%</color> is now online!" },
            { "clanmateDisconnect", "<color=#eb4034>%NAME%</color> is no longer online!" },
            { "clanmateFriendly", "<color=#eb4034>%NAME%</color> is unable to be damaged as friendly fire is not enabled!" },
            { "clanFull", "<color=#eb4034>%TAG%</color> currently has <color=#eb4034>%MAX%</color> members and is unable to hold any more members!" },
            { "clanless", "You currently are not a member of any clan!\nYou can create your own clan by typing: <color=#eb4034>/clan create \"TAG\"</color>" },
            { "clanInformation", "Clan information for <color=#eb4034>%TAG%</color>\nOnline: <color=#eb4034>%ONLINE%/%CLANSIZE%</color>\nPublic: <color=#eb4034>%PUBLIC%</color>\nLeader: <color=#eb4034>%LEADER%</color>\nOfficer(s): <color=#eb4034>%OFFICERS%</color>\nMember(s): <color=#eb4034>%MEMBERS%</color>\nActive: <color=#eb4034>%ACTIVE%</color>\nInactive: <color=#eb4034>%INACTIVE%</color>\nInvites: <color=#eb4034>%INVITES%</color>\n" },
            { "clanHelpFooter", "View more commands to manage your clan by typing: <color=#eb4034>/clan help</color>" },
            { "clanSyntaxSegment", "You did not follow the correct syntax, ensure you are using the following syntax: " },
            { "clanCreateSyntax", "<color=#eb4034>/clan create \"TAG\"</color>" },
            { "memberCurrentlyInClan", "You are already a member of a <color=#eb3043>%TAG%</color>, to perform that action you must leave your clan by typing <color=#eb3043>/clan leave</color> or by typing <color=#eb3043>/clan disband</color> if you are the leader!" },
            { "clanTagInvalid", "You are not allowed to use <color=#eb3043>%INVALIDTAG%</color>. All clan tags should only contain the letters <color=#eb3043>A</color> - <color=#eb3043>Z</color> and the numbers <color=#eb3043>0</color> - <color=#eb3043>9</color> while being <color=#eb3043>2</color> - <color=#eb3043>6</color> characters in length!" },
            { "clanTagUnavailable", "There is already a clan called <color=#eb3043>%TAG%</color> that appears to exist!" },
            { "clanCreated", "You have successfully created a clan by the name of <color=#eb3043>%TAG%</color>!\nYou may now begin inviting your friends by typing: <color=#eb3043>/clan invite [USERNAME/STEAM 64 ID]</color>" },
            { "clanTagProhibited", "You may not use <color=#eb4034>%TAG%</color> as a clan tag since it contains a blacklisted term!" },
            { "clanInviteSyntax", "<color=#eb4034>/clan invite [USERNAME/STEAM 64 ID]</color>" },
            { "clanOfficerRequired", "You must be a <color=#eb3043>Officer</color> or higher to perform this action!" },
            { "clanLeaderRequired"," You must be a <color=#eb3043>Leader</color> or higher to perform this action!" },
            { "playerInvalid", "There is either no player named <color=#eb3043>%PLAYER%</color>, or their name is not unique, if their name is common then try inviting them by using their Steam 64 ID!" },
            { "playerInCurrentClan", "You can not invite <color=#eb3043>%PLAYER%</color> as they are already a part of your clan!" },
            { "playerInOtherClan", "You can not invite <color=#eb3043>%PLAYER%</color> as thay are already a part of <color=#eb3043>%CLAN%</color>!" },
            { "playerAlreadyInvited", "You have already invited <color=#eb3043>%PLAYER%</color> to your clan!" },
            { "playerInviteSent", "A clan invite has been sent to <color=#eb3043>%PLAYER%</color> by <color=#eb3043>%ACTOR%</color>!" },
            { "playerInviteReceived", "You have been invited to <color=#eb3043>%TAG%</color> by <color=#eb3043>%ACTOR%</color>!\nIf you wish to accept this invitation then type: <color=#eb3043>/clan join %TAG%</color>" },
            { "clanJoinSyntax", "<color=#eb3043>/clan join \"TAG\"</color>" },
            { "clanDisband", "The clan you were in has been <color=#eb3043>permanently</color> disbanded!" },
            { "currentlyInClan", "You are currently in a clan, you must leave your clan to perform this action!" },
            { "clanInviteRequired", "You must be invited to this clan to join!" },
            { "clanPlayerJoined", "<color=#eb3043>%NAME%</color> has joined the clan!" },
            { "clanDoesNotExist", "There does not appear to be any clans under the name of <color=#eb3043>%TAG%</color>!" },
            { "clanLeft", "You have left your current clan!" },
            { "clanMemberLeft", "<color=#eb4043>%NAME%</color> has left the clan!" },
            { "clanPromoteSyntax", "<color=#eb3043>/clan promote [USERNAME/STEAM 64 ID]</color>" },
            { "playerNotInClan", "<color=#eb3043>%MEMBER%</color> is not a member of your clan!" },
            { "clanOfficerMaximum", "This clan has reached the maximum amount of officers!" },
            { "clanMemberAlreadyOfficer", "<color=#eb3043>%MEMBER%</color> is already an Officer!" },
            { "clanMemberPromoted", "<color=#eb3043>%MEMBER%</color> has been promoted to Officer by <color=#eb4043>%ACTOR%</color>!" },
            { "clanDemoteSyntax", "<color=#eb3043>/clan demote [USERNAME/STEAM 64 ID]</color>" },
            { "clanPlayerNotOfficer", "<color=#eb3043>%MEMBER%</color> is not an Officer in your clan!" },
            { "clanMemberDemoted", "<color=#eb3043>%MEMBER%</color> has been demoted to Member by <color=#eb3043>%ACTOR%</color>" },
            { "clanKickSyntax", "<color=#eb3043>/clan kick [USERNAME/STEAM 64 ID]</color>" },
            { "clanKickTooPowerful", "<color=#eb3043>%MEMBER%</color> is an Leader or Officer and can not be kicked!" },
            { "clanKickPlayer", "<color=#eb3043>%MEMBER%</color> has been kicked from the clan by <color=#eb3043>%ACTOR%</color>!" },
            { "clanDeletePlayer", "Your clan has been <color=#eb3043>deleted</color> by an Administrator!" },
            { "clanDeleteAdministrator", "You have successfully deleted <color=#eb3043>%TAG%</color>!" },
            { "clanFriendlyFireEnabled", "Friendly fire has been <color=#eb3043>enabled</color> by <color=#eb3043>%ACTOR%</color>!" },
            { "clanFriendlyFireDisabled", "Friendly fire has been <color=#53C739>disabled</color> by <color=#eb3043>%ACTOR%</color>!" },
            { "clanDeleteSyntax", "<color=#eb3043>/clan delete \"TAG\"</color>" },
            { "clanAdminPermissionRequired", "You must have administrator privileges to perform this action!" },
            { "clanHelp", "<color=#eb3043>/clan create \"TAG\"</color> - Invite members to your clan.\n<color=#eb3043>/clan join \"TAG\"</color> - Join a clan.\n<color=#eb3043>/clan kick [USERNAME/STEAM 64 ID]</color> - Kick members from your clan.\n<color=#eb3043>/clan disband</color> - Disband your clan permanently.\n<color=#eb3043>/clan leave</color> - Leave your clan.\n<color=#eb3043>/clan ff</color> - Toggle friendly fire.\n<color=#eb3043>/clan public</color> - Toggle your clan between public and private.\n<color=#eb3043>/clan promote [USERNAME/STEAM 64 ID]</color> - Promote member to Officer.\n<color=#eb3043>/clan demote [USERNAME/STEAM 64 ID]</color> - Demote Officer back to Member.\n<color=#eb3043>/cinfo \"TAG\"</color> - View information about a clan." },
            { "clanInfoSyntax", "<color=#eb3043>/cinfo \"TAG\"</color>" },
            { "clanPublicEnabled", "The clan has been set to <color=#eb3043>public</color> by <color=#eb3043>%ACTOR%</color>, anybody may join!" },
            { "clanPublicDisabled", "The clan has been set to <color=#53C739>private</color> by <color=#eb3043>%ACTOR%</color>, players must be invited!" }
        };

        #endregion

        #region Config

        protected override void LoadDefaultConfig()
        {
            var messages = new Dictionary<string, object>();

            foreach (var pair in texts)
            {
                var key = pair.Key;
                var value = pair.Value;

                if (messages.ContainsKey(key))
                    Puts("Duplicate translation string: " + value);
                else
                    messages.Add(key, value);
            }

            Config["messages"] = messages;
            Config["blacklisted"] = blacklistedWords;
            Config.Set("limit", "members", 8);
            Config.Set("limit", "officers", 3);
        }

        #endregion

        #region Utilities
        private string _(string text, Dictionary<string, string> replacements = null)
        {
            if (messages.ContainsKey(text) && messages[text] != null)
                text = messages[text];
            if (replacements != null)
                foreach (var replacement in replacements)
                    text = text.Replace("%" + replacement.Key + "%", replacement.Value);
            return text;
        }
        #endregion


        #region Clan Class

        public class Clan
        {
            #region Data Values

            public string tag;
            public string leader;
            public List<string> officers = new List<string>();
            public List<string> members = new List<string>();
            public List<string> invited = new List<string>();
            public bool friendlyfire = false;
            public bool open = false;
            #endregion

            #region Tools

            public static Clan Create(string tag, string leaderId)
            {
                var clan = new Clan()
                {
                    tag = tag.ToUpper(),
                    leader = leaderId,
                    friendlyfire = false,
                    open = false
                };

                clan.members.Add(leaderId);

                return clan;
            }

            public bool IsLeader(string userId)
            {
                return userId == leader;
            }

            public bool IsOfficer(string userId)
            {
                return officers.Contains(userId);
            }

            public bool IsMember(string userId)
            {
                return members.Contains(userId);
            }

            public bool IsInvited(string userId)
            {
                return invited.Contains(userId);
            }

            public bool IsFriendlyFire()
            {
                return friendlyfire;
            }

            public bool IsOpen()
            {
                return open;
            }

            public void Broadcast(string message, ulong senderId = 0)
            {
                foreach (var memberId in members)
                {
                    var player = BasePlayer.FindByID(Convert.ToUInt64(memberId));

                    if (player == null)
                        continue;

                    player.SendConsoleCommand("chat.add", "", senderId.ToString(), message);
                }
            }

            #endregion

            #region JSONObject

            internal JObject ToJObject()
            {
                var JSONObject = new JObject();

                JSONObject["tag"] = tag;
                JSONObject["leader"] = leader;
                JSONObject["friendlyfire"] = friendlyfire;
                JSONObject["open"] = open;
                JSONObject["size"] = members.Count;

                var OfficersArray = new JArray();
                foreach (var officer in officers)
                    OfficersArray.Add(officer);

                var MembersArray = new JArray();
                foreach (var member in members)
                    MembersArray.Add(member);

                var InvitedArray = new JArray();
                foreach (var invite in invited)
                    InvitedArray.Add(invite);

                JSONObject["officers"] = OfficersArray;
                JSONObject["members"] = MembersArray;
                JSONObject["invited"] = InvitedArray;

                return JSONObject;
            }

            #endregion

            #region Listeners
            internal void onCreate() => Interface.Call("OnClanCreate", tag);
            internal void onUpdate() => Interface.Call("OnClanUpdate", tag);
            internal void onDestroy() => Interface.Call("OnClanDestroy", tag);

            #endregion
        }

        #endregion

        #region Friendly Fire

        private Dictionary<string, DateTime> notificationTimes = new Dictionary<string, DateTime>();

        private object OnAttackShared(BasePlayer attacker, BasePlayer victim, HitInfo hit)
        {
            if (attacker == victim)
                return null;

            string attackerId = attacker.userID.ToString();
            string victimId = victim.userID.ToString();

            Clan attackerClan = findClanByUser(attackerId);
            Clan victimClan = findClanByUser(victimId);

            if (attackerClan == null)
                return null;

            if (victimClan == null)
                return null;

            if (attackerClan != victimClan)
                return null;

            if (attackerClan.IsFriendlyFire())
                return null;

            //

            DateTime now = DateTime.UtcNow;
            DateTime time;

            var key = attackerId + "-" + victimId;

            if (!notificationTimes.TryGetValue(key, out time) || time < now.AddSeconds(-10))
            {
                attacker.SendConsoleCommand("chat.add", "", "", _((string)Config.Get("messages", "clanmateFriendly"), new Dictionary<string, string>() { { "NAME", victim.displayName } }));

                notificationTimes[key] = now;
            }

            //

            hit.damageTypes = new DamageTypeList();
            hit.DidHit = false;
            hit.HitEntity = null;
            hit.Initiator = null;
            hit.DoHitEffects = false;

            return false;
        }

        [HookMethod("OnPlayerAttack")]
        void OnPlayerAttack(BasePlayer attacker, HitInfo hit)
        {
            try
            {
                if (hit.HitEntity is BasePlayer)
                    OnAttackShared(attacker, hit.HitEntity as BasePlayer, hit);
            }
            catch (Exception ex)
            {
                PrintError("OnPlayerAttack failed: " + ex.Message);
            }
        }

        [HookMethod("OnEntityTakeDamage")]
        void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo hit)
        {
            try
            {
                if (entity is BasePlayer && hit.Initiator is BasePlayer)
                    OnAttackShared(hit.Initiator as BasePlayer, entity as BasePlayer, hit);
            }
            catch (Exception ex)
            {
                PrintError("OnEntityTakeDamage failed: " + ex.Message);
            }
        }
        #endregion


        #region Private API

        private Clan findClan(string tag)
        {
            Clan clan;

            if (clans.TryGetValue(tag.ToUpper(), out clan))
                return clan;

            return null;
        }

        private Clan findClanByUser(string userId)
        {
            Clan clan;

            if (lookup.TryGetValue(userId, out clan))
                return clan;

            return null;
        }

        private BasePlayer findPlayer(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
                return null;
            BasePlayer player = null;
            var allPlayers = BasePlayer.activePlayerList.ToArray();

            foreach (var p in allPlayers)
            {
                if (player != null)
                    return null;

                if (p.UserIDString == playerId)
                    player = p;
            }

            if (player != null)
                return player;

            string name = playerId.ToLower();

            foreach (var p in allPlayers)
            {
                if (p.displayName == name)
                {
                    if (player != null)
                        return null;
                    player = p;
                }
            }

            if (player != null)
                return player;

            foreach (var p in allPlayers)
            {
                if (p.displayName.ToLower().IndexOf(name) >= 0)
                {
                    if (player != null)
                        return null; // Not Unique
                    player = p;
                }
            }

            return player;
        }

        private BasePlayer findPlayerByPartialName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            BasePlayer player = null;
            name = name.ToLower();
            var allPlayers = BasePlayer.activePlayerList.ToArray();

            foreach (var p in allPlayers)
            {
                if (p.displayName == name)
                {
                    if (player != null)
                        return null; // Not unique
                    player = p;
                }
            }
            if (player != null)
                return player;

            foreach (var p in allPlayers)
            {
                if (p.displayName.ToLower().IndexOf(name) >= 0)
                {
                    if (player != null)
                        return null; // Not unique
                    player = p;
                }
            }
            return player;
        }

        private void setupPlayer(BasePlayer player)
        {
            var prevName = player.displayName;
            var playerId = player.UserIDString;
            var clan = findClanByUser(playerId);
            player.displayName = stripTag(player.displayName, clan);
            string originalName = null;
            if (!originalNames.ContainsKey(playerId))
            {
                originalNames.Add(playerId, originalName = player.displayName);
            }
            else
            {
                originalName = originalNames[playerId];
            }
            if (clan == null)
            {
                player.displayName = originalName;
            }
            else
            {
                var tag = "[" + clan.tag + "]" + " ";
                if (!player.displayName.StartsWith(tag))
                    player.displayName = tag + originalName;
            }
            if (player.displayName != prevName)
                player.SendNetworkUpdate();
        }

        private void setupPlayers(List<string> playerIds)
        {
            foreach (var playerId in playerIds)
            {
                var uid = Convert.ToUInt64(playerId);
                var player = BasePlayer.FindByID(uid);
                if (player != null)
                    setupPlayer(player);
                else
                {
                    player = BasePlayer.FindSleeping(uid);
                    if (player != null)
                        setupPlayer(player);
                }
            }
        }

        private string stripTag(string name, Clan clan)
        {
            if (clan == null)
                return name;
            var re = new Regex(@"^\[[a-zA-Z0-9]{2,6}\]\s");
            //var re = new Regex(@"^\[" + clan.tag + @"\]\s");
            while (re.IsMatch(name))
                name = name.Substring(clan.tag.Length + 3);
            return name;
        }

        #endregion

        private void OnServerInitialized()
        {
            try
            {
                LoadConfig();

                permission.RegisterPermission("clans.admin", this);

                try
                {
                    var customMessages = Config.Get<Dictionary<string, object>>("messages");
                    if (customMessages != null)
                        foreach (var pair in customMessages)
                            messages[pair.Key] = (string)pair.Value;
                    loadData();
                }
                catch (Exception)
                {
                    PrintWarning("oxide/config/Clans.json is invalid, erase the current file and restart Clans Rewritten!");
                }

                foreach (var player in BasePlayer.activePlayerList)
                    setupPlayer(player);

                foreach (var player in BasePlayer.sleepingPlayerList)
                    setupPlayer(player);

                try { limitMembers = Config.Get<int>("limit", "members"); } catch { }
                try { limitOfficers = Config.Get<int>("limit", "officers"); } catch { }
            }
            catch (Exception ex)
            {
                PrintError("OnServerInitialized failed", ex);
            }
        }

        private void OnUserApprove(Network.Connection connection)
        {
            originalNames[connection.userid.ToString()] = connection.username;
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            string original;
            if (originalNames.TryGetValue(player.UserIDString, out original))
                player.displayName = original;

            try
            {
                setupPlayer(player);

                var clan = findClanByUser(player.UserIDString);
                if (clan != null)
                    clan.Broadcast(_((string)Config.Get("messages", "clanmateConnect"), new Dictionary<string, string>() { { "NAME", stripTag(player.displayName, clan) } }));
            }
            catch (Exception ex)
            {
                PrintError("OnPlayerConnected failed", ex);
            }
        }

        private void OnPlayerDisconnect(BasePlayer player)
        {
            try
            {
                var clan = findClanByUser(player.UserIDString);
                if (clan != null)
                    clan.Broadcast(_((string)Config.Get("messages", "clanmateDisconnect"), new Dictionary<string, string>() { { "NAME", stripTag(player.displayName, clan) } }));
            }
            catch (Exception ex)
            {
                PrintError("OnPlayerDisconnected failed", ex);
            }
        }

        private void Unload()
        {
            try
            {
                foreach (var pair in originalNames)
                {
                    var playerId = Convert.ToUInt64(pair.Key);
                    var player = BasePlayer.FindByID(playerId);
                    if (player != null)
                        player.displayName = pair.Value;
                    else
                    {
                        player = BasePlayer.FindSleeping(playerId);
                        if (player != null)
                            player.displayName = pair.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                PrintError("Unload failed", ex);
            }
        }

        [ChatCommand("cinfo")]
        private void CInfoCommand(BasePlayer player, string command, string[] args)
        {
            var userId = player.UserIDString;
            var myClan = findClanByUser(userId);
            var sb = new StringBuilder();
            sb.Append(_((string)Config.Get("messages", "header")));


            if (args.Length != 1)
                sb.Append(_((string)Config.Get("messages", "clanSyntaxSegment") + (string)Config.Get("messages", "clanInfoSyntax")));
            else
            {
                Clan searchedClan = findClan(args[0]);

                if (searchedClan == null)
                    sb.Append(_((string)Config.Get("messages", "clanDoesNotExist"), new Dictionary<string, string>()
                    {
                        { "TAG", args[0] }
                    }));
                else
                {
                    List<string> clanMembersList = searchedClan.members;
                    List<string> clanOfficersList = searchedClan.officers;
                    List<string> clanMembers = new List<string>();
                    List<string> clanOfficers = new List<string>();
                    List<string> onlineMembers = new List<string>();
                    List<string> offlineMembers = new List<string>();
                    List<string> clanInvites = new List<string>();

                    var clanLeader = covalence.Players?.FindPlayerById(searchedClan.leader)?.Name;

                    foreach (string userID in clanMembersList)
                    {
                        var member = covalence.Players?.FindPlayerById(userID);

                        if (covalence.Players.Connected.Contains(member))
                        {
                            onlineMembers.Add(stripTag(member.Name, searchedClan));
                            clanMembers.Add(stripTag(member.Name, searchedClan));
                        }
                        else
                        {
                            offlineMembers.Add(stripTag(member.Name, searchedClan));
                            clanMembers.Add(stripTag(member.Name, searchedClan));
                        }
                    }

                    foreach (string userID in clanOfficersList)
                    {
                        var member = covalence?.Players.FindPlayer(userID);

                        clanOfficers.Add(stripTag(member.Name, searchedClan));
                    }

                    foreach (string userID in searchedClan.invited)
                    {
                        var member = covalence?.Players.FindPlayer(userID);

                        clanInvites.Add(stripTag(member.Name, searchedClan));
                    }

                    sb.Append(_((string)Config.Get("messages", "clanInformation"), new Dictionary<string, string>()
                    {
                        { "TAG", searchedClan.tag },
                        { "MEMBERS", string.Join(", ", clanMembers) },
                        { "ONLINE", onlineMembers.Count.ToString() },
                        { "CLANSIZE", clanMembersList.Count.ToString() },
                        { "LEADER", stripTag(clanLeader, searchedClan) },
                        { "OFFICERS", (clanOfficers.Count > 0 ? string.Join(", ", clanOfficers) : "None") },
                        { "ACTIVE", (onlineMembers.Count > 0 ? string.Join(", ", onlineMembers) : "None") },
                        { "INACTIVE", (offlineMembers.Count > 0 ? string.Join(", ", offlineMembers) : "None") },
                        { "INVITES", (clanInvites.Count > 0 ? string.Join(", ", clanInvites) : "None") },
                        { "PUBLIC", (searchedClan.open ? "True" : "False") }
                    }));
                }

            }

            SendReply(player, "{0}", sb.ToString().TrimEnd());
        }

        [ChatCommand("clan")]
        private void ClanCommand(BasePlayer player, string command, string[] args)
        {
            var playerId = player.UserIDString;
            var playerClan = findClanByUser(playerId);
            var sb = new StringBuilder();

            if (args.Length == 0)
            {
                sb.Append(_((string)Config.Get("messages", "header")));

                if (playerClan == null)
                {
                    sb.Append(_((string)Config.Get("messages", "clanless")));
                }
                else
                {
                    List<string> clanMembersList = playerClan.members;
                    List<string> clanOfficersList = playerClan.officers;
                    List<string> clanMembers = new List<string>();
                    List<string> clanOfficers = new List<string>();
                    List<string> onlineMembers = new List<string>();
                    List<string> offlineMembers = new List<string>();
                    List<string> clanInvites = new List<string>();

                    var clanLeader = covalence.Players?.FindPlayerById(playerClan.leader)?.Name;

                    foreach (string userID in clanMembersList)
                    {
                        var member = covalence.Players?.FindPlayerById(userID);

                        if (covalence.Players.Connected.Contains(member))
                        {
                            onlineMembers.Add(stripTag(member.Name, playerClan));
                            clanMembers.Add(stripTag(member.Name, playerClan));
                        }
                        else
                        {
                            offlineMembers.Add(stripTag(member.Name, playerClan));
                            clanMembers.Add(stripTag(member.Name, playerClan));
                        }
                    }

                    foreach (string userID in clanOfficersList)
                    {
                        var member = covalence?.Players.FindPlayer(userID);

                        clanOfficers.Add(stripTag(member.Name, playerClan));
                    }

                    foreach (string userID in playerClan.invited)
                    {
                        var member = covalence?.Players.FindPlayer(userID);

                        clanInvites.Add(stripTag(member.Name, playerClan));
                    }

                    sb.Append(_((string)Config.Get("messages", "clanInformation"), new Dictionary<string, string>()
                    {
                        { "TAG", playerClan.tag },
                        { "MEMBERS", string.Join(", ", clanMembers) },
                        { "ONLINE", onlineMembers.Count.ToString() },
                        { "CLANSIZE", clanMembersList.Count.ToString() },
                        { "LEADER", stripTag(clanLeader, playerClan) },
                        { "OFFICERS", (clanOfficers.Count > 0 ? string.Join(", ", clanOfficers) : "None") },
                        { "ACTIVE", (onlineMembers.Count > 0 ? string.Join(", ", onlineMembers) : "None") },
                        { "INACTIVE", (offlineMembers.Count > 0 ? string.Join(", ", offlineMembers) : "None") },
                        { "INVITES", (clanInvites.Count > 0 ? string.Join(", ", clanInvites) : "None") },
                        { "PUBLIC", (playerClan.open ? "True" : "False") }
                    }));
                }
            }
            else
            {
                switch (args[0])
                {
                    case "create":

                        // Checking if a clan tag was not provided.
                        if (args.Length != 2)
                        {
                            sb.Append(_((string)Config.Get("messages", "clanSyntaxSegment") + (string)Config.Get("messages", "clanCreateSyntax")));

                            break;
                        }

                        // Checking if player is already in a clan.
                        if (playerClan != null)
                        {
                            sb.Append(_((string)Config.Get("messages", "memberCurrentlyInClan"), new Dictionary<string, string>()
                            {
                                { "TAG", playerClan.tag }
                            }));

                            break;
                        }

                        // Check if player is creating a clan that follows the 2-6 alphanumerical rules.
                        if (!tagRe.IsMatch(args[1]))
                        {
                            sb.Append(_((string)Config.Get("messages", "clanTagInvalid"), new Dictionary<string, string>()
                            {
                                { "INVALIDTAG", args[1] }
                            }));

                            break;
                        }

                        // Check if a clan already exists by that name.
                        if (clans.ContainsKey(args[1]))
                        {
                            sb.Append(_((string)Config.Get("messages", "clanTagUnavailable"), new Dictionary<string, string>()
                            {
                                { "TAG", args[1] }
                            }));

                            break;
                        }

                        // Check if they are using blacklisted words.
                        if (((IList)Config.Get("blacklisted")).Contains(args[1]))
                        {
                            sb.Append(_((string)Config.Get("messages", "clanTagProhibited"), new Dictionary<string, string>()
                            {
                                { "TAG", args[1] }
                            }));

                            break;
                        }

                        playerClan = Clan.Create(args[1], playerId);
                        clans.Add(playerClan.tag, playerClan);
                        saveData();
                        lookup[playerId] = playerClan;
                        setupPlayer(player);
                        sb.Append(_((string)Config.Get("messages", "clanCreated"), new Dictionary<string, string>()
                        {
                            { "TAG", args[1] }
                        }));
                        playerClan.onCreate();
                        break;
                    case "invite":

                        // Check if they provided a player to invite.
                        if (args.Length != 2)
                        {
                            sb.Append(_((string)Config.Get("messages", "clanSyntaxSegment") + (string)Config.Get("messages", "clanInviteSyntax")));

                            break;
                        }

                        // Check if actor is in a clan.
                        if (playerClan == null)
                        {
                            sb.Append(_((string)Config.Get("messages", "clanless")));

                            break;
                        }

                        // Check if actor is a clan officer or higher.
                        if (!playerClan.IsOfficer(playerId) && !playerClan.IsLeader(playerId))
                        {
                            sb.Append(_((string)Config.Get("messages", "clanOfficerRequired")));

                            break;
                        }

                        // Check if player exists.
                        var invPlayer = covalence?.Players.FindPlayer(args[1]);
                        if (invPlayer == null)
                        {
                            sb.Append(_((string)Config.Get("messages", "playerInvalid"), new Dictionary<string, string>()
                            {
                                { "PLAYER", args[1] }
                            }));

                            break;
                        }

                        var invUserClan = findClanByUser(invPlayer.Name);
                        if (invUserClan != null && invUserClan.tag != playerClan.tag)
                        {
                            sb.Append(_((string)Config.Get("messages", "playerInOtherClan"), new Dictionary<string, string>()
                            {
                                { "PLAYER", stripTag(invPlayer.Name, playerClan) },
                                { "CLAN", invUserClan.tag }
                            }));

                            break;
                        }

                        var invUserId = invPlayer.Id;
                        if (playerClan.members.Contains(invUserId))
                        {
                            sb.Append(_((string)Config.Get("messages", "playerInCurrentClan"), new Dictionary<string, string>()
                            {
                                { "PLAYER", stripTag(invPlayer.Name, playerClan) }
                            }));

                            break;
                        }

                        if (playerClan.invited.Contains(invUserId))
                        {
                            sb.Append(_((string)Config.Get("messages", "playerAlreadyInvited"), new Dictionary<string, string>()
                            {
                                { "PLAYER", stripTag(invPlayer.Name, playerClan) }
                            }));

                            break;
                        }

                        playerClan.invited.Add(invUserId);
                        saveData();
                        playerClan.Broadcast(_((string)Config.Get("messages", "playerInviteSent"), new Dictionary<string, string>()
                        {
                            { "PLAYER", stripTag(invPlayer.Name, playerClan) },
                            { "ACTOR", stripTag(player.displayName, playerClan) }
                        }));
                        (invPlayer.Object as BasePlayer).SendConsoleCommand("chat.add", "", "",
                            _((string)Config.Get("messages", "playerInviteReceived"), new Dictionary<string, string>()
                            {
                                { "TAG", playerClan.tag },
                                { "ACTOR", stripTag(player.displayName, playerClan) }
                            }));
                        playerClan.onUpdate();

                        break;
                    case "join":
                        if (args.Length != 2)
                        {
                            sb.Append(_((string)Config.Get("messages", "clanSyntaxSegment") + (string)Config.Get("messages", "clanJoinSyntax")));

                            break;
                        }

                        if (playerClan != null)
                        {
                            sb.Append(_((string)Config.Get("messages", "currentlyInClan")));

                            break;
                        }

                        var clanJoined = findClan(args[1]);

                        if (clanJoined == null)
                        {
                            sb.Append(_((string)Config.Get("messages", "clanDoesNotExist"), new Dictionary<string, string>()
                            {
                                { "TAG", args[1] }
                            }));

                            break;
                        }

                        if (!clanJoined.IsOpen() && !clanJoined.invited.Contains(playerId))
                        {
                            sb.Append(_((string)Config.Get("messages", "clanInviteRequired")));

                            break;
                        }

                        if ((int)Config.Get("limit", "members") >= 0 && clanJoined.members.Count >= (int)Config.Get("limit", "members"))
                        {
                            sb.Append(_((string)Config.Get("messages", "clanFull"), new Dictionary<string, string>()
                            {
                                { "TAG", clanJoined.tag },
                                { "MAX", Config.Get("limit", "members").ToString() }
                            }));

                            break;
                        }

                        playerClan = clanJoined;

                        playerClan.invited.Remove(playerId);
                        playerClan.members.Add(playerId);
                        saveData();
                        lookup[playerId] = playerClan;
                        setupPlayer(player);
                        playerClan.Broadcast(_((string)Config.Get("messages", "clanPlayerJoined"), new Dictionary<string, string>()
                        {
                            { "NAME", stripTag(player.displayName, playerClan) }
                        }));
                        playerClan.onUpdate();
                        break;
                    case "disband":
                        if (playerClan == null)
                        {
                            sb.Append(_((string)Config.Get("messages", "clanless")));
                            break;
                        }

                        if (!playerClan.IsLeader(playerId))
                        {
                            sb.Append(_((string)Config.Get("messages", "clanLeaderRequired")));

                            break;
                        }

                        clans.Remove(playerClan.tag);
                        saveData();
                        foreach (var member in playerClan.members)
                            lookup.Remove(member);
                        playerClan.Broadcast(_((string)Config.Get("messages", "clanDisband")));
                        setupPlayers(playerClan.members);
                        playerClan.onDestroy();
                        break;
                    case "leave":
                        if (playerClan == null)
                        {
                            sb.Append(_((string)Config.Get("messages", "clanless")));

                            break;
                        }

                        if (playerClan.members.Count == 1)
                        {
                            clans.Remove(player.tag);
                        }
                        else
                        {
                            playerClan.officers.Remove(playerId);
                            playerClan.members.Remove(playerId);
                            playerClan.invited.Remove(playerId);
                            if (playerClan.IsLeader(playerId) && playerClan.members.Count > 0)
                            {
                                playerClan.leader = playerClan.members[0];
                            }
                        }
                        saveData();
                        lookup.Remove(playerId);
                        setupPlayer(player);
                        sb.Append(_((string)Config.Get("messages", "clanLeft")));
                        playerClan.Broadcast(_((string)Config.Get("messages", "clanMemberLeft"), new Dictionary<string, string>()
                        {
                            { "NAME", stripTag(player.displayName, playerClan) }
                        }));
                        playerClan.onUpdate();
                        break;
                    case "promote":
                        if (args.Length != 2)
                        {
                            sb.Append(_((string)Config.Get("messages", "clanSyntaxSegment") + (string)Config.Get("messages", "clanPromoteSyntax")));

                            break;
                        }

                        if (playerClan == null)
                        {
                            sb.Append(_((string)Config.Get("messages", "clanless")));

                            break;
                        }

                        if (!playerClan.IsLeader(playerId))
                        {
                            sb.Append(_((string)Config.Get("messages", "clanLeaderRequired")));

                            break;
                        }

                        var promotePlayer = covalence?.Players.FindPlayer(args[1]);
                        
                        if (promotePlayer == null)
                        {
                            sb.Append(_((string)Config.Get("messages", "playerInvalid"), new Dictionary<string, string>()
                            {
                                { "PLAYER", args[1] }
                            }));

                            break;
                        }
                        

                        var promotePlayerUserId = promotePlayer.Id;
                        if (!playerClan.IsMember(promotePlayerUserId))
                        {
                            sb.Append(_((string)Config.Get("messages", "playerNotInClan"), new Dictionary<string, string>()
                            {
                                { "MEMBER", stripTag(promotePlayer.Name, playerClan) }
                            }));

                            break;
                        }
                        

                        if (playerClan.IsOfficer(promotePlayerUserId))
                        {
                            sb.Append(_((string)Config.Get("messages", "clanMemberAlreadyOfficer"), new Dictionary<string, string>()
                            {
                                { "MEMBER", stripTag(promotePlayer.Name, playerClan) }
                            }));

                            break;
                        }

                        
                        if ((int)Config.Get("limit", "officers") >= 0 && playerClan.officers.Count >= (int)Config.Get("limit", "officers"))
                        {
                            sb.Append(_((string)Config.Get("messages", "clanOfficerMaximum")));

                            break;
                        }

                        
                        playerClan.officers.Add(promotePlayerUserId);
                        saveData();
                        playerClan.Broadcast(_((string)Config.Get("messages", "clanMemberPromoted"), new Dictionary<string, string>()
                        {
                            { "MEMBER", stripTag(promotePlayer.Name, playerClan) },
                            { "ACTOR", stripTag(player.displayName, playerClan) }
                        }));
                        playerClan.onUpdate();
                        break;
                    case "demote":
                        if (args.Length != 2)
                        {
                            sb.Append(_((string)Config.Get("messages", "clanSyntaxSegment") + (string)Config.Get("messages", "clanDemoteSyntax")));

                            break;
                        }

                        if (playerClan == null)
                        {
                            sb.Append(_((string)Config.Get("messages", "clanless")));

                            break;
                        }

                        if (!playerClan.IsLeader(playerId))
                        {
                            sb.Append(_((string)Config.Get("messages", "clanLeaderRequired")));

                            break;
                        }

                        var demotePlayer = covalence?.Players.FindPlayer(args[1]);
                        if (demotePlayer == null)
                        {
                            sb.Append(_((string)Config.Get("messages", "playerInvalid"), new Dictionary<string, string>()
                            {
                                { "PLAYER", args[1] }
                            }));

                            break;
                        }

                        var demotePlayerUserId = demotePlayer.Id;
                        if (!playerClan.IsMember(demotePlayerUserId))
                        {
                            sb.Append(_((string)Config.Get("messages", "playerNotInClan"), new Dictionary<string, string>()
                            {
                                { "MEMBER", stripTag(demotePlayer.Name, playerClan) }
                            }));

                            break;
                        }

                        if (!playerClan.IsOfficer(demotePlayerUserId))
                        {
                            sb.Append(_((string)Config.Get("messages", "clanMemberNotOfficer"), new Dictionary<string, string>()
                            {
                                { "MEMBER", stripTag(demotePlayer.Name, playerClan) }
                            }));

                            break;
                        }

                        playerClan.officers.Remove(demotePlayerUserId);
                        saveData();
                        playerClan.Broadcast(_((string)Config.Get("messages", "clanMemberDemoted"), new Dictionary<string, string>()
                        {
                            { "MEMBER", stripTag(demotePlayer.Name, playerClan) },
                            { "ACTOR", stripTag(player.displayName, playerClan) }
                        }));
                        playerClan.onUpdate();
                        break;
                    case "kick":
                        if (args.Length != 2)
                        {
                            sb.Append(_((string)Config.Get("messages", "clanSyntaxSegment") + (string)Config.Get("messages", "clanKickSyntax")));

                            break;
                        }

                        if (playerClan == null)
                        {
                            sb.Append(_((string)Config.Get("messages", "clanless")));

                            break;
                        }

                        if (!playerClan.IsLeader(playerId) && !playerClan.IsOfficer(playerId))
                        {
                            sb.Append(_((string)Config.Get("messages", "clanOfficerRequired")));

                            break;
                        }

                        var kickPlayer = covalence.Players?.FindPlayer(args[1]);
                        if (kickPlayer == null)
                        {
                            sb.Append(_((string)Config.Get("messages", "playerInvalid"), new Dictionary<string, string>()
                            {
                                { "PLAYER", args[1] }
                            }));

                            break;
                        }

                        var kickPlayerUserId = kickPlayer.Id;
                        if (!playerClan.IsMember(kickPlayerUserId) && !playerClan.IsInvited(kickPlayerUserId))
                        {
                            sb.Append(_((string)Config.Get("messages", "playerNotInClan"), new Dictionary<string, string>()
                            {
                                { "MEMBER", stripTag(kickPlayer.Name, playerClan) }
                            }));

                            break;
                        }

                        if (playerClan.IsLeader(kickPlayerUserId) || playerClan.IsOfficer(kickPlayerUserId) && !playerClan.IsLeader(playerId))
                        {
                            sb.Append(_((string)Config.Get("messages", "clanKickTooPowerful"), new Dictionary<string, string>()
                            {
                                { "MEMBER", stripTag(kickPlayer.Name, playerClan) }
                            }));
                            break;
                        }

                        playerClan.members.Remove(kickPlayerUserId);
                        playerClan.invited.Remove(kickPlayerUserId);
                        playerClan.officers.Remove(kickPlayerUserId);
                        saveData();
                        lookup.Remove(kickPlayerUserId);
                        setupPlayer(kickPlayer.Object as BasePlayer);
                        playerClan.Broadcast(_((string)Config.Get("messages", "clanKickPlayer"), new Dictionary<string, string>()
                        {
                            { "MEMBER", stripTag(kickPlayer.Name, playerClan) },
                            { "ACTOR", stripTag(player.displayName, playerClan) }
                        }));
                        playerClan.onUpdate();

                        break;
                    case "ff":
                        if (playerClan == null)
                        {
                            sb.Append(_((string)Config.Get("messages", "clanless")));

                            break;
                        }

                        if (playerClan.IsOfficer(playerId) || playerClan.IsLeader(playerId))
                        {
                            if (!playerClan.IsFriendlyFire())
                            {
                                sb.Append(_((string)Config.Get("messages", "clanFriendlyFireEnabled"), new Dictionary<string, string>()
                                {
                                    { "ACTOR", stripTag(player.displayName, playerClan) }
                                }));
                            }
                            else
                            {
                                sb.Append(_((string)Config.Get("messages", "clanFriendlyFireDisabled"), new Dictionary<string, string>()
                                {
                                    { "ACTOR", stripTag(player.displayName, playerClan) }
                                }));
                            }

                            playerClan.friendlyfire = !playerClan.friendlyfire;
                            saveData();
                        }
                        else
                        {
                            sb.Append(_((string)Config.Get("messages", "clanOfficerRequired")));

                            break;
                        }
                        break;
                    case "public":
                        if (playerClan == null)
                        {
                            sb.Append(_((string)Config.Get("messages", "clanless")));

                            break;
                        }

                        if (playerClan.IsLeader(playerId))
                        {
                            if (!playerClan.IsOpen())
                            {
                                sb.Append(_((string)Config.Get("messages", "clanPublicEnabled"), new Dictionary<string, string>()
                                {
                                    { "ACTOR", stripTag(player.displayName, playerClan) }
                                }));
                            }
                            else
                            {
                                sb.Append(_((string)Config.Get("messages", "clanPublicDisabled"), new Dictionary<string, string>()
                                {
                                    { "ACTOR", stripTag(player.displayName, playerClan) }
                                }));
                            }

                            playerClan.open = !playerClan.open;
                            saveData();
                        }
                        else
                        {
                            sb.Append(_((string)Config.Get("messages", "clanLeaderRequired")));

                            break;
                        }
                        break;
                    case "delete":
                        if (args.Length != 2)
                        {
                            sb.Append(_((string)Config.Get("messages", "clanSyntaxSegment") + (string)Config.Get("messages", "clanDeleteSyntax")));

                            break;
                        }
                        if (!permission.UserHasPermission(player.UserIDString, "clans.admin"))
                        {
                            sb.Append(_((string)Config.Get("messages", "clanAdminPermissionRequired")));
                            break;
                        }
                        Clan clan;
                        if (!clans.TryGetValue(args[1], out clan))
                        {
                            sb.Append(_((string)Config.Get("messages", "clanDoesNotExist"), new Dictionary<string, string>()
                            {
                                { "TAG", args[1] }
                            }));

                            break;
                        }

                        clan.Broadcast(_((string)Config.Get("messages", "clanDeletePlayer")));
                        clans.Remove(args[1]);
                        saveData();
                        foreach (var member in clan.members)
                            lookup.Remove(member);
                        setupPlayers(clan.members);
                        sb.Append(_((string)Config.Get("messages", "clanDeleteAdministrator"), new Dictionary<string, string>()
                        {
                            { "TAG", args[1] }
                        }));
                        playerClan.onDestroy();
                        break;
                    default:
                        sb.Append(_((string)Config.Get("messages", "header") + (string)Config.Get("messages", "clanHelp")));

                        break;
                }
            }

            SendReply(player, "{0}", sb.ToString().TrimEnd());
        }

        #region Plugin APIs / Hook Methods

        [HookMethod("GetClan")]
        private JObject GetClan(string tag)
        {
            var clan = findClan(tag);
            if (clan == null)
                return null;
            return clan.ToJObject();
        }

        [HookMethod("GetAllClans")]
        private JArray GetAllClans()
        {
            return new JArray(clans.Keys);
        }

        [HookMethod("GetClanOf")]
        private string GetClanOf(object player)
        {
            if (player == null)
                throw new ArgumentException("player");
            if (player is ulong)
                player = ((ulong)player).ToString();
            else if (player is BasePlayer)
                player = (player as BasePlayer).userID.ToString();
            if (!(player is string))
                throw new ArgumentException("player");
            var clan = findClanByUser((string)player);
            if (clan == null)
                return null;
            return clan.tag;
        }

        #endregion
    }
}
