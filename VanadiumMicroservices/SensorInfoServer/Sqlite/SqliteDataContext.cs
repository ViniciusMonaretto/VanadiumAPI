using Microsoft.EntityFrameworkCore;
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

        public SqliteDataContext(DbContextOptions<SqliteDataContext> options) : base(options)
        {
            // If DB is created now for the first time, populate it
            if (Database.EnsureCreated())
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
        }

        private void SeedFromJson()
        {
            try
            {
                Console.WriteLine("Populating database from config.json...");

                var json = File.ReadAllText("config.json");
                var config = JsonSerializer.Deserialize<SeedConfig>(json);

                if (config == null) return;

                // Example: Add groups
                if (config.Groups != null)
                    Groups.AddRange(config.Groups);

                if (config.Panels != null)
                    Panels.AddRange(config.Panels);

                if (config.Alarms != null)
                    Alarms.AddRange(config.Alarms);

                SaveChanges();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error reading config.json: " + ex.Message);
            }
        }
    }

    // Classes that match your config.json structure
    public class SeedConfig
    {
        public List<Group>? Groups { get; set; }
        public List<Panel>? Panels { get; set; }
        public List<Alarm>? Alarms { get; set; }
    }
}
