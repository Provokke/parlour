using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Parlour;

public sealed class ServiceRegistry : IServiceProvider
{
    [ThreadStatic]
    private static HashSet<Type>? constructing;

    private readonly object gate = new();
    private Descriptor[] descriptors = Array.Empty<Descriptor>();

    public ServiceRegistry AddTransient(Type service, Type implementation)
    {
        return Register(new Descriptor(service, implementation, null, null, false));
    }

    public ServiceRegistry AddTransient(Type service, Func<IServiceProvider, object> factory)
    {
        return Register(new Descriptor(service, null, factory, null, false));
    }

    public ServiceRegistry AddSingleton(Type service, Type implementation)
    {
        return Register(new Descriptor(service, implementation, null, null, true));
    }

    public ServiceRegistry AddSingleton(Type service, Func<IServiceProvider, object> factory)
    {
        return Register(new Descriptor(service, null, factory, null, true));
    }

    public ServiceRegistry AddSingleton(Type service, object instance)
    {
        if (!service.IsInstanceOfType(instance))
        {
            throw new ArgumentException($"{instance.GetType().Name} is not a {service.Name}", nameof(instance));
        }

        return Register(new Descriptor(service, null, null, instance, true));
    }

    public ServiceRegistry AddTransient<TService, TImplementation>()
        where TImplementation : TService
    {
        return AddTransient(typeof(TService), typeof(TImplementation));
    }

    public ServiceRegistry AddSingleton<TService, TImplementation>()
        where TImplementation : TService
    {
        return AddSingleton(typeof(TService), typeof(TImplementation));
    }

    public ServiceRegistry AddSingleton<TService>(TService instance)
        where TService : class
    {
        return AddSingleton(typeof(TService), instance);
    }

    public bool IsRegistered(Type service)
    {
        return Array.Exists(descriptors, d => Matches(d, service));
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IServiceProvider))
        {
            return this;
        }

        if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            return ResolveAll(serviceType.GetGenericArguments()[0]);
        }

        var snapshot = descriptors;
        for (var i = snapshot.Length - 1; i >= 0; i--)
        {
            if (Matches(snapshot[i], serviceType) && TryResolve(snapshot[i], serviceType, out var instance))
            {
                return instance;
            }
        }

        return null;
    }

    private ServiceRegistry Register(Descriptor descriptor)
    {
        if (descriptor.Implementation is { } impl && (impl.IsAbstract || impl.IsInterface))
        {
            throw new ArgumentException($"{impl.Name} cannot be instantiated", nameof(descriptor));
        }

        lock (gate)
        {
            descriptors = descriptors.Append(descriptor).ToArray();
        }

        return this;
    }

    private static bool Matches(Descriptor d, Type requested)
    {
        if (d.Service == requested)
        {
            return true;
        }

        return d.Service.IsGenericTypeDefinition
            && requested.IsGenericType
            && requested.GetGenericTypeDefinition() == d.Service;
    }

    private Array ResolveAll(Type element)
    {
        var found = new List<object>();
        foreach (var d in descriptors)
        {
            if (Matches(d, element) && TryResolve(d, element, out var instance))
            {
                found.Add(instance!);
            }
        }

        var array = Array.CreateInstance(element, found.Count);
        for (var i = 0; i < found.Count; i++)
        {
            array.SetValue(found[i], i);
        }

        return array;
    }

    private bool TryResolve(Descriptor d, Type requested, out object? instance)
    {
        instance = null;
        if (d.Instance != null)
        {
            instance = d.Instance;
            return true;
        }

        Type? closed = null;
        if (d.Implementation != null)
        {
            closed = d.Implementation;
            if (d.Implementation.IsGenericTypeDefinition)
            {
                try
                {
                    closed = d.Implementation.MakeGenericType(requested.GetGenericArguments());
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }
        }

        if (!d.Singleton)
        {
            instance = Create(d, closed);
            return true;
        }

        instance = d.Cache.GetOrAdd(requested, _ => new Lazy<object>(() => Create(d, closed))).Value;
        return true;
    }

    private object Create(Descriptor d, Type? closed)
    {
        return d.Factory != null ? d.Factory(this) : Construct(closed!);
    }

    private object Construct(Type implementation)
    {
        constructing ??= new HashSet<Type>();
        if (!constructing.Add(implementation))
        {
            throw new InvalidOperationException($"circular dependency while creating {implementation.Name}");
        }

        try
        {
            var constructors = implementation.GetConstructors().OrderByDescending(c => c.GetParameters().Length);
            foreach (var ctor in constructors)
            {
                var args = TryResolveArguments(ctor);
                if (args == null)
                {
                    continue;
                }

                try
                {
                    return ctor.Invoke(args);
                }
                catch (TargetInvocationException e) when (e.InnerException != null)
                {
                    ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                }
            }
        }
        finally
        {
            constructing.Remove(implementation);
        }

        throw new InvalidOperationException($"no constructor of {implementation.Name} could be satisfied from the registered services");
    }

    private object?[]? TryResolveArguments(ConstructorInfo ctor)
    {
        var parameters = ctor.GetParameters();
        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var value = GetService(parameters[i].ParameterType);
            if (value == null)
            {
                if (!parameters[i].HasDefaultValue)
                {
                    return null;
                }

                value = parameters[i].DefaultValue;
            }

            args[i] = value;
        }

        return args;
    }

    private sealed class Descriptor
    {
        public Descriptor(Type service, Type? implementation, Func<IServiceProvider, object>? factory, object? instance, bool singleton)
        {
            Service = service;
            Implementation = implementation;
            Factory = factory;
            Instance = instance;
            Singleton = singleton;
        }

        public Type Service { get; }

        public Type? Implementation { get; }

        public Func<IServiceProvider, object>? Factory { get; }

        public object? Instance { get; }

        public bool Singleton { get; }

        public ConcurrentDictionary<Type, Lazy<object>> Cache { get; } = new();
    }
}
