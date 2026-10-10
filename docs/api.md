# API inventory

All HTTP routes are under `/api/v1`. Failures use sanitized Problem Details. Enums are serialized by stable names, list page sizes are bounded, and all administration routes require a staff MFA claim plus the endpoint-specific permission.

## Public and authentication

| Area | Routes | Flutter consumer |
| --- | --- | --- |
| App bootstrap | `GET /app/configuration` | Splash, maintenance, and provider availability state |
| Authentication | `POST /auth/register`, `/login`, `/mfa/login`, `/refresh`, `/logout`, `/logout-all` | Registration, login/MFA, session restoration, logout |
| Account recovery | `POST /auth/forgot-password`, `/reset-password`, `/verify-email`, `/resend-verification`, `/change-password` | Verification and security forms |
| Sessions and MFA | `GET /auth/sessions`, `DELETE /auth/sessions/{id}`, `POST /auth/mfa/enroll`, `/mfa/confirm` | Active devices, revocation, authenticator enrollment |
| Discovery | `GET /catalog/home`, `/categories`, `/brands`, `/options` | Home, categories, brands, catalog filters |
| Products | `GET /catalog/products`, `/products/{id}`, `/products/{id}/related`, `/products/{id}/reviews` | Search/filter/sort, product detail, variants, related products, reviews |

## Authenticated customer commerce

| Area | Routes | Enforcement |
| --- | --- | --- |
| Profile | `GET/PUT /customer/profile` | Current authenticated user only |
| Addresses | `GET/POST /customer/addresses`, `PUT/DELETE /customer/addresses/{id}` | Ownership in every mutation |
| Favorites | `GET /customer/favorites`, `POST/DELETE /customer/favorites/{productId}` | Current authenticated user only |
| Notifications | `GET /customer/notifications`, `POST /notifications/{id}/read`, `/notifications/read-all` | Ownership and paginated retrieval |
| Cart | `GET /cart`, `POST /cart/items`, `PUT/DELETE /cart/items/{id}`, `POST /cart/quote` | Server-side variant, stock, coupon, and total validation |
| Orders | `POST/GET /orders`, `GET /orders/{id}`, `POST /orders/{id}/cancel` | Ownership, idempotency, valid cancellation states, inventory restoration |
| Reviews | `PUT /catalog/products/{id}/reviews/mine` | Delivered verified purchase and moderation reset on edit |
| Returns | `POST /returns/orders/{id}`, `GET /returns`, `GET /returns/{id}`, `POST /returns/{id}/cancel` | Ownership, return window, quantities, and state transitions |
| Payments | Provider start/webhook routes under `/payments` | Backend status and signed replay-protected webhook are authoritative; no client success flag |

## Administration

| Area | Routes | Permission |
| --- | --- | --- |
| Dashboard/reports | `GET /admin/dashboard`, `/admin/reports` | `Reports.View` |
| Products | `GET/POST /admin/catalog/products`, `GET/PUT/DELETE /admin/catalog/products/{id}` | `Products.View/Create/Edit/Delete` |
| Variants | `POST /admin/catalog/products/{productId}/variants`, `PUT/DELETE .../variants/{id}` | Product create/edit/delete permissions |
| Reference data | Admin category, brand, color, and size list/create/update routes | Product view/create/edit permissions |
| Product media | Upload plus `GET /admin/media/products/{id}/images`, reorder, and delete | Staff MFA and product edit permission |
| Inventory | `GET /admin/inventory`, adjustment, and movement history routes | `Inventory.View/Adjust` |
| Orders | Admin list/detail/status/cancel routes | `Orders.View/UpdateStatus/Cancel` |
| Customers | `GET /admin/customers`, `/admin/customers/{id}` | `Customers.View` |
| Coupons | List/create/update and usage routes | `Coupons.Manage` |
| Reviews | Moderation queue and decision routes | `Products.Edit` |
| Returns | Queue/detail/status workflow routes | `Orders.View/UpdateStatus` |
| Staff access | Users, details, roles, permissions, active state, and session revocation under `/admin/access` | `Users.Manage/Roles.Manage` |
| Security | `GET /admin/audit`, `/admin/security-events` | `AuditLogs.View` |
| Notifications | Admin history/send routes | `Notifications.Manage` |
| Settings | `GET/PUT /admin/settings` | `Settings.Manage` |

The Flutter repositories and screens consume each operational group above. Presentation-only constants are limited to routes, enum labels, design tokens, and localization keys; business values come from these APIs.
