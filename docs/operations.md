# Setup, security, and deployment

## Configuration

Required production values are `ConnectionStrings:DefaultConnection`, `Jwt:Key`, `Jwt:Issuer`, `Jwt:Audience`, and `Cors:AllowedOrigins`. Put secrets in environment variables or a managed secret store. For local development use .NET User Secrets. The JWT signing key must contain at least 32 high-entropy characters.

Use an encrypted SQL Server connection and a dedicated least-privilege login. Do not use `sa` or a sysadmin account. The migration is under `backend/Marina.Persistence/Migrations`.

```powershell
dotnet ef database update --project backend/Marina.Persistence --startup-project backend/Marina.Api
```

## Deployment checklist

1. Back up the database and prove the restore process.
2. Apply reviewed migrations as an independent controlled step.
3. Inject secrets; never place them in images, source, logs, or deployment manifests.
4. Restrict CORS to exact trusted origins and terminate TLS at a trusted edge.
5. Keep HSTS and security headers enabled and restrict `/health` at infrastructure level.
6. Use a stable multi-instance signing-key strategy and rotate keys through a documented ceremony.
7. Centralize structured logs and alert on token reuse, repeated lockouts, permission changes, inventory adjustments, refunds, and cancellations.
8. Configure backups, retention, point-in-time recovery, and quarterly restore exercises.
9. Complete a provider-specific payment/webhook threat model before accepting money.
10. Run build, unit, integration, security, Flutter analyze, and Flutter test gates.

Never log passwords, reset/verification codes, JWTs, refresh tokens, OTP/TOTP secrets, recovery codes, card data, provider secrets, or connection-string passwords.

## Current external blockers

A real payment integration requires the selected provider, merchant account, webhook secret, supported Saudi payment methods, and refund policy. Email/SMS verification requires providers and sender identities. Production media upload requires object storage plus signature/MIME/dimension checks and malware scanning; the local development implementation is not registered outside Development. These external calls are deliberately not fabricated.
