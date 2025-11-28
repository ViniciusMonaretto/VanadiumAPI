using Microsoft.EntityFrameworkCore;
using Models;

namespace Data.Sqlite
{
    public class SqliteDataContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite("Data Source=app.db");
        public SqliteDataContext(DbContextOptions<SqliteDataContext> options) : base(options)
        {

        }

        public DbSet<Group> Groups { get; set; }

        public DbSet<Panel> Panels { get; set; }

        public DbSet<Alarm> Alarms { get; set; }

        public DbSet<AlarmEvent> AlarmEvents { get; set; }

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
                .WithMany(m=>m.AlarmEvents)
                .HasForeignKey(u => u.AlarmId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}