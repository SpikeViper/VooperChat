using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Client.Categories;
using Valour.Client.Channels;
using Valour.Client.Messages;
using Valour.Client.Shared;
using Valour.Shared;
using Valour.Shared.Planets;
using Valour.Shared.Roles;
using Valour.Shared.Users;

namespace Valour.Client.Planets
{
    public class ClientPlanetManager
    {
        public static ClientPlanetManager Current;

        public ClientPlanetManager()
        {
            Current = this;
        }

        /// <summary>
        /// The currently focused planet
        /// </summary>
        private ClientPlanet CurrentPlanet { get; set; }

        private Dictionary<ulong, ClientPlanet> OpenPlanets = new Dictionary<ulong, ClientPlanet>();
        private Dictionary<ulong, ClientPlanetChatChannel> OpenPlanetChatChannels = new Dictionary<ulong, ClientPlanetChatChannel>();
        private List<ChatChannelWindow> OpenPlanetChatWindows = new List<ChatChannelWindow>();
        private ConcurrentDictionary<ulong, PlanetRole> PlanetRolesCache = new ConcurrentDictionary<ulong, PlanetRole>();
        private ConcurrentDictionary<ulong, ClientPlanet> PlanetCache = new ConcurrentDictionary<ulong, ClientPlanet>();

        // Caches planet members by ID
        private static ConcurrentDictionary<ulong, ClientPlanetMember> PlanetMemberCache = new ConcurrentDictionary<ulong, ClientPlanetMember>();

        // Caches planet members by a key built from user ID and planet ID
        private static ConcurrentDictionary<string, ClientPlanetMember> PlanetMemberDualCache = new ConcurrentDictionary<string, ClientPlanetMember>();

        // Used to cache data for several open planets. Data is returned for the given planet id.
        private ConcurrentDictionary<ulong, List<ulong>> PlanetRolesListCache = new ConcurrentDictionary<ulong, List<ulong>>();
        private ConcurrentDictionary<ulong, List<ClientPlanetMember>> PlanetMembersListCache = new ConcurrentDictionary<ulong, List<ClientPlanetMember>>();

        public event Func<ClientPlanet, Task> OnPlanetChange;

        public event Func<ClientPlanet, Task> OnPlanetClose;

        public event Func<ClientPlanet, Task> OnPlanetOpen;

        public event Func<ClientPlanetMessage, Task> OnMessageDeletion;

        public event Func<Task> OnChannelsUpdate;

        public event Func<Task> OnChannelWindowUpdate;

        public event Func<Task> OnCategoriesUpdate;

        public event Func<PlanetRole, Task> OnRoleUpdate;
        public event Func<PlanetRole, Task> OnRoleDeletion;

        public event Func<ClientPlanetMember, Task> OnMemberUpdate;

        public event Func<ClientPlanetChatChannel, Task> OnChatChannelUpdate;
        public event Func<ClientPlanetChatChannel, Task> OnChatChannelDeletion;
        public event Func<ClientPlanetCategory, Task> OnCategoryDeletion;

        public event Func<ClientPlanetCategory, Task> OnCategoryUpdate;

        private readonly SignalRManager signalRManager;

        public ClientPlanetManager(SignalRManager signalrmanager)
        {
            this.signalRManager = signalrmanager;

            signalRManager.hubConnection.On<string>("Relay", OnMessageRecieve);
            signalRManager.hubConnection.On<string>("RoleUpdate", UpdateRole);
            signalRManager.hubConnection.On<string>("RoleDeletion", RoleDeletion);
            signalRManager.hubConnection.On<string>("MemberUpdate", UpdateMember);
            signalRManager.hubConnection.On<string>("ChatChannelUpdate", UpdateChatChannel);
            signalRManager.hubConnection.On<string>("CategoryUpdate", UpdateCategory);
            signalRManager.hubConnection.On<string>("ChatChannelDeletion", ChatChannelDeletion);
            signalRManager.hubConnection.On<string>("CategoryDeletion", CategoryDeletion);
            signalrmanager.hubConnection.On<string>("MessageDeletion", MessageDeletion);
        }

        public bool IsChatChannelOpen(ClientPlanetChatChannel channel)
        {
            return OpenPlanetChatChannels.ContainsKey(channel.Id);
        }

        public bool IsChatChannelOpen(ulong channel_id)
        {
            return OpenPlanetChatChannels.ContainsKey(channel_id);
        }

        public List<ClientPlanet> GetOpenPlanets()
        {
            return OpenPlanets.Values.ToList();
        }

        public async Task AddPlanetAsync(ClientPlanet planet)
        {
            PlanetCache.AddOrUpdate(planet.Id, planet, (key, old) => planet);

            // Pre-cache roles
            await GetPlanetRoles(planet.Id);
        }

        /// <summary>
        /// Returns a user from the given id
        /// </summary>
        public async Task<ClientPlanet> GetPlanetAsync(ulong id)
        {
            // Attempt to retrieve from cache
            if (PlanetCache.ContainsKey(id))
            {
                return PlanetCache[id];
            }

            // Retrieve from server
            ClientPlanet planet = await ClientPlanet.GetClientPlanetAsync(id);

            if (planet == null)
            {
                Console.WriteLine($"Failed to fetch planet with id {id}.");
                return null;
            }

            // Add to cache
            PlanetCache.TryAdd(id, planet);

            return planet;

        }

        public async Task<List<ClientPlanetMember>> GetCachedPlanetMembers(ClientPlanet planet)
        {
            return await GetCachedPlanetMembers(planet.Id);
        }

        public async Task<List<ClientPlanetMember>> GetCachedPlanetMembers(ulong planet_id)
        {
            if (PlanetMembersListCache.ContainsKey(planet_id))
                return PlanetMembersListCache[planet_id];

            return new List<ClientPlanetMember>();
        }

        /// <summary>
        /// Loads all of the roles for a given planet
        /// </summary>
        public async Task LoadPlanetRoles(ClientPlanet planet)
        {
            await LoadPlanetRoles(planet.Id);
        }

        /// <summary>
        /// Loads all of the roles for a given planet
        /// </summary>
        public async Task LoadPlanetRoles(ulong planet_id)
        {
            // Get planet roles
            string json = await ClientUserManager.Http.GetStringAsync($"Planet/GetPlanetRoles?planet_id={planet_id}&token={ClientUserManager.UserSecretToken}");

            Console.WriteLine(json);

            TaskResult<List<PlanetRole>> result = JsonConvert.DeserializeObject<TaskResult<List<PlanetRole>>>(json);

            if (result == null)
            {
                Console.WriteLine($"Critical error in getting planet roles for planet {planet_id}");
                return;
            }

            if (!result.Success)
            {
                Console.WriteLine($"Critical error in getting planet roles for planet {planet_id}");
                Console.WriteLine(result.Message);
                return;
            }

            PlanetRolesListCache[planet_id] = result.Data.Select(x => x.Id).ToList();

            // Set roles into role cache
            foreach (var role in result.Data)
            {
                PlanetRolesCache.AddOrUpdate(role.Id, role, (key, old) => role);
            }

            Console.WriteLine($"Loaded {result.Data.Count} roles into cache for planet {planet_id}");
        }

        /// <summary>
        /// Returns the roles for a given planet
        /// </summary>
        public async Task<List<PlanetRole>> GetPlanetRoles(ulong planet_id)
        {
            // If we don't have them, try to load them
            if (!PlanetRolesListCache.ContainsKey(planet_id))
            {
                await LoadPlanetRoles(planet_id);
            }

            // If we still don't have them, give up
            if (!PlanetRolesListCache.ContainsKey(planet_id))
            {
                return null;
            }

            List<PlanetRole> roles = new List<PlanetRole>();

            foreach (var id in PlanetRolesListCache[planet_id])
            {
                roles.Add(PlanetRolesCache[id]);
            }

            // Return the roles
            return roles;
        }

        public async Task<PlanetRole> GetPlanetRole(ulong role_id)
        {
            if (PlanetRolesCache.ContainsKey(role_id))
            {
                return PlanetRolesCache[role_id];
            }

            // Get planet roles
            string json = await ClientUserManager.Http.GetStringAsync($"Planet/GetPlanetRole?role_id={role_id}&token={ClientUserManager.UserSecretToken}");

            Console.WriteLine(json);

            TaskResult<PlanetRole> result = JsonConvert.DeserializeObject<TaskResult<PlanetRole>>(json);

            if (result == null)
            {
                Console.WriteLine($"Critical error in getting planet role with id {role_id}");
                return null;
            }

            if (!result.Success)
            {
                Console.WriteLine($"Critical error in getting planet role with id {role_id}");
                Console.WriteLine(result.Message);
                return null;
            }

            PlanetRolesCache.TryAdd(role_id, result.Data);

            return result.Data;
        }

        public async Task SetUpdatedMember(ClientPlanetMember member)
        {
            string dual_key = $"{member.Planet_Id}-{member.User_Id}";

            PlanetMemberDualCache.AddOrUpdate(dual_key, member, (key, old) => member);
            PlanetMemberCache.AddOrUpdate(member.Id, member, (key, old) => member);
        }

        public async Task RemoveMember(ClientPlanetMember member)
        {
            string dual_key = $"{member.Planet_Id}-{member.User_Id}";

            ClientPlanetMember o;
            PlanetMemberDualCache.Remove(dual_key, out o);
            PlanetMemberCache.Remove(member.Id, out o);
        }

        public async Task SetUpdatedRole(PlanetRole role)
        {
            PlanetRolesCache.AddOrUpdate(role.Id, role, (key, old) => role);
        }

        public async Task LoadAllPlanetMemberInfoAsync(ulong planet_id)
        {
            string json = await ClientUserManager.Http.GetStringAsync($"Planet/GetPlanetMemberInfo?planet_id={planet_id}&token={ClientUserManager.UserSecretToken}");

            //Console.WriteLine(json);

            TaskResult<List<PlanetMemberInfo>> result = JsonConvert.DeserializeObject<TaskResult<List<PlanetMemberInfo>>>(json);

            List<ClientPlanetMember> memberList = new List<ClientPlanetMember>();

            foreach (PlanetMemberInfo info in result.Data)
            {
                ClientPlanetMember member = ClientPlanetMember.FromBase(info.Member);
                member.SetCacheValues(info);

                string key = $"{planet_id}-{member.User_Id}";

                if (!PlanetMemberDualCache.ContainsKey(key))
                {
                    PlanetMemberDualCache.TryAdd(key, member);
                }
                if (!PlanetMemberCache.ContainsKey(member.Id))
                {
                    PlanetMemberCache.TryAdd(member.Id, member);
                }

                memberList.Add(member);
            }

            // Set entire list
            PlanetMembersListCache.AddOrUpdate(planet_id, memberList, (key, old) => memberList);
        }

        /// <summary>
        /// Returns a planet member from the given Id
        /// </summary>
        public async Task<ClientPlanetMember> GetPlanetMemberAsync(ulong member_id)
        {
            if (PlanetMemberCache.ContainsKey(member_id))
            {
                return PlanetMemberCache[member_id];
            }

            // Otherwise attempt to fetch from server
            // Retrieve from server
            string json = await ClientUserManager.Http.GetStringAsync($"Member/GetMember?id={member_id}&auth={ClientUserManager.UserSecretToken}");

            TaskResult<ClientPlanetMember> result = JsonConvert.DeserializeObject<TaskResult<ClientPlanetMember>>(json);

            if (result == null)
            {
                Console.WriteLine("A fatal error occurred retrieving a planet member from the server.");
                return null;
            }

            if (!result.Success)
            {
                Console.WriteLine(result.ToString());
                return null;
            }

            ClientPlanetMember member = result.Data;

            if (member == null)
            {
                Console.WriteLine($"Failed to fetch member with id {member_id}.");
                return null;
            }

            Console.WriteLine($"Fetched member {member_id} for planet {member.Planet_Id}.");

            // Add to cache
            string key = $"{member.Planet_Id}-{member.User_Id}";
            PlanetMemberDualCache.TryAdd(key, member);
            PlanetMemberCache.TryAdd(member.Id, member);

            return member;
        }

        /// <summary>
        /// Returns a user from the given user and planet id
        /// </summary>
        public async Task<ClientPlanetMember> GetPlanetMemberAsync(ulong user_id, ulong planet_id)
        {
            if (user_id == 0)
            {
                return new ClientPlanetMember()
                {
                    Id = 0,
                    Planet_Id = planet_id,
                };
            }

            string key = $"{planet_id}-{user_id}";

            // Attempt to retrieve from cache
            if (PlanetMemberDualCache.ContainsKey(key))
            {
                return PlanetMemberDualCache[key];
            }

            // Retrieve from server
            string json = await ClientUserManager.Http.GetStringAsync($"Planet/GetPlanetMember?user_id={user_id}&planet_id={planet_id}&auth={ClientUserManager.UserSecretToken}");

            TaskResult<ClientPlanetMember> result = JsonConvert.DeserializeObject<TaskResult<ClientPlanetMember>>(json);

            if (result == null)
            {
                Console.WriteLine("A fatal error occurred retrieving a planet member from the server.");
                return null;
            }

            if (!result.Success)
            {
                Console.WriteLine(result.ToString());
                return null;
            }

            ClientPlanetMember member = result.Data;

            if (member == null)
            {
                Console.WriteLine($"Failed to fetch planet user with user id {user_id} and planet id {planet_id}.");
                return null;
            }

            Console.WriteLine($"Fetched planet user {user_id} for planet {planet_id}");

            // Add to cache
            PlanetMemberDualCache.TryAdd(key, member);
            PlanetMemberCache.TryAdd(member.Id, member);

            return member;

        }

        /// <summary>
        /// Attempts to rejoin all lost connections during a reconnect
        /// </summary>
        public async Task HandleReconnect()
        {
            foreach (ClientPlanet planet in OpenPlanets.Values)
            {
                await signalRManager.hubConnection.SendAsync("JoinPlanet", planet.Id, ClientUserManager.UserSecretToken);
                Console.WriteLine($"Rejoined SignalR group for planet {planet.Id}");
            }

            foreach (ClientPlanetChatChannel channel in OpenPlanetChatChannels.Values)
            {
                await signalRManager.hubConnection.SendAsync("JoinChannel", channel.Id, ClientUserManager.UserSecretToken);
                Console.WriteLine($"Rejoined SignalR group for channel {channel.Id}");
            }

            foreach (ChatChannelWindow window in OpenPlanetChatWindows)
            {
                await window.Component.SetupNewChannelAsync();
            }
        }

        public async Task OpenPlanet(ClientPlanet planet)
        {
            if (planet == null)
                return;

            if (OpenPlanets.ContainsKey(planet.Id))
            {
                // Already opened
                return;
            }

            Console.WriteLine("Opening planet " + planet.Name);

            // Load roles for planet
            await LoadPlanetRoles(planet);

            // Load member info for planet
            await LoadAllPlanetMemberInfoAsync(planet.Id);

            // Joins planet via SignalR
            await signalRManager.hubConnection.SendAsync("JoinPlanet", planet.Id, ClientUserManager.UserSecretToken);

            // Refresh channels and categories from server

            await planet.RequestChannelsAsync();
            await planet.RequestCategoriesAsync();

            // Add to open planet list
            OpenPlanets.Add(planet.Id, planet);

            Console.WriteLine($"Joined SignalR group for planet {planet.Id}");

            if (OnPlanetOpen != null)
            {
                Console.WriteLine($"Invoking open planet event for {planet.Name}");
                await OnPlanetOpen.Invoke(planet);
            }
        }

        public async Task ClosePlanet(ClientPlanet planet)
        {
            if (!OpenPlanets.ContainsKey(planet.Id))
                return;

            // Joins planet via SignalR
            await signalRManager.hubConnection.SendAsync("LeavePlanet", planet.Id);

            // Remove from list
            OpenPlanets.Remove(planet.Id);

            // Clear members list
            List<ClientPlanetMember> l;
            PlanetMembersListCache.Remove(planet.Id, out l);

            Console.WriteLine($"Left SignalR group for planet {planet.Id}");

            if (OnPlanetClose != null)
            {
                Console.WriteLine($"Invoking close planet event for {planet.Name}");
                await OnPlanetClose.Invoke(planet);
            }
        }

        public async Task ReplacePlanetChatChannel(ChatChannelWindow window, ClientPlanetChatChannel oldChannel, ClientPlanetChatChannel newChannel)
        {
            if (oldChannel.Id == newChannel.Id)
                return;

            Console.WriteLine("Swapping chat channel " + oldChannel.Name + " for " + newChannel.Name);

            bool close_planet = true;

            if (oldChannel.Planet_Id == newChannel.Planet_Id)
                close_planet = false;

            await ClosePlanetChatChannel(window, oldChannel, close_planet);
            await OpenPlanetChatChannel(window);
        }

        public async Task OpenPlanetChatChannel(ChatChannelWindow window)
        {
            ClientPlanetChatChannel channel = window.Channel;

            if (!OpenPlanetChatChannels.ContainsKey(channel.Id))
            {
                ClientPlanet planet = await channel.GetPlanetAsync();

                // Ensure planet is opened
                await OpenPlanet(planet);

                // Joins channel via SignalR
                await signalRManager.hubConnection.SendAsync("JoinChannel", channel.Id, ClientUserManager.UserSecretToken);

                // Add to open channel list
                OpenPlanetChatChannels.Add(channel.Id, channel);

                Console.WriteLine($"Joined SignalR group for channel {channel.Id}");
            }

            if (!OpenPlanetChatWindows.Contains(window))
                OpenPlanetChatWindows.Add(window);
        }

        public async Task ClosePlanetChatChannel(ChatChannelWindow window, ClientPlanetChatChannel channel, bool close_planet = true)
        {
            if (!OpenPlanetChatChannels.ContainsKey(channel.Id))
                return;

            Console.WriteLine("Closing chat channel " + channel.Name);

            // If there are no longer any windows open for the channel, leave
            if (!OpenPlanetChatWindows.Any(x => x.Channel.Id == channel.Id))
            {
                // Leaves channel via signalr
                await signalRManager.hubConnection.SendAsync("LeaveChannel", channel.Id);

                // Remove from list
                OpenPlanetChatChannels.Remove(channel.Id);

                Console.WriteLine($"Left SignalR group for channel {channel.Id}");
            } 

            // If there are no windows open for a planet, close the planet
            if (close_planet && !OpenPlanetChatWindows.Any(x => x.Channel.Planet_Id == channel.Planet_Id))
            {
                await ClosePlanet(await channel.GetPlanetAsync());
                await SetCurrentPlanet(null);
            }
        }

        public void SetChannelWindowClosed(ChatChannelWindow window)
        {
            OpenPlanetChatWindows.Remove(window);
        }

        public async Task RefreshOpenedChannels() {
            await OnChannelWindowUpdate.Invoke();
        }

        public async Task RefreshChannels() {
            await OnChannelsUpdate.Invoke();
        }

        public async Task RefreshCategories() {
            await OnCategoriesUpdate.Invoke();
        }

        public async Task SetCurrentPlanet(ClientPlanet planet)
        {
            if (planet == null)
            {
                if (CurrentPlanet == null)
                {
                    return;
                }
            }
            else
            {
                if (CurrentPlanet != null)
                {
                    if (planet.Id == CurrentPlanet.Id)
                    {
                        return;
                    }
                }
            }

            // Console.WriteLine("Egg");

            CurrentPlanet = planet;

            // Open planet if it's not opened
            await OpenPlanet(planet);

            
            if (planet == null)
                Console.WriteLine($"Set current planet to: null");
            else
                Console.WriteLine($"Set current planet to: " + planet.Name);

            if (OnPlanetChange != null)
            {
                Console.WriteLine($"Invoking planet change event");
                await OnPlanetChange?.Invoke(planet);
            }
        }

        public ClientPlanet GetCurrent()
        {
            return CurrentPlanet;
        }

        public override string ToString()
        {
            return $"Planet: {CurrentPlanet.Id}";
        }

        public async Task OnMessageRecieve(string json)
        {
            ClientPlanetMessage message = Newtonsoft.Json.JsonConvert.DeserializeObject<ClientPlanetMessage>(json);

            //Console.WriteLine("RECIEVE: ");
            //Console.WriteLine(json);

            //Console.WriteLine($"Recieved message {message.Message_Index} from channel {message.Channel_Id}.");

            if (!OpenPlanetChatChannels.ContainsKey(message.Channel_Id))
            {
                Console.WriteLine("Error: Recieved a message for a closed channel.");
            }

            foreach (ChatChannelWindow window in OpenPlanetChatWindows.Where(x => x.Channel.Id == message.Channel_Id))
            {
                await window.Component.OnRecieveMessage(message);
            }
        }

        public async Task MessageDeletion(string json)
        {
            ClientPlanetMessage msg = JsonConvert.DeserializeObject<ClientPlanetMessage>(json);

            if (msg == null)
            {
                Console.WriteLine("Failed to deserialize message in message deleton.");
                return;
            }

            Console.WriteLine($"RECIEVE: Planet message deletion ping for message {msg.Id}");
            Console.WriteLine(json);

            if (OnMessageDeletion != null)
            {
                await OnMessageDeletion.Invoke(msg);
            }
        }

        public async Task RoleDeletion(string json)
        {
            PlanetRole role = JsonConvert.DeserializeObject<PlanetRole>(json);

            if (role == null)
            {
                Console.WriteLine("Failed to deserialize role in role deletion.");
                return;
            }

            Console.WriteLine($"RECIEVE: Planet role deletion ping for role {role.Id}");
            Console.WriteLine(json);
            PlanetRole r;
            PlanetRolesCache.TryRemove(role.Id, out r);
            if (PlanetRolesListCache[role.Planet_Id].Contains(role.Id))
            {
                PlanetRolesListCache[role.Planet_Id].Remove(role.Id);
            }

            // check every member

            foreach (ClientPlanetMember member in PlanetMemberCache.Values)
            {
                member.RemoveRoleId(role.Id);
            }


            if (OnRoleDeletion != null)
            {
                await OnRoleDeletion.Invoke(role);
            }
        }

        public async Task UpdateRole(string json)
        {
            PlanetRole role = JsonConvert.DeserializeObject<PlanetRole>(json);

            if (role == null)
            {
                Console.WriteLine("Failed to deserialize role in role update.");
                return;
            }

            Console.WriteLine($"RECIEVE: Planet role update ping for role {role.Id}");
            Console.WriteLine(json);
            await SetUpdatedRole(role);
            if (!PlanetRolesListCache[role.Planet_Id].Contains(role.Id)) {
                var rolesList = PlanetRolesListCache[role.Planet_Id];
                rolesList.Insert(rolesList.Count()-1, role.Id);
            }
            

            if (OnRoleUpdate != null)
            {
                await OnRoleUpdate.Invoke(role);
            }
        }

        public async Task UpdateMember(string json)
        {
            ClientPlanetMember member = JsonConvert.DeserializeObject<ClientPlanetMember>(json);

            if (member == null)
            {
                Console.WriteLine("Failed to deserialize member in member update.");
                return;
            }

            Console.WriteLine("RECIEVE: Planet member update ping");
            Console.WriteLine(json);

            // get new roles for this member

            await member.LoadUserAsync();

            await member.GetRoleIdsAsync();

            await SetUpdatedMember(member);

            ClientPlanetMember m = PlanetMembersListCache[member.Planet_Id].Find(x => x.Id == member.Id);
            PlanetMembersListCache[member.Planet_Id].Remove(m);

            PlanetMembersListCache[member.Planet_Id].Add(member);

            if (OnMemberUpdate != null)
            {
                await OnMemberUpdate.Invoke(member);
            }
        }

        public async Task UpdateChatChannel(string json)
        {
            ClientPlanetChatChannel channel = JsonConvert.DeserializeObject<ClientPlanetChatChannel>(json);

            if (channel == null)
            {
                Console.WriteLine("Failed to deserialize channel in chat channel update.");
                return;
            }

            Console.WriteLine("RECIEVE: Planet chat channel update ping");
            Console.WriteLine(json);

            // update planet cache

            await PlanetCache[channel.Planet_Id].NotifyUpdateChannel(channel);

            if (OnChatChannelUpdate != null)
            {
                await OnChatChannelUpdate.Invoke(channel);
            }
        }
        public async Task CategoryDeletion(string json)
        {
            ClientPlanetCategory category = JsonConvert.DeserializeObject<ClientPlanetCategory>(json);

            if (category == null)
            {
                Console.WriteLine("Failed to deserialize category in chat category update.");
                return;
            }

            Console.WriteLine("RECIEVE: Planet Category Deletion ping");
            Console.WriteLine(json);

            // Update planet cache

            PlanetCache[category.Planet_Id].NotifyDeleteCategory(category);

            if (OnCategoryDeletion != null)
            {
                await OnCategoryDeletion.Invoke(category);
            }
        }
        public async Task ChatChannelDeletion(string json)
        {
            ClientPlanetChatChannel channel = JsonConvert.DeserializeObject<ClientPlanetChatChannel>(json);

            if (channel == null)
            {
                Console.WriteLine("Failed to deserialize channel in chat channel update.");
                return;
            }

            Console.WriteLine("RECIEVE: Planet chat channel Deletion ping");
            Console.WriteLine(json);

            // Update planet cache

            PlanetCache[channel.Planet_Id].NotifyDeleteChannel(channel);

            if (OnChatChannelDeletion != null)
            {
                await OnChatChannelDeletion.Invoke(channel);
            }
        }

        public async Task UpdateCategory(string json)
        {
            ClientPlanetCategory category = JsonConvert.DeserializeObject<ClientPlanetCategory>(json);

            if (category == null)
            {
                Console.WriteLine("Failed to deserialize category in chat category update.");
                return;
            }

            Console.WriteLine("RECIEVE: Planet chat category update ping");
            Console.WriteLine(json);

            // update planet cache

            await PlanetCache[category.Planet_Id].NotifyUpdateCategory(category);

            if (OnCategoryUpdate != null)
            {
                await OnCategoryUpdate.Invoke(category);
            }
        }
    }
}
