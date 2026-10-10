# Architecture

## Dependency direction

```text
Marina.Application  -> Marina.Domain
Marina.Infrastructure -> Marina.Application + Marina.Domain
Marina.Persistence  -> Marina.Application + Marina.Domain
Marina.Api          -> Application + Infrastructure + Persistence
```

`Marina.Domain` has no EF Core, Identity, ASP.NET, or provider dependency. Application services operate through explicit ports. `CheckoutService` validates the command, rejects duplicate variant lines, loads authoritative product/price/inventory data, validates the owned address and coupon, snapshots order data, records stock movements, and saves inside a serializable transaction. `PostPurchaseService` enforces verified-purchase reviews, return eligibility, transition rules, quantity limits, and one-time inventory restoration. The client cannot submit price, discount, tax, shipping, total, payment state, inventory quantity, or another user ID.

## Data model

Catalog supports hierarchical translated categories, translated brands/products/colors/sizes, images, and size/color inventory-bearing variants. Inventory uses a rowversion concurrency token, non-negative database checks, reservations, thresholds, and traceable movement records.

Commerce includes addresses, favorites, carts, coupons/usages, orders/items/status history, notifications, reviews, return requests/items/history, payments, and provider transaction replay keys. Order and return numbers are separate from internal IDs and line items preserve historical snapshots. Return requests and inventory use rowversion concurrency control. Review and return ownership is protected by foreign keys to Identity users.

Security includes Identity users/roles, granular permissions, role mappings, device sessions, hashed refresh tokens, login attempts, security events, and audit logs. Sensitive material is never placed in audit JSON.

## Flutter

The app uses feature-oriented folders, Riverpod state, `go_router`, Dio, `flutter_secure_storage`, centralized bilingual strings, and a shared theme extracted from the approved visual direction. Customer and administration routes are separate shells. The administration shell changes from a drawer to a fixed sidebar at 900px; its navigation is scrollable and permission-aware, so a long catalog of admin modules cannot overflow short viewports and unauthorized actions are not presented.

API repositories are the data source for catalog, product variants, authentication, server-side cart quotes, customer profile, addresses, favorites, notifications, order history/details, verified reviews, returns, and administration. Cart identity, quantities, availability, coupon validation, and totals are server-authoritative; the client reloads the cart after every mutation. Notification deep links are allowlisted in the client, while every destination still enforces ownership and authorization on the server. Common loading, empty, network failure, image failure, and retry states are reusable widgets.

Administrative dashboard metrics, catalog and reference management, product images and variants, inventory and movements, orders, customers, coupons, reports, staff access, audit/security events, application settings, customer notifications, review moderation, and return processing load from protected APIs. Staff sign-in requires MFA; a first-time staff account receives an authenticator setup key only after its password has been verified, then receives a normal token pair only after validating the TOTP.

## External boundaries

`IPaymentProvider` intentionally exposes payment creation and webhook validation without pretending a provider is configured. Development provides path-contained local media storage only. Production email/SMS, object storage, malware scanning, CDN, payment, tax, and shipping providers require explicit implementations and credentials.
