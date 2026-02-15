# Docker Setup Guide

This project includes Docker support with PostgreSQL and ASP.NET Core API containers.

## Quick Start

1. **Ensure you're in the project root directory:**
   ```bash
   cd "./Backend Template"
   ```

2. **Start the containers:**
   ```bash
   docker-compose up -d
   ```

   This will:
   - Build the .NET API Docker image
   - Start a PostgreSQL 16 database container
   - Start the ASP.NET Core API container
   - All configured via the `.env` file

3. **Verify services are running:**
   ```bash
   docker-compose ps
   ```

4. **View logs:**
   ```bash
   docker-compose logs -f api      # API logs
   docker-compose logs -f postgres # Database logs
   ```

5. **Stop the containers:**
   ```bash
   docker-compose down
   ```

6. **Remove volumes (delete database):**
   ```bash
   docker-compose down -v
   ```

## Configuration

All environment variables are defined in the `.env` file at the project root:

- `ASPNETCORE_ENVIRONMENT`: Set to `Development` or `Production`
- `API_PORT`: API port mapping (default: 5000 → 8080 in container)
- `DB_USER`: PostgreSQL username
- `DB_PASSWORD`: PostgreSQL password
- `DB_NAME`: Database name
- `DB_PORT`: PostgreSQL port mapping (default: 5432)
- `CONNECTION_STRING`: Full connection string for the API

## Files

- **`Dockerfile`**: Multi-stage build for the .NET API
  - Build stage: Restores dependencies and compiles
  - Publish stage: Publishes the application
  - Runtime stage: Runs the application with ASP.NET Core runtime

- **`docker-compose.yml`**: Orchestrates API and PostgreSQL services
  - Includes health checks
  - Manages volume persistence
  - Sets environment variables from `.env`

- **`.env`**: Environment configuration file
  - Edit this file to change database credentials, ports, etc.
  - Not committed to git (added to `.gitignore`)

- **`.dockerignore`**: Excludes unnecessary files from Docker build context

## Troubleshooting

**Connection refused when API starts:**
- PostgreSQL health check may still be running
- Docker Compose waits for `postgres` service to be healthy before starting `api`
- Check logs: `docker-compose logs postgres`

**Port already in use:**
- Change port mappings in `.env`:
  ```
  API_PORT=5001     # Instead of 5000
  DB_PORT=5433      # Instead of 5432
  ```

**Rebuild the API image:**
```bash
docker-compose up --build
```

**Access the database from terminal:**
```bash
docker exec -it mybackendtemplate-db psql -U postgres -d mybackendtemplate_db
```

## Development vs Production

- Change `ASPNETCORE_ENVIRONMENT` in `.env` to `Production` for production builds
- In Production, the API will skip OpenAPI/Swagger endpoints
