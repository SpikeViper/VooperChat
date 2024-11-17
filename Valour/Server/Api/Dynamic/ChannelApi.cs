using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Valour.Server.Requests;
using Valour.Shared.Authorization;
using Valour.Shared.Channels;
using Valour.Shared.Models;

namespace Valour.Server.Api.Dynamic;

public class ChannelApi
{
    [ValourRoute(HttpVerbs.Get, "api/channels/{id}")]
    [UserRequired]
    public static async Task<IResult> GetRouteAsync(
        long id,
        ChannelService channelService,
        TokenService tokenService)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        
        var channel = await channelService.GetAsync(id);
        if (channel is null)
            return ValourResult.NotFound<Channel>();

        if (!await channelService.IsMemberAsync(channel, token.UserId))
            return ValourResult.Forbid("You are not a member of this channel");
        
        if (channel.ChannelType == ChannelTypeEnum.DirectChat)
        {
            if (!token.HasScope(UserPermissions.DirectMessages))
            {
                return ValourResult.Forbid("Token lacks permission to post messages in direct chat channels");
            }
        }
        
        return Results.Json(channel);
    }

    [ValourRoute(HttpVerbs.Put, "api/channels/{id}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> UpdateRouteAsync(
        [FromBody] Channel updated,
        long id,
        ChannelService channelService,
        PlanetMemberService memberService)
    {
        if (updated.Id != id)
            return ValourResult.BadRequest("Channel id in body does not match channel id in route");
        
        var old = await channelService.GetAsync(id);
        if (old is null)
            return ValourResult.NotFound<Channel>();

        if (old.PlanetId is null)
        {
            return ValourResult.BadRequest("Only planet channels can be updated through this endpoint");
        }
        
        // Get the planet member
        var member = await memberService.GetCurrentAsync(old.PlanetId.Value);
        if (member is null)
            return ValourResult.Forbid("You are not a member of this channel");

        if (!await channelService.HasPermissionAsync(old, member, ChatChannelPermissions.ManageChannel))
        {
            return ValourResult.Forbid("You do not have permission to update this channel");
        }

        var result = await channelService.UpdateAsync(updated);
        if (!result.Success)
        {
            return ValourResult.BadRequest(result.Message);
        }

        return ValourResult.Json(result.Data);
    }
    
    [ValourRoute(HttpVerbs.Delete, "api/channels/{id}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> DeleteRouteAsync(
        long id,
        ChannelService channelService,
        PlanetMemberService memberService)
    {
        var channel = await channelService.GetAsync(id);
        if (channel is null)
            return ValourResult.NotFound("Channel not found");

        if (channel.PlanetId is null)
        {
            return ValourResult.BadRequest("Only planet channels can be deleted through this endpoint");
        }
        
        var member = await memberService.GetCurrentAsync(channel.PlanetId.Value);
        if (member is null)
            return ValourResult.Forbid("You are not a member of this channel");

        if (!await channelService.HasPermissionAsync(channel, member, ChatChannelPermissions.ManageChannel))
        {
            return ValourResult.Forbid("You do not have permission to delete this channel");
        }

        var result = await channelService.DeleteAsync(id);
        if (!result.Success)
        {
            return ValourResult.BadRequest(result.Message);
        }

        return Results.Ok();
    }

    [ValourRoute(HttpVerbs.Post, "api/channels")]
    [UserRequired]
    public static async Task<IResult> CreateAsync(
        [FromBody] CreateChannelRequest request,
        ChannelService channelService,
        TokenService tokenService,
        PlanetMemberService memberService,
        PlanetRoleService roleService)
    {
        var channel = request.Channel;
        
        if (channel is null)
            return ValourResult.BadRequest("Include channel in body");
        
        var token = await tokenService.GetCurrentTokenAsync();
        
        // Planet channel fun
        if (channel.PlanetId is null)
        {
            return ValourResult.BadRequest("Only planet channels can be created through this endpoint");
        }
        
        var member = await memberService.GetCurrentAsync(channel.PlanetId.Value);
        
        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.CreateChannels))
        {
            return ValourResult.BadRequest("You do not have permission to create channels");
        }
        
        // Check permission for the category we are inserting into
        if (channel.ParentId is not null)
        {
            var parent = await channelService.GetAsync(channel.ParentId.Value);
            if (parent is null || parent.ChannelType != ChannelTypeEnum.PlanetCategory)
            {
                return ValourResult.BadRequest("Invalid parent id");
            }

            if (!await memberService.HasPermissionAsync(member, parent, CategoryPermissions.ManageCategory))
            {
                return ValourResult.BadRequest("You do not have permission to insert into this category");
            }
        }

        if (request.Nodes is not null && request.Nodes.Count > 0)
        {
            var memberAuthority = await memberService.GetAuthorityAsync(member);
            
            foreach (var node in request.Nodes)
            {
                var role = await roleService.GetAsync(node.RoleId);
                if (memberAuthority < role.GetAuthority())
                {
                    return ValourResult.Forbid("A permission node's role cannot have higher authority than the member creating it");
                }
            }
        }
        
        var result = await channelService.CreateAsync(request.Channel, request.Nodes);
        if (!result.Success)
        {
            return ValourResult.BadRequest(result.Message);
        }
        
        return Results.Json(result.Data);
    }
    
    [ValourRoute(HttpVerbs.Get, "api/channels/direct/{otherUserId}")]
    [UserRequired(UserPermissionsEnum.DirectMessages)]
    public static async Task<IResult> GetDirectRouteAsync(
        long otherUserId,
        ChannelService channelService,
        UserService userService,
        [FromQuery] bool create = true)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        
        var channel = await channelService.GetDirectChatAsync(userId, otherUserId, create);
        if (channel is null)
            return ValourResult.NotFound<Channel>();

        return Results.Json(channel);
    }

    [ValourRoute(HttpVerbs.Get, "api/channels/direct/self")] 
    [UserRequired(UserPermissionsEnum.DirectMessages)]
    public static async Task<IResult> GetAllDirectRouteAsync(
        ChannelService channelService,
        TokenService tokenService)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        var channels = await channelService.GetAllDirectAsync(token.UserId);
        
        if (channels is null)
            channels = new List<Channel>();

        return Results.Json(channels);
    }
    
    
    [ValourRoute(HttpVerbs.Post, "api/channels/{channelId}/messages")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> PostMessageRouteAsync(
        [FromBody] Message message, 
        long channelId,
        ChannelService channelService,
        PlanetMemberService memberService,
        TokenService tokenService,
        PlanetRoleService roleService)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        var userId = token.UserId;
        
        if (message is null)
            return ValourResult.BadRequest("Include message in body");
        
        var channel = await channelService.GetAsync(channelId);
        if (channel is null)
            return ValourResult.NotFound("Channel not found");
        
        if (!await channelService.IsMemberAsync(channel, userId))
            return ValourResult.Forbid("You are not a member of this channel");

        if (channel.ChannelType == ChannelTypeEnum.DirectChat)
        {
            if (!token.HasScope(UserPermissions.DirectMessages))
            {
                return ValourResult.Forbid("Token lacks permission to post messages in direct chat channels");
            }
        }
        
        // For planet channels, planet roles and membership are used
        // to determine if the user can post messages and content
        if (channel.PlanetId is not null)
        {
            var member = await memberService.GetCurrentAsync(channel.PlanetId.Value);
            if (member is null)
                return ValourResult.Forbid("You are not a member of the planet this channel belongs to");
            
            // NOTE: We don't have to check View permission because lacking view will
            // cause every other permission check to fail
            
            // Check for message posting permissions
            if (!await memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.PostMessages))
                return ValourResult.Forbid("You lack permission to post messages in this channel");
            
            // If the message has attachments...
            if (!string.IsNullOrWhiteSpace(message.AttachmentsData))
            {
                if (!await memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.AttachContent))
                    return ValourResult.Forbid("You lack permission to attach content to messages in this channel");
            }

            // If the message has embed data...
            if (!string.IsNullOrWhiteSpace(message.EmbedData))
            {
                if (!await memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.Embed))
                    return ValourResult.Forbid("You lack permission to attach embeds to messages in this channel");
            }
            
            // Check mention permissions...
            if (!string.IsNullOrWhiteSpace(message.MentionsData))
            {
                var mentions = JsonSerializer.Deserialize<List<Mention>>(message.MentionsData);
                if (mentions is not null)
                {
                    foreach (var mention in mentions)
                    {
                        if (mention.Type == MentionType.Role)
                        {
                            var role = await roleService.GetAsync(mention.TargetId);
                            if (role is null)
                                return ValourResult.BadRequest("Invalid role mention");

                            if (role.AnyoneCanMention)
                                continue;

                            if (!await memberService.HasPermissionAsync(member, PlanetPermissions.MentionAll))
                                return ValourResult.Forbid($"You lack permission to mention the role {role.Name}");
                        }
                    }
                }
                else
                {
                    message.MentionsData = null;
                }
            }
        }
        
        var result = await channelService.PostMessageAsync(message);
        if (!result.Success)
        {
            return ValourResult.BadRequest(result.Message);
        }

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Put, "api/channels/{channelId}/messages/{messageId}")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> EditMessageRouteAsync(
        [FromBody] Message message,
        long channelId,
        long messageId,
        ChannelService channelService,
        TokenService tokenService)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        
        if (message.PlanetId is null)
        {
            if (!token.HasScope(UserPermissions.DirectMessages))
            {
                return ValourResult.Forbid("Token lacks permission to delete messages in direct chat channels");
            }
        }
        
        if (message.Id != messageId)
            return ValourResult.BadRequest("Message id in body does not match message id in route");
        
        if (message.ChannelId != channelId)
            return ValourResult.BadRequest("Channel id in body does not match channel id in route");
        
        if (message.AuthorUserId != token.UserId)
            return ValourResult.Forbid("You are not the author of this message");
        
        var result = await channelService.EditMessageAsync(message);
        if (!result.Success)
        {
            return ValourResult.BadRequest(result.Message);
        }

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Delete, "api/channels/{channelId}/messages/{messageId}")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> DeleteMessageRouteAsync(
        long messageId,
        long channelId,
        ChannelService channelService,
        TokenService tokenService,
        PlanetMemberService memberService)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        var message = await channelService.GetMessageNoReplyAsync(messageId);

        if (message is null)
            return ValourResult.NotFound<Message>();

        if (message.ChannelId != channelId)
            return ValourResult.BadRequest("Channel id in message does not match channel id in route");

        // Direct messages require stronger token perms
        if (message.PlanetId is null)
        {
            if (!token.HasScope(UserPermissions.DirectMessages))
            {
                return ValourResult.Forbid("Token lacks permission to delete messages in direct chat channels");
            }
        }
        
        // Determine permissions

        // First off, if the user is the sender they can always delete
        if (message.AuthorUserId != token.UserId)
        {
            if (message.PlanetId is null)
                return ValourResult.Forbid("Only the sender can delete these messages");
            
            // Otherwise, we check planet channel permissions
            var member = await memberService.GetCurrentAsync(message.PlanetId.Value);
            if (member is null)
                return ValourResult.Forbid("You are not a member of the planet this channel belongs to");

            var channel = await channelService.GetAsync(message.ChannelId);

            if (!await memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.ManageMessages))
            {
                return ValourResult.Forbid("You do not have permission to manage messages in this channel");
            }
        }
        
        var result = await channelService.DeleteMessageAsync(message.Id);
        if (!result.Success)
        {
            return ValourResult.BadRequest(result.Message);
        }
        
        return Results.Ok();
    }

    [ValourRoute(HttpVerbs.Get, "api/channels/{channelId}/messages/{messageId}")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> GetMessageAsync(
        long channelId,
        long messageId,
        ChannelService channelService,
        TokenService tokenService)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        
        var message = await channelService.GetMessageAsync(messageId);
        if (message is null)
            return ValourResult.NotFound<Message>();
        
        if (message.ChannelId != channelId)
            return ValourResult.BadRequest("Channel id in message does not match channel id in route");

        var channel = await channelService.GetAsync(channelId);
        if (!await channelService.IsMemberAsync(channel, token.UserId))
            return ValourResult.Forbid("You are not a member of this channel");
        
        if (message.PlanetId is null)
        {
            if (!token.HasScope(UserPermissions.DirectMessages))
            {
                return ValourResult.Forbid("Token lacks permission to delete messages in this channel");
            }
        }
        
        return Results.Json(message);
    }
    
    [ValourRoute(HttpVerbs.Get, "api/channels/{channelId}/messages")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> GetMessagesAsync(
        long channelId,
        ChannelService channelService,
        TokenService tokenService,
        long index = long.MaxValue,
        int count = 10)
    {
        if (count > 64)
            return Results.BadRequest("Maximum count is 64.");
        
        var token = await tokenService.GetCurrentTokenAsync();
        var channel = await channelService.GetAsync(channelId);
        
        if (!await channelService.IsMemberAsync(channel, token.UserId))
            return ValourResult.Forbid("You are not a member of this channel");
        
        if (channel.PlanetId is null)
        {
            if (!token.HasScope(UserPermissions.DirectMessages))
            {
                return ValourResult.Forbid("Token lacks permission to delete messages in this channel");
            }
        }
        
        var messages = await channelService.GetMessagesAsync(channelId, count, index);
        
        return Results.Json(messages);
    }

    [ValourRoute(HttpVerbs.Get, "api/channels/{channelId}/children")]
    [UserRequired]
    public static async Task<IResult> GetChildrenAsync(
        long channelId,
        ChannelService channelService,
        PlanetMemberService memberService)
    {
        var channel = await channelService.GetAsync(channelId);
        if (channel is null)
            return ValourResult.NotFound("Channel not found");

        if (channel.ChannelType != ChannelTypeEnum.PlanetCategory)
            return Results.Json(Array.Empty<long>());

        var member = await memberService.GetCurrentAsync(channel.PlanetId!.Value);
        if (member is null)
            return ValourResult.NotPlanetMember();
        
        var children = await channelService.GetChildrenIdsAsync(channelId);
        return Results.Json(children);
    }
    
    [ValourRoute(HttpVerbs.Get, "api/channels/{channelId}/nodes")]
    [UserRequired]
    public static async Task<IResult> GetNodesAsync(
        long channelId,
        ChannelService channelService,
        PlanetMemberService memberService)
    {
        var channel = await channelService.GetAsync(channelId);
        if (channel is null)
            return ValourResult.NotFound("Channel not found");

        if (channel.ChannelType != ChannelTypeEnum.PlanetCategory)
            return Results.Json(Array.Empty<PermissionsNode>());

        var member = await memberService.GetCurrentAsync(channel.PlanetId!.Value);
        if (member is null)
            return ValourResult.NotPlanetMember();
        
        var nodes = await channelService.GetPermissionNodesAsync(channelId);
        return Results.Json(nodes);
    }

    [ValourRoute(HttpVerbs.Get, "api/channels/{channelId}/nonPlanetMembers")]
    [UserRequired]
    // Note: DOES NOT RETURN PLANET MEMBERS!
    public static async Task<IResult> GetChannelMembersAsync(
        long channelId,
        ChannelService channelService,
        TokenService tokenService)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        
        if (!await channelService.IsMemberAsync(channelId, token.UserId))
            return ValourResult.Forbid("You are not a member of this channel");

        return Results.Json(await channelService.GetMembersNonPlanetAsync(channelId));
    }
    
    [ValourRoute(HttpVerbs.Post, "api/channels/{id}/typing")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> PostTypingAsync(
        long id, 
        CurrentlyTypingService typingService,
        ChannelService channelService,
        TokenService tokenService)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        
        // Get the channel
        var channel = await channelService.GetAsync(id);
        if (channel is null)
            return ValourResult.NotFound("Channel not found");

        if (!await channelService.IsMemberAsync(channel, token.UserId))
        {
            return ValourResult.Forbid("You are not a member of this channel");
        }

        typingService.AddCurrentlyTyping(id, token.UserId);
        
        return Results.Ok();
    }

    [ValourRoute(HttpVerbs.Post, "api/channels/{id}/state")]
    [UserRequired]
    public static async Task<IResult> UpdateStateAsync(
        long id,
        [FromBody] UpdateUserChannelStateRequest request,
        ChannelStateService stateService,
        ChannelService channelService,
        TokenService tokenService)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        
        var channel = await channelService.GetAsync(id);
        if (channel is null)
            return ValourResult.NotFound("Channel not found");
        
        if (!await channelService.IsMemberAsync(channel, token.UserId))
        {
            return ValourResult.Forbid("You are not a member of this channel");
        }

        var updated = await stateService.UpdateUserChannelState(id, token.UserId, request.UpdateTime);

        return ValourResult.Json(updated);
    }
}