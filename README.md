# MyBackendTemplate

Production-oriented .NET 9 backend template using Clean Architecture, with built-in authentication flows, Redis-backed idempotency, Hangfire background jobs, structured logging, and full integration testing with real infrastructure.

## Features

- Internal authentication with JWT access/refresh tokens
- External authentication with Google OAuth
- Email flows for registration, device confirmation, resend confirmation, and password reset
- Background email dispatch with Hangfire + PostgreSQL storage
- Redis-backed idempotency filter for sensitive operations
- Guest session support and guest-to-user promotion
- Rate limiting and request-size protections
- Structured logging with Serilog and optional Seq
- Integration tests using xUnit + Testcontainers + MailHog + Respawn

## Architecture

The solution follows Clean Architecture with strict dependency flow:

```text
API -> Application -> Domain
         ^
         |
  Infrastructure (implements application interfaces)
```

Projects:

- `Src/API`: Controllers, middleware, filters, startup, HTTP contracts
- `Src/Application`: Use cases, service interfaces/implementations, DTOs, validators
- `Src/Domain`: Entities, enums, domain exceptions, core business rules
- `Src/Infrastructure`: EF Core, repositories, persistence, external service adapters
- `Tests`: Integration and stress testing infrastructure

## Quick Start

### 1. Prerequisites

- .NET SDK 9
- Docker + Docker Compose

### 2. Configure environment

Copy the environment template:

```bash
cp .env.example .env
```

The provided `.env.example` contains development defaults that work with `docker-compose.yml`.

### 3. Start the stack

```bash
docker compose up -d --build
```

### 4. Verify containers are healthy

```bash
docker compose ps
```

### 5. Useful local endpoints

- API health: `http://localhost/health`
- Swagger (Development): `http://localhost/api-docs`
- Hangfire dashboard: `http://localhost/hangfire`
- Seq UI: `http://localhost:5341`

Note: In this setup, the API runs inside Docker and is fronted by Nginx on port 80/443.

## Local API Run (Optional)

By default, this repository is wired for Docker-first startup.

If you want to run the API process directly on your host machine, expose Redis and RabbitMQ ports from compose first (or run those dependencies locally), then run the API from `Src/API` so `.env` is resolved correctly by `Env.Load("../../.env")`:

```bash
cd Src/API
dotnet run
```

## Running Tests

Run all tests:

```bash
dotnet test Tests/MyBackendTemplate.Tests.csproj
```

Run a specific test class or file via filter:

```bash
dotnet test Tests/MyBackendTemplate.Tests.csproj --filter "FullyQualifiedName~ForgetPasswordSuccessTests"
```

## Environment Variables

See `.env.example` for the complete list. Key groups:

- API/runtime: `ASPNETCORE_ENVIRONMENT`, `API_PORT`
- PostgreSQL: `POSTGRES_*`, `CONNECTION_STRING`
- Redis: `REDIS_CONNECTION_STRING`
- RabbitMQ: `RABBITMQ_HOST`, `RABBITMQ_PORT`, `RABBITMQ_USERNAME`, `RABBITMQ_PASSWORD`
- JWT: `JWT_KEY`, `JWT_ISSUER`, `JWT_AUDIENCE`, `JWT_DURATION_IN_MINUTES`
- Email: `EMAIL_HOST`, `EMAIL_PORT`, `EMAIL_USERNAME`, `EMAIL_PASSWORD`, `EMAIL_FROM`, `EMAIL_ENABLE_SSL`
- External auth: `Google_ClientId`, `Google_ClientSecret`

## Repository Layout

```text
Src/
  API/
  Application/
  Domain/
  Infrastructure/
Tests/
docs/
  adr/
```

## Documentation

- `DOCKER_SETUP.md`: Docker and local infrastructure guide
- `TECHNOLOGIES.md`: Technology and pattern reference
- `docs/README.md`: Documentation index
- `docs/adr/README.md`: ADR process and index
- `docs/adr/001-backend-architecture-and-technology-stack.md`: Architecture decision record

## Production Notes

- Protect or disable `/hangfire` in production.
- Replace permissive CORS policy used for local/testing scenarios.
- Rotate and securely store all secrets; do not commit `.env`.
- Review container hardening, TLS certs, and network exposure before deployment.
