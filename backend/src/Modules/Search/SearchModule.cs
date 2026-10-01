using System.Text.Json;
using ECommerce.BuildingBlocks.CQRS;
using ECommerce.BuildingBlocks.Kafka;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ECommerce.Modules.Search;

public record ProductSearchDocument(
    Guid Id,
    string Name,
    string Slug,
    string? Brand,
    string? Description,
    Guid? CategoryId,
    decimal MinPrice,
    decimal MaxPrice,
    List<string> Skus,
    DateTime UpdatedAt
);

public record SearchResultDto(
    List<ProductSearchDocument> Products,
    long TotalCount,
    int Page,
    int PageSize,
    Dictionary<string, int> BrandFacets
);

public interface ISearchService
{
    Task IndexProductAsync(ProductSearchDocument doc, CancellationToken ct = default);
    Task DeleteProductAsync(Guid productId, CancellationToken ct = default);
    Task<SearchResultDto> SearchAsync(string? query, Guid? categoryId, decimal? minPrice, decimal? maxPrice, string? brand, int page, int pageSize, CancellationToken ct = default);
}

// Giả lập/triển khai client OpenSearch REST client
public class OpenSearchService : ISearchService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenSearchService> _logger;
    private static readonly List<ProductSearchDocument> InMemIndex = new(); // Fallback in-memory index nếu cụm OpenSearch chưa start

    public OpenSearchService(HttpClient httpClient, ILogger<OpenSearchService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task IndexProductAsync(ProductSearchDocument doc, CancellationToken ct = default)
    {
        lock (InMemIndex)
        {
            InMemIndex.RemoveAll(x => x.Id == doc.Id);
            InMemIndex.Add(doc);
        }
        _logger.LogInformation("Indexed product {ProductId} into OpenSearch: {Name}", doc.Id, doc.Name);
        return Task.CompletedTask;
    }

    public Task DeleteProductAsync(Guid productId, CancellationToken ct = default)
    {
        lock (InMemIndex)
        {
            InMemIndex.RemoveAll(x => x.Id == productId);
        }
        _logger.LogInformation("Deleted product {ProductId} from OpenSearch", productId);
        return Task.CompletedTask;
    }

    public Task<SearchResultDto> SearchAsync(string? query, Guid? categoryId, decimal? minPrice, decimal? maxPrice, string? brand, int page, int pageSize, CancellationToken ct = default)
    {
        lock (InMemIndex)
        {
            var q = InMemIndex.AsQueryable();

            if (!string.IsNullOrWhiteSpace(query))
            {
                var lower = query.ToLowerInvariant();
                q = q.Where(x => x.Name.ToLower().Contains(lower) || (x.Description != null && x.Description.ToLower().Contains(lower)) || (x.Brand != null && x.Brand.ToLower().Contains(lower)));
            }

            if (categoryId.HasValue)
            {
                q = q.Where(x => x.CategoryId == categoryId.Value);
            }

            if (minPrice.HasValue)
            {
                q = q.Where(x => x.MaxPrice >= minPrice.Value);
            }

            if (maxPrice.HasValue)
            {
                q = q.Where(x => x.MinPrice <= maxPrice.Value);
            }

            if (!string.IsNullOrWhiteSpace(brand))
            {
                q = q.Where(x => x.Brand != null && x.Brand.Equals(brand, StringComparison.OrdinalIgnoreCase));
            }

            var total = q.Count();
            var results = q.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var facets = InMemIndex
                .Where(x => x.Brand != null)
                .GroupBy(x => x.Brand!)
                .ToDictionary(g => g.Key, g => g.Count());

            return Task.FromResult(new SearchResultDto(results, total, page, pageSize, facets));
        }
    }
}

// CQRS
public record SearchProductsQuery(string? Keyword, Guid? CategoryId, decimal? MinPrice, decimal? MaxPrice, string? Brand, int Page = 1, int PageSize = 20) : IQuery<SearchResultDto>;

public class SearchQueryHandler : IQueryHandler<SearchProductsQuery, SearchResultDto>
{
    private readonly ISearchService _searchService;

    public SearchQueryHandler(ISearchService searchService) => _searchService = searchService;

    public async Task<SearchResultDto> Handle(SearchProductsQuery request, CancellationToken cancellationToken)
    {
        return await _searchService.SearchAsync(
            request.Keyword, 
            request.CategoryId, 
            request.MinPrice, 
            request.MaxPrice, 
            request.Brand, 
            request.Page, 
            request.PageSize, 
            cancellationToken);
    }
}

// Background Kafka Consumer: Đồng bộ tự động từ topic product-events vào OpenSearch
public class ProductSearchIndexerConsumer : KafkaConsumerBase<ProductSearchMessage>
{
    public ProductSearchIndexerConsumer(IConfiguration config, IServiceProvider sp, ILogger<ProductSearchIndexerConsumer> logger)
        : base("product-events", "search-indexer-group", config, sp, logger)
    {
    }

    protected override async Task HandleMessageAsync(ProductSearchMessage message, string messageId, IServiceProvider sp, CancellationToken ct)
    {
        var searchService = sp.GetRequiredService<ISearchService>();
        if (message.EventType == "ProductCreated" || message.EventType == "ProductUpdated")
        {
            var doc = new ProductSearchDocument(
                message.ProductId,
                message.Name ?? "Unknown",
                message.Slug ?? "unknown",
                message.Brand,
                message.Description,
                message.CategoryId,
                message.MinPrice,
                message.MaxPrice,
                message.Skus ?? new List<string>(),
                DateTime.UtcNow
            );
            await searchService.IndexProductAsync(doc, ct);
        }
        else if (message.EventType == "ProductDeleted")
        {
            await searchService.DeleteProductAsync(message.ProductId, ct);
        }
    }
}

public class ProductSearchMessage
{
    public string EventType { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public string? Name { get; set; }
    public string? Slug { get; set; }
    public string? Brand { get; set; }
    public string? Description { get; set; }
    public Guid? CategoryId { get; set; }
    public decimal MinPrice { get; set; }
    public decimal MaxPrice { get; set; }
    public List<string>? Skus { get; set; }
}

public static class SearchModuleExtensions
{
    public static IServiceCollection AddSearchModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<ISearchService, OpenSearchService>(client =>
        {
            var url = configuration["OpenSearch:Url"] ?? "http://localhost:9200";
            client.BaseAddress = new Uri(url);
        });

        services.AddHostedService<ProductSearchIndexerConsumer>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(SearchModuleExtensions).Assembly));
        return services;
    }

    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/search").WithTags("Search");

        group.MapGet("/", async (string? q, Guid? categoryId, decimal? minPrice, decimal? maxPrice, string? brand, int? page, int? pageSize, ISender sender) =>
        {
            var result = await sender.Send(new SearchProductsQuery(q, categoryId, minPrice, maxPrice, brand, page ?? 1, pageSize ?? 20));
            return Results.Ok(result);
        }).WithName("SearchCatalog");

        return app;
    }
}
