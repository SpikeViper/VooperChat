using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Api.Dynamic;

public class PlanetMemberApi
{
     // Helpful route to return the member for the authorizing user
    [ValourRoute(HttpVerbs.Get, "api/members/self/{planetId}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetSelfRouteAsync(
        long planetId, 
        PlanetMemberService memberService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotFound("Member not found");

        return Results.Json(member);
    }
        

    [ValourRoute(HttpVerbs.Get, "api/members/{id}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetRouteAsync(
        long id, 
        PlanetMemberService service)
    {
        // Get other member
        var member = await service.GetAsync(id);
        if (member is null)
            return ValourResult.NotFound("Member not found");
        
        // Need to be a member to see other members
        var self = await service.GetCurrentAsync(member.PlanetId);
        if (self is null)
            return ValourResult.NotPlanetMember();
        
        return Results.Json(member);
    }

    [ValourRoute(HttpVerbs.Get, "api/members/{id}/authority")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetAuthorityRouteAsync(
        long id, 
        PlanetMemberService service)
    {
        // Get other member
        var member = await service.GetAsync(id);
        if (member is null)
            return ValourResult.NotFound("Member not found");
        
        // Need to be a member to see other members
        var self = await service.GetCurrentAsync(member.PlanetId);
        if (self is null)
            return ValourResult.NotPlanetMember();

        var authority = await service.GetAuthorityAsync(member);
        
        return Results.Json(authority);
    }

    [ValourRoute(HttpVerbs.Get, "api/members/byuser/{planetId}/{userId}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetRoute(
        long planetId, 
        long userId,
        PlanetMemberService service)
    {
        var member = await service.GetByUserAsync(userId, planetId);
        if (member is null)
            return ValourResult.NotFound("Member not found");
        
        // Need to be a member to see other members
        var self = await service.GetCurrentAsync(planetId);
        if (self is null)
            return ValourResult.NotPlanetMember();

        return Results.Json(member);
    }

    [ValourRoute(HttpVerbs.Put, "api/members/{id}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> PutRouteAsync(
        [FromBody] PlanetMember member, 
        long id, 
        PlanetMemberService service,
        UserService userService)
    {
        var selfId = await userService.GetCurrentUserId();
        if (selfId != member.UserId)
            return Results.BadRequest("You can only modify your own membership.");

        await service.UpdateAsync(member);

        return Results.Json(member);
    }

    [ValourRoute(HttpVerbs.Delete, "api/members/{id}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> DeleteRouteAsync(
        long id,
        PlanetMemberService memberService)
    {
        var targetMember = await memberService.GetAsync(id);
        if (targetMember is null)
            return ValourResult.NotFound("Target member not found.");
        
        var selfMember = await memberService.GetCurrentAsync(targetMember.PlanetId);
        if (selfMember is null)
            return ValourResult.NotPlanetMember();

        // You can always delete your own membership, so we only check permissions
        // if you are not the same as the target
        if (selfMember.UserId != targetMember.Id)
        {
            if (!await memberService.HasPermissionAsync(selfMember, PlanetPermissions.Kick))
                return ValourResult.LacksPermission(PlanetPermissions.Kick);

            if (await memberService.GetAuthorityAsync(selfMember) < await memberService.GetAuthorityAsync(targetMember))
                return ValourResult.Forbid("You have less authority than the target member.");
        }

        await memberService.DeleteAsync(targetMember);

        return Results.NoContent();
    }
    

    [ValourRoute(HttpVerbs.Get, "api/members/{id}/roles")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetAllRolesForMember(
        long id,
        PlanetMemberService memberService)
    {
        var targetMember = await memberService.GetAsync(id);
        if (targetMember is null)
            return ValourResult.NotFound("Target member not found.");

        if (!await memberService.CurrentExistsAsync(targetMember.PlanetId))
            return ValourResult.NotPlanetMember();
        
        return Results.Json(await memberService.GetRoleIdsAsync(targetMember.Id));
    }

    [ValourRoute(HttpVerbs.Post, "api/members/{id}/roles/{roleId}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> AddRoleToMemberRouteAsync(
        long id,
        long roleId,
        PlanetMemberService memberService,
        PlanetRoleService roleService)
    {
        var targetMember = await memberService.GetAsync(id);
        if (targetMember is null)
            return ValourResult.NotFound("Target member not found.");
        
        var selfMember = await memberService.GetCurrentAsync(targetMember.PlanetId);
        if (selfMember is null)
            return ValourResult.NotPlanetMember();
        
        if (!await memberService.HasPermissionAsync(selfMember, PlanetPermissions.ManageRoles))
            return ValourResult.LacksPermission(PlanetPermissions.ManageRoles);

        var role = await roleService.GetAsync(roleId);
        if (role is null)
            return ValourResult.NotFound("Role not found.");

        var selfAuthority = await memberService.GetAuthorityAsync(selfMember);
        if (role.GetAuthority() >= selfAuthority)
            return ValourResult.Forbid("You can only add roles with a lower authority than your own.");

        
        var result = await memberService.AddRoleAsync(targetMember, role);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }


    [ValourRoute(HttpVerbs.Delete, "api/members/{id}/roles/{roleId}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> RemoveRoleFromMemberRouteAsync(
        long id,
        long roleId,
        PlanetMemberService memberService,
        PlanetRoleService roleService)
    {
        var targetMember = await memberService.GetAsync(id);
        if (targetMember is null)
            return ValourResult.NotFound("Target member not found.");
        
        var selfMember = await memberService.GetCurrentAsync(targetMember.PlanetId);
        if (selfMember is null)
            return ValourResult.NotPlanetMember();
        
        if (!await memberService.HasPermissionAsync(selfMember, PlanetPermissions.ManageRoles))
            return ValourResult.LacksPermission(PlanetPermissions.ManageRoles);

        var role = await roleService.GetAsync(roleId);
        if (role is null)
            return ValourResult.NotFound("Role not found.");

        var selfAuthority = await memberService.GetAuthorityAsync(selfMember);
        if (role.GetAuthority() >= selfAuthority)
            return ValourResult.Forbid("You can only remove roles with a lower authority than your own.");

        
        var result = await memberService.RemoveRoleAsync(targetMember, role);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.NoContent();
    }
}