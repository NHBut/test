-- PostgreSQL Schema Initialization for ECommerce Modular Monolith

CREATE SCHEMA IF NOT EXISTS identity;
CREATE SCHEMA IF NOT EXISTS product;
CREATE SCHEMA IF NOT EXISTS inventory;
CREATE SCHEMA IF NOT EXISTS cart;
CREATE SCHEMA IF NOT EXISTS orders;
CREATE SCHEMA IF NOT EXISTS payment;
CREATE SCHEMA IF NOT EXISTS promotion;
CREATE SCHEMA IF NOT EXISTS shipping;
CREATE SCHEMA IF NOT EXISTS review;
CREATE SCHEMA IF NOT EXISTS notification;
CREATE SCHEMA IF NOT EXISTS common;

-- COMMON SCHEMA TABLES
CREATE TABLE IF NOT EXISTS common.outbox_events (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "AggregateType" VARCHAR(100) NOT NULL,
    "AggregateId" VARCHAR(100) NOT NULL,
    "EventType" VARCHAR(150) NOT NULL,
    "Payload" JSONB NOT NULL,
    "Status" VARCHAR(30) NOT NULL DEFAULT 'PENDING',
    "RetryCount" INT NOT NULL DEFAULT 0,
    "ErrorMessage" TEXT,
    "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "ProcessedAt" TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS idx_outbox_pending_poll ON common.outbox_events ("CreatedAt" ASC) WHERE "Status" = 'PENDING';

CREATE TABLE IF NOT EXISTS common.idempotency_keys (
    "Key" VARCHAR(255) PRIMARY KEY,
    "UserId" UUID,
    "Endpoint" VARCHAR(255) NOT NULL,
    "RequestHash" VARCHAR(64) NOT NULL,
    "Status" VARCHAR(30) NOT NULL,
    "ResponseCode" INT,
    "ResponseBody" TEXT,
    "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "ExpiresAt" TIMESTAMPTZ NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_idempotency_expiry ON common.idempotency_keys ("ExpiresAt");

CREATE TABLE IF NOT EXISTS common.processed_events (
    "EventId" VARCHAR(150) NOT NULL,
    "ConsumerGroup" VARCHAR(100) NOT NULL,
    "ProcessedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY ("EventId", "ConsumerGroup")
);

-- IDENTITY SCHEMA TABLES
CREATE TABLE IF NOT EXISTS identity.users (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "Email" VARCHAR(255) NOT NULL UNIQUE,
    "PasswordHash" VARCHAR(255) NOT NULL,
    "FullName" VARCHAR(150) NOT NULL,
    "PhoneNumber" VARCHAR(30),
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "IsEmailVerified" BOOLEAN NOT NULL DEFAULT FALSE,
    "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS identity.roles (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "Name" VARCHAR(50) NOT NULL UNIQUE,
    "Description" VARCHAR(255)
);

CREATE TABLE IF NOT EXISTS identity.user_roles (
    "UserId" UUID NOT NULL REFERENCES identity.users("Id") ON DELETE CASCADE,
    "RoleId" UUID NOT NULL REFERENCES identity.roles("Id") ON DELETE CASCADE,
    "AssignedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY ("UserId", "RoleId")
);

CREATE TABLE IF NOT EXISTS identity.refresh_tokens (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "UserId" UUID NOT NULL REFERENCES identity.users("Id") ON DELETE CASCADE,
    "TokenHash" VARCHAR(255) NOT NULL UNIQUE,
    "ExpiresAt" TIMESTAMPTZ NOT NULL,
    "IsRevoked" BOOLEAN NOT NULL DEFAULT FALSE,
    "ReplacedByTokenHash" VARCHAR(255),
    "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- PRODUCT SCHEMA TABLES
CREATE TABLE IF NOT EXISTS product.categories (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "ParentId" UUID,
    "Name" VARCHAR(150) NOT NULL,
    "Slug" VARCHAR(200) NOT NULL UNIQUE,
    "Description" TEXT,
    "DisplayOrder" INT NOT NULL DEFAULT 0,
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS product.products (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "Name" VARCHAR(255) NOT NULL,
    "Slug" VARCHAR(300) NOT NULL UNIQUE,
    "Brand" VARCHAR(100),
    "Description" TEXT,
    "CategoryId" UUID,
    "IsPublished" BOOLEAN NOT NULL DEFAULT TRUE,
    "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS product.product_variants (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "ProductId" UUID NOT NULL REFERENCES product.products("Id") ON DELETE CASCADE,
    "Sku" VARCHAR(100) NOT NULL UNIQUE,
    "VariantName" VARCHAR(150) NOT NULL,
    "Price" NUMERIC(15, 2) NOT NULL CHECK ("Price" >= 0),
    "CompareAtPrice" NUMERIC(15, 2),
    "AttributesJson" JSONB NOT NULL DEFAULT '{}'::jsonb,
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- INVENTORY SCHEMA TABLES
CREATE TABLE IF NOT EXISTS inventory.product_inventories (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "SkuId" UUID NOT NULL UNIQUE,
    "AvailableQuantity" INT NOT NULL DEFAULT 0 CHECK ("AvailableQuantity" >= 0),
    "ReservedQuantity" INT NOT NULL DEFAULT 0 CHECK ("ReservedQuantity" >= 0),
    "Version" INT NOT NULL DEFAULT 0,
    "UpdatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS inventory.inventory_reservations (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "OrderId" UUID NOT NULL,
    "SkuId" UUID NOT NULL,
    "Quantity" INT NOT NULL CHECK ("Quantity" > 0),
    "Status" VARCHAR(30) NOT NULL,
    "ExpiresAt" TIMESTAMPTZ NOT NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_order_sku UNIQUE ("OrderId", "SkuId")
);
CREATE INDEX IF NOT EXISTS idx_reservations_pending_expired ON inventory.inventory_reservations ("ExpiresAt") WHERE "Status" = 'PENDING';

-- ORDERS SCHEMA TABLES
CREATE TABLE IF NOT EXISTS orders.orders (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "OrderNumber" VARCHAR(50) NOT NULL UNIQUE,
    "CustomerId" UUID NOT NULL,
    "Status" VARCHAR(30) NOT NULL,
    "Subtotal" NUMERIC(15, 2) NOT NULL CHECK ("Subtotal" >= 0),
    "DiscountAmount" NUMERIC(15, 2) NOT NULL DEFAULT 0,
    "ShippingFee" NUMERIC(15, 2) NOT NULL DEFAULT 0,
    "TotalAmount" NUMERIC(15, 2) NOT NULL CHECK ("TotalAmount" >= 0),
    "ShippingAddressJson" JSONB NOT NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_orders_customer_created ON orders.orders ("CustomerId", "CreatedAt" DESC);

CREATE TABLE IF NOT EXISTS orders.order_items (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "OrderId" UUID NOT NULL REFERENCES orders.orders("Id") ON DELETE CASCADE,
    "SkuId" UUID NOT NULL,
    "ProductName" VARCHAR(255) NOT NULL,
    "VariantName" VARCHAR(150) NOT NULL,
    "Sku" VARCHAR(100) NOT NULL,
    "UnitPrice" NUMERIC(15, 2) NOT NULL,
    "Quantity" INT NOT NULL,
    "TotalPrice" NUMERIC(15, 2) NOT NULL
);

CREATE TABLE IF NOT EXISTS orders.order_status_history (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "OrderId" UUID NOT NULL REFERENCES orders.orders("Id") ON DELETE CASCADE,
    "FromStatus" VARCHAR(30),
    "ToStatus" VARCHAR(30) NOT NULL,
    "Reason" TEXT,
    "ChangedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- PAYMENT SCHEMA TABLES
CREATE TABLE IF NOT EXISTS payment.payments (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "OrderId" UUID NOT NULL UNIQUE,
    "Amount" NUMERIC(15, 2) NOT NULL CHECK ("Amount" > 0),
    "Currency" VARCHAR(10) NOT NULL DEFAULT 'VND',
    "PaymentMethod" VARCHAR(50) NOT NULL,
    "Status" VARCHAR(30) NOT NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS payment.payment_transactions (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "PaymentId" UUID NOT NULL REFERENCES payment.payments("Id") ON DELETE CASCADE,
    "Gateway" VARCHAR(50) NOT NULL,
    "TransactionCode" VARCHAR(100) NOT NULL,
    "Type" VARCHAR(30) NOT NULL,
    "Amount" NUMERIC(15, 2) NOT NULL,
    "Status" VARCHAR(30) NOT NULL,
    "RawResponseJson" JSONB,
    "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_gateway_tx UNIQUE ("Gateway", "TransactionCode")
);

-- PROMOTION SCHEMA TABLES
CREATE TABLE IF NOT EXISTS promotion.coupons (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "Code" VARCHAR(50) NOT NULL UNIQUE,
    "DiscountType" VARCHAR(30) NOT NULL,
    "DiscountValue" NUMERIC(15, 2) NOT NULL,
    "MinOrderValue" NUMERIC(15, 2) NOT NULL DEFAULT 0,
    "MaxDiscountAmount" NUMERIC(15, 2),
    "UsageLimit" INT NOT NULL DEFAULT 1000,
    "UsageCount" INT NOT NULL DEFAULT 0,
    "ExpiresAt" TIMESTAMPTZ NOT NULL,
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE
);

-- SHIPPING SCHEMA TABLES
CREATE TABLE IF NOT EXISTS shipping.shipments (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "OrderId" UUID NOT NULL UNIQUE,
    "TrackingNumber" VARCHAR(100) NOT NULL UNIQUE,
    "Carrier" VARCHAR(50) NOT NULL DEFAULT 'GHTK',
    "Status" VARCHAR(30) NOT NULL DEFAULT 'PENDING',
    "DestinationAddress" TEXT NOT NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- REVIEW SCHEMA TABLES
CREATE TABLE IF NOT EXISTS review.reviews (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "ProductId" UUID NOT NULL,
    "UserId" UUID NOT NULL,
    "OrderId" UUID NOT NULL,
    "Rating" INT NOT NULL CHECK ("Rating" BETWEEN 1 AND 5),
    "Comment" TEXT,
    "VerifiedPurchase" BOOLEAN NOT NULL DEFAULT TRUE,
    "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_user_order_product UNIQUE ("UserId", "OrderId", "ProductId")
);

-- NOTIFICATION SCHEMA TABLES
CREATE TABLE IF NOT EXISTS notification.notifications (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "UserId" UUID NOT NULL,
    "Channel" VARCHAR(30) NOT NULL DEFAULT 'EMAIL',
    "Title" VARCHAR(255) NOT NULL,
    "Content" TEXT NOT NULL,
    "Status" VARCHAR(30) NOT NULL DEFAULT 'SENT',
    "SentAt" TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
