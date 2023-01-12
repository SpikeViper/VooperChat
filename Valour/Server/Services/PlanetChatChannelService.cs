using IdGen;
using StackExchange.Redis;
using Valour.Server.Database;
using Valour.Server.Database.Items.Channels.Planets;
using Valour.Server.Database.Items.Planets;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Server.Hubs;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class PlanetChatChannelService
{
    private readonly ValourDB _db;
    private readonly PlanetService _planetService;
    private readonly PlanetCategoryService _categoryService;
    private readonly PlanetMemberService _memberService;
    private readonly PermissionsService _permissionsService;
	private readonly ILogger<PlanetChatChannelService> _logger;
    private readonly CoreHubService _coreHub;

	public PlanetChatChannelService(
        ValourDB db,
        PlanetService planetService,
        PlanetCategoryService categoryService,
        PlanetMemberService memberService,
        PermissionsService permissionsService,
        CoreHubService coreHubService,
		ILogger<PlanetChatChannelService> logger)
    {
        _db = db;
        _planetService = planetService;
        _categoryService = categoryService;
        _memberService = memberService;
        _permissionsService = permissionsService;
        _logger = logger;
        _coreHub = coreHubService;

	}

    /// <summary>
    /// Returns the chat channel with the given id
    /// </summary>
    public async ValueTask<PlanetChatChannel> GetAsync(long id) =>
        (await _db.Planets.FindAsync(id)).ToModel();


    /// <summary>
    /// Soft deletes the given channel
    /// </summary>
    public async Task DeleteAsync(PlanetChatChannel channel)
    {
        channel.IsDeleted = true;
        _db.PlanetChatChannels.Update(channel);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Creates the given planet chat channel
    /// </summary>
    public async Task<TaskResult<PlanetChatChannel>> CreateAsync(PlanetChatChannel channel)
    {
        var baseValid = await ValidateBasic(channel);
        if (!baseValid.Success)
            return new TaskResult<PlanetChatChannel>(false, baseValid.Message);

        await using var tran = await _db.Database.BeginTransactionAsync();

        try
        {
			await _db.PlanetChatChannels.AddAsync(channel.ToDatabase());
			await _db.SaveChangesAsync();

			await tran.CommitAsync();
		}
		catch (Exception e)
		{
			_logger.LogError(e, "Failed to create planet chat channel");
			await tran.RollbackAsync();
			return new TaskResult<PlanetChatChannel>(false, "Failed to create channel");
		}

		_coreHub.NotifyPlanetItemChange(channel);

		return new TaskResult<PlanetChatChannel>(true, "PlanetChatChannel created successfully", channel);
	}


    /// <summary>
    /// Common basic validation for channels
    /// </summary>
    private async Task<TaskResult> ValidateBasic(PlanetChatChannel channel)
    {
        var nameValid = ValidateName(channel.Name);
        if (!nameValid.Success)
            return new TaskResult(false, nameValid.Message);

        var descValid = ValidateDescription(channel.Description);
        if (!descValid.Success)
            return new TaskResult(false, nameValid.Message);

        var positionValid = await ValidateParentAndPosition(channel);
        if (!positionValid.Success)
            return new TaskResult(false, nameValid.Message);

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// The regex used for name validation
    /// </summary>
    public static readonly Regex nameRegex = new Regex(@"^[a-zA-Z0-9 _-]+$");

    /// <summary>
    /// Validates that a given name is allowable
    /// </summary>
    public static TaskResult ValidateName(string name)
    {
        if (name.Length > 32)
            return new TaskResult(false, "Channel names must be 32 characters or less.");

        if (!nameRegex.IsMatch(name))
            return new TaskResult(false, "Channel names may only include letters, numbers, dashes, and underscores.");

        return new TaskResult(true, "The given name is valid.");
    }

    /// <summary>
    /// Validates that a given description is allowable
    /// </summary>
    public static TaskResult ValidateDescription(string desc)
    {
        if (desc.Length > 500)
        {
            return new TaskResult(false, "Planet descriptions must be 500 characters or less.");
        }

        return TaskResult.SuccessResult;
    }

    public async Task<TaskResult> ValidateParentAndPosition(PlanetChatChannel channel)
    {
        // Logic to check if parent is legitimate
        if (channel.ParentId is not null)
        {
            var parent = await _db.PlanetCategories.FirstOrDefaultAsync
            (x => x.Id == channel.ParentId
                  && x.PlanetId == channel.PlanetId); // This ensures the result has the same planet id

            if (parent is null)
                return new TaskResult(false, "Parent ID is not valid");
        }

        // Auto determine position
        if (channel.Position < 0)
        {
            channel.Position = (ushort)(await _db.PlanetChannels.CountAsync(x => x.ParentId == channel.ParentId));
        }
        else
        {
            if (!await HasUniquePosition(channel))
                return new TaskResult(false, "The position is already taken.");
        }

        return new TaskResult(true, "Valid");
    }

    public async Task<bool> HasUniquePosition(PlanetChannel channel) =>
        // Ensure position is not already taken
        !await _db.PlanetChannels.AnyAsync(x => x.ParentId == channel.ParentId && // Same parent
                                                x.Position == channel.Position && // Same position
                                                x.Id != channel.Id); // Not self
}