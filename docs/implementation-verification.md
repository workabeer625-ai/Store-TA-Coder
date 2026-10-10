# MARINA implementation verification

This is a running change/verification ledger, not a production sign-off.

## Baseline preservation

- Workspace has no `.git` directory; no reset or checkout was performed.
- Source backup: `C:\Users\hp\AppData\Local\Temp\MarinaBeforeFix-d519013d5ce44b7bbf48243bf0e2151b`.
- Existing development API on port 5199 was left running. Diagnostic instance used 5298, Development, migrations disabled.
- SQL runtime tests create an individually named `MarinaTest_<guid>` database, apply migrations, and delete only that exact owned database.

## P0

### Swagger

- Actual exception: schema ID collision between `AdminReferenceController.CategoryRequest` and `AdminCatalogController.CategoryRequest`.
- Changed: `backend/Marina.Api/Program.cs` (qualified schema identifiers).
- Verified: `/swagger/v1/swagger.json` HTTP 200, OpenAPI 3.0.1; UI HTML HTTP 200 on diagnostic Kestrel instance.
- Browser visual check pending: browser tool reports no connected browsers.

### Add to cart

- SQL regression reproduced HTTP 409 / DbUpdateConcurrencyException when adding a new item to an existing cart.
- Cause: application-assigned domain GUIDs were treated as generated keys by EF; tracked navigation additions could become UPDATE instead of INSERT.
- Changed: `MarinaDbContext.OnModelCreating` uses ValueGeneratedNever for domain entity IDs.
- Test: `SqlRuntimeTests.Swagger_and_cart_add_persist_and_reload_on_SQL_Server` checks POST, GET, SQL row, repeat add, delete/re-add.
- Confirmed server runtime fix; Flutter live rendering still requires verification.

## P1 changes

- Explicit mutation DTOs: `Contracts/MutationRequests.cs`, `GlobalUsings.cs`, Auth/Cart/Orders/AdminUsers/AdminOperations controllers and corresponding Flutter repositories/screens.
- Cart clear endpoint and UI; quantity DTO `{quantity}`; server reload after checkout.
- Typed enum and pagination readers: `core/network/api_models.dart`, admin screens.
- Session validation: `Services/SessionTokenValidator.cs`, Program; checks live user/session/roles/permissions.
- Password reset revokes sessions; refresh rotation uses atomic consume within a transaction.
- Account boundary creates a fresh ProviderScope: `main.dart`, `api_client.dart`; routing permits public/authentication flows.
- Guest product/favorite actions no longer load protected favorite data unconditionally.
- SQL tests verify quantity validation, ownership, clear, logout rejection, disabled-user rejection, password-reset rejection.
- Latest completed full backend test run: 25 unit + 7 security + 12 integration = 44 passed (before subsequent P2 work).
- Flutter analyze passed after initial P1 changes; rerun required after subsequent work.

## Final verification

- Checkout creates pending COD payment and removes purchased cart lines in its SQL transaction.
- Configurable SMTP sender and Development-only private pickup directory; no credentials added.
- Email token link handling, password validation alignment.
- Transactions for default addresses and image reorder; unified availability checks underway.
- Backend Release build and full test suite pass: 25 unit, 7 security, and 14 SQL integration tests.
- Flutter analysis passes and all 54 Flutter tests pass.
- `flutter build web --release` succeeds and produces `frontend/marina_app/build/web`.
- Android APK build reached Gradle packaging but is blocked by unavailable `storage.googleapis.com` access while downloading Flutter native artifacts; this is an environment/network limitation.
- Browser visual E2E was not run because no connected in-app browser was available.
- Migration `20260907145843_RuntimeIntegrity` was applied successfully by isolated SQL runtime tests. Apply it to the target deployment database through the normal release migration process.
