﻿using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using Valour.Server.Database;
using Valour.Server.Database.Items.Messages;
using Valour.Server.Database.Items.Planets.Channels;

namespace Valour.Server.Workers
{
    public class PlanetMessageWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        public readonly ILogger<PlanetMessageWorker> _logger;

        public PlanetMessageWorker(ILogger<PlanetMessageWorker> logger,
                            IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        private static BlockingCollection<PlanetMessage> MessageQueue = new(new ConcurrentQueue<PlanetMessage>());

        // Prevents deleted messages from being staged
        private static HashSet<ulong> BlockSet = new();

        private static ConcurrentDictionary<ulong, PlanetMessage> StagedMessages = new();

        private static ValourDB Context;

        public static Dictionary<ulong, ulong> ChannelMessageIndices = new();

        public static PlanetMessage GetStagedMessage(ulong id)
        {
            StagedMessages.TryGetValue(id, out PlanetMessage msg);
            return msg;
        }

        public static void AddToQueue(PlanetMessage message)
        {
            MessageQueue.Add(message);
        }

        public static void RemoveFromQueue(PlanetMessage message)
        {
            // Remove currently staged
            StagedMessages.Remove(message.Id, out _);

            // Protect from being staged
            BlockSet.Add(message.Id);
        }

        public static List<PlanetMessage> GetStagedMessages(ulong channelId, int max)
        {
            return StagedMessages.Values.Where(x => x.ChannelId == channelId).TakeLast(max).Reverse().ToList();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                Task task = Task.Run(async () =>
                {
                    //try
                    //{

                    Context = new ValourDB(ValourDB.DBOptions);

                    // This is a stream and will run forever
                    foreach (PlanetMessage Message in MessageQueue.GetConsumingEnumerable())
                    {
                        if (BlockSet.Contains(Message.Id))
                        {
                            BlockSet.Remove(Message.Id);
                            continue;
                        }

                        ulong channelId = Message.ChannelId;

                        PlanetChatChannel channel = await Context.PlanetChatChannels.FindAsync(channelId);

                        // Get index for message
                        ulong index = channel.MessageCount;

                        // Update message count. May have to queue this in the future to prevent concurrency issues (done).
                        channel.MessageCount += 1;
                        Message.MessageIndex = index;
                        Message.TimeSent = DateTime.UtcNow;

                        // This is not awaited on purpose
                        PlanetHub.Current.Clients.Group($"c-{channelId}").SendAsync("Relay", Message);

                        StagedMessages.TryAdd(Message.Id, Message);
                    }


                    //}
                    //catch (System.Exception e)
                    //{
                    //    Console.WriteLine("FATAL MESSAGE WORKER ERROR:");
                    //    Console.WriteLine(e.Message);
                    //}
                });

                while (!task.IsCompleted)
                {
                    _logger.LogInformation($"Planet Message Worker running at: {DateTimeOffset.Now.ToString()}");
                    _logger.LogInformation($"Queue size: {MessageQueue.Count.ToString()}");
                    _logger.LogInformation($"Saving {StagedMessages.Count.ToString()} messages to DB.");

                    if (Context != null)
                    {
                        await Context.PlanetMessages.AddRangeAsync(StagedMessages.Values);
                        await Context.SaveChangesAsync();
                        BlockSet.Clear();
                        StagedMessages.Clear();
                        _logger.LogInformation($"Saved successfully.");
                    }

                    // Save to DB


                    await Task.Delay(30000, stoppingToken);
                }

                _logger.LogInformation("Planet Message Worker task stopped at: {time}", DateTimeOffset.Now.ToString());
                _logger.LogInformation("Restarting.", DateTimeOffset.Now.ToString());
            }
        }
    }
}
