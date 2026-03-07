# Docker Setup Guide

This guide explains how to run the full local stack with Docker Compose.

## Services

`docker-compose.yml` provisions the following containers:

- `postgres` (PostgreSQL 16)
- `redis` (Redis 7)
- `rabbitmq` (RabbitMQ)
- `api` (.NET API image built from `Src/API/Dockerfile`)
- `nginx` (reverse proxy and static hosting for OAuth test pages)
- `seq` (log viewer)

## Quick Start

1. Copy env template:

```bash
cp .env.example .env
```

2. Start the stack:

```bash
docker compose up -d --build
```

3. Verify status:

```bash
docker compose ps
```

4. Check logs when needed:

```bash
docker compose logs -f api
docker compose logs -f postgres
docker compose logs -f nginx
```

5. Confirm API is reachable:

```bash
curl -i http://localhost/health
```

6. Stop services:

```bash
docker compose down
```

7. Stop and remove volumes (destructive):

```bash
docker compose down -v
```

## Access Points

- API via Nginx: `http://localhost`
- Swagger (Development): `http://localhost/api-docs`
- Hangfire dashboard: `http://localhost/hangfire`
- Seq UI: `http://localhost:5341`

## Environment Configuration

All runtime values come from `.env`.

Important keys:

- `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB`, `POSTGRES_PORT`
- `CONNECTION_STRING`
- `REDIS_CONNECTION_STRING`
- `RABBITMQ_HOST`, `RABBITMQ_PORT`, `RABBITMQ_USERNAME`, `RABBITMQ_PASSWORD`
- `JWT_KEY`, `JWT_ISSUER`, `JWT_AUDIENCE`, `JWT_DURATION_IN_MINUTES`
- `EMAIL_HOST`, `EMAIL_PORT`, `EMAIL_USERNAME`, `EMAIL_PASSWORD`, `EMAIL_FROM`, `EMAIL_ENABLE_SSL`
- `SEQ_URL`

The repository ships with working development defaults in `.env.example`. For local startup, copy it as-is first, then adjust secrets/credentials if needed.

Local MailHog usage:

- Set `EMAIL_ENABLE_SSL=false` when SMTP target is MailHog.

## Common Commands

Rebuild API image:

```bash
docker compose up -d --build api
```

Open a shell inside API container:

```bash
docker compose exec api sh
```

Open psql inside Postgres container:

```bash
docker compose exec postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB"
```

## Troubleshooting

API does not start:

- Check env vars first (`CONNECTION_STRING`, JWT keys, Redis/RabbitMQ values).
- Check container logs: `docker compose logs -f api`.

Port conflicts:

- Change host-side ports in `.env` (for PostgreSQL) or `docker-compose.yml` (for exposed services).

Nginx returns 502:

- API container may be unhealthy or restarting.
- Confirm `api` container status and logs.

SMTP errors in local environment:

- Verify `EMAIL_HOST`, `EMAIL_PORT`, and `EMAIL_ENABLE_SSL=false` for MailHog.

## Production Guidance

- Do not expose internal service ports unless required.
- Configure TLS certificates in `Nginx/ssl`.
- Restrict access to `/hangfire` and `/api-docs`.
- Store secrets in a secure secret manager instead of plain `.env` files.
