﻿using Valour.Api.Client;
using Valour.Client.Components.Windows;
using Valour.Shared;
using Valour.Api.Nodes;
using Valour.Api.Models;
using Valour.Client.Windows.ChatWindows;
using System.Collections.Concurrent;
using Valour.Shared.Models;

namespace Valour.Client.Windows;

public class WindowManager
{
    /// <summary>
    /// A statically accessible instance of the WindowManager.
    /// This should usually be accessed via DI
    /// </summary>
    public static WindowManager Instance { get; set; }

    // Private window store
    private List<ClientWindow> Windows { get; set; } = new();

    /// <summary>
    /// Returns all the current windows
    /// </summary>
    public List<ClientWindow> GetWindows() => Windows;

    /// <summary>
    /// The total number of current windows
    /// </summary>
    public int WindowCount => Windows.Count;

    /// <summary>
    /// The planet currently focused by the user (last clicked)
    /// </summary>
    public Planet FocusedPlanet { get; private set; }

    /// <summary>
    /// The window currently selected by the user (last clicked)
    /// </summary>
    private ClientWindow _selectedWindow;

    /// <summary>
    /// Event for when a new window is selected
    /// </summary>
    public event Func<Task> OnWindowSelect;

    /// <summary>
    /// Event for when a new planet is focused on
    /// </summary>
    public event Func<Planet, Task> OnPlanetFocused;

    /// <summary>
    /// Event for when a window is opened
    /// </summary>
    public event Func<ClientWindow, Task> OnWindowOpened;

    /// <summary>
    /// Event for when a window is closed
    /// </summary>
    public event Func<ClientWindow, Task> OnWindowClosed;

    /// <summary>
    /// The component for the main windows
    /// </summary>
    public MainWindowsComponent MainWindowsComponent { get; set; }

	/// <summary>
	/// channel.Id : text content
	/// </summary>
	public readonly ConcurrentDictionary<long, string> NotYetSentMessages = new();

	public WindowManager()
    {
        Instance = this;
        ValourClient.OnPlanetClose += OnPlanetClose;
    }

    public async Task Log(string msg)
    {
        await Logger.Log(msg, "purple");
    }

    public async Task OnPlanetClose(Planet planet)
    {
        if (FocusedPlanet.Id == planet.Id)
            await SetFocusedPlanet(null);
    }

    /// <summary>
    /// Adds a window to the client
    /// </summary>
    public async Task AddWindowAsync(ClientWindow window, IWindowHolder target)
    {
        window.Holder = target;

        // Add window to list
        Windows.Add(window);

        // Add window to the specified target
        await target.OpenWindow(window);

        if (OnWindowOpened is not null)
            await OnWindowOpened.Invoke(window);

        await Log($"[WindowManager]: Added window {window.Id}");
        
        await SetSelectedWindow(window);
        await SetFocusedPlanet(await window.GetPlanetAsync());
    }


    public async Task SetFocusedPlanet(Planet planet)
    {

        if (planet is null)
        {
            if (FocusedPlanet is null)
                return;
        }
        else
        {
            if (FocusedPlanet is not null)
                if (planet.Id == FocusedPlanet.Id)
                    return;
        }

        FocusedPlanet = planet;

        // Ensure focused planet is open
        await ValourClient.OpenPlanet(planet);

        if (planet is null)
            await Log("[WindowManager]: Set current planet to null");
        else
            await Log($"[WindowManager]: Set focused planet to {planet.Name} ({planet.Id})");

        if (OnPlanetFocused is not null)
            await OnPlanetFocused.Invoke(planet);
    }

    /// <summary>
    /// Swaps the channel a chat channel window is showing
    /// </summary>
    public async Task SwapWindowChannel(ChatChannelWindow window, Channel newChannel)
    {
        // Already that channel
        if (window.Channel.Id == newChannel.Id)
            return;
        
        // Not a chat channel
        if (!ISharedChannel.ChatChannelTypes.Contains(newChannel.ChannelType))
            return;

        await Log("[WindowManager]: Swapping chat channel " + window.Channel.Name + " for " + newChannel.Name);

        var chatWindows = Windows.OfType<ChatChannelWindow>();

        var oldChannel = window.Channel;

        // Set window's new channel
        window.Channel = newChannel;

        // Open connection to channel
        await newChannel.Open();

        if (!(chatWindows.Any(x => x.Id != window.Id && x.Channel.Id == oldChannel.Id)))
        {
            // Close the channel connection if this is the only open window for it
            await oldChannel.Close();
        }
    }
    
    /// <summary>
    /// Sets the window selected by the user (last interacted with)
    /// </summary>
    public async Task SetSelectedWindow(ClientWindow window)
    {
        // Check to ensure window is new and valid
        if (window == null || (_selectedWindow == window)) return;

        // Set selected window
        _selectedWindow = window;

        await Log($"[WindowManager]: Set active window to {window.Id}");
        
        await SetFocusedPlanet(await window.GetPlanetAsync());
        
        // Run event for window selection
        if (OnWindowSelect != null)
        {
            await Log($"[WindowManager]: Invoking window change event");
            
            if (OnWindowSelect is not null)
                await OnWindowSelect.Invoke();
        }
    }

    /// <summary>
    /// Returns the currently selected window
    /// </summary>
    public ClientWindow GetSelectedWindow()
    {
        if (_selectedWindow is null)
            _selectedWindow = Windows.FirstOrDefault();
            
        return _selectedWindow;
    }

    /// <summary>
    /// Returns the total number of windows
    /// </summary>
    public int GetWindowCount()
    {
        return Windows.Count;
    }

    /// <summary>
    /// Returns the window with the given id
    /// </summary>
    public ClientWindow GetWindow(string id) =>
        Windows.FirstOrDefault(x => x.Id == id);
        
    /// <summary>
    /// Clears all of the windows
    /// </summary>
    public async Task ClearWindows()
    {
        var toClose = Windows.ToList();
        foreach (ClientWindow window in toClose)
        {
            await CloseWindow(window);
        }
    }

    /// <summary>
    /// Replaces a window with another window
    /// </summary>
    public async Task ReplaceWindow(ClientWindow old, ClientWindow newWindow)
    {
        if (old.Id == newWindow.Id)
            return;

        Windows.Add(newWindow);

        newWindow.Holder = old.Holder;

        await old.Holder.ReplaceWindow(old, newWindow);

        // Don't refresh! We're doing that ourselves
        await CloseWindow(old);

        //await ForceChatRefresh();
        
        await SetSelectedWindow(newWindow);
        await SetFocusedPlanet(await newWindow.GetPlanetAsync());
    }

    /// <summary>
    /// Closes the given window
    /// </summary>
    public async Task CloseWindow(ClientWindow window)
    {
        await window.OnClosedAsync();
        
        Windows.Remove(window);

        if (window is ChatChannelWindow chatWindow)
        {
            var chatWindows = Windows.OfType<ChatChannelWindow>();

            if (!chatWindows.Any(x => x.Id != window.Id))
            {

                // Close the channel connection if this is the only open window for it
                await chatWindow.Channel.Close();
            }
        }

        if (OnWindowClosed is not null)
            await OnWindowClosed.Invoke(window);
    }

    /// <summary>
    /// Resets number of windows and returns main window to home
    /// </summary>
    public async Task ResetAllWindows()
    {
        await SetSelectedWindow(Windows[0]);
        for (var i = GetWindowCount() - 1; i > 0; i--)
        {
            await CloseWindow(Windows[i]);
        }
        await GetSelectedWindow().ReturnHomeAsync();
    }
}
