using Microsoft.EntityFrameworkCore;

namespace PokerApp;

public sealed class PokerDbContext : DbContext
{
    public PokerDbContext(DbContextOptions<PokerDbContext> options)
        : base(options)
    {
    }

    public DbSet<LlmAgentPersonality> LlmAgentPersonalities => Set<LlmAgentPersonality>();

    public DbSet<OpenAiPreset> OpenAiPresets => Set<OpenAiPreset>();

    public DbSet<SavedHand> SavedHands => Set<SavedHand>();

    public DbSet<HandPlayer> HandPlayers => Set<HandPlayer>();

    public DbSet<TournamentSeries> TournamentSeries => Set<TournamentSeries>();

    public DbSet<SeriesTournament> SeriesTournaments => Set<SeriesTournament>();

    public DbSet<TournamentSeriesSetupPreferences> TournamentSeriesSetupPreferences => Set<TournamentSeriesSetupPreferences>();

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

        modelBuilder.Entity<OpenAiPreset>(e =>
        {
            e.ToTable("openai_preset");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(512).IsRequired();
            e.Property(x => x.ApiUrl).HasColumnName("api_url").HasMaxLength(2048).IsRequired();
            e.Property(x => x.ApiKey).HasColumnName("api_key").HasMaxLength(2048).IsRequired();
            e.Property(x => x.ModelName).HasColumnName("model_name").HasMaxLength(512).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.EditedAt).HasColumnName("edited_at");
        });

        modelBuilder.Entity<TournamentSeries>(e =>
        {
            e.ToTable("tournament_series");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(512).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.StatsTournamentCount).HasColumnName("stats_tournament_count");
            e.Property(x => x.StatsWinsByPlayerJson).HasColumnName("stats_wins_by_player_json").IsRequired();
            e.Property(x => x.StatsAvgHandsPerTournament).HasColumnName("stats_avg_hands_per_tournament");
            e.Property(x => x.StatsAvgPromptsPerHand).HasColumnName("stats_avg_prompts_per_hand");
            e.Property(x => x.StatsAvgPotPerHand).HasColumnName("stats_avg_pot_per_hand");
            e.Property(x => x.StatsAvgTournamentDurationSeconds).HasColumnName("stats_avg_tournament_duration_sec");
            e.HasMany(x => x.SeriesTournaments).WithOne(x => x.TournamentSeries).HasForeignKey(x => x.TournamentSeriesId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.SavedHands).WithOne(x => x.TournamentSeries).HasForeignKey(x => x.TournamentSeriesId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SeriesTournament>(e =>
        {
            e.ToTable("series_tournament");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TournamentSeriesId).HasColumnName("tournament_series_id");
            e.Property(x => x.TournamentIndex).HasColumnName("tournament_index");
            e.HasMany(x => x.SavedHands).WithOne(x => x.SeriesTournament).HasForeignKey(x => x.SeriesTournamentId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SavedHand>(e =>
        {
            e.ToTable("saved_hand");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.HandName).HasColumnName("hand_name").HasMaxLength(256).IsRequired();
            e.Property(x => x.HandHistoryJson).HasColumnName("hand_history_json").IsRequired();
            e.Property(x => x.HandTimeIso).HasColumnName("hand_time").IsRequired();
            e.Property(x => x.MaxPot).HasColumnName("max_pot");
            e.Property(x => x.TournamentSeriesId).HasColumnName("tournament_series_id");
            e.Property(x => x.SeriesTournamentId).HasColumnName("series_tournament_id");
            e.HasMany(x => x.HandPlayers).WithOne(x => x.SavedHand).HasForeignKey(x => x.HandId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.TournamentSeries).WithMany(x => x.SavedHands).HasForeignKey(x => x.TournamentSeriesId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.SeriesTournament).WithMany(x => x.SavedHands).HasForeignKey(x => x.SeriesTournamentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TournamentSeriesSetupPreferences>(e =>
        {
            e.ToTable("tournament_series_setup_prefs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.OptionsJson).HasColumnName("options_json").IsRequired();
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
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
            e.Property(x => x.OpenAiPresetId).HasColumnName("openai_preset");
            e.HasOne(x => x.LlmPersonality).WithMany().HasForeignKey(x => x.LlmPersonalityId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.OpenAiPreset).WithMany().HasForeignKey(x => x.OpenAiPresetId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
