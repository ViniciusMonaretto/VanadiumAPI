using Microsoft.EntityFrameworkCore;
using VanadiumAPI.Services;
using Shared.Models;
using System.Text.Json;

namespace Data.Sqlite
{
    public class SqliteDataContext : DbContext
    {
        public DbSet<Group> Groups { get; set; }
        public DbSet<Panel> Panels { get; set; }
        public DbSet<Alarm> Alarms { get; set; }
        public DbSet<AlarmEvent> AlarmEvents { get; set; }
        public DbSet<UserInfo> Users { get; set; }
        public DbSet<Enterprise> Enterprises { get; set; }

        public SqliteDataContext(DbContextOptions<SqliteDataContext> options) : base(options)
        {
            Database.EnsureCreated();
            if (Users != null && !Users.Any())
                SeedDefaultAdmin();
            if (Groups != null && Panels != null && !Groups.Any() && !Panels.Any())
                SeedFromJson();
        }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite("Data Source=app.db");

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Panel>()
                .HasOne(p => p.Group)
                .WithMany(g => g.Panels)
                .HasForeignKey(p => p.GroupId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Alarm>()
                .HasOne(u => u.Panel)
                .WithMany(u => u.Alarms)
                .HasForeignKey(u => u.PanelId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<AlarmEvent>()
                .HasOne(u => u.Alarm)
                .WithMany(m => m.AlarmEvents)
                .HasForeignKey(u => u.AlarmId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<UserInfo>()
                .HasMany(u => u.ManagedUsers)
                .WithOne()
                .HasForeignKey(u => u.ManagerId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<UserInfo>()
                .HasIndex(u => u.Email)
                .IsUnique();

            builder.Entity<UserInfo>()
                .HasIndex(u => u.UserName)
                .IsUnique();

            builder.Entity<Enterprise>()
                .HasMany(x => x.Groups)
                .WithOne()
                .HasForeignKey(g => g.EnterpriseId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Enterprise>()
                .HasOne(x => x.Manager)
                .WithMany(x => x.ManagedEnterprises)
                .HasForeignKey(x => x.ManagerId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Enterprise>()
                .HasMany(x => x.Users)
                .WithMany(x => x.UserEnterprises);

            builder.Entity<Enterprise>()
                .HasIndex(x => x.Name)
                .IsUnique();
        }

        private void SeedFromJson()
        {
            try
            {
                var json = File.ReadAllText("config.json");
                var config = JsonSerializer.Deserialize<SeedConfig>(json);
                if (config == null) return;

                UserInfo? firstUser = null;
                if (config.Users != null && config.Users.Count > 0)
                {
                    foreach (var seedUser in config.Users)
                    {
                        var user = new UserInfo
                        {
                            Name = seedUser.Name,
                            Email = seedUser.Email,
                            UserName = seedUser.UserName,
                            Company = seedUser.Company,
                            UserType = seedUser.UserType,
                            PasswordHash = string.IsNullOrEmpty(seedUser.Password)
                                ? AuthService.HashPassword("password")
                                : AuthService.HashPassword(seedUser.Password),
                            ManagerId = seedUser.ManagerId
                        };
                        if (firstUser == null) firstUser = user;
                        Users.Add(user);
                    }
                    SaveChanges();
                }
                else
                {
                    firstUser = Users.FirstOrDefault();
                }

                Enterprise? defaultEnterprise = null;
                if (config.Enterprises != null && config.Enterprises.Count > 0)
                {
                    if (firstUser == null) throw new Exception("At least one user is required when seeding enterprises from config.");
                    foreach (var enterprise in config.Enterprises)
                    {
                        enterprise.ManagerId = firstUser.Id;
                        enterprise.Users.Add(firstUser);
                        enterprise.Manager = firstUser;
                    }
                    Enterprises.AddRange(config.Enterprises);
                    SaveChanges();
                    defaultEnterprise = config.Enterprises.First();
                }
                else
                {
                    if (firstUser != null)
                    {
                        defaultEnterprise = new Enterprise { Name = "Default", ManagerId = firstUser.Id, Manager = firstUser };
                        defaultEnterprise.Users.Add(firstUser);
                        Enterprises.Add(defaultEnterprise);
                        SaveChanges();
                    }
                }

                if (config.Groups != null && config.Groups.Count > 0)
                {
                    if (defaultEnterprise == null) throw new Exception("At least one enterprise is required when seeding groups (add Enterprises or Users in config.json).");
                    foreach (var group in config.Groups)
                        group.EnterpriseId = defaultEnterprise.Id;
                    Groups.AddRange(config.Groups);
                    SaveChanges();
                }

                if (config.Panels != null && config.Panels.Count > 0)
                {
                    Panels.AddRange(config.Panels);
                    SaveChanges();
                }

                if (config.Alarms != null && config.Alarms.Count > 0)
                {
                    Alarms.AddRange(config.Alarms);
                    SaveChanges();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error reading config.json: " + ex.Message);
            }
        }

        private void SeedDefaultAdmin()
        {
            try
            {
                if (Users.Any(u => u.Email == "admin@admin.com")) return;
                var defaultAdmin = new UserInfo
                {
                    Name = "Admin",
                    Email = "admin@admin.com",
                    UserName = "admin",
                    Company = "System",
                    UserType = UserType.Admin,
                    PasswordHash = AuthService.HashPassword("admin"),
                    ManagerId = null
                };
                Users.Add(defaultAdmin);
                SaveChanges();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error creating default admin user: " + ex.Message);
            }
        }
    }

    public class SeedConfig
    {
        public List<Group>? Groups { get; set; }
        public List<Panel>? Panels { get; set; }
        public List<Alarm>? Alarms { get; set; }
        public List<SeedUserInfo>? Users { get; set; }
        public List<Enterprise>? Enterprises { get; set; }
    }

    public class SeedUserInfo
    {
        public required string Name { get; set; }
        public required string Email { get; set; }
        public required string UserName { get; set; }
        public string? Company { get; set; }
        public UserType UserType { get; set; }
        public string? Password { get; set; }
        public int? ManagerId { get; set; }
    }
}
