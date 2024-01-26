using Microsoft.EntityFrameworkCore.Diagnostics;
using System;

namespace TemporalTables
{
    /// <summary>
    /// https://github.com/dotnet/efcore/issues/26463#issuecomment-1558631148
    /// </summary>
    public class TemporalEntityMaterializationInterceptor : IMaterializationInterceptor
    {
        //https://stackoverflow.com/questions/75601346/adding-interceptor-in-dbcontext-gives-microsoft-entityframeworkcore-infrastructu
        public static TemporalEntityMaterializationInterceptor Instance { get; } = new();

        private TemporalEntityMaterializationInterceptor() { }

        public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
        {
            if (entity is not ITemporalEntity temporalEntity)
                return entity;

            temporalEntity.FromSysDate = materializationData.GetPropertyValue<DateTime>(TemporalEntityConstants.FromSysDateShadow);
            temporalEntity.ToSysDate = materializationData.GetPropertyValue<DateTime>(TemporalEntityConstants.ToSysDateShadow);

            return temporalEntity;
        }
    }
}
