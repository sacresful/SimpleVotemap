using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using System.Data;
using System.Text.RegularExpressions;
using System.Threading;
using System.Linq;
using System.Threading;

using PRoCon.Core;
using PRoCon.Core.Plugin;
using PRoCon.Core.Plugin.Commands;
using PRoCon.Core.Players;
using PRoCon.Core.Players.Items;
using PRoCon.Core.Battlemap;
using PRoCon.Core.Maps;
using System.Net;
using System.Runtime.Remoting.Metadata.W3cXsd2001;

namespace PRoConEvents
{
    public class SimpleVotemap : PRoConPluginAPI, IPRoConPluginInterface
    {
        #region PluginSetup
        public string GetPluginName()
        {
            return "SimpleVotemap";
        }
        public string GetPluginVersion()
        {
            return "1.0";
        }
        public string GetPluginAuthor()
        {
            return "sacresful";
        }
        public string GetPluginWebsite()
        {
            return "github.com/sacresful/SimpleVotemap";
        }
        public string GetPluginDescription()
        {
            return "Simple votemap plugin";
        }

        public List<CPluginVariable> GetDisplayPluginVariables()
        {
            List<CPluginVariable> lstReturn = new List<CPluginVariable>();
            lstReturn.Add(new CPluginVariable("Voting|Uservote prefix", this.m_strHosVotePrefix.GetType(), this.m_strHosVotePrefix));
            lstReturn.Add(new CPluginVariable("Xtras|Debug Level", this.m_iDebugLevel.GetType(), this.m_iDebugLevel));
            return lstReturn;

        }
        public List<CPluginVariable> GetPluginVariables()
        {
            List<CPluginVariable> lstReturn = new List<CPluginVariable>();
            lstReturn.Add(new CPluginVariable("Uservote prefix", this.m_strHosVotePrefix.GetType(), this.m_strHosVotePrefix));
            lstReturn.Add(new CPluginVariable("Debug Level", this.m_iDebugLevel.GetType(), this.m_iDebugLevel));
            return lstReturn;
        }
        public void SetPluginVariable(string strVariable, string strValue)
        {
            int iValue = 0;
            if (strVariable.CompareTo("Debug Level") == 0 && int.TryParse(strValue, out iValue) == true)
            {
                m_iDebugLevel = iValue;
            }
            else if (strVariable.CompareTo("Uservote prefix") == 0)
            {
                this.m_strHosVotePrefix = strValue;
            }
        }
        #endregion

        #region Procon Events
        public void OnPluginLoaded(string strHostName, string strPort, string strPRoConVersion)
        {
            this.m_strHostName = strHostName;
            this.m_strPort = strPort;
            this.m_strPRoConVersion = strPRoConVersion;
            this.RegisterEvents(this.GetType().Name, "OnServerInfo", "OnListPlayers", "OnPlayerLeft", "OnGlobalChat", "OnTeamChat", "OnSquadChat", "OnRoundOver", "OnRestartLevel");
        }
        public void OnPluginEnable()
        {
            hasVotingStarted = false;
            this.ExecuteCommand("procon.protected.pluginconsole.write", "^bSimpleVotemap ^2Enabled!");
        }
        public void OnPluginDisable()
        {
            hasVotingStarted = false;
            this.ExecuteCommand("procon.protected.pluginconsole.write", "^bSimpleVotemap ^1Disabled =(");
        }
        public void OnGlobalChat(string speaker, string message)
        {
            ProcessChatMessage(speaker, message);
        }
        public void OnTeamChat(string speaker, string message, int teamId)
        {
            ProcessChatMessage(speaker, message);
        }
        public void OnSquadChat(string speaker, string message, int teamId, int squadId)
        {
            ProcessChatMessage(speaker, message);
        }
        public void OnListPlayers(List<CPlayerInfo> lstPlayers, CPlayerSubset cpsSubset)
        {
            this.CurrPlayerCount = lstPlayers.Count;
            foreach (CPlayerInfo player in lstPlayers)
            {
                m_players.UpdatePlayer(player);
            }
            WritePluginConsole("There are " + m_players.Count + " players in the db.", "Info", 5);
        }
        public void OnPlayerLeft(CPlayerInfo playerInfo)
        {
            m_players.Remove(playerInfo.SoldierName);
            WritePluginConsole(playerInfo.SoldierName + " removed from the db.", "Info", 5);
        }
        public void OnRestartLevel()
        {
            WritePluginConsole("Level Restarted: Stopping voting system", "Work", 3);
            StopVotingPoll();
            votingTimer.Dispose();
        }
        public void OnRoundOver(int winningTeamId)
        {
            WritePluginConsole("Round Over: Stopping voting system", "Work", 3);
            StopVotingPoll();
            votingTimer.Dispose();
        }

        public void OnRunNextLevel()
        {
            WritePluginConsole("Running nextmap: Stopping voting system", "Work", 3);
            StopVotingPoll();
            votingTimer.Dispose();
        }

        public void OnServerInfo(CServerInfo csiServerInfo)
        {
            try
            {
                WritePluginConsole("ServerInfo updating.", "Info", 5);
                this.currentMap = csiServerInfo.Map;
                this.currentGameMode = csiServerInfo.GameMode;
                this.currentTotalRounds = csiServerInfo.CurrentRound;
                this.currentTotalRoundsInString = this.currentTotalRounds.ToString();
                WritePluginConsole("Current player count is: " + csiServerInfo.PlayerCount, "Info", 5);
                WritePluginConsole("Current map is: " + csiServerInfo.Map, "Info", 5);
                WritePluginConsole("Current game mode is: " + csiServerInfo.GameMode, "Info", 5);
                WritePluginConsole("Current number of total rounds is: " + csiServerInfo.TotalRounds, "Info", 5);
            }
            catch (Exception e)
            {
                WritePluginConsole("Exception caught in: OnServerInfo", "Error", 1);
                WritePluginConsole(e.Message, "Error", 1);
            }
        }

        #endregion

        private string m_strHostName;
        private string m_strPort;
        private string m_strPRoConVersion;
        private string m_strHosVotePrefix = @"/";
        private int m_iDebugLevel = 3;

        private bool hasVotingStarted = false;
        private DateTime timeVoteStart;
        private TimeSpan voteDuration;
        private int CurrPlayerCount = 0;
        System.Timers.Timer votingTimer = new System.Timers.Timer(30000);

        private string playerVotesInString;

        private string currentMap;
        private string currentGameMode;
        private int currentTotalRounds;
        private string currentTotalRoundsInString;

        private class Players
        {
            private List<Player> m_listPlayers = new List<Player>();

            public void UpdatePlayer(CPlayerInfo player)
            {
                bool updated = false;
                foreach (Player p in m_listPlayers)
                {
                    if (p.SoldierName == player.SoldierName)
                    {
                        p.TeamID = player.TeamID;
                        p.SquadID = player.SquadID;
                        updated = true;
                        break;
                    }
                }
                if (!updated)
                {
                    Add(player.SoldierName, player.TeamID, player.SquadID);
                }
            }

            public bool SetVote(string name, int vote)
            {
                foreach (Player p in m_listPlayers)
                {
                    if (p.SoldierName == name)
                    {
                        p.Vote = vote;
                        return true;
                    }
                }
                return false;
            }

            public bool HasVoted { get; private set; } = false;

            public bool SetVoteBool()
            {
                if (HasVoted)
                {
                    return false; 
                }

                HasVoted = true; 
                return true; 
            }
            public void ResetVote()
            {
                HasVoted = false;
            }
            public void ResetVotes()
            {
                foreach (Player p in m_listPlayers)
                {
                    ResetVote();
                }
            }

            public Player GetPlayer(string name)
            {
                foreach (Player p in m_listPlayers)
                {
                    if (p.SoldierName == name)
                    {
                        return p;
                    }
                }
                return null;
            }

            public List<Squad> GetNonvotedSquads()
            {
                List<Squad> squads = new List<Squad>();

                foreach (Player p in m_listPlayers)
                {
                    if (p.Vote == -1 && !(p.TeamID == 0 && p.SquadID == 0))
                    {
                        bool found = false;
                        foreach (Squad s in squads)
                        {
                            if (s.TeamID == p.TeamID && s.SquadID == p.SquadID)
                            {
                                found = true;
                                break;
                            }
                        }
                        if (!found)
                        {
                            squads.Add(new Squad(p.TeamID, p.SquadID));
                        }
                    }
                }

                squads.Sort();

                return squads;
            }

            public void Add(string name, int teamId, int squadId)
            {
                if (!this.Contains(name))
                {
                    m_listPlayers.Add(new Player(name, teamId, squadId));
                }
            }

            public void Remove(string name)
            {
                foreach (Player p in m_listPlayers)
                {
                    if (p.SoldierName == name)
                    {
                        m_listPlayers.Remove(p);
                    }
                }
            }

            public int Count
            {
                get { return m_listPlayers.Count; }
            }

            public bool Contains(string name)
            {
                foreach (Player p in m_listPlayers)
                {
                    if (p.SoldierName == name)
                    {
                        return true;
                    }
                }
                return false;
            }

            public class Player
            {
                private string m_Name = "";
                private int m_TeamId = -1;
                private int m_SquadId = -1;
                private int m_Vote = -1;

                public Player(string name, int teamId, int squadId)
                {
                    m_Name = name;
                    m_TeamId = teamId;
                    m_SquadId = squadId;
                    m_Vote = -1;
                }

                public string SoldierName
                {
                    get { return m_Name; }
                    private set { m_Name = value; }
                }

                public int TeamID
                {
                    get { return m_TeamId; }
                    set { m_TeamId = value; }
                }

                public int SquadID
                {
                    get { return m_SquadId; }
                    set { m_SquadId = value; }
                }

                public int Vote
                {
                    get { return m_Vote; }
                    set { m_Vote = value; }
                }

            }
            public class Squad : IComparable<Squad>
            {
                private int m_TeamId = -1;
                private int m_SquadId = -1;
                public Squad(int team, int squad)
                {
                    m_TeamId = team;
                    m_SquadId = squad;
                }

                public int TeamID
                {
                    get { return m_TeamId; }
                }

                public int SquadID
                {
                    get { return m_SquadId; }
                }

                public int CompareTo(Squad other)
                {
                    if (this.TeamID == other.TeamID)
                    {
                        return this.SquadID.CompareTo(other.SquadID);
                    }
                    return this.TeamID.CompareTo(other.TeamID);
                }
            }
        }
        private Players m_players = new Players();

        private Dictionary<string, int> playerVotes = new Dictionary<string, int>();
        private Dictionary<string, string> mappedMaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase){
            // Vanilla
            { "Dawnbreaker",  "mp_tremors" },
            { "Dragonvalley", "mp_valley" },
            { "Floodzone",    "mp_flooded" },
            { "Golmud",       "mp_journey" },
            { "Hainan",       "mp_resort" },
            { "Lancang",      "mp_damage" },
            { "Locker",       "mp_prison" },
            { "Paracel",      "mp_naval" },
            { "Rogue",        "mp_dish" },
            { "Siege",        "mp_siege" },
            { "Zavod",        "mp_abandoned" },
            // China Rising
            { "Altai",      "xp1_001" },
            { "Dragonpass", "xp1_002" },
            { "Guilin",     "xp1_003" },
            { "Silk",       "xp1_004" },
            // Second Assault
            { "Caspian",   "mp_007" },
            { "Oman",      "xp1_002_Oman" },
            { "Firestorm", "mp_012_Firestorm" },
            { "Metro",     "mp_subway" },
            // Naval Strike
            { "Lost",        "xp2_001"  },
            { "Nansha",      "xp2_002"  },
            { "Mortar",      "xp2_003"  },
            { "Wavebreaker", "xp2_004"  },
            // Dragon's Teeth
            { "Pearl",      "xp3_Marketplace" },
            { "Propaganda", "xp3_Propaganda" },
            { "Lumphini",   "xp3_UrbanGarden" },
            { "Sunken",     "xp3_Waterfront" },
            // Final Stand
            { "Whiteout",   "xp4_Arctic" },
            { "Hammerhead", "xp4_SubBase" },
            { "Hangar21",   "xp4_Titan" },
            { "Karelia",    "xp4_WalkerFactory" }
        };
        private Dictionary<string, string> mappedGamemodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase){
            { "AS", "AirSuperiority0" },
            { "CTF", "CaptureFlag0" },
            { "CA", "CarrierAssaultSmall0" },
            { "CAL", "CarrierAssaultLarge0" },
            { "CL", "Chainlink0" },
            { "CQS", "ConquestSmall0" },
            { "CQL", "ConquestLarge0" },
            { "DEF", "Elimination0" },
            { "DOM", "Domination0" },
            { "GM", "GunMaster0" },
            { "OB", "Obliteration" },
            { "R", "RushLarge0" },
            { "SDM", "SquadDeathMatch0" },
            { "TDM", "TeamDeathMatch0" }
            //{ "SquadObliteration",        "OB" },
        };
        private void WritePluginConsole(string message, string tag, int level)
        {
            if (tag.ToLower() == "error")
            {
                tag = "^8" + tag;
            }
            else if (tag.ToLower() == "work")
            {
                tag = "^4" + tag;
            }
            else
            {
                tag = "^5" + tag;
            }
            string line = "^b[" + this.GetPluginName() + " " + this.GetPluginVersion() + "] " + tag + ": ^0^n" + message;

            if (this.m_iDebugLevel >= level)
            {
                this.ExecuteCommand("procon.protected.pluginconsole.write", line);
            }
            //if (this.m_iDebugLevel >= level)
            //{
            //    this.ExecuteCommand("procon.protected.chat.write", line);
            //}
        }
        private void DisplayListOfMaps()
        {
            List<string> listOfMaps = mappedMaps.Keys.ToList();
            int chunkSize = 11;

            for (int i = 0; i < listOfMaps.Count; i += chunkSize)
            {
                string mapChunk = string.Join(", ", listOfMaps.Skip(i).Take(chunkSize));
                this.ExecuteCommand("procon.protected.send", "admin.say", mapChunk, "all");
            }
        }
        private void DisplayListOfGamemodes()
        {
            List<string> listOfGamemodes = mappedGamemodes.Keys.ToList();

            string linkedGamemodes = string.Join(", ", listOfGamemodes);
            this.ExecuteCommand("procon.protected.send", "admin.say", linkedGamemodes, "all");
        }
        private void DisplayListOfFullGamemodes()
        {
            List<string> listOfFullGamemodes = mappedGamemodes.Values.ToList();
            int chunkSize = 3;

            for (int i = 0; i < listOfFullGamemodes.Count; i += chunkSize)
            {
                string gamemodeChunk = string.Join(", ", listOfFullGamemodes.Skip(i).Take(chunkSize));
                this.ExecuteCommand("procon.protected.send", "admin.say", gamemodeChunk, "all");
            }
        }
        private void ProcessChatMessage(string speaker, string message)
        {
            CPrivileges cpPlayerPrivs = this.GetAccountPrivileges(speaker);

            Match match;
            match = Regex.Match(message, @"" + m_strHosVotePrefix + @"maps\s*", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                WritePluginConsole(speaker + " requested list of maps", "Info", 3);
                DisplayListOfMaps();
                return;
            }

            match = Regex.Match(message, @"" + m_strHosVotePrefix + @"map\s*(\S*)\s*(\S*)\s*(\S*)$", RegexOptions.IgnoreCase);
            if (match.Success && hasVotingStarted == false)
            {
                string mapName = match.Groups[1].Value;
                string gameMode = match.Groups[2].Value;
                string numberOfRoundsString = match.Groups[3].Value;
                if (string.IsNullOrEmpty(mapName) || string.IsNullOrEmpty(gameMode) || string.IsNullOrEmpty(numberOfRoundsString))
                {
                    this.ExecuteCommand("procon.protected.send", "admin.say", "All parameters (map name, game mode, and number of rounds) must be from the /maps /gamemodes.", "player", speaker);
                    return;
                }
                if (!int.TryParse(numberOfRoundsString, out int numberOfRounds))
                {
                    this.ExecuteCommand("procon.protected.send", "admin.say", "Invalid number of rounds. Please enter a valid number.", "player", speaker);
                    return;
                }
                if (!mappedGamemodes.ContainsKey(gameMode))
                {
                    this.ExecuteCommand("procon.protected.send", "admin.say", "Invalid gamemode. Please use a valid gamemode from the list.", "player", speaker);
                    return;
                }
                if (!mappedMaps.ContainsKey(mapName))
                {
                    this.ExecuteCommand("procon.protected.send", "admin.say", "Invalid map name. Please use a valid map from the list.", "player", speaker);
                    return;
                }

                mappedMaps.TryGetValue(mapName, out string internal_mapName);
                mappedGamemodes.TryGetValue(gameMode, out string internal_gameMode);

                string actualMapName = mappedMaps.FirstOrDefault(x => x.Value == internal_mapName).Key;
                string actualGameMode = mappedGamemodes.FirstOrDefault(x => x.Value == internal_gameMode).Key;

                this.ExecuteCommand("procon.protected.send", "admin.say", $"{speaker} has started a vote on a map: {actualMapName} {actualGameMode} {numberOfRounds}", "all", speaker);
                this.ExecuteCommand("procon.protected.send", "mapList.clear");
                this.ExecuteCommand("procon.protected.send", "mapList.add", internal_mapName, internal_gameMode, numberOfRounds.ToString());
                StartVotingPoll();
                votingTimer.Elapsed += (sender, e) => VotingExpired();

                void VotingExpired()
                {
                    this.ExecuteCommand("procon.protected.send", "serverInfo");
                    this.ExecuteCommand("procon.protected.send", "mapList.clear");
                    this.ExecuteCommand("procon.protected.send", "mapList.add", currentMap, currentGameMode, "1");
                    this.ExecuteCommand("procon.protected.send", "admin.say", "Canceling map vote, because of inactivity, to start again type /map", "all", speaker);
                    StopVotingPoll();
                }

                return;
            }
            else if (match.Success && hasVotingStarted == true)
            {
                this.ExecuteCommand("procon.protected.send", "admin.say", "There is vote currently in progress", "player", speaker);
            }

            match = Regex.Match(message, @"" + m_strHosVotePrefix + @"gamemodes\s*", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                WritePluginConsole(speaker + " has requested list of gamemodes", "Info", 3);
                DisplayListOfGamemodes();
                return;
            }

            match = Regex.Match(message, @"" + m_strHosVotePrefix + @"fullgamemodes\s*", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                WritePluginConsole(speaker + " has requested list of full gamemodes", "Info", 3);
                DisplayListOfFullGamemodes();
                return;
            }

            match = Regex.Match(message, @"" + m_strHosVotePrefix + @"cancel\s*", RegexOptions.IgnoreCase);
            if (match.Success && hasVotingStarted == true)
            {
                WritePluginConsole(speaker + " has cancelled mapvote", "Info", 3);
                StopVotingPoll();
                this.ExecuteCommand("procon.protected.send", "admin.say", speaker + " has cancelled a votemap", "all", speaker);
                return;
            }
            else if (match.Success)
            {
                this.ExecuteCommand("procon.protected.send", "admin.say", "There is no vote currently in progress.", "player", speaker);
                return;
            }

            match = Regex.Match(message, @"" + m_strHosVotePrefix + @"yes\s*", RegexOptions.IgnoreCase);
            if (match.Success && hasVotingStarted == true)
            {
                int vote = 1;
                if (!playerVotes.ContainsKey(speaker))
                {
                    this.ExecuteCommand("procon.protected.send", "admin.listPlayers", "all");
                    playerVotes.Add(speaker, vote);
                    bool success = m_players.SetVoteBool();
                    playerVotesInString = playerVotes.Keys.Count.ToString();
                    if (!success)
                    {
                        WritePluginConsole(speaker + " was not found in player list. Vote not counted.", "Error", 1);
                        this.ExecuteCommand("procon.protected.send", "admin.say", "Something went wrong, vote not counted, try restarting the round", "player", speaker);
                    }
                    else
                    {
                        this.ExecuteCommand("procon.protected.send", "admin.say", speaker + " voted.", "all", speaker);
                        WritePluginConsole("^7" + speaker + "^0 voted", "Info", 2);

                        /* DEBUG
                        WritePluginConsole(speaker + " - vote counted", "Info", 1);
                        this.ExecuteCommand("procon.protected.send", "admin.say", playerVotesInString, "all", speaker);
                        */

                        if (playerVotes.Count >= CurrPlayerCount / 2)
                        {
                            this.ExecuteCommand("procon.protected.send", "admin.say", "50% player have voted for the map change, type /confirm to change the map.", "all", speaker);
                        }
                    }
                }
                else
                {
                    this.ExecuteCommand("procon.protected.send", "admin.say", speaker + ": You have already voted", "player", speaker);
                }
            }
            else if (match.Success && hasVotingStarted == false)
            {
                this.ExecuteCommand("procon.protected.send", "admin.say", speaker + ": There is no vote currently in progress.", "player", speaker);
            }

            match = Regex.Match(message, @"" + m_strHosVotePrefix + @"confirm\s*", RegexOptions.IgnoreCase);
            if (match.Success && hasVotingStarted == true)
            {
                if (playerVotes.Count >= CurrPlayerCount / 2)
                {
                    Thread.Sleep(250);
                    this.ExecuteCommand("procon.protected.send", "admin.say", "Changing map...", "all", speaker);
                    this.ExecuteCommand("procon.protected.send", "mapList.runNextRound");
                    votingTimer.Stop();
                    votingTimer.Dispose();
                    return;
                }
                else
                {
                    this.ExecuteCommand("procon.protected.send", "admin.say", "Not enough player have voted", "all", speaker);
                }
            }

            match = Regex.Match(message, @"" + m_strHosVotePrefix + @"restart\s*", RegexOptions.IgnoreCase);
            if (match.Success && hasVotingStarted == true && cpPlayerPrivs.CanUseMapFunctions)
            {
                StopVotingPoll();
                this.ExecuteCommand("procon.protected.send", "admin.say", "Restarting round", "all", speaker);
                this.ExecuteCommand("procon.protected.send", "mapList.restartRound");
                return;
            }
            else if (match.Success) 
            {
                this.ExecuteCommand("procon.protected.send", "admin.say", "You do not have enough privilages.", "all", speaker);
            }
            match = Regex.Match(message, @"" + m_strHosVotePrefix + @"changemap\s*(\S*)\s*(\S*)\s*(\S*)$", RegexOptions.IgnoreCase);
            if (match.Success && cpPlayerPrivs.CanUseMapFunctions)
            {
                string mapName = match.Groups[1].Value;
                string gameMode = match.Groups[2].Value;
                string numberOfRoundsString = match.Groups[3].Value;

                if (string.IsNullOrEmpty(mapName) || string.IsNullOrEmpty(gameMode) || string.IsNullOrEmpty(numberOfRoundsString))
                {
                    this.ExecuteCommand("procon.protected.send", "admin.say", "All parameters (map name, game mode, and number of rounds) must be from the /maps /gamemodes.", "player", speaker);
                    return;
                }

                if (!int.TryParse(numberOfRoundsString, out int numberOfRounds))
                {
                    this.ExecuteCommand("procon.protected.send", "admin.say", "Invalid number of rounds. Please enter a valid number.", "player", speaker);
                    return;
                }

                if (!mappedGamemodes.ContainsKey(gameMode))
                {
                    this.ExecuteCommand("procon.protected.send", "admin.say", "Invalid gamemode. Please use a valid gamemode from the list.", "player", speaker);
                    return;
                }

                if (!mappedMaps.ContainsKey(mapName))
                {
                    this.ExecuteCommand("procon.protected.send", "admin.say", "Invalid map name. Please use a valid map from the list.", "player", speaker);
                    return;
                }

                mappedMaps.TryGetValue(mapName, out string internal_mapName);
                mappedGamemodes.TryGetValue(gameMode, out string internal_gameMode);

                StopVotingPoll();
                this.ExecuteCommand("procon.protected.send", "mapList.clear");
                this.ExecuteCommand("procon.protected.send", "mapList.add", internal_mapName, internal_gameMode, numberOfRounds.ToString());
                this.ExecuteCommand("procon.protected.send", "admin.say", $"Changing map to: {mapName} {gameMode} {numberOfRounds}", "all", speaker);
                this.ExecuteCommand("procon.protected.send", "mapList.runNextRound");
                return;
            }
            else if (match.Success)
            {
                this.ExecuteCommand("procon.protected.send", "admin.say", "You do not have enough privilages.", "player", speaker);
            }

            /* DEBUG
            match = Regex.Match(message, @"^/stopvote\s*", RegexOptions.IgnoreCase);
            if (match.Success && hasVotingStarted == true) //&& cpPlayerPrivs.CanUseMapFunctions)
            {
                StopVotingPoll();
                this.ExecuteCommand("procon.protected.send", "admin.say", "Stopping the voting poll", "player", speaker);
                return;
            }
            
            match = Regex.Match(message, @"^/test\s*", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                this.ExecuteCommand("procon.protected.send", "mapList.add", "mp_siege", "ConquestLarge0", "1");
            }
            */
        }
        private void StartVotingPoll()
        {
            try
            {
                WritePluginConsole("Voting poll: Starting...", "Info", 4);
                hasVotingStarted = true;
                playerVotes = new Dictionary<string, int>();
                WritePluginConsole("^bVoting poll: Started!", "Info", 3);
                votingTimer.AutoReset = false;
                votingTimer.Start();

            }
            catch (Exception e)
            {
                WritePluginConsole("EXCEPTION CAUGHT IN: StartVotingPoll", "Error", 1);
                WritePluginConsole(e.Message, "Error", 1);
            }
        }
        private void StopVotingPoll()
        {
            try
            {
                hasVotingStarted = false;
                playerVotes.Clear();
                m_players.ResetVotes();
                votingTimer.Stop();
                WritePluginConsole("^bVoting poll: Stopped", "Info", 3);
            }
            catch (Exception e)
            {
                WritePluginConsole("EXCEPTION CAUGHT IN: StartVotingPoll", "Error", 1);
                WritePluginConsole(e.Message, "Error", 1);
            }
        }
    }
}
