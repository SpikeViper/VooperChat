﻿using System.Text.Json.Serialization;
using Valour.Sdk.Client;
using Valour.Sdk.Nodes;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Sdk.Items
{
    /// <summary>
    /// A live model is a model that is updated in real time
    /// </summary>
    public abstract class LiveModel : ISharedItem
    {
        public long Id { get; set; }
        
        [JsonIgnore]
        public virtual string IdRoute => $"{BaseRoute}/{Id}";

        [JsonIgnore]
        public virtual string BaseRoute => $"api/{GetType().Name}";

        /// <summary>
        /// Ran when this item is updated
        /// </summary>
        public event Func<ModelUpdateEvent, Task> OnUpdated;

        /// <summary>
        /// Ran when this item is deleted
        /// </summary>
        public event Func<Task> OnDeleted;

        /// <summary>
        /// Custom logic on model update
        /// </summary>
        public virtual Task OnUpdate(ModelUpdateEvent eventData)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Custom logic on model deletion
        /// </summary>
        public virtual Task OnDelete()
        {
            return Task.CompletedTask;
        }

        [JsonIgnore]
        public Node Node
        {
            get
            {
                switch (this)
                {
                    case Planet planet: 
                        // Planets have node known
                        return NodeManager.GetNodeFromName(planet.NodeName);
                    case IPlanetModel planetItem: 
                        // Planet items can just check their planet
                        return NodeManager.GetKnownByPlanet(planetItem.PlanetId);
                    default: 
                        // Everything else can just use the primary node
                        return ValourClient.PrimaryNode;
                }
            }
        }
        
        /// <summary>
        /// This exists because of some type weirdness in C#
        /// Basically, if we do not use a generic, for some reason the cache does not
        /// insert into the right type. So yes, it's weird the item has to be passed in to
        /// its own method, but it works.
        /// </summary>
        public virtual async Task AddToCache<T>(T item, bool skipEvent = false) where T : LiveModel
        {
            await ValourCache.Put<T>(this.Id, item, skipEvent);
        }

        /// <summary>
        /// Safely invokes the updated event
        /// </summary>
        public async Task InvokeUpdatedEventAsync(ModelUpdateEvent eventData)
        {
            await OnUpdate(eventData);

            if (OnUpdated != null)
                await OnUpdated.Invoke(eventData);
        }

        /// <summary>
        /// Safely invokes the deleted event
        /// </summary>
        public async Task InvokeDeletedEventAsync()
        {
            await OnDelete();
            
            if (OnDeleted != null)
                await OnDeleted.Invoke();
        }

        /// <summary>
        /// Attempts to create this item
        /// </summary>
        /// <typeparam name="T">The type of object being created</typeparam>
        /// <param name="item">The item to create</param>
        /// <returns>The result, with the created item (if successful)</returns>
        public static async Task<TaskResult<T>> CreateAsync<T>(T item) where T : LiveModel
        {
            Node node;

            if (item is IPlanetModel planetItem)
                node = await NodeManager.GetNodeForPlanetAsync(planetItem.PlanetId);
            else
                node = ValourClient.PrimaryNode;

            return await node.PostAsyncWithResponse<T>(item.BaseRoute, item);
        }

        /// <summary>
        /// Attempts to update this item
        /// </summary>
        /// <typeparam name="T">The type of object being created</typeparam>
        /// <param name="item">The item to update</param>
        /// <returns>The result, with the updated item (if successful)</returns>
        public static async Task<TaskResult<T>> UpdateAsync<T>(T item) where T : LiveModel
        {
            return await item.Node.PutAsyncWithResponse(item.IdRoute, item);
        }

        /// <summary>
        /// Attempts to delete this item
        /// </summary>
        /// <typeparam name="T">The type of object being deleted</typeparam>
        /// <param name="item">The item to delete</param>
        /// <returns>The result</returns>
        public static async Task<TaskResult> DeleteAsync<T>(T item) where T : LiveModel
        {
            return await item.Node.DeleteAsync(item.IdRoute);
        }
    }
}
