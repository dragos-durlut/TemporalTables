using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.SqlServer.Internal;
using Microsoft.EntityFrameworkCore.SqlServer.Query.Internal;
using System;
using System.Linq;
#nullable enable

namespace Microsoft.EntityFrameworkCore.SqlServer.Query
{
#pragma warning disable EF1001 // Internal EF Core API usage.
    /// <summary>
    /// Fixes AsOf and FromTo temporal query with join with non-temporal child entities
    /// See https://github.com/dotnet/efcore/issues/27259
    /// </summary>
    public class CustomSqlServerNavigationExpansionExtensibilityHelper : SqlServerNavigationExpansionExtensibilityHelper, INavigationExpansionExtensibilityHelper
    {
        public CustomSqlServerNavigationExpansionExtensibilityHelper(NavigationExpansionExtensibilityHelperDependencies dependencies) : base(dependencies)
        {
        }

        public override EntityQueryRootExpression CreateQueryRoot(IEntityType entityType, EntityQueryRootExpression? source)
        {
            //BEGIN CUSTOM CODE
            if (source is TemporalQueryRootExpression
                && !entityType.IsMappedToJson()
                && !OwnedEntityMappedToSameTableAsOwner(entityType))
            {
                if (!entityType.GetRootType().IsTemporal())
                {
                    //Expand the non-temporal type with a default EntityQueryRootExpression
                    if (source is TemporalAsOfQueryRootExpression || source is TemporalFromToQueryRootExpression)
                    {
                        return source.QueryProvider != null ? new EntityQueryRootExpression(source.QueryProvider, entityType) : new EntityQueryRootExpression(entityType);
                    }
                }
            }
            //END CUSTOM CODE

            return base.CreateQueryRoot(entityType, source);
        }

        public override void ValidateQueryRootCreation(IEntityType entityType, EntityQueryRootExpression? source)
        {
            if (source is TemporalQueryRootExpression
                && !entityType.IsMappedToJson()
                && !OwnedEntityMappedToSameTableAsOwner(entityType))
            {
                if (!entityType.GetRootType().IsTemporal())
                {
                    //BEGIN CUSTOM CODE
                    //Allow temporal queries 'AsOf' and 'FromTo' to navigate to non temporal types 
                    if (source is TemporalAsOfQueryRootExpression || source is TemporalFromToQueryRootExpression)
                        return;
                    //END CUSTOM CODE

                    throw new InvalidOperationException(
                        SqlServerStrings.TemporalNavigationExpansionBetweenTemporalAndNonTemporal(entityType.DisplayName()));
                }

                if (source is not TemporalAsOfQueryRootExpression)
                    throw new InvalidOperationException(SqlServerStrings.TemporalNavigationExpansionOnlySupportedForAsOf("AsOf"));
            }

            base.ValidateQueryRootCreation(entityType, source);
        }
        private bool OwnedEntityMappedToSameTableAsOwner(IEntityType entityType)
            => entityType.IsOwned()
                && entityType.FindOwnership()!.PrincipalEntityType.GetTableMappings().FirstOrDefault()?.Table is ITable ownerTable
                    && entityType.GetTableMappings().FirstOrDefault()?.Table is ITable entityTable
                    && ownerTable == entityTable;
    }
#pragma warning restore EF1001 // Internal EF Core API usage.
}