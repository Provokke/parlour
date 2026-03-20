using Parlour;

namespace Parlour.Tests;

public class RegistryTests
{
    public interface IClock
    {
        int Id { get; }
    }

    public sealed class Clock : IClock
    {
        private static int next;

        public Clock()
        {
            Id = Interlocked.Increment(ref next);
        }

        public int Id { get; }
    }

    public sealed class NeedsClock
    {
        public NeedsClock(IClock clock)
        {
            Clock = clock;
        }

        public IClock Clock { get; }
    }

    public sealed class Greedy
    {
        public Greedy()
        {
            Used = "default";
        }

        public Greedy(IClock clock)
        {
            Used = "clock";
        }

        public Greedy(IClock clock, NeedsClock other)
        {
            Used = "both";
        }

        public string Used { get; }
    }

    public sealed class WithDefault
    {
        public WithDefault(IClock? clock = null, int size = 5)
        {
            HasClock = clock != null;
            Size = size;
        }

        public bool HasClock { get; }

        public int Size { get; }
    }

    public sealed class Unsatisfiable
    {
        public Unsatisfiable(IDisposable thing)
        {
        }
    }

    public sealed class CycleA
    {
        public CycleA(CycleB b)
        {
        }
    }

    public sealed class CycleB
    {
        public CycleB(CycleA a)
        {
        }
    }

    public sealed class Throws
    {
        public Throws()
        {
            throw new FormatException("ctor failed");
        }
    }

    public interface IBox<T>
    {
    }

    public sealed class Box<T> : IBox<T>
    {
    }

    public sealed class StructBox<T> : IBox<T>
        where T : struct
    {
    }

    [Fact]
    public void TransientsAreCreatedEveryTime()
    {
        var r = new ServiceRegistry().AddTransient<IClock, Clock>();
        Assert.NotSame(r.GetService(typeof(IClock)), r.GetService(typeof(IClock)));
    }

    [Fact]
    public void SingletonsAreCreatedOnce()
    {
        var r = new ServiceRegistry().AddSingleton<IClock, Clock>();
        Assert.Same(r.GetService(typeof(IClock)), r.GetService(typeof(IClock)));
    }

    [Fact]
    public void InstancesAreReturnedAsIs()
    {
        var clock = new Clock();
        var r = new ServiceRegistry().AddSingleton<IClock>(clock);
        Assert.Same(clock, r.GetService(typeof(IClock)));
    }

    [Fact]
    public void InstanceMustMatchTheServiceType()
    {
        Assert.Throws<ArgumentException>(() => new ServiceRegistry().AddSingleton(typeof(IClock), "not a clock"));
    }

    [Fact]
    public void AbstractImplementationsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new ServiceRegistry().AddTransient(typeof(IClock), typeof(IClock)));
    }

    [Fact]
    public void FactoriesReceiveTheRegistry()
    {
        var r = new ServiceRegistry()
            .AddSingleton<IClock>(new Clock())
            .AddTransient(typeof(NeedsClock), sp => new NeedsClock((IClock)sp.GetService(typeof(IClock))!));
        Assert.NotNull(((NeedsClock)r.GetService(typeof(NeedsClock))!).Clock);
    }

    [Fact]
    public void SingletonFactoriesRunOnce()
    {
        var calls = 0;
        var r = new ServiceRegistry().AddSingleton(typeof(IClock), _ =>
        {
            calls++;
            return new Clock();
        });
        r.GetService(typeof(IClock));
        r.GetService(typeof(IClock));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ConstructorsGetTheirDependencies()
    {
        var r = new ServiceRegistry()
            .AddSingleton<IClock>(new Clock())
            .AddTransient(typeof(NeedsClock), typeof(NeedsClock));
        var made = (NeedsClock)r.GetService(typeof(NeedsClock))!;
        Assert.Same(r.GetService(typeof(IClock)), made.Clock);
    }

    [Fact]
    public void TheGreediestSatisfiableConstructorWins()
    {
        var withNothing = new ServiceRegistry().AddTransient(typeof(Greedy), typeof(Greedy));
        Assert.Equal("default", ((Greedy)withNothing.GetService(typeof(Greedy))!).Used);

        var withClock = new ServiceRegistry().AddSingleton<IClock>(new Clock()).AddTransient(typeof(Greedy), typeof(Greedy));
        Assert.Equal("clock", ((Greedy)withClock.GetService(typeof(Greedy))!).Used);

        var withBoth = new ServiceRegistry()
            .AddSingleton<IClock>(new Clock())
            .AddTransient(typeof(NeedsClock), typeof(NeedsClock))
            .AddTransient(typeof(Greedy), typeof(Greedy));
        Assert.Equal("both", ((Greedy)withBoth.GetService(typeof(Greedy))!).Used);
    }

    [Fact]
    public void DefaultParameterValuesAreUsedWhenNothingIsRegistered()
    {
        var r = new ServiceRegistry().AddTransient(typeof(WithDefault), typeof(WithDefault));
        var made = (WithDefault)r.GetService(typeof(WithDefault))!;
        Assert.False(made.HasClock);
        Assert.Equal(5, made.Size);
    }

    [Fact]
    public void UnsatisfiableConstructorsThrowWhenResolved()
    {
        var r = new ServiceRegistry().AddTransient(typeof(Unsatisfiable), typeof(Unsatisfiable));
        var e = Assert.Throws<InvalidOperationException>(() => r.GetService(typeof(Unsatisfiable)));
        Assert.Contains("Unsatisfiable", e.Message);
    }

    [Fact]
    public void CircularDependenciesAreReportedNotOverflowed()
    {
        var r = new ServiceRegistry()
            .AddTransient(typeof(CycleA), typeof(CycleA))
            .AddTransient(typeof(CycleB), typeof(CycleB));
        var e = Assert.Throws<InvalidOperationException>(() => r.GetService(typeof(CycleA)));
        Assert.Contains("circular", e.Message);
        Assert.Throws<InvalidOperationException>(() => r.GetService(typeof(CycleA)));
    }

    [Fact]
    public void ConstructorExceptionsAreNotWrapped()
    {
        var r = new ServiceRegistry().AddTransient(typeof(Throws), typeof(Throws));
        var e = Assert.Throws<FormatException>(() => r.GetService(typeof(Throws)));
        Assert.Equal("ctor failed", e.Message);
    }

    [Fact]
    public void UnknownServicesComeBackNull()
    {
        Assert.Null(new ServiceRegistry().GetService(typeof(IClock)));
    }

    [Fact]
    public void TheRegistryResolvesItself()
    {
        var r = new ServiceRegistry();
        Assert.Same(r, r.GetService(typeof(IServiceProvider)));
    }

    [Fact]
    public void LastRegistrationWinsForSingleResolution()
    {
        var first = new Clock();
        var second = new Clock();
        var r = new ServiceRegistry().AddSingleton<IClock>(first).AddSingleton<IClock>(second);
        Assert.Same(second, r.GetService(typeof(IClock)));
    }

    [Fact]
    public void EnumerablesListEveryRegistrationInOrder()
    {
        var a = new Clock();
        var b = new Clock();
        var r = new ServiceRegistry().AddSingleton<IClock>(a).AddSingleton<IClock>(b);
        var all = (IEnumerable<IClock>)r.GetService(typeof(IEnumerable<IClock>))!;
        Assert.Equal(new IClock[] { a, b }, all);
    }

    [Fact]
    public void EnumerablesAreEmptyNotNull()
    {
        var all = (IEnumerable<IClock>)new ServiceRegistry().GetService(typeof(IEnumerable<IClock>))!;
        Assert.Empty(all);
    }

    [Fact]
    public void OpenGenericsAreClosedOnDemand()
    {
        var r = new ServiceRegistry().AddTransient(typeof(IBox<>), typeof(Box<>));
        Assert.IsType<Box<int>>(r.GetService(typeof(IBox<int>)));
        Assert.IsType<Box<string>>(r.GetService(typeof(IBox<string>)));
    }

    [Fact]
    public void OpenGenericsThatBreakConstraintsAreSkipped()
    {
        var r = new ServiceRegistry()
            .AddTransient(typeof(IBox<>), typeof(StructBox<>))
            .AddTransient(typeof(IBox<>), typeof(Box<>));
        Assert.IsType<Box<string>>(r.GetService(typeof(IBox<string>)));
        var strings = (IEnumerable<IBox<string>>)r.GetService(typeof(IEnumerable<IBox<string>>))!;
        Assert.Single(strings);
        var ints = (IEnumerable<IBox<int>>)r.GetService(typeof(IEnumerable<IBox<int>>))!;
        Assert.Equal(new[] { typeof(StructBox<int>), typeof(Box<int>) }, ints.Select(x => x.GetType()));
    }

    [Fact]
    public void OpenGenericSingletonsAreCachedPerClosedType()
    {
        var r = new ServiceRegistry().AddSingleton(typeof(IBox<>), typeof(Box<>));
        Assert.Same(r.GetService(typeof(IBox<int>)), r.GetService(typeof(IBox<int>)));
        Assert.NotSame(r.GetService(typeof(IBox<int>)), r.GetService(typeof(IBox<long>)));
    }

    [Fact]
    public void IsRegisteredSeesClosedAndOpenRegistrations()
    {
        var r = new ServiceRegistry().AddTransient(typeof(IBox<>), typeof(Box<>)).AddTransient<IClock, Clock>();
        Assert.True(r.IsRegistered(typeof(IClock)));
        Assert.True(r.IsRegistered(typeof(IBox<Guid>)));
        Assert.False(r.IsRegistered(typeof(NeedsClock)));
    }

    [Fact]
    public async Task ConcurrentResolutionOfASingletonYieldsOneInstance()
    {
        var created = 0;
        var r = new ServiceRegistry().AddSingleton(typeof(IClock), _ =>
        {
            Interlocked.Increment(ref created);
            Thread.Sleep(20);
            return new Clock();
        });
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => r.GetService(typeof(IClock)))));
        Assert.Equal(1, created);
        Assert.Single(results.Distinct());
    }
}
