using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.SqlServer.Query.Internal;
using System;
using System.Linq;

public static class SqlServerDbSetExtensions
{
    /// <summary>
    ///     Applies temporal 'AsOf' operation on the given DbSet, which only returns elements that were present in the database at a given
    ///     point in time.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Temporal information is stored in UTC format on the database, so any <see cref="DateTime" /> arguments in local time may lead to
    ///         unexpected results.
    ///     </para>
    ///     <para>
    ///         The default tracking behavior for queries can be controlled by <see cref="Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker.QueryTrackingBehavior" />.
    ///     </para>
    ///     <para>
    ///         See <see href="https://aka.ms/efcore-docs-temporal">Using SQL Server temporal tables with EF Core</see>
    ///         for more information and examples.
    ///     </para>
    /// </remarks>
    /// <param name="source">Source DbSet on which the temporal operation is applied.</param>
    /// <param name="utcPointInTime"><see cref="DateTime" /> representing a point in time for which the results should be returned.</param>
    /// <returns>An <see cref="IQueryable" /> representing the entities at a given point in time.</returns>
    public static IQueryable<TEntity> TemporalAsOf<TEntity>(
        this DbSet<TEntity> source,
        DateTime utcPointInTime,
        QueryTrackingBehavior queryTrackingBehavior
        )
        where TEntity : class
    {
#pragma warning disable EF1001 // Internal EF Core API usage.
        var queryableSource = (IQueryable)source;
        var entityQueryRootExpression = (EntityQueryRootExpression)queryableSource.Expression;
        var entityType = entityQueryRootExpression.EntityType;

        var query = queryableSource.Provider.CreateQuery<TEntity>(
            new TemporalAsOfQueryRootExpression(
                entityQueryRootExpression.QueryProvider!,
                entityType,
                utcPointInTime)).AsTracking(queryTrackingBehavior);
        return query;
#pragma warning restore EF1001 // Internal EF Core API usage.
    }
}
