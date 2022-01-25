using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;


#pragma warning disable EF1001 // Internal EF Core API usage.
public class TemporalEntityMaterializerSource : EntityMaterializerSource, Microsoft.EntityFrameworkCore.Query.IEntityMaterializerSource
{
	public TemporalEntityMaterializerSource(EntityMaterializerSourceDependencies dependencies) : base(dependencies)
	{
	}

	public override Expression CreateMaterializeExpression(IEntityType entityType, string entityInstanceName, Expression materializationContextExpression)
	{
		var baseExpression = base.CreateMaterializeExpression(entityType, entityInstanceName, materializationContextExpression);
		if (entityType.ClrType.GetInterfaces().FirstOrDefault(i => i == typeof(ITemporalEntity)) != null)
		{
			var fromSysDatePropertyInfo = entityType.ClrType.GetProperty(nameof(ITemporalEntity.FromSysDate));
			var toSysDatePropertyInfo = entityType.ClrType.GetProperty(nameof(ITemporalEntity.ToSysDate));

			var shadowPropertiesHashSet = new HashSet<IPropertyBase>(
			entityType.GetServiceProperties().Cast<IPropertyBase>()
				.Concat(
					entityType
						.GetProperties()
						.Where(p => p.IsShadowProperty()))
				);

			var blockExpressions = new List<Expression>(((BlockExpression)baseExpression).Expressions);
			var instanceVariable = blockExpressions.Last() as ParameterExpression;

			var valueBufferExpression = Expression.Call(materializationContextExpression, MaterializationContext.GetValueBufferMethod);

			var bindingInfo = new ParameterBindingInfo(
									entityType,
									materializationContextExpression);

			var temporalBlockExpressions = new List<Expression>();
			foreach (var shadowPropertyBase in shadowPropertiesHashSet)
			{
				var shadowPropertyMemberType = typeof(DateTime);
				var readValueExpression =
					valueBufferExpression.CreateValueBufferReadValueExpression(
							shadowPropertyMemberType,
							shadowPropertyBase.GetIndex(),
							shadowPropertyBase);
				if (shadowPropertyBase.Name == TemporalEntityConstants.FromSysDateShadow)
					temporalBlockExpressions.Add(CreateMemberAssignment(instanceVariable, fromSysDatePropertyInfo, shadowPropertyBase, readValueExpression));
				if (shadowPropertyBase.Name == TemporalEntityConstants.ToSysDateShadow)
					temporalBlockExpressions.Add(CreateMemberAssignment(instanceVariable, toSysDatePropertyInfo, shadowPropertyBase, readValueExpression));
			}

			blockExpressions.InsertRange(blockExpressions.Count - 1, temporalBlockExpressions);

			return Expression.Block(new[] { instanceVariable }, blockExpressions);
		}

		return baseExpression;

		static Expression CreateMemberAssignment(Expression parameter, MemberInfo memberInfo, IPropertyBase property, Expression value)
		{
			if (property.IsIndexerProperty())
				return Expression.Assign(
					Expression.MakeIndex(
						parameter, (PropertyInfo)memberInfo, new List<Expression> { Expression.Constant(property.Name) }),
					value);
			return Expression.MakeMemberAccess(parameter, memberInfo).Assign(value);
		}
	}
}
#pragma warning restore EF1001 // Internal EF Core API usage.