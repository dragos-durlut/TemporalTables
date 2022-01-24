using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.SqlServer.Internal;
using Microsoft.EntityFrameworkCore.SqlServer.Query.Internal;
using System;
#nullable enable

namespace Microsoft.EntityFrameworkCore.SqlServer.Query
{
#pragma warning disable EF1001 // Internal EF Core API usage.
    public class CustomSqlServerNavigationExpansionExtensibilityHelper : SqlServerNavigationExpansionExtensibilityHelper, INavigationExpansionExtensibilityHelper
    {
        public CustomSqlServerNavigationExpansionExtensibilityHelper(NavigationExpansionExtensibilityHelperDependencies dependencies) : base(dependencies)
        {
        }

        public override QueryRootExpression CreateQueryRoot(IEntityType entityType, QueryRootExpression? source)
        {
            if (source is TemporalQueryRootExpression)
            {
                if (!entityType.GetRootType().IsTemporal())
                {
                    if (source is TemporalAsOfQueryRootExpression asOf1)
                    {
#pragma warning disable CS8604 // Possible null reference argument.
                        return source.QueryProvider != null ? new QueryRootExpression(source.QueryProvider, entityType) : new QueryRootExpression(entityType);
#pragma warning restore CS8604 // Possible null reference argument.
                    }
                    throw new InvalidOperationException(
                        SqlServerStrings.TemporalNavigationExpansionBetweenTemporalAndNonTemporal(entityType.DisplayName()));
                }

                if (source is TemporalAsOfQueryRootExpression asOf)
                {
                    return source.QueryProvider != null
                        ? new TemporalAsOfQueryRootExpression(source.QueryProvider, entityType, asOf.PointInTime)
                        : new TemporalAsOfQueryRootExpression(entityType, asOf.PointInTime);
                }

                throw new InvalidOperationException(SqlServerStrings.TemporalNavigationExpansionOnlySupportedForAsOf("AsOf"));
            }

            return base.CreateQueryRoot(entityType, source);
        }
                
        public override bool AreQueryRootsCompatible(QueryRootExpression? first, QueryRootExpression? second)
        {
            if (!base.AreQueryRootsCompatible(first, second))
            {
                return false;
            }

            var firstTemporal = first is TemporalQueryRootExpression;
            var secondTemporal = second is TemporalQueryRootExpression;

            if (firstTemporal && secondTemporal)
            {
                if (first is TemporalAsOfQueryRootExpression firstAsOf
                    && second is TemporalAsOfQueryRootExpression secondAsOf
                    && firstAsOf.PointInTime == secondAsOf.PointInTime)
                {
                    return true;
                }

                if (first is TemporalAllQueryRootExpression
                    && second is TemporalAllQueryRootExpression)
                {
                    return true;
                }

                if (first is TemporalRangeQueryRootExpression firstRange
                    && second is TemporalRangeQueryRootExpression secondRange
                    && firstRange.From == secondRange.From
                    && firstRange.To == secondRange.To)
                {
                    return true;
                }
            }

            if (firstTemporal || secondTemporal)
            {
                var entityType = first?.EntityType ?? second?.EntityType;

                throw new InvalidOperationException(SqlServerStrings.TemporalSetOperationOnMismatchedSources(entityType!.DisplayName()));
            }

            return true;
        }
    }
#pragma warning restore EF1001 // Internal EF Core API usage.
}
