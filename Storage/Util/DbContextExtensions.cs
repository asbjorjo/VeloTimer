using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace VeloTime.Storage.Util
{
    public static class DbContextExtensions
    {
        public static ModelBuilder SnakeCaseModel(this ModelBuilder builder)
        {
            foreach (var entity in builder.Model.GetEntityTypes())
            {
                // Replace table names
                var tableName = entity.GetTableName();
                if (tableName != null)
                {

                    entity.SetTableName(tableName.ToSnakeCase());

                    // Replace column names            
                    foreach (var property in entity.GetProperties())
                    {
                        var columnName = property.GetColumnName(StoreObjectIdentifier.Table(tableName, entity.GetSchema()));
                        if (columnName != null)
                            property.SetColumnName(columnName.ToSnakeCase());
                    }
                }

                foreach (var key in entity.GetKeys())
                {
                    var keyName = key.GetName();
                    if (keyName != null)
                        key.SetName(keyName.ToSnakeCase());
                }

                foreach (var key in entity.GetForeignKeys())
                {
                    var principalKeyName = key.PrincipalKey.GetName();
                    if (principalKeyName != null)
                        key.PrincipalKey.SetName(principalKeyName.ToSnakeCase());
                    var constraintName = key.GetConstraintName();
                    if (constraintName != null)
                        key.SetConstraintName(constraintName.ToSnakeCase());
                }

                foreach (var index in entity.GetIndexes())
                {
                    index.SetDatabaseName(index.GetDatabaseName().ToSnakeCase());
                }
            }

            return builder;
        }
    }
}
