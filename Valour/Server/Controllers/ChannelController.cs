using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Channels;
using Valour.Shared.Messages;
using Valour.Shared.Oauth;
using Microsoft.EntityFrameworkCore;
using Valour.Server.Workers;
using Valour.Server.Planets;
using AutoMapper;
using Valour.Server.Oauth;
using Valour.Server.Categories;
using WebPush;
using Valour.Server.MPS;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Controllers
{
    /// <summary>
    /// Controls all routes for channels
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class ChannelController
    {

        public static List<PlanetMessage> messageCache = new List<PlanetMessage>();

        /// <summary>
        /// Database context
        /// </summary>
        private readonly ValourDB Context;

        private readonly IMapper Mapper;
        private readonly WebPushClient PushClient;

        // Dependency injection
        public ChannelController(ValourDB context, IMapper mapper, WebPushClient pushClient)
        {
            this.Context = context;
            this.Mapper = mapper;
            this.PushClient = pushClient;
        }
        
        public async Task<TaskResult> Delete(ulong id, ulong user_id, string token)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            // Return the same if the token is for the wrong user to prevent someone
            // from knowing if they cracked another user's token. This is basically 
            // impossible to happen by chance but better safe than sorry in the case that
            // the literal impossible odds occur, more likely someone gets a stolen token
            // but is not aware of the owner but I'll shut up now - Spike
            if (authToken == null || authToken.User_Id != user_id)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            ServerPlanetChatChannel channel = await Context.PlanetChatChannels.FindAsync(id);

            if (channel == null)
            {
                return new TaskResult(false, "That channel does not exist.");
            }

            ServerPlanet planet = await ServerPlanet.FindAsync(channel.Planet_Id);

            if (planet == null)
            {
                return new TaskResult(false, "Could not find the planet.");
            }

            if (!(await planet.AuthorizedAsync(authToken, PlanetPermissions.ManageChannels)))
            {
                return new TaskResult(false, "You are not authorized to do this.");
            }

            if (channel.Id == planet.Main_Channel_Id) {
                return new TaskResult(false, "You can not delete your main channel!");
            }

            // remove permission nodes

            Context.ChatChannelPermissionsNodes.RemoveRange(Context.ChatChannelPermissionsNodes.Where(x => x.Channel_Id == id));

            await Context.SaveChangesAsync();

            // remove all messages in this channel

            Context.PlanetMessages.RemoveRange(Context.PlanetMessages.Where(x => x.Channel_Id == id));

            await Context.SaveChangesAsync();

            Context.PlanetChatChannels.Remove(channel);

            await Context.SaveChangesAsync();

            // Notify of update
            await PlanetHub.NotifyChatChannelDeletion(channel);

            return new TaskResult(true, "Successfully deleted.");
        }

        public async Task<TaskResult> SetParentId(ulong id, ulong parent_id, string token)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            if (!authToken.HasScope(UserPermissions.PlanetManagement))
            {
                return new TaskResult(false, "Your token doesn't have planet management scope.");
            }

            ServerPlanetChatChannel channel = await Context.PlanetChatChannels.FindAsync(id);

            ServerPlanet planet = await ServerPlanet.FindAsync(channel.Planet_Id);

            if (!(await planet.AuthorizedAsync(authToken, PlanetPermissions.ManageChannels)))
            {
                return new TaskResult(false, "You are not authorized to do this.");
            }

            // Get parent category
            ServerPlanetCategory category = await Context.PlanetCategories.FindAsync(parent_id);

            if (category == null)
            {
                return new TaskResult(false, $"The category with id {parent_id} could not be found.");
            }

            channel.Parent_Id = parent_id;

            await Context.SaveChangesAsync();

            // Notify of update
            await PlanetHub.NotifyChatChannelChange(channel);
            
            return new TaskResult(true, "Successfully set parentid.");
        }

        /// <summary>
        /// Creates a server and if successful returns a task result with the created
        /// planet's id
        /// </summary>
        public async Task<TaskResult<ulong>> CreateChannel(string name, ulong planet_id, ulong user_id, ulong parentid, string token)
        {
            TaskResult nameValid = ServerPlanetChatChannel.ValidateName(name);

            if (!nameValid.Success)
            {
                return new TaskResult<ulong>(false, nameValid.Message, 0);
            }

            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            // Return the same if the token is for the wrong user to prevent someone
            // from knowing if they cracked another user's token. This is basically 
            // impossible to happen by chance but better safe than sorry in the case that
            // the literal impossible odds occur, more likely someone gets a stolen token
            // but is not aware of the owner but I'll shut up now - Spike
            if (authToken == null || authToken.User_Id != user_id)
            {
                return new TaskResult<ulong>(false, "Failed to authorize user.", 0);
            }

            ServerPlanet planet = await ServerPlanet.FindAsync(planet_id);

            if (!(await planet.AuthorizedAsync(authToken, PlanetPermissions.ManageChannels)))
            {
                return new TaskResult<ulong>(false, "You are not authorized to do this.", 0);
            }

            // User is verified and given channel info is valid by this point

            // Creates the channel channel

            ServerPlanetChatChannel channel = new ServerPlanetChatChannel()
            {
                Id = IdManager.Generate(),
                Name = name,
                Planet_Id = planet_id,
                Parent_Id = parentid,
                Message_Count = 0,
                Description = "A chat channel",
                Position = (ushort)Context.PlanetChatChannels.Where(x => x.Parent_Id == parentid).Count()
            };

            // Add channel to database
            await Context.PlanetChatChannels.AddAsync(channel);

            // Save changes to DB
            await Context.SaveChangesAsync();

            // Send channel refresh
            await PlanetHub.NotifyChatChannelChange(channel);

            // Return success
            return new TaskResult<ulong>(true, "Successfully created channel.", channel.Id);
        }
        
        public async Task<TaskResult<IEnumerable<ulong>>> GetChannelIdsAsync(ulong planet_id, string token)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null)
            {
                return new TaskResult<IEnumerable<ulong>>(false, "Please supply a valid authentication token.", null);
            }
            
            ServerPlanetMember member = await ServerPlanetMember.FindAsync(authToken.User_Id, planet_id, Context);

            if (member == null)
            {
                return new TaskResult<IEnumerable<ulong>>(false, "You are not a member of this planet.", null);
            }

            var channels = Context.PlanetChatChannels.Where(c => c.Planet_Id == planet_id);

            List<ulong> result = new List<ulong>();

            foreach (var channel in channels)
            {
                if (await channel.HasPermission(member, ChatChannelPermissions.View, Context))
                {
                    result.Add(channel.Id);
                }
            }

            return new TaskResult<IEnumerable<ulong>>(true, "Successfully retrieved channels.", result);;
        }

        [HttpGet]
        public async Task<TaskResult<IEnumerable<PlanetChatChannel>>> GetPlanetChannelsAsync(ulong planet_id, string token)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null)
            {
                return new TaskResult<IEnumerable<PlanetChatChannel>>(false, "Please supply a valid authentication token.", null);
            }

            ServerPlanetMember member = await ServerPlanetMember.FindAsync(authToken.User_Id, planet_id, Context);

            if (member == null)
            {
                return new TaskResult<IEnumerable<PlanetChatChannel>>(false, "You are not a member of this planet.", null);
            }

            var channels = Context.PlanetChatChannels.Where(c => c.Planet_Id == planet_id);

            List<PlanetChatChannel> result = new List<PlanetChatChannel>();

            foreach (var channel in channels)
            {
                if (await channel.HasPermission(member, ChatChannelPermissions.View, Context))
                {
                    result.Add(channel);
                }
            }

            return new TaskResult<IEnumerable<PlanetChatChannel>>(true, "Successfully retrieved channels.", result);
        }

        [HttpGet]
        public async Task<IEnumerable<PlanetMessage>> GetMessages(ulong channel_id, ulong index = ulong.MaxValue, int count = 10)
        {
            // Prevent requesting a ridiculous amount of messages
            if (count > 64)
            {
                count = 64;
            }

            List<PlanetMessage> staged = PlanetMessageWorker.GetStagedMessages(channel_id, count);
            List<PlanetMessage> messages = null;

            count = count - staged.Count;

            if (count > 0)
            {
                await Task.Run(() =>
                {
                    messages =
                    Context.PlanetMessages.Where(x => x.Channel_Id == channel_id && x.Message_Index < index)
                                          .OrderByDescending(x => x.Message_Index)
                                          .Take(count)
                                          .Reverse()
                                          .ToList();
                });

                messages.AddRange(staged.Where(x => x.Message_Index < index));
            }

            return messages;
        }

        [HttpGet]
        public async Task<IEnumerable<PlanetMessage>> GetLastMessages(ulong channel_id, int count = 10)
        {
            // Prevent requesting a ridiculous amount of messages
            if (count > 64)
            {
                count = 64;
            }

            List<PlanetMessage> staged = PlanetMessageWorker.GetStagedMessages(channel_id, count);
            List<PlanetMessage> messages = null;

            count = count - staged.Count;

            if (count > 0)
            {
                await Task.Run(() =>
                {
                    messages =
                    Context.PlanetMessages.Where(x => x.Channel_Id == channel_id)
                                          .OrderByDescending(x => x.Message_Index)
                                          .Take(count)
                                          .Reverse()
                                          .ToList();
                });

                messages.AddRange(staged);
            }

            return messages;
        }
        public async Task<TaskResult> DeleteMessage(ulong id, string token)
        {

            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            // first check if message is in message worker

            bool FromStageMessages = true;

            PlanetMessage msg = PlanetMessageWorker.TryGetMessage(id);

            if (msg == null)
            {
                msg = await Context.PlanetMessages.FirstOrDefaultAsync(x => x.Id == id);
                FromStageMessages = false;
            }

            // if we still can't find message, return error

            if (msg == null)
            {
                return new TaskResult(false, "Failed to delete message: could not find message!");
            }

            ServerPlanetChatChannel channel = await Context.PlanetChatChannels.FirstOrDefaultAsync(x => x.Id == msg.Channel_Id);

            if (channel == null)
            {
                return new TaskResult(false, "Failed to delete message: Could not find channel!");
            }

            ServerPlanetMember member = await Context.PlanetMembers.FirstOrDefaultAsync(x => x.User_Id == authToken.User_Id && x.Planet_Id == channel.Planet_Id);

            if (member == null)
            {
                return new TaskResult(false, "Failed to delete message: You are not in the planet!");
            }

            // check permission to see if the user can delete a message

            // only check permission if the author is not the same
            if (!(msg.Author_Id == member.Id))
            {
                if (!(await channel.HasPermission(member, ChatChannelPermissions.ManageChannel, Context)))
                {
                    return new TaskResult(false, "Failed to delete message: You don't have permission to delete!");
                }

                if (!(await channel.HasPermission(member, ChatChannelPermissions.FullControl, Context)))
                {
                    return new TaskResult(false, "Failed to delete message: You don't have permission to delete!");
                }
            }

            if (FromStageMessages)
            {
                PlanetMessageWorker.RemoveFromStaged(msg);
            }

            else
            {
                Context.PlanetMessages.Remove(msg);
            }

            string json = JsonConvert.SerializeObject(msg);

            // This is not awaited on purpose
            PlanetHub.Current.Clients.Group($"c-{msg.Channel_Id}").SendAsync("MessageDeletion", json);

            await Context.SaveChangesAsync();

            return new TaskResult(true, "Deleted Message");
        }

            [HttpPost]
        public async Task<TaskResult> PostMessage(PlanetMessage msg, string token)
        {

            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null || authToken.User_Id != msg.Author_Id)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            ServerPlanetChatChannel channel = await Context.PlanetChatChannels.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Id == msg.Channel_Id);

            if (channel == null)
            {
                return new TaskResult(false, "Failed to post message: The given channel does not exist!");
            }

            ServerPlanetMember member = await Context.PlanetMembers.FindAsync(msg.Member_Id);

            if (member == null)
            {
                return new TaskResult(false, "Failed to post message: You are not in the planet!");
            }

            if (member.User_Id != msg.Author_Id)
            {
                return new TaskResult(false, "Failed to post message: User Id mismatch! Attempt at impersonation?");
            }

            if (!(await channel.HasPermission(member, ChatChannelPermissions.View, Context)))
            {
                return new TaskResult(false, "Failed to post message: You don't have permission to see this channel!");
            }

            if (!(await channel.HasPermission(member, ChatChannelPermissions.PostMessages, Context)))
            {
                return new TaskResult(false, "Failed to post message: You don't have permission to post here!");
            }

            //ClientMessage msg = JsonConvert.DeserializeObject<ClientMessage>(json);

            if (msg == null)
            {
                return new TaskResult(false, "Malformed message.");
            }

            // Stop people from sending insanely large messages
            if (msg.Content.Length > 2048)
            {
                return new TaskResult(false, "Message is longer than 2048 chars.");
            }

            // Media proxy layer
            msg.Content = await MPSManager.HandleUrls(msg.Content);

            PlanetMessageWorker.AddToQueue(msg);

            StatWorker.IncreaseMessageCount();

            /*

            // Run this in another thread as quickly as possible
#pragma warning disable CS4014 
            Task.Run(async () =>
            {
                using (ValourDB db = new ValourDB(ValourDB.DBOptions))
                {
                    // Get all members of channel
                    var not_members = await channel.GetChannelMembersAsync(db);

                    // For each member get their subscribed devices and send notifications
                    //Parallel.ForEach(not_members, async mem =>
                    //{
                    foreach (var mem in not_members)
                    {
                        var not_subs = db.NotificationSubscriptions.Where(x => x.User_Id == mem.User_Id);

                        //Parallel.ForEach(not_subs, async sub =>
                        //{
                        foreach (var sub in not_subs)
                        {
                            var subscription = new PushSubscription(sub.Endpoint, sub.Not_Key, sub.Auth);

                            try
                            {
                                var details = VapidConfig.Current.GetDetails();

                                var payload = $"{{'title':'{member.Id}','message':'{msg.Content}'}}";

                                var pushClient = new WebPushClient();

                                await pushClient.SendNotificationAsync(subscription, payload, details);
                            }
                            catch (System.Exception e)
                            { 
                                Console.WriteLine(e.Message);
                            }
                        }
                        //});
                    }
                    //});
                }
            });

            var sub = await Context.NotificationSubscriptions.FirstOrDefaultAsync();

            var subscription = new PushSubscription(ep, ke, au);


            var details = new VapidDetails(VapidConfig.Current.Subject, VapidConfig.Current.PublicKey, VapidConfig.Current.PrivateKey);

            var payload = JsonSerializer.Serialize(
                new
                {
                    title = member.Nickname,
                    message = msg.Content
                }
            );

            var pushClient = new TestWebPushClient();

            await pushClient.SendNotificationAsync(subscription, payload, details);

            */

#pragma warning restore CS4014 


            return new TaskResult(true, "Added message to post queue.");
        }

        public async Task<TaskResult> SetName(ulong channel_id, string name, string token)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            ServerPlanetChatChannel channel = await Context.PlanetChatChannels.Include(x => x.Planet)
                                                                              .FirstOrDefaultAsync(x => x.Id == channel_id);

            if (channel == null)
            {
                return new TaskResult(false, $"Could not find channel {channel_id}");
            }

            if (authToken == null)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            if (!authToken.HasScope(UserPermissions.PlanetManagement))
            {
                return new TaskResult(false, "Your token doesn't have planet management scope.");
            }

            // Membership check

            ServerPlanetMember member = await ServerPlanetMember.FindAsync(authToken.User_Id, channel.Planet.Id);

            if (member == null)
            {
                return new TaskResult(false, "You are not a member of the target planet.");
            }

            if (!(await member.HasPermissionAsync(PlanetPermissions.ManageChannels)))
            {
                return new TaskResult(false, "You do not have planet channel management permissions.");
            }

            channel.Name = name;
            await Context.SaveChangesAsync();

            // Send channel refresh
            await PlanetHub.NotifyChatChannelChange(channel);

            return new TaskResult(true, "Successfully changed channel name.");
        }

        public async Task<TaskResult> SetDescription(ulong channel_id, string description, string token)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            ServerPlanetChatChannel channel = await Context.PlanetChatChannels.Include(x => x.Planet)
                                                                              .FirstOrDefaultAsync(x => x.Id == channel_id);

            if (channel == null)
            {
                return new TaskResult(false, $"Could not find channel {channel_id}");
            }

            if (authToken == null)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            if (!authToken.HasScope(UserPermissions.PlanetManagement))
            {
                return new TaskResult(false, "Your token doesn't have planet management scope.");
            }

            // Membership check

            ServerPlanetMember member = await ServerPlanetMember.FindAsync(authToken.User_Id, channel.Planet.Id);

            if (member == null)
            {
                return new TaskResult(false, "You are not a member of the target planet.");
            }

            if (!(await member.HasPermissionAsync(PlanetPermissions.ManageChannels)))
            {
                return new TaskResult(false, "You do not have planet channel management permissions.");
            }

            channel.Description = description;
            await Context.SaveChangesAsync();

            // Send channel refresh
            await PlanetHub.NotifyChatChannelChange(channel);

            return new TaskResult(true, "Successfully changed channel description.");
        }

        public async Task<TaskResult> SetPermissionInheritMode(ulong channel_id, bool value, string token)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            ServerPlanetChatChannel channel = await Context.PlanetChatChannels.Include(x => x.Planet)
                                                                              .FirstOrDefaultAsync(x => x.Id == channel_id);

            if (channel == null)
            {
                return new TaskResult(false, $"Could not find channel {channel_id}");
            }

            if (authToken == null)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            if (!authToken.HasScope(UserPermissions.PlanetManagement))
            {
                return new TaskResult(false, "Your token doesn't have planet management scope.");
            }

            // Membership check

            ServerPlanetMember member = await ServerPlanetMember.FindAsync(authToken.User_Id, channel.Planet.Id);

            if (member == null)
            {
                return new TaskResult(false, "You are not a member of the target planet.");
            }

            if (!(await member.HasPermissionAsync(PlanetPermissions.ManageChannels)))
            {
                return new TaskResult(false, "You do not have planet channel management permissions.");
            }

            channel.Inherits_Perms = value;
            await Context.SaveChangesAsync();

            // Send channel refresh
            await PlanetHub.NotifyChatChannelChange(channel);

            return new TaskResult(true, $"Successfully set permission inheritance to {value}.");
        }
    }
}
