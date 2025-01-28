using Microsoft.AspNetCore.Mvc;
using Valour.Sdk.Models;
using Valour.Server.Database;

namespace Valour.Server.API;

public class NotificationsAPI : BaseAPI
{
    /// <summary>
    /// Adds the routes for this API section
    /// </summary>
    public static void AddRoutes(WebApplication app)
    {
        app.MapPost("api/notification/subscribe", SubscribeAsync);
        app.MapPost("api/notification/unsubscribe", UnsubscribeAsync);
    }

    public static async Task<IResult> SubscribeAsync(ValourDb db, [FromBody] NotificationSubscription subscription, UserService userService, [FromHeader] string authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization))
            return ValourResult.NoToken();

        var userId = await userService.GetCurrentUser IdAsync();

        // Check for a valid user ID
        if (userId <= 0)
            return ValourResult.InvalidToken();

        // Force subscription to use auth token's user ID
        subscription.UserId = userId;

        // Ensure subscription data is complete
        if (string.IsNullOrWhiteSpace(subscription.Endpoint)
            || string.IsNullOrWhiteSpace(subscription.Auth)
            || string.IsNullOrWhiteSpace(subscription.Key))
        {
            return ValourResult.BadRequest("Subscription data is incomplete.");
        }

        // Look for an existing subscription
        var oldSubscription = await db.NotificationSubscriptions.FirstOrDefaultAsync(x => x.Endpoint == subscription.Endpoint);

        if (oldSubscription != null)
        {
            if (oldSubscription.Auth == subscription.Auth && oldSubscription.Key == subscription.Key)
                return Results.Ok("There is already a subscription for this endpoint.");

            // Update the old subscription
            oldSubscription.Auth = subscription.Auth;
            oldSubscription.Key = subscription.Key;

            db.NotificationSubscriptions.Update(oldSubscription);
            await db.SaveChangesAsync();

            return Results.Ok("Updated subscription.");
        }

        // Generate a new ID for the subscription
        subscription.Id = IdManager.Generate();

        await db.NotificationSubscriptions.AddAsync(subscription.ToDatabase());
        await db.SaveChangesAsync();

        return Results.Created($"api/notification/subscribe/{subscription.Id}", "Subscription was accepted.");
    }

    public static async Task<IResult> UnsubscribeAsync(ValourDb db, [FromBody] NotificationSubscription subscription, UserService userService, [FromHeader] string authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization))
            return ValourResult.NoToken();

        var userId = await userService.GetCurrentUser IdAsync();

        // Check for a valid user ID
        if (userId <= 0)
            return ValourResult.InvalidToken();

        // Look for an existing subscription
        var oldSubscription = await db.NotificationSubscriptions.FirstOrDefaultAsync(x => x.Endpoint == subscription.Endpoint);

        if (oldSubscription is null)
            return Results.Ok("Subscription already removed.");

        db.NotificationSubscriptions.Remove(oldSubscription);
        await db.SaveChangesAsync();

        return Results.Ok("Removed subscription.");
    }
}
