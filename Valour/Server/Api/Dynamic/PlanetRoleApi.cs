using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;

namespace Valour.Server.Api.Dynamic;

public class PlanetRoleApi
{
    [ValourRoute(HttpVerbs.Get, "api/roles/{id}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetRouteAsync(
        long id, 
        PlanetRoleService roleService,
        PlanetMemberService memberService)
    {
        // Get the role
        var role = await roleService.GetAsync(id);
        if (role is null)
            return ValourResult.NotFound("Role not found");

        // Get member
        var member = await memberService.GetCurrentAsync(role.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        // Return json
        return Results.Json(role);
    }


    [ValourRoute(HttpVerbs.Post, "api/roles")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PostRouteAsync(
        [FromBody] PlanetRole role,
        PlanetRoleService roleService,
        PlanetMemberService memberService)
    {
        if (role is null)
            return ValourResult.BadRequest("Include role in body.");
        // Get member
        var member = await memberService.GetCurrentAsync(role.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (role.GetAuthority() > await memberService.GetAuthorityAsync(member))
            return ValourResult.Forbid("You cannot create roles with higher authority than your own.");

        var result = await roleService.CreateAsync(role);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Created($"api/roles/{result.Data.Id}", result.Data);
    }

    [ValourRoute(HttpVerbs.Put, "api/roles/{id}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PutRouteAsync(
        [FromBody] PlanetRole role, 
        long id,
        PlanetMemberService memberService,
        PlanetRoleService roleService)
    {
        var oldRole = await roleService.GetAsync(id);

        // Get member
        var member = await memberService.GetCurrentAsync(oldRole.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageRoles))
            return ValourResult.LacksPermission(PlanetPermissions.ManageRoles);

        var result = await roleService.PutAsync(role);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Delete, "api/roles/{id}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> DeleteRouteAsync(
        long id,
        PlanetMemberService memberService,
        PlanetRoleService roleService)
    {
        var role = await roleService.GetAsync(id);

        // Get member
        var member = await memberService.GetCurrentAsync(role.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageRoles))
            return ValourResult.LacksPermission(PlanetPermissions.ManageRoles);

        var result = await roleService.DeleteAsync(role);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.NoContent();

    }
}