﻿using System.Text.Json;
using Microsoft.Azure.NotificationHubs;
using Valour.Config.Configs;
using Valour.Database;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Models;
using WebPush;
using Message = Valour.Server.Models.Message;
using Notification = Valour.Server.Models.Notification;

namespace Valour.Server.Services;

public class NotificationService
{
    private static VapidDetails _vapidDetails;
    private static WebPushClient _webPush;
    
    private readonly ValourDb _db;
    private readonly UserService _userService;
    private readonly CoreHubService _coreHub;
    private readonly NodeLifecycleService _nodeLifecycleService;
    private readonly IServiceScopeFactory _scopeFactory;
    
    private readonly NotificationHubClient _hubClient;
    
    public NotificationService(
        ValourDb db, 
        UserService userService, 
        CoreHubService coreHub,
        NodeLifecycleService nodeLifecycleService,
        IServiceScopeFactory scopeFactory)
    {
        _db = db;
        _userService = userService;
        _coreHub = coreHub;
        _nodeLifecycleService = nodeLifecycleService;
        _scopeFactory = scopeFactory;
        
        if (!string.IsNullOrWhiteSpace(NotificationsConfig.Current.AzureConnectionString))
        {
            _hubClient = NotificationHubClient.CreateClientFromConnectionString(
                NotificationsConfig.Current.AzureConnectionString, 
                NotificationsConfig.Current.AzureHubName);
        }
    }

    public async Task<TaskResult> SubscribeAsync(WebPushSubscription subscription)
    {
        var subId = Guid.NewGuid().ToString();
        
        var installation = new Installation
        {
            InstallationId = subId,
            Platform = NotificationPlatform.WebPush,
            PushChannel = subscription.Endpoint, // The endpoint from the subscription object
            Tags = new List<string> { userTag }, // Associate with user or channel tags
            PushChannelHeaders = new Dictionary<string, string>
            {
                { "p256dh", subscription.Keys.P256dh },
                { "auth", subscription.Keys.Auth }
            }
        };
    }
    
    public async Task<Notification> GetNotificationAsync(long id)
        => (await _db.Notifications.FindAsync(id)).ToModel();

    public async Task<TaskResult> SetNotificationRead(long id, bool value)
    {
        var notification = await _db.Notifications.FindAsync(id);
        if (notification is null)
            return new TaskResult(false, "Notification not found");

        if (value)
        {
            notification.TimeRead = DateTime.UtcNow;
        }
        else
        {
            notification.TimeRead = null;
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception)
        {
            return new TaskResult(false, "Error saving changes to database");
        }

        _coreHub.RelayNotificationReadChange(notification.ToModel(), _nodeLifecycleService);
        
        return TaskResult.SuccessResult;
    }

    public async Task<TaskResult> ClearNotificationsForUser(long userId)
    {
        int changes = 0;
        
        try
        {
            changes = await _db.Notifications.Where(x => x.UserId == userId && x.TimeRead == null).ExecuteUpdateAsync(
                x => x.SetProperty(n => n.TimeRead, DateTime.UtcNow));
        }
        catch (Exception)
        {
            return new TaskResult(false, "Error saving changes to database");
        }
        
        _coreHub.RelayNotificationsCleared(userId, _nodeLifecycleService);

        return new TaskResult(true, $"Cleared {changes} notifications");
    }

    public async Task<List<Notification>> GetNotifications(long userId, int page = 0)
        => await _db.Notifications.Where(x => x.UserId == userId)
            .OrderBy(x => x.TimeSent)
            .Skip(page * 50)
            .Take(50)
            .Select(x => x.ToModel())
            .ToListAsync();
    
    public async Task<List<Notification>> GetUnreadNotifications(long userId, int page = 0)
        => await _db.Notifications.Where(x => x.UserId == userId && x.TimeRead == null)
            .OrderBy(x => x.TimeSent)
            .Skip(page * 50)
            .Take(50)
            .Select(x => x.ToModel())
            .ToListAsync();
    
    public async Task<List<Notification>> GetAllUnreadNotifications(long userId)
        => await _db.Notifications.Where(x => x.UserId == userId && x.TimeRead == null)
            .OrderBy(x => x.TimeSent)
            .Select(x => x.ToModel())
            .ToListAsync();

    public async Task AddNotificationAsync(Notification notification)
    {
        // Create id for notification
        notification.Id = IdManager.Generate();
        // Set time of notification
        notification.TimeSent = DateTime.UtcNow;
        
        // Add notification to database
        await _db.Notifications.AddAsync(notification.ToDatabase());
        await _db.SaveChangesAsync();

        // Send notification to all of the user's online Valour instances
        _coreHub.RelayNotification(notification, _nodeLifecycleService);
        
        // Send actual push notification to devices
        await SendPushNotificationBasicAsync(
            notification.UserId, 
            notification.ImageUrl, 
            notification.Title, 
            notification.Body, 
            notification.ClickUrl
        );
    }
    
    public async Task AddBatchedNotificationAsync(Notification notification, ValourDb db)
    {
        // Create id for notification
        notification.Id = IdManager.Generate();
        // Set time of notification
        notification.TimeSent = DateTime.UtcNow;

        // Send notification to all of the user's online Valour instances
        _coreHub.RelayNotification(notification, _nodeLifecycleService);
        
        // Send actual push notification to devices
        await SendPushNotificationBasicAsync(
            notification.UserId, 
            notification.ImageUrl, 
            notification.Title, 
            notification.Body, 
            notification.ClickUrl,
            db
        );
    }
    
    public Task AddRoleNotificationAsync(Notification baseNotification, long roleId)
    {
        // Do not block entire thread on this
        Task.Run(async () =>
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            await using var db = scope.ServiceProvider.GetService<ValourDb>();
        
            var notifications = await db.PlanetRoleMembers.Where(x => x.RoleId == roleId).Select(x => new Notification()
            {
                Id = IdManager.Generate(),
                Title = baseNotification.Title,
                Body = baseNotification.Body,
                ImageUrl = baseNotification.ImageUrl,
                ClickUrl = baseNotification.ClickUrl,
                PlanetId = baseNotification.PlanetId,
                ChannelId = baseNotification.ChannelId,
                Source = NotificationSource.PlanetRoleMention,
                SourceId = baseNotification.SourceId,
                UserId = x.UserId,
                TimeSent = DateTime.UtcNow,
            }).ToListAsync();

            foreach (var notification in notifications)
            {
                await AddBatchedNotificationAsync(notification, db);
            }

            await db.Notifications.AddRangeAsync(notifications.Select(x => x.ToDatabase()));
            await db.SaveChangesAsync();
        });

        return Task.CompletedTask;
    }
    
    public async Task SendPushNotificationAzureAsync(long userId, string iconUrl, string title, string message, string clickUrl)
    {

    }

    public async Task SendPushNotificationBasicAsync(long userId, string iconUrl, string title, string message, string clickUrl, ValourDb db = null)
    {
        var adb = db ?? _db;
        
        // Get all subscriptions for user
        var subs = await adb.NotificationSubscriptions.Where(x => x.UserId == userId).ToListAsync();
        
        bool dbChange = false;
        
        // List of tasks for notifications. Will return subscription if it fails to be removed.
        var tasks = new List<Task<WebPushSubscription>>();
        
        // Send notification to all
        foreach (var sub in subs)
        {
            var pushSubscription = new PushSubscription(sub.Endpoint, sub.Key, sub.Auth);

            var payload = JsonSerializer.Serialize(new
            {
                title,
                message,
                iconUrl,
                url = clickUrl,
            });
            
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await _webPush.SendNotificationAsync(pushSubscription, payload, _vapidDetails);
                }
                catch (WebPushException wex)
                {
                    if (wex.Message.Contains("no longer valid"))
                    {
                        // Clean old subscriptions that are now invalid
                        return sub;
                    }
                    else
                    {
                        Console.WriteLine("Error sending push notification: " + wex.Message);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error sending push notification: " + ex.Message);
                }
                
                return null;
            }));
        }

        var results = await Task.WhenAll(tasks);
        
        // Remove expired subscriptions
        foreach (var result in results)
        {
            if (result is not null)
            {
                adb.NotificationSubscriptions.Remove(result);
                dbChange = true;
            }
        }

        if (dbChange)
            await _db.SaveChangesAsync();
    }

    public async Task HandleMentionAsync(
        Mention mention, 
        Models.Planet planet, 
        Message message, 
        Valour.Database.PlanetMember member, 
        Valour.Database.User user, 
        Models.Channel channel)
    {
        switch (mention.Type)
        {
            case MentionType.PlanetMember:
            {
                // Member mentions only work in planet channels
                if (planet is null)
                    return;
            
                var targetMember = await _db.PlanetMembers.FindAsync(mention.TargetId);
                if (targetMember is null)
                    return;

                var content = message.Content.Replace($"«@m-{mention.TargetId}»", $"@{targetMember.Nickname}");

                Notification notif = new()
                {
                    Title = member.Nickname + " in " + planet.Name,
                    Body = content,
                    ImageUrl = user.GetAvatarUrl(AvatarFormat.Webp128),
                    UserId = targetMember.UserId,
                    PlanetId = planet.Id,
                    ChannelId = channel.Id,
                    SourceId = message.Id,
                    Source = NotificationSource.PlanetMemberMention,
                    ClickUrl = $"/channels/{channel.Id}/{message.Id}"
                };

                await AddNotificationAsync(notif);
                
                break;
            }
            case MentionType.User:
            {
                // Ensure that the user is a member of the channel
                if (await _db.ChannelMembers.AnyAsync(x =>
                        x.UserId == mention.TargetId && x.ChannelId == message.ChannelId))
                    return;
            
                var mentionTargetUser = await _db.Users.FindAsync(mention.TargetId);

                var content = message.Content.Replace($"«@u-{mention.TargetId}»", $"@{mentionTargetUser.Name}");

                Notification notif = new()
                {
                    Title = user.Name + " mentioned you in DMs",
                    Body = content,
                    ImageUrl = user.GetAvatarUrl(AvatarFormat.Webp128),
                    ClickUrl = $"/channels/{channel.Id}/{message.Id}",
                    ChannelId = channel.Id,
                    Source = NotificationSource.DirectMention,
                    SourceId = message.Id,
                    UserId = mentionTargetUser.Id,
                };
            
                await AddNotificationAsync(notif);
                
                break;
            }
            case MentionType.Role:
            {
                // Member mentions only work in planet channels
                if (planet is null)
                    return;
            
                var targetRole = await _db.PlanetRoles.FindAsync(mention.TargetId);
                if (targetRole is null)
                    return;
	        
                var content = message.Content.Replace($"«@r-{mention.TargetId}»", $"@{targetRole.Name}");

                Notification notif = new()
                {
                    Title = member.Nickname + " in " + planet.Name,
                    Body = content,
                    ImageUrl = user.GetAvatarUrl(AvatarFormat.Webp128),
                    PlanetId = planet.Id,
                    ChannelId = channel.Id,
                    SourceId = message.Id,
                    ClickUrl = $"/channels/{channel.Id}/{message.Id}"
                };

                await AddRoleNotificationAsync(notif, targetRole.Id);
                
                break;
            }
            default:
                return;
        }
    }
}