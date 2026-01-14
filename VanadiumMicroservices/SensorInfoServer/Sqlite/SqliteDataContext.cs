using Microsoft.EntityFrameworkCore;
using SensorInfoServer.Services;
using Shared.Models;
using System.Text.Json;  // for JSON reading

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
            // Ensure database exists before any operations
            Database.EnsureCreated();
            
            if (Users != null && !Users.Any())
            {
                // Seed default admin user
                SeedDefaultAdmin();
            }
            
            // If DB was just created, populate from config.json
            // Note: EnsureCreated() returns false if DB already existed
            // So we check if tables are empty to determine if we should seed
            if (Groups != null && Panels != null && !Groups.Any() && !Panels.Any())
            {
                SeedFromJson();
            }
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

            // UserInfo self-referencing relationship for manager hierarchy
            builder.Entity<UserInfo>()
                .HasMany(u => u.ManagedUsers)
                .WithOne()
                .HasForeignKey(u => u.ManagerId)
                .OnDelete(DeleteBehavior.SetNull);

            // Make Email unique
            builder.Entity<UserInfo>()
                .HasIndex(u => u.Email)
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
                Console.WriteLine("Populating database from config.json...");

                var json = File.ReadAllText("config.json");
                var config = JsonSerializer.Deserialize<SeedConfig>(json);

                if (config == null) return;

                // Step 1: Add users first and save to get their IDs
                UserInfo? firstUser = null; 

                if (config.Users != null)
                {
                    foreach (var seedUser in config.Users)
                    {
                        var user = new UserInfo
                        {
                            Name = seedUser.Name,
                            Email = seedUser.Email,
                            Company = seedUser.Company,
                            UserType = seedUser.UserType,
                            PasswordHash = string.IsNullOrEmpty(seedUser.Password)
                                ? AuthService.HashPassword("password")
                                : AuthService.HashPassword(seedUser.Password),
                            ManagerId = seedUser.ManagerId
                        };

                        if (firstUser == null)
                        {
                            firstUser = user;
                        }
                        Users.Add(user);
                    }
                    // Save users first to get their IDs assigned
                    SaveChanges();
                }

                // Step 2: Add enterprises (they reference users by ManagerId) and save to get their IDs
                if (config.Enterprises != null)
                {
                    foreach (var enterprise in config.Enterprises)
                    {
                        if (firstUser == null)
                        {
                            throw new Exception("First user not found");
                        }
                        enterprise.ManagerId = firstUser.Id;
                        enterprise.Users.Add(firstUser);
                        enterprise.Manager = firstUser;
                    }
                    Enterprises.AddRange(config.Enterprises);
                    // Save enterprises to get their IDs before adding groups
                    SaveChanges();
                }

                // Step 3: Add groups (they reference enterprises by UserGroupId)
                if (config.Groups != null)
                {
                    Groups.AddRange(config.Groups);
                    // Save groups to get their IDs before adding panels
                    SaveChanges();
                }
                    
                // Step 4: Add panels (they reference groups by GroupId)
                if (config.Panels != null)
                {
                    Panels.AddRange(config.Panels);
                    // Save panels to get their IDs before adding alarms
                    SaveChanges();
                }

                // Step 5: Add alarms (they reference panels by PanelId)
                if (config.Alarms != null)
                {
                    Alarms.AddRange(config.Alarms);
                    SaveChanges();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error reading config.json: " + ex.Message);
                Console.WriteLine(ex.StackTrace);
            }
        }

        private void SeedDefaultAdmin()
        {
            try
            {
                // Check if admin user already exists
                var adminExists = Users.Any(u => u.Email == "admin@admin.com");
                
                if (!adminExists)
                {
                    var defaultAdmin = new UserInfo
                    {
                        Name = "Admin",
                        Email = "admin",
                        Company = "System",
                        UserType = UserType.Admin,
                        PasswordHash = AuthService.HashPassword("Admin"),
                        ManagerId = null
                    };

                    Users.Add(defaultAdmin);
                    SaveChanges();
                    Console.WriteLine("Default admin user created: admin@admin.com / Admin");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error creating default admin user: " + ex.Message);
            }
        }
    }

    // Classes that match your config.json structure
    public class SeedConfig
    {
        public List<Group>? Groups { get; set; }
        public List<Panel>? Panels { get; set; }
        public List<Alarm>? Alarms { get; set; }
        public List<SeedUserInfo>? Users { get; set; }
        public List<Enterprise>? Enterprises { get; set; }
    }

    // DTO for seeding users from JSON (allows Password instead of PasswordHash)
    public class SeedUserInfo
    {
        public required string Name { get; set; }
        public required string Email { get; set; }
        public string? Company { get; set; }
        public UserType UserType { get; set; }
        public string? Password { get; set; }
        public int? ManagerId { get; set; }
    }
}
