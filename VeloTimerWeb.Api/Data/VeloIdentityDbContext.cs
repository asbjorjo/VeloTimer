using IdentityServer4.EntityFramework.Entities;
using IdentityServer4.EntityFramework.Extensions;
using IdentityServer4.EntityFramework.Interfaces;
using IdentityServer4.EntityFramework.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;
using VeloTimerWeb.Api.Models;
using VeloTimerWeb.Api.Util;

namespace VeloTimerWeb.Api.Data
{
    public class VeloIdentityDbContext : IdentityDbContext<User, Role, Guid>, IPersistedGrantDbContext
    {
        private readonly IOptions<OperationalStoreOptions> _operationalStoreOptions;

        public VeloIdentityDbContext(
            DbContextOptions<VeloIdentityDbContext> options,
            IOptions<OperationalStoreOptions> operationalStoreOptions) : base(options)
        {
            _operationalStoreOptions = operationalStoreOptions;
        }

        public DbSet<PersistedGrant> PersistedGrants { get; set; }
        public DbSet<DeviceFlowCodes> DeviceFlowCodes { get; set; }

        Task<int> IPersistedGrantDbContext.SaveChangesAsync() => base.SaveChangesAsync();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.HasDefaultSchema("identity");

            base.OnModelCreating(builder);
            builder.ConfigurePersistedGrantContext(_operationalStoreOptions.Value);

            builder.Entity<User>()
                .ToTable("users");
            builder.Entity<Role>()
                .ToTable("roles");
            builder.Entity<IdentityRoleClaim<Guid>>()
                .ToTable("role_claims");
            builder.Entity<IdentityUserClaim<Guid>>()
                .ToTable("user_claims");
            builder.Entity<IdentityUserLogin<Guid>>()
                .ToTable("user_logins");
            builder.Entity<IdentityUserToken<Guid>>()
                .ToTable("user_tokens");
            builder.Entity<IdentityUserRole<Guid>>()
                .ToTable("userroles");
            builder.Entity<DeviceFlowCodes>()
                .ToTable("device_codes");
            builder.Entity<PersistedGrant>()
                .ToTable("persisted_grants");

            builder.SnakeCaseModel();

            builder.Entity<Role>()
                .HasData(
                    new Role { Id = Guid.NewGuid(),Name = "User", NormalizedName = "USER " },
                    new Role { Id = Guid.NewGuid(), Name = "Admin", NormalizedName = "ADMIN" }
                );
        }
    }
}
