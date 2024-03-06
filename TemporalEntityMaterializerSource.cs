﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.EntityFrameworkCore.Diagnostics.Internal;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Identity.Client;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace TemporalTables;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class TemporalEntityMaterializerSource : IEntityMaterializerSource
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public static readonly bool UseOldBehavior31866 =
        AppContext.TryGetSwitch("Microsoft.EntityFrameworkCore.Issue31866", out var enabled31866) && enabled31866;

    private static readonly MethodInfo InjectableServiceInjectedMethod
        = typeof(IInjectableService).GetMethod(nameof(IInjectableService.Injected))!;

    private ConcurrentDictionary<IEntityType, Func<MaterializationContext, object>>? _materializers;
    private ConcurrentDictionary<IEntityType, Func<MaterializationContext, object>>? _emptyMaterializers;
    private readonly List<IInstantiationBindingInterceptor> _bindingInterceptors;
    private readonly IMaterializationInterceptor? _materializationInterceptor;

    private static readonly MethodInfo PopulateListMethod
        = typeof(TemporalEntityMaterializerSource).GetMethod(
            nameof(PopulateList), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public TemporalEntityMaterializerSource(EntityMaterializerSourceDependencies dependencies)
    {
        Dependencies = dependencies;
        _bindingInterceptors = dependencies.SingletonInterceptors.OfType<IInstantiationBindingInterceptor>().ToList();

        _materializationInterceptor =
            (IMaterializationInterceptor?)new MaterializationInterceptorAggregator().AggregateInterceptors(
                dependencies.SingletonInterceptors.OfType<IMaterializationInterceptor>().ToList());
    }

    /// <summary>
    ///     Dependencies for this service.
    /// </summary>
    protected virtual EntityMaterializerSourceDependencies Dependencies { get; }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    [Obsolete("Use the overload that accepts an EntityMaterializerSourceParameters object.")]
    public virtual Expression CreateMaterializeExpression(
        IEntityType entityType,
        string entityInstanceName,
        Expression materializationContextExpression)
        => CreateMaterializeExpression(
            new EntityMaterializerSourceParameters(entityType, entityInstanceName, null), materializationContextExpression);

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public Expression CreateMaterializeExpression(
        EntityMaterializerSourceParameters parameters,
        Expression materializationContextExpression)
    {
        var (structuralType, entityInstanceName) = (parameters.StructuralType, parameters.InstanceName);

        if (structuralType.IsAbstract())
        {
            throw new InvalidOperationException(CoreStrings.CannotMaterializeAbstractType(structuralType.DisplayName()));
        }

        var constructorBinding = ModifyBindings(structuralType, structuralType.ConstructorBinding!);
        var bindingInfo = new ParameterBindingInfo(parameters, materializationContextExpression);
        var blockExpressions = new List<Expression>();

        var instanceVariable = Expression.Variable(constructorBinding.RuntimeType, entityInstanceName);
        bindingInfo.ServiceInstances.Add(instanceVariable);

        var properties = new HashSet<IPropertyBase>(
            structuralType.GetProperties().Cast<IPropertyBase>().Where(p => !p.IsShadowProperty())
                .Concat(structuralType.GetComplexProperties().Where(p => !p.IsShadowProperty())));

        List<Expression> temporalPropertiesBlockExpressions = CreateTemporalPropertiesBlockExpressions(structuralType, instanceVariable, bindingInfo);

        if (structuralType is IEntityType entityType)
        {
            var serviceProperties = entityType.GetServiceProperties().ToList();
            CreateServiceInstances(constructorBinding, bindingInfo, blockExpressions, serviceProperties);

            foreach (var serviceProperty in serviceProperties)
            {
                properties.Add(serviceProperty);
            }
        }

        foreach (var consumedProperty in constructorBinding.ParameterBindings.SelectMany(p => p.ConsumedProperties))
        {
            properties.Remove(consumedProperty);
        }

        var constructorExpression = constructorBinding.CreateConstructorExpression(bindingInfo);

        if (_materializationInterceptor == null)
        {
            return properties.Count == 0 && blockExpressions.Count == 0
                ? constructorExpression
                : CreateMaterializeExpression(blockExpressions, instanceVariable, constructorExpression, properties, bindingInfo, temporalPropertiesBlockExpressions);
        }

        // TODO: This currently applies the materialization interceptor only on the root structural type - any contained complex types
        // don't get intercepted.
        return CreateInterceptionMaterializeExpression(
            structuralType,
            properties,
            _materializationInterceptor,
            bindingInfo,
            constructorExpression,
            instanceVariable,
            blockExpressions,
            temporalPropertiesBlockExpressions
            );
    }

    private List<Expression> CreateTemporalPropertiesBlockExpressions(ITypeBase structuralType, ParameterExpression instanceVariable, ParameterBindingInfo bindingInfo)
    {
        if (structuralType.ImplementsTemporalEntityInterface() && structuralType.HasShadowProperties(out var shadowPropertiesHashSet))
        {
            var fromSysDatePropertyInfo = structuralType.ClrType.GetProperty(nameof(ITemporalEntity.FromSysDate));
            var toSysDatePropertyInfo = structuralType.ClrType.GetProperty(nameof(ITemporalEntity.ToSysDate));

            var valueBufferExpression = Expression.Call(bindingInfo.MaterializationContextExpression, MaterializationContext.GetValueBufferMethod);

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

            return temporalBlockExpressions;
        }
        else
        {
            return null;
        }
    }

    private static Expression CreateMemberAssignment(Expression parameter, MemberInfo memberInfo, IPropertyBase property, Expression value)
    {
        if (property.IsIndexerProperty())
            return Expression.Assign(
                Expression.MakeIndex(
                    parameter, (PropertyInfo)memberInfo, new List<Expression> { Expression.Constant(property.Name) }),
                value);
        return Expression.MakeMemberAccess(parameter, memberInfo).Assign(value);
    }

    private void AddInitializeExpressions(
        HashSet<IPropertyBase> properties,
        ParameterBindingInfo bindingInfo,
        Expression instanceVariable,
        List<Expression> blockExpressions)
    {
        var valueBufferExpression = Expression.Call(
            bindingInfo.MaterializationContextExpression,
            MaterializationContext.GetValueBufferMethod);

        foreach (var property in properties)
        {
            var memberInfo = property.GetMemberInfo(forMaterialization: true, forSet: true);

            var valueExpression = property switch
            {
                IProperty
                    => valueBufferExpression.CreateValueBufferReadValueExpression(
                        memberInfo.GetMemberType(), property.GetIndex(), property),

                IServiceProperty serviceProperty
                    => serviceProperty.ParameterBinding.BindToParameter(bindingInfo),

                IComplexProperty complexProperty
                    => CreateMaterializeExpression(
                        new EntityMaterializerSourceParameters(
                            complexProperty.ComplexType, "complexType", null /* TODO: QueryTrackingBehavior */),
                        bindingInfo.MaterializationContextExpression),

                _ => throw new UnreachableException()
            };

            blockExpressions.Add(CreateMemberAssignment(instanceVariable, memberInfo, property, valueExpression));
        }

        static Expression CreateMemberAssignment(Expression parameter, MemberInfo memberInfo, IPropertyBase property, Expression value)
        {
            if (property is IProperty { IsPrimitiveCollection: true, ClrType.IsArray: false })
            {
                var currentVariable = Expression.Variable(property.ClrType);
                return Expression.Block(
                    new[] { currentVariable },
                    Expression.Assign(
                        currentVariable,
                        Expression.MakeMemberAccess(parameter, property.GetMemberInfo(forMaterialization: true, forSet: false))),
                    Expression.IfThenElse(
                        Expression.OrElse(
                            Expression.ReferenceEqual(currentVariable, Expression.Constant(null)),
                            Expression.ReferenceEqual(value, Expression.Constant(null))),
                        Expression.MakeMemberAccess(parameter, memberInfo).Assign(value),
                        Expression.Call(
                            PopulateListMethod.MakeGenericMethod(property.ClrType.TryGetElementType(typeof(IEnumerable<>))!),
                            value,
                            currentVariable)
                    ));
            }

            return property.IsIndexerProperty()
                ? Expression.Assign(
                    Expression.MakeIndex(
                        parameter, (PropertyInfo)memberInfo, new List<Expression> { Expression.Constant(property.Name) }),
                    value)
                : Expression.MakeMemberAccess(parameter, memberInfo).Assign(value);
        }
    }

    private static IList<T> PopulateList<T>(IList<T> buffer, IList<T> target)
    {
        target.Clear();
        foreach (var value in buffer)
        {
            target.Add(value);
        }

        return target;
    }

    private static void AddAttachServiceExpressions(
        ParameterBindingInfo bindingInfo,
        Expression instanceVariable,
        List<Expression> blockExpressions)
    {
        var contextProperty = typeof(MaterializationContext).GetProperty(nameof(MaterializationContext.Context))!;
        var getContext = Expression.Property(bindingInfo.MaterializationContextExpression, contextProperty);

        foreach (var serviceInstance in bindingInfo.ServiceInstances)
        {
            blockExpressions.Add(
                Expression.IfThen(
                    Expression.TypeIs(serviceInstance, typeof(IInjectableService)),
                    Expression.Call(
                        Expression.Convert(serviceInstance, typeof(IInjectableService)),
                        InjectableServiceInjectedMethod,
                        getContext,
                        instanceVariable,
                        Expression.Constant(bindingInfo, typeof(ParameterBindingInfo)))));
        }
    }

    private static readonly ConstructorInfo MaterializationInterceptionDataConstructor
        = typeof(MaterializationInterceptionData).GetDeclaredConstructor(
            new[]
            {
                typeof(MaterializationContext),
                typeof(IEntityType),
                typeof(QueryTrackingBehavior?),
                typeof(Dictionary<IPropertyBase, (object, Func<MaterializationContext, object?>)>)
            })!;

    private static readonly MethodInfo CreatingInstanceMethod
        = typeof(IMaterializationInterceptor).GetMethod(nameof(IMaterializationInterceptor.CreatingInstance))!;

    private static readonly MethodInfo CreatedInstanceMethod
        = typeof(IMaterializationInterceptor).GetMethod(nameof(IMaterializationInterceptor.CreatedInstance))!;

    private static readonly MethodInfo InitializingInstanceMethod
        = typeof(IMaterializationInterceptor).GetMethod(nameof(IMaterializationInterceptor.InitializingInstance))!;

    private static readonly MethodInfo InitializedInstanceMethod
        = typeof(IMaterializationInterceptor).GetMethod(nameof(IMaterializationInterceptor.InitializedInstance))!;

    private static readonly PropertyInfo HasResultMethod
        = typeof(InterceptionResult<object>).GetProperty(nameof(InterceptionResult<object>.HasResult))!;

    private static readonly PropertyInfo ResultProperty
        = typeof(InterceptionResult<object>).GetProperty(nameof(InterceptionResult<object>.Result))!;

    private static readonly PropertyInfo IsSuppressedProperty
        = typeof(InterceptionResult).GetProperty(nameof(InterceptionResult.IsSuppressed))!;

    private static readonly MethodInfo DictionaryAddMethod
        = typeof(Dictionary<IPropertyBase, (object, Func<MaterializationContext, object?>)>).GetMethod(
            nameof(Dictionary<IPropertyBase, object>.Add),
            new[] { typeof(IPropertyBase), typeof((object, Func<MaterializationContext, object?>)) })!;

    private static readonly ConstructorInfo DictionaryConstructor
        = typeof(ValueTuple<object, Func<MaterializationContext, object?>>).GetConstructor(
            new[] { typeof(object), typeof(Func<MaterializationContext, object?>) })!;

    private Expression CreateMaterializeExpression(
        List<Expression> blockExpressions,
        ParameterExpression instanceVariable,
        Expression constructorExpression,
        HashSet<IPropertyBase> properties,
        ParameterBindingInfo bindingInfo,
        List<Expression> temporalPropertiesBlockExpressions = null
        )
    {
        blockExpressions.Add(Expression.Assign(instanceVariable, constructorExpression));

        AddInitializeExpressions(properties, bindingInfo, instanceVariable, blockExpressions);

        if (temporalPropertiesBlockExpressions != null && temporalPropertiesBlockExpressions.Any())
        {
            blockExpressions.InsertRange(blockExpressions.Count - 1, temporalPropertiesBlockExpressions);
        }

        if (bindingInfo.StructuralType is IEntityType)
        {
            AddAttachServiceExpressions(bindingInfo, instanceVariable, blockExpressions);
        }

        blockExpressions.Add(instanceVariable);

        return Expression.Block(bindingInfo.ServiceInstances, blockExpressions);
    }

    private Expression CreateInterceptionMaterializeExpression(
        ITypeBase structuralType,
        HashSet<IPropertyBase> properties,
        IMaterializationInterceptor materializationInterceptor,
        ParameterBindingInfo bindingInfo,
        Expression constructorExpression,
        ParameterExpression instanceVariable,
        List<Expression> blockExpressions,
        List<Expression> temporalPropertiesBlockExpressions = null
        )
    {
        // Something like:
        // Dictionary<IPropertyBase, (object, Func<MaterializationContext, object?>)> accessorFactory = CreateAccessors()
        // var creatingResult = interceptor.CreatingInstance(materializationData, new InterceptionResult<object>());
        //
        // var instance = interceptor.CreatedInstance(materializationData,
        //     creatingResult.HasResult ? creatingResult.Result : create(materializationContext));
        //
        // if (!interceptor.InitializingInstance(materializationData, instance, default(InterceptionResult)).IsSuppressed)
        // {
        //     initialize(materializationContext, instance);
        // }
        //
        // instance = interceptor.InitializedInstance(materializationData, instance);
        //
        // return instance;

        var materializationDataVariable = Expression.Variable(typeof(MaterializationInterceptionData), "materializationData");
        var creatingResultVariable = Expression.Variable(typeof(InterceptionResult<object>), "creatingResult");
        var interceptorExpression = Expression.Constant(materializationInterceptor, typeof(IMaterializationInterceptor));
        var accessorDictionaryVariable = Expression.Variable(
            typeof(Dictionary<IPropertyBase, (object, Func<MaterializationContext, object?>)>), "accessorDictionary");

        blockExpressions.Add(
            Expression.Assign(
                accessorDictionaryVariable,
                CreateAccessorDictionaryExpression()));
        blockExpressions.Add(
            Expression.Assign(
                materializationDataVariable,
                Expression.New(
                    MaterializationInterceptionDataConstructor,
                    bindingInfo.MaterializationContextExpression,
                    Expression.Constant(structuralType),
                    Expression.Constant(bindingInfo.QueryTrackingBehavior, typeof(QueryTrackingBehavior?)),
                    accessorDictionaryVariable)));
        blockExpressions.Add(
            Expression.Assign(
                creatingResultVariable,
                Expression.Call(
                    interceptorExpression,
                    CreatingInstanceMethod,
                    materializationDataVariable,
                    Expression.Default(typeof(InterceptionResult<object>)))));
        blockExpressions.Add(
            Expression.Assign(
                instanceVariable,
                Expression.Convert(
                    Expression.Call(
                        interceptorExpression,
                        CreatedInstanceMethod,
                        materializationDataVariable,
                        Expression.Condition(
                            Expression.Property(
                                creatingResultVariable,
                                HasResultMethod),
                            Expression.Convert(
                                Expression.Property(
                                    creatingResultVariable,
                                    ResultProperty),
                                instanceVariable.Type),
                            constructorExpression)),
                    instanceVariable.Type)));
        blockExpressions.Add(
            properties.Count == 0
                ? Expression.Call(
                    interceptorExpression,
                    InitializingInstanceMethod,
                    materializationDataVariable,
                    instanceVariable,
                    Expression.Default(typeof(InterceptionResult)))
                : Expression.IfThen(
                    Expression.Not(
                        Expression.Property(
                            Expression.Call(
                                interceptorExpression,
                                InitializingInstanceMethod,
                                materializationDataVariable,
                                instanceVariable,
                                Expression.Default(typeof(InterceptionResult))),
                            IsSuppressedProperty)),
                    CreateInitializeExpression()));
        blockExpressions.Add(
            Expression.Assign(
                instanceVariable,
                Expression.Convert(
                    Expression.Call(
                        interceptorExpression,
                        InitializedInstanceMethod,
                        materializationDataVariable,
                        instanceVariable),
                    instanceVariable.Type)));

        if (temporalPropertiesBlockExpressions != null && temporalPropertiesBlockExpressions.Any())
        {
            blockExpressions.InsertRange(blockExpressions.Count, temporalPropertiesBlockExpressions);
        }

        blockExpressions.Add(instanceVariable);

        return Expression.Block(
            bindingInfo.ServiceInstances.Concat(new[] { accessorDictionaryVariable, materializationDataVariable, creatingResultVariable }),
            blockExpressions);

        BlockExpression CreateAccessorDictionaryExpression()
        {
            var dictionaryVariable = Expression.Variable(
                typeof(Dictionary<IPropertyBase, (object, Func<MaterializationContext, object?>)>), "dictionary");
            var valueBufferExpression = Expression.Call(
                bindingInfo.MaterializationContextExpression, MaterializationContext.GetValueBufferMethod);
            var snapshotBlockExpressions = new List<Expression>
            {
                Expression.Assign(
                    dictionaryVariable,
                    Expression.New(
                        typeof(Dictionary<IPropertyBase, (object, Func<MaterializationContext, object?>)>)
                            .GetConstructor(Type.EmptyTypes)!))
            };

            if (structuralType is IEntityType entityType)
            {
                foreach (var property in entityType.GetServiceProperties().Cast<IPropertyBase>().Concat(structuralType.GetProperties()))
                {
                    snapshotBlockExpressions.Add(
                        Expression.Call(
                            dictionaryVariable,
                            DictionaryAddMethod,
                            Expression.Constant(property),
                            Expression.New(
                                DictionaryConstructor,
                                Expression.Lambda(
                                    typeof(Func<,>).MakeGenericType(typeof(MaterializationContext), property.ClrType),
                                    CreateAccessorReadExpression(),
                                    (ParameterExpression)bindingInfo.MaterializationContextExpression),
                                Expression.Lambda<Func<MaterializationContext, object?>>(
                                    Expression.Convert(CreateAccessorReadExpression(), typeof(object)),
                                    (ParameterExpression)bindingInfo.MaterializationContextExpression))));

                    Expression CreateAccessorReadExpression()
                        => property is IServiceProperty serviceProperty
                            ? serviceProperty.ParameterBinding.BindToParameter(bindingInfo)
                            : (property as IProperty)?.IsPrimaryKey() == true
                                ? Expression.Convert(
                                    valueBufferExpression.CreateValueBufferReadValueExpression(
                                        typeof(object),
                                        property.GetIndex(),
                                        property),
                                    property.ClrType)
                                : valueBufferExpression.CreateValueBufferReadValueExpression(
                                    property.ClrType,
                                    property.GetIndex(),
                                    property);
                }
            }

            snapshotBlockExpressions.Add(dictionaryVariable);

            return Expression.Block(new[] { dictionaryVariable }, snapshotBlockExpressions);
        }

        BlockExpression CreateInitializeExpression()
        {
            var initializeBlockExpressions = new List<Expression>();

            AddInitializeExpressions(properties, bindingInfo, instanceVariable, initializeBlockExpressions);

            if (bindingInfo.StructuralType is IEntityType)
            {
                AddAttachServiceExpressions(bindingInfo, instanceVariable, blockExpressions);
            }

            return Expression.Block(initializeBlockExpressions);
        }
    }

    private ConcurrentDictionary<IEntityType, Func<MaterializationContext, object>> Materializers
        => LazyInitializer.EnsureInitialized(
            ref _materializers,
            () => new ConcurrentDictionary<IEntityType, Func<MaterializationContext, object>>());

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public virtual Func<MaterializationContext, object> GetMaterializer(
        IEntityType entityType)
    {
        return UseOldBehavior31866
            ? Materializers.GetOrAdd(entityType, static (e, s) => CreateMaterializer(s, e), this)
            : CreateMaterializer(this, entityType);

        static Func<MaterializationContext, object> CreateMaterializer(TemporalEntityMaterializerSource self, IEntityType e)
        {
            var materializationContextParameter
                = Expression.Parameter(typeof(MaterializationContext), "materializationContext");

            return Expression.Lambda<Func<MaterializationContext, object>>(
                    ((IEntityMaterializerSource)self).CreateMaterializeExpression(
                        new EntityMaterializerSourceParameters(e, "instance", null), materializationContextParameter),
                    materializationContextParameter)
                .Compile();
        }
    }

    private ConcurrentDictionary<IEntityType, Func<MaterializationContext, object>> EmptyMaterializers
        => LazyInitializer.EnsureInitialized(
            ref _emptyMaterializers,
            () => new ConcurrentDictionary<IEntityType, Func<MaterializationContext, object>>());

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public virtual Func<MaterializationContext, object> GetEmptyMaterializer(IEntityType entityType)
    {
        return UseOldBehavior31866
            ? EmptyMaterializers.GetOrAdd(entityType, static (e, s) => CreateEmptyMaterializer(s, e), this)
            : CreateEmptyMaterializer(this, entityType);

        static Func<MaterializationContext, object> CreateEmptyMaterializer(TemporalEntityMaterializerSource self, IEntityType e)
        {
            var binding = e.ServiceOnlyConstructorBinding;
            if (binding == null)
            {
                var _ = e.ConstructorBinding;
                binding = e.ServiceOnlyConstructorBinding;
                if (binding == null)
                {
                    throw new InvalidOperationException(CoreStrings.NoParameterlessConstructor(e.DisplayName()));
                }
            }

            binding = self.ModifyBindings(e, binding);

            var materializationContextExpression = Expression.Parameter(typeof(MaterializationContext), "mc");
            var bindingInfo = new ParameterBindingInfo(
                new EntityMaterializerSourceParameters(e, "instance", null), materializationContextExpression);

            var blockExpressions = new List<Expression>();
            var instanceVariable = Expression.Variable(binding.RuntimeType, "instance");
            var serviceProperties = e.GetServiceProperties().ToList();
            bindingInfo.ServiceInstances.Add(instanceVariable);

            CreateServiceInstances(binding, bindingInfo, blockExpressions, serviceProperties);

            var constructorExpression = binding.CreateConstructorExpression(bindingInfo);

            var properties = new HashSet<IPropertyBase>(serviceProperties);
            foreach (var consumedProperty in binding.ParameterBindings.SelectMany(p => p.ConsumedProperties))
            {
                properties.Remove(consumedProperty);
            }

            return Expression.Lambda<Func<MaterializationContext, object>>(
                    self._materializationInterceptor == null
                        ? properties.Count == 0 && blockExpressions.Count == 0
                            ? constructorExpression
                            : self.CreateMaterializeExpression(
                                blockExpressions, instanceVariable, constructorExpression, properties, bindingInfo)
                        : self.CreateInterceptionMaterializeExpression(
                            e,
                            new HashSet<IPropertyBase>(),
                            self._materializationInterceptor,
                            bindingInfo,
                            constructorExpression,
                            instanceVariable,
                            blockExpressions),
                    materializationContextExpression)
                .Compile();
        }
    }

    private InstantiationBinding ModifyBindings(ITypeBase structuralType, InstantiationBinding binding)
    {
        var interceptionData = new InstantiationBindingInterceptionData(structuralType);
        foreach (var bindingInterceptor in _bindingInterceptors)
        {
            binding = bindingInterceptor.ModifyBinding(interceptionData, binding);
        }

        return binding;
    }

    private static void CreateServiceInstances(
        InstantiationBinding constructorBinding,
        ParameterBindingInfo bindingInfo,
        List<Expression> blockExpressions,
        List<IServiceProperty> serviceProperties)
    {
        foreach (var parameterBinding in constructorBinding.ParameterBindings.OfType<ServiceParameterBinding>())
        {
            if (bindingInfo.ServiceInstances.All(s => s.Type != parameterBinding.ServiceType))
            {
                var variable = Expression.Variable(parameterBinding.ServiceType);
                blockExpressions.Add(Expression.Assign(variable, parameterBinding.BindToParameter(bindingInfo)));
                bindingInfo.ServiceInstances.Add(variable);
            }
        }

        foreach (var serviceProperty in serviceProperties)
        {
            var serviceType = serviceProperty.ParameterBinding.ServiceType;
            if (bindingInfo.ServiceInstances.All(e => e.Type != serviceType))
            {
                var variable = Expression.Variable(serviceType);
                blockExpressions.Add(Expression.Assign(variable, serviceProperty.ParameterBinding.BindToParameter(bindingInfo)));
                bindingInfo.ServiceInstances.Add(variable);
            }
        }
    }
}

internal static class EntityFrameworkMemberInfoExtensions
{
    internal static Type GetMemberType(this MemberInfo memberInfo)
    => (memberInfo as PropertyInfo)?.PropertyType ?? ((FieldInfo)memberInfo).FieldType;

    public static ConstructorInfo? GetDeclaredConstructor(
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
        this Type type,
        Type[]? types)
    {
        types ??= Array.Empty<Type>();

        return type.GetTypeInfo().DeclaredConstructors
            .SingleOrDefault(
                c => !c.IsStatic
                    && c.GetParameters().Select(p => p.ParameterType).SequenceEqual(types))!;
    }

    public static Type? TryGetElementType(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] this Type type,
        Type interfaceOrBaseType)
    {
        if (type.IsGenericTypeDefinition)
        {
            return null;
        }

        var types = GetGenericTypeImplementations(type, interfaceOrBaseType);

        Type? singleImplementation = null;
        foreach (var implementation in types)
        {
            if (singleImplementation == null)
            {
                singleImplementation = implementation;
            }
            else
            {
                singleImplementation = null;
                break;
            }
        }

        return singleImplementation?.GenericTypeArguments.FirstOrDefault();
    }

    public static IEnumerable<Type> GetGenericTypeImplementations(this Type type, Type interfaceOrBaseType)
    {
        var typeInfo = type.GetTypeInfo();
        if (!typeInfo.IsGenericTypeDefinition)
        {
            var baseTypes = interfaceOrBaseType.GetTypeInfo().IsInterface
                ? typeInfo.ImplementedInterfaces
                : type.GetBaseTypes();
            foreach (var baseType in baseTypes)
            {
                if (baseType.IsGenericType
                    && baseType.GetGenericTypeDefinition() == interfaceOrBaseType)
                {
                    yield return baseType;
                }
            }

            if (type.IsGenericType
                && type.GetGenericTypeDefinition() == interfaceOrBaseType)
            {
                yield return type;
            }
        }
    }

    public static IEnumerable<Type> GetBaseTypes(this Type type)
    {
        var currentType = type.BaseType;

        while (currentType != null)
        {
            yield return currentType;

            currentType = currentType.BaseType;
        }
    }

    
}

internal static class TemporalEntitiesShadowPropertiesExtensions
{
    public static bool ImplementsTemporalEntityInterface(this ITypeBase structuralType)
    {
        return structuralType.ClrType.GetInterfaces().Any(i => i == typeof(ITemporalEntity));
    }

    public static bool HasShadowProperties(this ITypeBase structuralType, out HashSet<IPropertyBase> shadowPropertiesHashSet)
    {        
        shadowPropertiesHashSet = new HashSet<IPropertyBase>(
        structuralType.GetProperties().Cast<IPropertyBase>().Where(p => p.IsShadowProperty())
            .Concat(structuralType.GetComplexProperties().Where(p => p.IsShadowProperty())));
        if(shadowPropertiesHashSet.Any())
            return true;
        else return false;
    }
}