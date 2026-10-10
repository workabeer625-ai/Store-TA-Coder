# MARINA | مارينا

MARINA is a bilingual family-fashion commerce platform built with ASP.NET Core 8, SQL Server, and Flutter.

## Workspace

- `backend/Marina.sln` — the .NET solution.
- `backend/Marina.Domain` — entities and domain enums without infrastructure dependencies.
- `backend/Marina.Application` — use cases and ports, including authoritative checkout, cart quotes, reviews, and returns.
- `backend/Marina.Persistence` — Identity, EF Core, SQL Server configuration, migrations, and guarded development seed data.
- `backend/Marina.Infrastructure` — JWT and external-service boundary implementations.
- `backend/Marina.Api` — versioned HTTP endpoints, permission policies, rate limiting, and error middleware.
- `backend/Marina.UnitTests`, `Marina.IntegrationTests`, and `Marina.SecurityTests` — risk-focused automated tests.
- `frontend/marina_app` — the Flutter customer application and responsive administration interface.

## Run locally

```powershell
cd backend/Marina.Api
dotnet user-secrets set "Jwt:Key" "a-local-key-with-at-least-32-random-characters"
dotnet user-secrets set "Bootstrap:SuperAdminEmail" "admin@marina.local"
dotnet user-secrets set "Bootstrap:SuperAdminPassword" "generate-a-unique-strong-development-password"
dotnet run
```

Development applies SQL Server migrations and idempotent catalog seed data when the development settings enable them. SuperAdmin credentials remain in User Secrets; they are never stored in source. For controlled environments, disable `Database:ApplyMigrations` and apply reviewed migrations as a separate deployment step.

```powershell
cd frontend/marina_app
flutter pub get
flutter run --dart-define=API_URL=https://your-api/api/v1
```

## Quality gates

```powershell
dotnet restore backend/Marina.sln
dotnet build backend/Marina.sln --configuration Release
dotnet test backend/Marina.sln --configuration Release
cd frontend/marina_app
dart format --output=none --set-exit-if-changed lib test
flutter analyze
flutter test
flutter build web --release
```

The development environment uses guarded local media storage. COD is operational; online payment, SMTP/SMS/push, and production object storage remain explicit provider boundaries and never simulate successful delivery or payment. Read [operations](docs/operations.md), [API inventory](docs/api.md), and the [final gap analysis](docs/final-gap-analysis.md) before deployment.
