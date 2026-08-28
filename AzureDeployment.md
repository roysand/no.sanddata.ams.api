# Azure Deployment Guide

This document contains all the Azure CLI commands needed to provision the Azure infrastructure that hosts the AMS API. Commands are added incrementally as each piece of infrastructure is introduced — right now that's the resource group and the PostgreSQL (TimescaleDB) database. Hosting for the API itself (App Service / Container Apps) will be appended here later.

**Environments:** `dev` runs locally via Docker (see below) — no Azure cost, no need to provision/stop anything. `test` and `prod` are the two environments actually provisioned in Azure with the commands in this doc.

## Local Development (Docker)

Run Postgres with TimescaleDB locally instead of provisioning a `dev` server in Azure:

```bash
docker run -d \
  --name ams-postgres-dev \
  -e POSTGRES_USER=amsadmin \
  -e POSTGRES_PASSWORD=devpassword \
  -e POSTGRES_DB=amsdb \
  -p 5432:5432 \
  timescale/timescaledb:latest-pg16
```

Enable the extension once, inside the running container:

```bash
docker exec -it ams-postgres-dev psql -U amsadmin -d amsdb -c "CREATE EXTENSION IF NOT EXISTS timescaledb;"
```

Connection string for `local.settings.json` → `ApplicationSettings:DbConnectionString`:

```
Host=localhost;Database=amsdb;Username=amsadmin;Password=devpassword;
```

Stop/start it like any container: `docker stop ams-postgres-dev` / `docker start ams-postgres-dev`. Data persists in the container's writable layer across stop/start, but is lost if the container is removed — add a volume mount (`-v ams-postgres-dev-data:/var/lib/postgresql/data`) if you want it to survive a `docker rm`.

## Prerequisites

```bash
# Install/update the Azure CLI: https://learn.microsoft.com/en-us/cli/azure/install-azure-cli

# Log in
az login

# Confirm/select the target subscription
az account show --output table
az account set --subscription "<subscription-id-or-name>"
```

## Naming & Environment Variables

All commands below use these variables. Set them once per shell session (adjust `ENV` per environment — `test` or `prod`; `dev` is local Docker, not provisioned in Azure — see [Local Development](#local-development-docker)).

```bash
export ENV="test"
export LOCATION="norwayeast"
export RESOURCE_GROUP="rg-ams-api-${ENV}"
export PG_SERVER_NAME="psql-ams-api-${ENV}"   # must be globally unique across Azure
export PG_DATABASE_NAME="amsdb"
export PG_ADMIN_USER="amsadmin"
```

Generate the admin password once and keep it out of shell history / source control (store it in a password manager or Key Vault — see [Notes](#notes)):

```bash
export PG_ADMIN_PASSWORD="$(openssl rand -base64 24)"
```

## 1. Resource Group

```bash
az group create \
  --name "$RESOURCE_GROUP" \
  --location "$LOCATION"
```

## 2. PostgreSQL (Flexible Server) with TimescaleDB

Azure Database for PostgreSQL **Flexible Server** is required here — it's the offering that supports allow-listing the `TIMESCALEDB` extension (Single Server does not).

### 2.1 Create the Flexible Server

```bash
az postgres flexible-server create \
  --resource-group "$RESOURCE_GROUP" \
  --name "$PG_SERVER_NAME" \
  --location "$LOCATION" \
  --admin-user "$PG_ADMIN_USER" \
  --admin-password "$PG_ADMIN_PASSWORD" \
  --sku-name Standard_B1ms \
  --tier Burstable \
  --storage-size 32 \
  --version 16 \
  --high-availability Disabled \
  --public-access None \
  --yes
```

**Notes:**
- `Standard_B1ms` / `Burstable` / 32 GB is a low-cost tier suitable for `test` — bump `--sku-name`/`--tier`/`--storage-size` for `prod` (e.g. `Standard_D2ds_v5` / `GeneralPurpose`).
- `--public-access None` provisions no firewall rule; see [2.4](#24-firewall-rules) to allow access.
- If the API will run inside Azure with VNet integration, prefer `--vnet`/`--subnet` (private access) over public firewall rules for `prod`.

### 2.2 Allow-list and enable the TimescaleDB extension

Allow-listing makes the extension installable; it still needs `CREATE EXTENSION` to be run per database.

```bash
az postgres flexible-server parameter set \
  --resource-group "$RESOURCE_GROUP" \
  --server-name "$PG_SERVER_NAME" \
  --name azure.extensions \
  --value TIMESCALEDB
```

### 2.3 Create the application database

```bash
az postgres flexible-server db create \
  --resource-group "$RESOURCE_GROUP" \
  --server-name "$PG_SERVER_NAME" \
  --database-name "$PG_DATABASE_NAME"
```

Then connect and enable the extension inside the database (`psql`, or any PostgreSQL client):

```bash
az postgres flexible-server connect \
  --name "$PG_SERVER_NAME" \
  --admin-user "$PG_ADMIN_USER" \
  --admin-password "$PG_ADMIN_PASSWORD" \
  --database-name "$PG_DATABASE_NAME" \
  --querytext "CREATE EXTENSION IF NOT EXISTS timescaledb;"
```

### 2.4 Firewall rules

Allow Azure services (App Service/Container Apps) to reach the server:

```bash
az postgres flexible-server firewall-rule create \
  --resource-group "$RESOURCE_GROUP" \
  --name "$PG_SERVER_NAME" \
  --rule-name AllowAzureServices \
  --start-ip-address 0.0.0.0 \
  --end-ip-address 0.0.0.0
```

To connect directly from your machine (e.g. to inspect `test` data with a Postgres client), allow your own IP instead of opening the server to the internet:

```bash
MY_IP="$(curl -s https://ifconfig.me)"
az postgres flexible-server firewall-rule create \
  --resource-group "$RESOURCE_GROUP" \
  --name "$PG_SERVER_NAME" \
  --rule-name AllowMyIp \
  --start-ip-address "$MY_IP" \
  --end-ip-address "$MY_IP"
```

### 2.5 Retrieve the connection string

```bash
az postgres flexible-server show-connection-string \
  --server-name "$PG_SERVER_NAME" \
  --database-name "$PG_DATABASE_NAME" \
  --admin-user "$PG_ADMIN_USER" \
  --admin-password "$PG_ADMIN_PASSWORD" \
  --query connectionStrings.psql_cs \
  --output tsv
```

Translate this into an Npgsql connection string for `ApplicationSettings:DbConnectionString` (see [CLAUDE.md](CLAUDE.md#configuration)) once the EF Core provider switches to PostgreSQL, e.g.:

```
Host=<PG_SERVER_NAME>.postgres.database.azure.com;Database=<PG_DATABASE_NAME>;Username=<PG_ADMIN_USER>;Password=<PG_ADMIN_PASSWORD>;Ssl Mode=Require;
```

## Cost management (test)

Stop the `test` Postgres server when you're not using it — you're not billed for compute while stopped (storage is still billed, but that's marginal at 32 GB).

```bash
az postgres flexible-server stop \
  --resource-group "$RESOURCE_GROUP" \
  --name "$PG_SERVER_NAME"
```

```bash
az postgres flexible-server start \
  --resource-group "$RESOURCE_GROUP" \
  --name "$PG_SERVER_NAME"
```

Azure auto-restarts a stopped server after 7 days (it can't stay stopped indefinitely) — if you're not touching `test` for longer than that, just re-run `stop` when you notice it's back up.

## Notes

- **Secrets:** Don't commit `PG_ADMIN_PASSWORD` or the resulting connection string anywhere in this repo. For test/prod, store them in Azure Key Vault and reference via a managed identity from the API's hosting environment instead of an env var.
- **Idempotency:** All `create` commands above are safe to re-run against an existing resource of the same name (Azure CLI updates in place / no-ops rather than failing), except where noted.

## Coming next

- App hosting (App Service or Container Apps) for `src/api`
- Container registry + image push/deploy commands
- Key Vault for secrets (JWT signing key, DB connection string)
