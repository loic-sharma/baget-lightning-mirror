using System;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Newtonsoft.Json;

namespace BaGet
{
    public class SqlServerContext : DbContext, IContext
    {
        public const int DefaultMaxStringLength = 4000;

        public const int MaxPackageIdLength = 128;
        public const int MaxPackageVersionLength = 64;
        public const int MaxPackageMinClientVersionLength = 44;
        public const int MaxPackageLanguageLength = 20;
        public const int MaxPackageTitleLength = 256;
        public const int MaxRepositoryTypeLength = 100;

        public const int MaxPackageDependencyVersionRangeLength = 256;
        public const int MaxPackageDependencyTargetFrameworkLength = 256;

        /// <summary>
        /// The SQL Server error code for when a unique contraint is violated.
        /// </summary>
        private const int UniqueConstraintViolationErrorCode = 2627;

        public SqlServerContext(DbContextOptions<SqlServerContext> options)
            : base(options)
        { }

        public DbSet<Package> Packages { get; set; }
        public DbSet<PackageDependency> PackageDependencies { get; set; }

        public Task<int> SaveChangesAsync() => SaveChangesAsync(default(CancellationToken));

        public virtual bool SupportsLimitInSubqueries => true;

        /// <summary>
        /// Check whether a <see cref="DbUpdateException"/> is due to a SQL unique constraint violation.
        /// </summary>
        /// <param name="exception">The exception to inspect.</param>
        /// <returns>Whether the exception was caused to SQL unique constraint violation.</returns>
        public bool IsUniqueConstraintViolationException(DbUpdateException exception)
        {
            if (exception.GetBaseException() is SqlException sqlException)
            {
                return sqlException.Errors
                    .OfType<SqlError>()
                    .Any(error => error.Number == UniqueConstraintViolationErrorCode);
            }

            return false;
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Package>(BuildPackageEntity);
            builder.Entity<PackageDependency>(BuildPackageDependencyEntity);
        }

        private void BuildPackageEntity(EntityTypeBuilder<Package> package)
        {
            package.HasKey(p => p.Key);
            package.HasIndex(p => p.Id);
            package.HasIndex(p => new { p.Id, p.VersionString })
                .IsUnique();

            package.Property(p => p.Id)
                .HasMaxLength(MaxPackageIdLength)
                .IsRequired();

            package.Property(p => p.VersionString)
                .HasColumnName("Version")
                .HasMaxLength(MaxPackageVersionLength)
                .IsRequired();

            package.Property(p => p.Authors)
                .HasConversion(StringArrayToJsonConverter.Instance)
                .HasMaxLength(DefaultMaxStringLength);

            package.Property(p => p.IconUrl)
                .HasConversion(UriToStringConverter.Instance)
                .HasMaxLength(DefaultMaxStringLength);

            package.Property(p => p.LicenseUrl)
                .HasConversion(UriToStringConverter.Instance)
                .HasMaxLength(DefaultMaxStringLength);

            package.Property(p => p.ProjectUrl)
                .HasConversion(UriToStringConverter.Instance)
                .HasMaxLength(DefaultMaxStringLength);

            package.Property(p => p.RepositoryUrl)
                .HasConversion(UriToStringConverter.Instance)
                .HasMaxLength(DefaultMaxStringLength);

            package.Property(p => p.Tags)
                .HasConversion(StringArrayToJsonConverter.Instance)
                .HasMaxLength(DefaultMaxStringLength);

            package.Property(p => p.Description).HasMaxLength(DefaultMaxStringLength);
            package.Property(p => p.Language).HasMaxLength(MaxPackageLanguageLength);
            package.Property(p => p.MinClientVersion).HasMaxLength(MaxPackageMinClientVersionLength);
            package.Property(p => p.Summary).HasMaxLength(DefaultMaxStringLength);
            package.Property(p => p.Title).HasMaxLength(MaxPackageTitleLength);
            package.Property(p => p.RepositoryType).HasMaxLength(MaxRepositoryTypeLength);

            package.Ignore(p => p.Version);
            package.Ignore(p => p.IconUrlString);
            package.Ignore(p => p.LicenseUrlString);
            package.Ignore(p => p.ProjectUrlString);
            package.Ignore(p => p.RepositoryUrlString);

            package.Property(p => p.RowVersion).IsRowVersion();
        }

        private void BuildPackageDependencyEntity(EntityTypeBuilder<PackageDependency> dependency)
        {
            dependency.HasKey(d => d.Key);

            dependency.Property(d => d.Id).HasMaxLength(MaxPackageIdLength);
            dependency.Property(d => d.VersionRange).HasMaxLength(MaxPackageDependencyVersionRangeLength);
            dependency.Property(d => d.TargetFramework).HasMaxLength(MaxPackageDependencyTargetFrameworkLength);
        }
    }

    public class StringArrayToJsonConverter : ValueConverter<string[], string>
    {
        public static readonly StringArrayToJsonConverter Instance = new StringArrayToJsonConverter();

        public StringArrayToJsonConverter()
            : base(
                  v => JsonConvert.SerializeObject(v),
                  v => (!string.IsNullOrEmpty(v)) ? JsonConvert.DeserializeObject<string[]>(v) : new string[0])
        {
        }
    }

    public class UriToStringConverter : ValueConverter<Uri, string>
    {
        public static readonly UriToStringConverter Instance = new UriToStringConverter();

        public UriToStringConverter()
            : base(
                  v => v.AbsoluteUri,
                  v => new Uri(v))
        {
        }
    }
}
