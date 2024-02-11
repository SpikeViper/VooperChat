﻿using System.Text.Json.Serialization;
using Valour.Shared.Models;

namespace Valour.Api.Models;

public class Referral : ISharedReferral
{
    public long UserId { get; set; }
    public long ReferrerId { get; set; }
    public DateTime Created { get; set; }
    public decimal Reward { get; set; }
}

