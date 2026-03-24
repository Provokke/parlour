<div align="center">

# parlour

A small mediator library for .NET. Send a request and get one response back, publish a notification and have any number of handlers react, and wrap it all in a pipeline of behaviours for logging, validation, timeouts and the like. No dependencies, and no dependency injection container required.

![License](https://img.shields.io/badge/LICENSE-MIT-blue?style=for-the-badge)  ![Language](https://img.shields.io/badge/C%23-512bd4?style=for-the-badge&logo=dotnet&logoColor=white)  ![Tests](https://img.shields.io/badge/TESTS-95-brightgreen?style=for-the-badge)

Refer to [LICENSE](LICENSE) for the legal bits.

</div>

## What's in it

- **Requests**: `IRequest<TResponse>` with one `IRequestHandler<TRequest, TResponse>`. Requests with no result use `IRequest` and the `RequestHandler<T>` base class, and return `Unit`. A missing handler throws `HandlerNotFoundException`, and a handler that throws sync still comes back as a faulted task.
- **Notifications**: `INotification` with any number of `INotificationHandler<T>`. Three publishers are included: `SequentialPublisher` (the default, runs every handler even if one fails, then reports all failures), `StopOnFirstFailurePublisher`, and `ParallelPublisher`.
- **Pipeline behaviours**: `IPipelineBehavior<TRequest, TResponse>`, registered open generic (`LoggingBehavior<,>`) or closed. The first one registered is the outermost. Constraints are respected, so a behaviour with `where TRequest : ICacheable` only wraps the requests that qualify. The `next` delegate takes a `CancellationToken`, so a behaviour can hand the handler a different one.
- **Built-in behaviours**: `ValidationBehavior` (collects failures from every `IValidator<T>` and throws `ValidationException`), `LoggingBehavior` (timing, failures and cancellation, sent to an `IRequestLogger` you provide), `RequestProcessorBehavior` (pre and post processors) and `TimeoutBehavior` (for requests that implement `ITimeLimited`, and it works even when the handler ignores its token).
- **A tiny container**: `MediatorBuilder` wraps a `ServiceRegistry` with transient and singleton lifetimes, constructor injection (greediest satisfiable constructor), open generics, factories and `IEnumerable<T>` resolution. It can also scan an assembly for handlers.
- **Or bring your own**: `Mediator` only needs an `IServiceProvider`. Register handlers in whatever container you already use and pass its provider in.
- Handlers can take `IMediator`, `ISender` or `IPublisher` as dependencies, so they can send further requests or publish events.

```csharp
var mediator = new MediatorBuilder()
    .AddHandler<IRequestHandler<Ping, string>, PingHandler>()
    .AddBehavior(typeof(LoggingBehavior<,>))
    .AddSingleton<IRequestLogger>(new DelegateRequestLogger(Console.WriteLine))
    .Build();

string reply = await mediator.Send(new Ping("hello"));
```

## 🏗️ Architecture

```
 caller ──► IMediator.Send(request) ──► Mediator ──► cached wrapper for (TRequest, TResponse)
                                                         │
                          IServiceProvider ◄─────────────┤ resolves handler + IEnumerable<IPipelineBehavior<,>>
                                                         ▼
                          behaviour 1 ─► behaviour 2 ─► ... ─► IRequestHandler<TRequest, TResponse>

 caller ──► IMediator.Publish(note) ──► resolve IEnumerable<INotificationHandler<T>> ──► INotificationPublisher
                                                                   (sequential | stop on first failure | parallel)
```

- **Mediator** keeps a static cache of small generic wrapper objects, one per request type, so the per-call cost is a dictionary lookup, a handler lookup and a chain of delegates, with no reflection after the first call.
- **Pipeline** is built per call by wrapping the handler with behaviours from the last registered to the first, which puts the first registered on the outside.
- **Publishers** decide how notification handlers run. Failures are never swallowed: one failure is rethrown as it was, several are rethrown as an `AggregateException`.
- **ServiceRegistry** keeps an immutable snapshot of its registrations, so resolving is lock-free apart from singleton creation. Open generics are closed on demand, and a closing that breaks a generic constraint just means that registration doesn't apply.
- **MediatorBuilder** is the convenient front door: register by interface, by instance (every handler interface the object implements), or by assembly scan, then `Build()`.

## 🚀 Quick start

```
dotnet build
dotnet test
dotnet run --project samples/Shop
```

The sample places an order through a logging and validation pipeline, publishes an event to two handlers, and then tries a bad order:

```
handling PlaceOrder
  receipt sent for kettle
  stock reduced by 2
handled PlaceOrder in 2 ms
order for 2 x kettle accepted
handling PlaceOrder
failed PlaceOrder after 0 ms: ValidationException: PlaceOrder failed validation: Quantity: must be at least 1
rejected: PlaceOrder failed validation: Quantity: must be at least 1
```

(The millisecond figures are timings, so they will differ on your machine.)

## 🧪 Testing

95 xUnit tests, all passing:

- `RegistryTests` (23): lifetimes, constructor choice, defaults, cycles, constructor exceptions, open generics and constraint skipping, enumerables, and concurrent singleton creation.
- `SendTests` (17): responses, void requests, missing handlers, sync and async exceptions, cancellation, nested sends, last-registration-wins, and 2000 concurrent sends.
- `NotificationTests` (17): ordering, no handlers, failure handling in each publisher, a rendezvous test that only passes when handlers really run in parallel (and one showing the sequential publisher would stall), and the non-generic `Publish`.
- `BuiltInBehaviourTests` (17): validation, logging, processors and timeouts, including a handler that ignores its token.
- `PipelineTests` (11): nesting order, short-circuiting, response changes, token substitution, constrained open generics and per-call instances.
- `BuilderTests` (10): assembly scanning, instance registration, bringing your own `IServiceProvider` and classes that handle several requests.
