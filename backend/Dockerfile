# Multi-Stage Production Dockerfile for ECommerce Modular Monolith
# Stage 1: Build & Restore
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy solution và csproj files trước để tận dụng Docker Layer Cache
COPY ECommerce.slnx ./
COPY src/ECommerce.BuildingBlocks/ECommerce.BuildingBlocks.csproj src/ECommerce.BuildingBlocks/
COPY src/ECommerce.Api/ECommerce.Api.csproj src/ECommerce.Api/
COPY src/Modules/Identity/ECommerce.Modules.Identity.csproj src/Modules/Identity/
COPY src/Modules/Product/ECommerce.Modules.Product.csproj src/Modules/Product/
COPY src/Modules/Inventory/ECommerce.Modules.Inventory.csproj src/Modules/Inventory/
COPY src/Modules/Cart/ECommerce.Modules.Cart.csproj src/Modules/Cart/
COPY src/Modules/Order/ECommerce.Modules.Order.csproj src/Modules/Order/
COPY src/Modules/Payment/ECommerce.Modules.Payment.csproj src/Modules/Payment/
COPY src/Modules/Promotion/ECommerce.Modules.Promotion.csproj src/Modules/Promotion/
COPY src/Modules/Shipping/ECommerce.Modules.Shipping.csproj src/Modules/Shipping/
COPY src/Modules/Review/ECommerce.Modules.Review.csproj src/Modules/Review/
COPY src/Modules/Notification/ECommerce.Modules.Notification.csproj src/Modules/Notification/
COPY src/Modules/Search/ECommerce.Modules.Search.csproj src/Modules/Search/
COPY tests/UnitTests/ECommerce.UnitTests.csproj tests/UnitTests/
COPY tests/ArchitectureTests/ECommerce.ArchitectureTests.csproj tests/ArchitectureTests/
COPY tests/IntegrationTests/ECommerce.IntegrationTests.csproj tests/IntegrationTests/

RUN dotnet restore src/ECommerce.Api/ECommerce.Api.csproj

# Copy toàn bộ mã nguồn
COPY . .

# Publish ứng dụng ở chế độ Release
WORKDIR /app/src/ECommerce.Api
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime Image (Minimal Alpine / Chiseled)
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS runtime
WORKDIR /app

# Cài đặt curl để phục vụ Healthcheck
RUN apk add --no-cache curl

# Chạy dưới người dùng phi đặc quyền (non-root) app:app
USER app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "ECommerce.Api.dll"]
