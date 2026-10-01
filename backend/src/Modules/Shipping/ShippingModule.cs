using ECommerce.BuildingBlocks.CQRS;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.Modules.Shipping;

public class Shipment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public string Carrier { get; set; } = "GHTK";
    public string Status { get; set; } = "PENDING"; // PENDING, PICKED_UP, IN_TRANSIT, DELIVERED
    public string DestinationAddress { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class ShippingDbContext : DbContext
{
    public ShippingDbContext(DbContextOptions<ShippingDbContext> options) : base(options) { }
    public DbSet<Shipment> Shipments => Set<Shipment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("shipping");

        modelBuilder.Entity<Shipment>(b =>
        {
            b.ToTable("shipments");
            b.HasKey(x => x.Id);
            b.Property(x => x.OrderId).IsRequired();
            b.HasIndex(x => x.OrderId).IsUnique();
            b.Property(x => x.TrackingNumber).HasMaxLength(100).IsRequired();
            b.HasIndex(x => x.TrackingNumber).IsUnique();
        });
    }
}

public record CreateShipmentCommand(Guid OrderId, string Carrier, string DestinationAddress) : ICommand<Shipment>;
public record GetShipmentByOrderQuery(Guid OrderId) : IQuery<Shipment?>;

public class ShippingHandlers :
    ICommandHandler<CreateShipmentCommand, Shipment>,
    IQueryHandler<GetShipmentByOrderQuery, Shipment?>
{
    private readonly ShippingDbContext _db;

    public ShippingHandlers(ShippingDbContext db) => _db = db;

    public async Task<Shipment> Handle(CreateShipmentCommand request, CancellationToken cancellationToken)
    {
        var shipment = new Shipment
        {
            OrderId = request.OrderId,
            Carrier = request.Carrier,
            DestinationAddress = request.DestinationAddress,
            TrackingNumber = $"TRACK-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(100000, 999999)}",
            Status = "PENDING"
        };
        _db.Shipments.Add(shipment);
        await _db.SaveChangesAsync(cancellationToken);
        return shipment;
    }

    public async Task<Shipment?> Handle(GetShipmentByOrderQuery request, CancellationToken cancellationToken)
    {
        return await _db.Shipments.AsNoTracking().FirstOrDefaultAsync(x => x.OrderId == request.OrderId, cancellationToken);
    }
}

public static class ShippingModuleExtensions
{
    public static IServiceCollection AddShippingModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? "Host=localhost;Port=5432;Database=ecommerce_db;Username=postgres;Password=postgres;Pooling=true;";

        services.AddDbContext<ShippingDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "shipping");
            });
        });

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ShippingModuleExtensions).Assembly));
        return services;
    }

    public static IEndpointRouteBuilder MapShippingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/shipping").WithTags("Shipping");

        group.MapPost("/", async (CreateShipmentCommand command, ISender sender) =>
        {
            var shipment = await sender.Send(command);
            return Results.Created($"/api/v1/shipping/{shipment.Id}", shipment);
        }).WithName("CreateShipment");

        group.MapGet("/order/{orderId:guid}", async (Guid orderId, ISender sender) =>
        {
            var shipment = await sender.Send(new GetShipmentByOrderQuery(orderId));
            return shipment != null ? Results.Ok(shipment) : Results.NotFound();
        }).WithName("GetShipmentByOrder");

        return app;
    }
}
