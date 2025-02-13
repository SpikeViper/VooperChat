﻿using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

/// <summary>
/// Database model for a planet member
/// </summary>
public class PlanetMember : ISharedPlanetMember
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    [JsonIgnore]
    public Planet Planet { get; set; }
    
    [JsonIgnore]
    public virtual User User { get; set; }
    
    [JsonIgnore]
    public virtual ICollection<Message> Messages { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////

    public long Id { get; set; }
    public long UserId { get; set; }
    public long PlanetId { get; set; }
    public string Nickname { get; set; }
    public string MemberAvatar { get; set; }
    public bool IsDeleted { get; set; }
    
    // Together, these role bits can be used to determine the roles of the member
    public long Rf0 { get; set; }
    public long Rf1 { get; set; }
    public long Rf2 { get; set; }
    public long Rf3 { get; set; }
    
    public PlanetRoleMembership RoleMembership
    {
        get => new PlanetRoleMembership(Rf0, Rf1, Rf2, Rf3);
        set
        {
            Rf0 = value.Rf0;
            Rf1 = value.Rf1;
            Rf2 = value.Rf2;
            Rf3 = value.Rf3;
        }
    }

    /// <summary>
    /// Configures the entity model for the `PlanetMember` class using fluent configuration.
    /// </summary>
    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<PlanetMember>(e =>
        {
            // Table
            e.ToTable("planet_members");
            
            // Keys
            e.HasKey(x => x.Id);
            
            // Properties
            e.Property(x => x.Id)
                .HasColumnName("id");
            
            e.Property(x => x.UserId)
                .HasColumnName("user_id");
            
            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");

            e.Property(x => x.Nickname)
                .HasColumnName("nickname")
                .HasMaxLength(32);
            
            e.Property(x => x.MemberAvatar)
                .HasColumnName("member_pfp");
            
            e.Property(x => x.IsDeleted)
                .HasColumnName("is_deleted");
            
            e.Property(x => x.Rf0)
                .HasColumnName("rf0");
            
            e.Property(x => x.Rf1)
                .HasColumnName("rf1");
            
            e.Property(x => x.Rf2)
                .HasColumnName("rf2");
            
            e.Property(x => x.Rf3)
                .HasColumnName("rf3");
            
            // Relationships

            e.HasOne(x => x.Planet)
                .WithMany(x => x.Members)
                .HasForeignKey(x => x.PlanetId);

            e.HasOne(x => x.User)
                .WithMany(x => x.Membership)
                .HasForeignKey(x => x.UserId);

            e.HasMany(x => x.Messages)
                .WithOne(x => x.AuthorMember)
                .HasForeignKey(x => x.AuthorMemberId);
            
            // Indices
            e.HasIndex(x => new { x.UserId, x.PlanetId })
                .IsUnique();
            
            e.HasIndex(x => new
            {
                x.Rf0,
                x.Rf1,
                x.Rf2,
                x.Rf3
            });
        });
    }
}

