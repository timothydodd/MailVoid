using MailVoidApi.Models;
using MailVoidWeb;
using MailVoidWeb.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MailVoidApi.Data;

public class MailVoidDbContext : DbContext
{
    public MailVoidDbContext(DbContextOptions<MailVoidDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<Mail> Mails { get; set; }
    public DbSet<MailGroup> MailGroups { get; set; }
    public DbSet<MailGroupUser> MailGroupUsers { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<Contact> Contacts { get; set; }
    public DbSet<UserMailRead> UserMailReads { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure DateTime properties to handle UTC conversion
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
                        v => v,
                        v => DateTime.SpecifyKind(v, DateTimeKind.Utc)));
                }
            }
        }

        // User configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("User");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserName).IsUnique();
            entity.Property(e => e.PasswordHash).HasMaxLength(255);
            entity.Property(e => e.UserName).IsRequired();
        });

        // Mail configuration
        modelBuilder.Entity<Mail>(entity =>
        {
            entity.ToTable("Mail");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.To);
            entity.HasIndex(e => e.From);
            entity.HasIndex(e => e.MailGroupPath);
            entity.Property(e => e.To).IsRequired();
            entity.Property(e => e.Text).IsRequired();
            entity.Property(e => e.From).IsRequired();
            entity.Property(e => e.Subject).IsRequired();
            entity.Property(e => e.CreatedOn).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        // MailGroup configuration
        modelBuilder.Entity<MailGroup>(entity =>
        {
            entity.ToTable("MailGroup");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Path);
            entity.HasIndex(e => e.Subdomain);
            entity.Property(e => e.Path).IsRequired(false);
            entity.Property(e => e.Subdomain).IsRequired(false);
            entity.Property(e => e.IsUserPrivate).HasDefaultValue(false);
            entity.Property(e => e.IsDefaultMailbox).HasDefaultValue(false);
            entity.Property(e => e.OwnerUserId).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Foreign key relationship to User
            entity.HasOne<User>()
                  .WithMany()
                  .HasForeignKey(e => e.OwnerUserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // MailGroupUser configuration
        modelBuilder.Entity<MailGroupUser>(entity =>
        {
            entity.ToTable("MailGroupUser");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.MailGroupId, e.UserId }).IsUnique();
            entity.Property(e => e.MailGroupId).IsRequired();
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.GrantedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Foreign key relationships
            entity.HasOne(e => e.MailGroup)
                  .WithMany(mg => mg.MailGroupUsers)
                  .HasForeignKey(e => e.MailGroupId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // RefreshToken configuration
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshToken");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Token, e.UserId });
            entity.HasIndex(e => e.Token);
            entity.Property(e => e.Token).IsRequired();
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.CreatedDate).HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Foreign key relationship to User
            entity.HasOne<User>()
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Contact configuration
        modelBuilder.Entity<Contact>(entity =>
        {
            entity.ToTable("Contact");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.From).IsUnique();
            entity.Property(e => e.From).IsRequired();
            entity.Property(e => e.Name).IsRequired();
        });

        // UserMailRead configuration
        modelBuilder.Entity<UserMailRead>(entity =>
        {
            entity.ToTable("UserMailRead");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.MailId }).IsUnique();
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.MailId).IsRequired();
            entity.Property(e => e.ReadAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Foreign key relationships
            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Mail)
                  .WithMany()
                  .HasForeignKey(e => e.MailId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

    }
}
