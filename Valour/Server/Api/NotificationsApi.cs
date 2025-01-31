﻿using Microsoft.AspNetCore.Mvc;
using Valour.Sdk.Models;
using Valour.Server.Database;
using NotificationSubscription = Valour.Sdk.Models.NotificationSubscription;

namespace Valour.Server.API;

public class NotificationsApi : BaseAPI
{
    /// <summary>
    /// Adds the routes for this API section
    /// </summary>
    public new static void AddRoutes(WebApplication app)
    {
        app.MapPost("api/notification/subscribe", Subscribe);
        app.MapPost("api/notification/unsubscribe", Unsubscribe);
    }

    public static async Task<IResult> Subscribe(ValourDb db, [FromBody] NotificationSubscription subscription, UserService userService, [FromHeader] string authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization))
            return ValourResult.NoToken();

        var userId = await userService.GetCurrentUserIdAsync();

        if (userId is long.MinValue)
            return ValourResult.InvalidToken();

        // Force subscription to use auth token's user id
        subscription.UserId = userId;

        // Ensure subscription data is there
        if (string.IsNullOrWhiteSpace(subscription.Endpoint)
            || string.IsNullOrWhiteSpace(subscription.Auth)
            || string.IsNullOrWhiteSpace(subscription.Key))
        {
            return ValourResult.BadRequest("Subscription data is incomplete.");
        }

        // Look for old subscription
        var old = await db.PushNotificationSubscriptions.FirstOrDefaultAsync(x => x.Endpoint == subscription.Endpoint);

        if (old != null)
        {
            if (old.Auth == subscription.Auth && old.Key == subscription.Key)
                return Results.Ok("There is already a subscription for this endpoint.");

            // Update old subscription
            old.Auth = subscription.Auth;
            old.Key = subscription.Key;

            db.PushNotificationSubscriptions.Update(old);
            await db.SaveChangesAsync();

            return Results.Ok("Updated subscription.");
        }

        subscription.Id = IdManager.Generate();

        await db.PushNotificationSubscriptions.AddAsync(subscription.ToDatabase());
        await db.SaveChangesAsync();

        return Results.Ok("Subscription was accepted.");
    }

    public static async Task<IResult> Unsubscribe(ValourDb db, [FromBody] NotificationSubscription subscription, UserService userService, [FromHeader] string authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization))
            return ValourResult.NoToken();

        var userId = await userService.GetCurrentUserIdAsync();

        if (userId is long.MinValue)
            return ValourResult.InvalidToken();

        // Look for old subscription
        var old = await db.PushNotificationSubscriptions.FirstOrDefaultAsync(x => x.Endpoint == subscription.Endpoint);

        if (old is null)
            return Results.Ok("Subscription already removed.");


        db.PushNotificationSubscriptions.Remove(old);
        await db.SaveChangesAsync();

        return Results.Ok("Removed subscription.");
    }
}
