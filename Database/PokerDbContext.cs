using Microsoft.EntityFrameworkCore;

namespace PokerApp;

public sealed class PokerDbContext : DbContext
{
    public PokerDbContext(DbContextOptions<PokerDbContext> options)
        : base(options)
    {
    }

    public DbSet<LlmAgentPersonality> LlmAgentPersonalities => Set<LlmAgentPersonality>();

    public DbSet<SavedHand> SavedHands => Set<SavedHand>();

    public DbSet<HandPlayer> HandPlayers => Set<HandPlayer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LlmAgentPersonality>(e =>
        {
            e.ToTable("llm_agent_personalities");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(512).IsRequired();
            e.Property(x => x.PersonalityDescription).HasColumnName("personality_description").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.EditedAt).HasColumnName("edited_at");
        });

        modelBuilder.Entity<SavedHand>(e =>
        {
            e.ToTable("saved_hand");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.HandName).HasColumnName("hand_name").HasMaxLength(256).IsRequired();
            e.Property(x => x.HandHistoryJson).HasColumnName("hand_history_json").IsRequired();
            e.Property(x => x.HandTimeIso).HasColumnName("hand_time").IsRequired();
            e.HasMany(x => x.HandPlayers).WithOne(x => x.SavedHand).HasForeignKey(x => x.HandId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HandPlayer>(e =>
        {
            e.ToTable("hand_player");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.HandId).HasColumnName("hand_id");
            e.Property(x => x.PlayerName).HasColumnName("player_name").HasMaxLength(512).IsRequired();
            e.Property(x => x.PlayerType).HasColumnName("player_type").HasMaxLength(32).IsRequired();
            e.Property(x => x.LlmPersonalityId).HasColumnName("llm_personality");
            e.HasOne(x => x.LlmPersonality).WithMany().HasForeignKey(x => x.LlmPersonalityId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
