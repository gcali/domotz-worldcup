using Microsoft.EntityFrameworkCore;
using Worldcup.Api.Models;

namespace Worldcup.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Player> Players => Set<Player>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Setting>().HasKey(s => s.Key);

        b.Entity<Team>().HasIndex(t => t.FifaCode).IsUnique();

        b.Entity<Assignment>()
            .HasOne(a => a.Player).WithMany(p => p.Assignments)
            .HasForeignKey(a => a.PlayerId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Assignment>()
            .HasOne(a => a.Team).WithMany()
            .HasForeignKey(a => a.TeamId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Assignment>().HasIndex(a => new { a.PlayerId, a.TeamId }).IsUnique();

        b.Entity<Match>()
            .HasOne(m => m.HomeTeam).WithMany()
            .HasForeignKey(m => m.HomeTeamId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Match>()
            .HasOne(m => m.AwayTeam).WithMany()
            .HasForeignKey(m => m.AwayTeamId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Match>().HasIndex(m => m.ExternalId);
    }
}
