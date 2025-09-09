using Parlour;

var mediator = new MediatorBuilder()
    .AddSingleton<IRequestLogger>(new DelegateRequestLogger(Console.WriteLine))
    .AddHandler<IRequestHandler<PlaceOrder, string>, PlaceOrderHandler>()
    .AddHandler<INotificationHandler<OrderPlaced>, SendReceipt>()
    .AddHandler<INotificationHandler<OrderPlaced>, UpdateStock>()
    .AddHandler<IValidator<PlaceOrder>, OrderValidator>()
    .AddBehavior(typeof(LoggingBehavior<,>))
    .AddBehavior(typeof(ValidationBehavior<,>))
    .Build();

Console.WriteLine(await mediator.Send(new PlaceOrder("kettle", 2)));

try
{
    await mediator.Send(new PlaceOrder("kettle", 0));
}
catch (ValidationException e)
{
    Console.WriteLine("rejected: " + e.Message);
}

public record PlaceOrder(string Item, int Quantity) : IRequest<string>;

public record OrderPlaced(string Item, int Quantity) : INotification;

public sealed class OrderValidator : IValidator<PlaceOrder>
{
    public IEnumerable<ValidationFailure> Validate(PlaceOrder request)
    {
        if (request.Quantity < 1)
        {
            yield return new ValidationFailure("Quantity", "must be at least 1");
        }
    }
}

public sealed class PlaceOrderHandler : IRequestHandler<PlaceOrder, string>
{
    private readonly IPublisher publisher;

    public PlaceOrderHandler(IPublisher publisher)
    {
        this.publisher = publisher;
    }

    public async Task<string> Handle(PlaceOrder request, CancellationToken cancellationToken)
    {
        await publisher.Publish(new OrderPlaced(request.Item, request.Quantity), cancellationToken);
        return $"order for {request.Quantity} x {request.Item} accepted";
    }
}

public sealed class SendReceipt : INotificationHandler<OrderPlaced>
{
    public Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"  receipt sent for {notification.Item}");
        return Task.CompletedTask;
    }
}

public sealed class UpdateStock : INotificationHandler<OrderPlaced>
{
    public Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"  stock reduced by {notification.Quantity}");
        return Task.CompletedTask;
    }
}
