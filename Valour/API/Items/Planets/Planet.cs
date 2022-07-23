﻿using System.Text.Json.Serialization;
using Valour.Api.Client;
using Valour.Api.Items.Planets.Channels;
using Valour.Api.Items.Planets.Members;
using Valour.Shared;
using Valour.Shared.Items;
using Valour.Shared.Items.Planets;

namespace Valour.Api.Items.Planets;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */
public class Planet : Item, ISharedPlanet
{
    // Cached values
    private List<PlanetChatChannel> Channels { get; set; }
    private List<PlanetCategoryChannel> Categories { get; set; }
    private List<PlanetRole> Roles { get; set; }
    private List<PlanetMember> Members { get; set; }
    private List<PlanetInvite> Invites { get; set; }

    /// <summary>
    /// The Id of the owner of this planet
    /// </summary>
    public long OwnerId { get; set; }

    /// <summary>
    /// The name of this planet
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The image url for the planet 
    /// </summary>
    public string IconUrl { get; set; }

    /// <summary>
    /// The description of the planet
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// If the server requires express allowal to join a planet
    /// </summary>
    public bool Public { get; set; }

    /// <summary>
    /// The default role for the planet
    /// </summary>
    public long? DefaultRoleId { get; set; }

    /// <summary>
    /// The id of the main channel of the planet
    /// </summary>
    public long? PrimaryChannelId { get; set; }

    public Planet()
    {
        ItemObserver<PlanetMember>.OnAnyUpdated += OnMemberUpdateAsync;
    }

    public async Task OnMemberUpdateAsync(PlanetMember member, bool newItem, int flags)
    {
        if (member.PlanetId != Id)
            return;

        // Ensure that we add new members to the planet cache
        if (newItem)
            Members.Add(member);
    }

    /// <summary>
    /// Retrieves and returns a client planet by requesting from the server
    /// </summary>
    public static async ValueTask<Planet> FindAsync(long id, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<Planet>(id);
            if (cached is not null)
                return cached;
        }

        var item = (await ValourClient.GetJsonAsync<Planet>($"api/{nameof(Planet)}/{id}")).Data;

        if (item is not null)
            await item.AddToCache();

        return item;
    }

    /// <summary>
    /// Returns the primary channel of the planet
    /// </summary>
    public async ValueTask<PlanetChatChannel> GetPrimaryChannelAsync(bool refresh = false)
    {
        if (Channels == null || refresh)
            await LoadChannelsAsync();

        return await PlanetChatChannel.FindAsync(PrimaryChannelId.Value, Id, refresh);
    }

    /// <summary>
    /// Returns the categories of this planet
    /// </summary>
    public async ValueTask<List<PlanetCategoryChannel>> GetCategoriesAsync(bool refresh = false)
    {
        if (Categories == null || refresh)
            await LoadCategoriesAsync();

        return Categories;
    }

    /// <summary>
    /// Requests and caches categories from the server
    /// </summary>
    public async Task LoadCategoriesAsync()
    {
        var categories = (await ValourClient.GetJsonAsync<List<PlanetCategoryChannel>>($"{IdRoute}/categories")).Data;

        if (categories is null)
            return;

        // Update cache values
        foreach (var category in categories)
        {
            // Skip event for bulk loading
            await ValourCache.Put(category.Id, category, true);
        }

        // Create container if needed
        if (Categories == null)
            Categories = new List<PlanetCategoryChannel>();
        else
            Categories.Clear();

        // Retrieve cache values (this is necessary to ensure single copies of items)
        foreach (var category in categories)
        {
            var cCat = ValourCache.Get<PlanetCategoryChannel>(category.Id);

            if (cCat is not null)
                Categories.Add(cCat);
        }

        // Sort via position
        Categories.Sort((a, b) => a.Position.CompareTo(b.Position));
    }

    /// <summary>
    /// Returns the channels of a planet
    /// </summary>
    public async ValueTask<List<PlanetChatChannel>> GetChannelsAsync(bool force_refresh = false)
    {
        if (Channels == null || force_refresh)
        {
            await LoadChannelsAsync();
        }

        return Channels;
    }

    /// <summary>
    /// Requests and caches channels from the server
    /// </summary>
    public async Task LoadChannelsAsync()
    {
        var channels = (await ValourClient.GetJsonAsync<List<PlanetChatChannel>>($"{IdRoute}/chatchannels")).Data;

        if (channels is null)
            return;

        foreach (var channel in channels)
        {
            // Skip event for bulk loading
            await ValourCache.Put(channel.Id, channel, true);
        }

        // Create container if needed
        if (Channels == null)
            Channels = new List<PlanetChatChannel>();
        else
            Channels.Clear();

        // Retrieve cache values (this is necessary to ensure single copies of items)
        foreach (var channel in channels)
        {
            var cChan = ValourCache.Get<PlanetChatChannel>(channel.Id);

            if (cChan is not null)
                Channels.Add(cChan);
        }

        // Sort via position
        Channels.Sort((a, b) => a.Position.CompareTo(b.Position));
    }

    /// <summary>
    /// Returns the members of the planet
    /// </summary>
    public async ValueTask<List<PlanetMember>> GetMembersAsync(bool force_refresh = false)
    {
        if (Members is null || force_refresh)
        {
            await LoadMemberDataAsync();
        }

        return Members;
    }

    /// <summary>
    /// Loads the member data for the planet (this is quite heavy) 
    /// </summary>
    public async Task LoadMemberDataAsync()
    {
        var result = (await ValourClient.GetJsonAsync<PlanetMemberInfo>($"{IdRoute}/memberinfo")).Data;

        if (Members is null)
            Members = new List<PlanetMember>();
        else
            Members.Clear();

        foreach (var info in result.Members)
        {
            // Set role id data manually
            await info.Member.SetLocalRoleIds(info.RoleIds);

            // Set in cache
            // Skip event for bulk loading
            await ValourCache.Put(info.Member.Id, info.Member, true);
            await ValourCache.Put((info.Member.PlanetId, info.Member.UserId), info.Member, true);
            await ValourCache.Put(info.Member.UserId, info.User, true);
        }

        foreach (var info in result.Members)
        {
            var member = ValourCache.Get<PlanetMember>(info.Member.Id);

            if (member is not null)
                Members.Add(member);
        }
    }

    /// <summary>
    /// Returns the invites of the planet
    /// </summary>
    public async ValueTask<List<PlanetInvite>> GetInvitesAsync(bool refresh = false)
    {
        if (Invites is null || refresh)
            await LoadInvitesAsync();

        return Invites;
    }

    /// <summary>
    /// Loads the planet's invites from the server
    /// </summary>
    public async Task LoadInvitesAsync()
    {
        var invites = (await ValourClient.GetJsonAsync<List<PlanetInvite>>($"api/Planet/{Id}/invites")).Data;

        if (invites is null)
            return;

        foreach (var invite in invites)
        {
            // Skip event for bulk loading
            await ValourCache.Put(invite.Id, invite, true);
            await ValourCache.Put(invite.Code, invite, true);
        }

        if (Invites is null)
            Invites = new();
        else
            Invites.Clear();

        foreach (var invite in invites)
        {
            var cInvite = await PlanetInvite.FindAsync(invite.Code);

            if (cInvite is not null)
                Invites.Add(cInvite);
        }
    }

    /// <summary>
    /// Returns the roles of a planet
    /// </summary>
    public async ValueTask<List<PlanetRole>> GetRolesAsync(bool force_refresh = false)
    {
        if (Roles is null || force_refresh)
        {
            await LoadRolesAsync();
        }

        return Roles;
    }

    /// <summary>
    /// Loads the roles of a planet from the server
    /// </summary>
    public async Task LoadRolesAsync()
    {
        var roles = (await ValourClient.GetJsonAsync<List<PlanetRole>>($"{IdRoute}/roles")).Data;

        if (roles is null)
            return;

        foreach (var role in roles)
        {
            // Skip event for bulk loading
            await ValourCache.Put(role.Id, role, true);
        }

        if (Roles is null)
            Roles = new List<PlanetRole>();
        else
            Roles.Clear();

        foreach (var role in roles)
        {
            var cRole = await PlanetRole.FindAsync(role.Id, Id);

            if (cRole is not null)
                Roles.Add(cRole);
        }

        Roles.Sort((a, b) => a.Position.CompareTo(b.Position));
    }

    /// <summary>
    /// Returns the member for a given user id
    /// </summary>
    public async ValueTask<PlanetMember> GetMemberByUserAsync(long userId, bool force_refresh = false)
    {
        return await PlanetMember.FindAsync(userId, Id, force_refresh);
    }

    /// <summary>
    /// Ran to notify the planet that a channel has been updated
    /// </summary>
    public async ValueTask NotifyUpdateChannel(PlanetChatChannel channel)
    {
        if (Channels == null)
            await LoadChannelsAsync();

        if (!Channels.Contains(channel))
            return;

        // Resort
        Channels.Sort((a, b) => a.Position.CompareTo(b.Position));
    }

    /// <summary>
    /// Ran to notify the planet that a channel has been deleted
    /// </summary>
    public async ValueTask NotifyDeleteChannel(PlanetChatChannel channel)
    {
        if (Channels == null)
            await LoadChannelsAsync();

        if (!Channels.Contains(channel))
            return;

        Channels.Remove(channel);
    }

    /// <summary>
    /// Ran to notify the planet that a category has been updated
    /// </summary>
    public async ValueTask NotifyUpdateCategory(PlanetCategoryChannel category)
    {
        if (Categories == null)
            await LoadCategoriesAsync();

        if (!Categories.Contains(category))
            return;

        // Resort
        Categories.Sort((a, b) => a.Position.CompareTo(b.Position));
    }

    /// <summary>
    /// Ran to notify the planet that a category has been deleted
    /// </summary>
    public async ValueTask NotifyDeleteCategory(PlanetCategoryChannel category)
    {
        if (Categories == null)
            await LoadCategoriesAsync();

        if (!Categories.Contains(category))
            return;

        Categories.Remove(category);
    }

    /// <summary>
    /// Ran to notify the planet that a role has been updated
    /// </summary>
    public async ValueTask NotifyUpdateRole(PlanetRole role)
    {
        if (Roles == null)
            await LoadRolesAsync();

        if (!Roles.Any(x => x.Id == role.Id))
        {
            Roles.Add(role);
        }

        // Resort
        Roles.Sort((a, b) => a.Position.CompareTo(b.Position));
    }

    /// <summary>
    /// Ran to notify the planet that a role has been deleted
    /// </summary>
    public async ValueTask NotifyDeleteRole(PlanetRole role)
    {
        if (Roles == null)
            await LoadRolesAsync();

        if (!Roles.Contains(role))
            return;

        // Resort
        Roles.Remove(role);
    }

}
