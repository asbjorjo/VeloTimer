using Microsoft.EntityFrameworkCore;
using VeloTimer.Shared.Data.Models.Timing;

namespace PassingLoader.Services.Storage
{
    public class StorageContext : DbContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<PassingRegister>()
                .Property(p => p.Track).IsRequired();
            modelBuilder.Entity<PassingRegister>()
                .Property(p => p.TimingSystem).IsRequired();
            modelBuilder.Entity<PassingRegister>()
                .Property(p => p.LoopId).IsRequired();
            modelBuilder.Entity<PassingRegister>()
                .Property(p => p.Time).IsRequired();
            modelBuilder.Entity<PassingRegister>()
                .Property(p => p.TransponderId).IsRequired();
            modelBuilder.Entity<PassingRegister>()
                .HasKey(p => new { p.Track, p.TimingSystem, p.TransponderId, p.Time });
            modelBuilder.Entity<PassingRegisterStatus>()
                .HasOne(p => p.Passing);
        }
    }
}
