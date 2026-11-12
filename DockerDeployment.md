# Docker Deployment Guide

This document explains how to wire up the CI/CD pipeline so that **every push to
`main` builds a new Docker image, pushes it to your private Docker Hub
repository, and deploys it to your container on Hetzner automatically.**

## How it works

```
git push (main)
   │
   ▼
GitHub Actions: build-and-push job
   │  - builds src/api/Dockerfile
   │  - tags image :latest and :<short-sha>
   │  - pushes to Docker Hub (private repo)
   ▼
GitHub Actions: deploy job
   │  - SSH into the Hetzner server
   │  - docker compose pull api && docker compose up -d api
   ▼
Hetzner container running the new image
```

> **The pipeline deploys the image only.** It does not copy `compose.prod.yaml`, the `Caddyfile` or `.env` to the
> server. After you change `compose.prod.yaml`, copy it yourself (`scp compose.prod.yaml deploy@<host>:/opt/ams-api/`)
> and run `docker compose -f compose.prod.yaml up -d` on the server. Database migrations are different: the API
> applies them itself at startup (`RunMigrationsAtStartup`), so a deploy with a new migration needs no extra step.

Files involved:

| File | Purpose |
|------|---------|
| `.github/workflows/docker-publish.yml` | CI/CD pipeline (build → push → deploy) |
| `src/api/Dockerfile` | Multi-stage build for the API (fixed: now copies all project references + `Directory.Build.props`/`Directory.Packages.props`/`Nuget.Config` before `dotnet restore`) |
| `compose.yaml` | **Local development** — builds the image from source |
| `compose.prod.yaml` | **Hetzner/production** — Caddy (TLS), the API (prebuilt image from Docker Hub) and Postgres |
| `Caddyfile.example` | Template for the server's `Caddyfile`: TLS, reverse proxy to the API, Basic Auth on the docs |
| `compose.db.yaml` | Local-only Postgres/TimescaleDB for dev |

---

## 1. Docker Hub setup (private repository)

1. Log in to [hub.docker.com](https://hub.docker.com).
2. Create a repository named `ams-api` (or any name) and set **Visibility: Private**.
3. Create an **Access Token** (not your account password):
   - Account Settings → Security → **New Access Token**
   - Permissions: **Read & Write**
   - Copy the token — you won't see it again.

You now have two values: your Docker Hub **username** and the **access token**.

---

## 2. GitHub repository secrets

Go to the GitHub repo → **Settings → Secrets and variables → Actions → New repository secret**, and add:

| Secret name | Value |
|-------------|-------|
| `DOCKERHUB_USERNAME` | Your Docker Hub username |
| `DOCKERHUB_TOKEN` | The access token created above |
| `HETZNER_HOST` | IP address or hostname of your Hetzner server |
| `HETZNER_USERNAME` | SSH user on the Hetzner server (e.g. `deploy` or `root`) |
| `HETZNER_SSH_KEY` | **Private** SSH key (PEM format) used to connect — see step 3 |
| `HETZNER_DEPLOY_PATH` | Absolute path on the server containing `compose.prod.yaml`, e.g. `/opt/ams-api` |

> Never put these values directly in the workflow file or commit them — secrets only.

---

## 3. Prepare the Hetzner server

SSH into your Hetzner server once to set it up manually. After this, deployments are automatic.

### 3.1 Install Docker

```bash
curl -fsSL https://get.docker.com | sh
```

### 3.2 Create a deploy user + SSH key pair (recommended over using `root`)

On your **local machine**:

```bash
ssh-keygen -t ed25519 -f ams-deploy-key -C "github-actions-deploy" -N ""
```

This creates `ams-deploy-key` (private) and `ams-deploy-key.pub` (public).

On the **Hetzner server**:

```bash
useradd -m -s /bin/bash deploy
usermod -aG docker deploy
mkdir -p /home/deploy/.ssh
# paste the contents of ams-deploy-key.pub into this file:
nano /home/deploy/.ssh/authorized_keys
chown -R deploy:deploy /home/deploy/.ssh
chmod 700 /home/deploy/.ssh
chmod 600 /home/deploy/.ssh/authorized_keys
```

Back in GitHub, set the secret `HETZNER_SSH_KEY` to the **full contents** of the
private key file (`ams-deploy-key`), and `HETZNER_USERNAME` to `deploy`.

### 3.3 Log the server in to Docker Hub

Since the repository is private, the server needs credentials to `docker compose pull`:

```bash
su - deploy
docker login -u <your-dockerhub-username>
# paste the same access token from step 1 when prompted for the password
```

This stores credentials in `~/.docker/config.json` for the `deploy` user — it's a
one-time step (unless you revoke the token).

### 3.4 Create the deploy directory and environment file

```bash
mkdir -p /opt/ams-api
chown deploy:deploy /opt/ams-api
```

Copy `compose.prod.yaml` from this repo to `/opt/ams-api/compose.prod.yaml` on the server
(e.g. `scp compose.prod.yaml deploy@<host>:/opt/ams-api/`). Repeat this whenever the file changes.

Create `/opt/ams-api/Caddyfile` from `Caddyfile.example` (your domain, and the Basic Auth hash for the docs from
`docker exec -it caddy caddy hash-password --plaintext '<password>'`). The domain's DNS A/AAAA record must point
at the server; Caddy gets and renews the TLS certificate by itself.

Create `/opt/ams-api/.env` on the server (this file is **never committed to git**
and only ever exists on the server) with real secret values:

```bash
# /opt/ams-api/.env
DOCKERHUB_USERNAME=<your-dockerhub-username>
POSTGRES_PASSWORD=<choose-a-strong-password>
DB_CONNECTION_STRING=Server=postgres;Port=5432;Database=appdb;User Id=appuser;Password=<same-as-POSTGRES_PASSWORD>;
JWT_SECRET_KEY=<at-least-32-characters-random-string>
JWT_ISSUER=AmsApi
JWT_AUDIENCE=AmsApiClients
BOOTSTRAP_OWNER_PASSWORD=<choose-a-strong-password-for-the-first-admin-login>
ENTSOE_SECURITY_TOKEN=<your-entso-e-security-token>
```

> `ENTSOE_SECURITY_TOKEN` is the ENTSO-E Transparency Platform token used to fetch day-ahead spot prices.
> `compose.prod.yaml` passes it to the API as `EntsoE__SecurityToken`. Without it the API still starts, but no
> spot prices are fetched and the log says `ENTSO-E security token is not configured (reason: TokenMissing)`.
> Request the token at transparency.entsoe.eu (register, then email transparency@entsoe.eu with the subject
> "Restful API access"; approval takes about 3 business days), then create it under My Account.

> Generate a strong `JWT_SECRET_KEY` with e.g. `openssl rand -base64 48`.

> `BOOTSTRAP_OWNER_PASSWORD` creates the `Bootstrap:OwnerEmail` account (default `roy@sanddata.no`) the first
> time the API starts with no Admin yet, so you can log in without a manual SQL step - see
> [AuthenticationGuide.md](AuthenticationGuide.md#first-admin). It's a no-op once any Admin exists, so it's
> safe to leave in `.env` after the first login.

Set `HETZNER_DEPLOY_PATH` (GitHub secret) to `/opt/ams-api`.

### 3.5 First manual start (optional sanity check)

```bash
cd /opt/ams-api
docker compose -f compose.prod.yaml pull
docker compose -f compose.prod.yaml up -d
docker compose -f compose.prod.yaml ps
```

Visit `https://<your-domain>/scalar/v1` to confirm the API responds. The browser asks for the Basic Auth user
`docs` and the password you hashed into the `Caddyfile`.

> The API container is not published on the host: it only listens on port 8080 inside Docker, and Caddy
> proxies to it. Scalar/OpenAPI (`/scalar/v1`, `/openapi/v1.json`) only serve outside Development when
> `EnableScalarDocs=true` is set on the `api` service (already in `compose.prod.yaml`), and Caddy gates those
> two paths with HTTP Basic Auth - see `Caddyfile.example`. Everything else uses the API's own login
> (`POST /api/auth/login`, JWT) or a sensor key (`X-API-Key`).

---

## 4. Trigger the pipeline

Push to `main` (or merge a PR into it):

```bash
git push origin main
```

Watch progress under the repo's **Actions** tab. On success:
1. A new image is pushed to Docker Hub as `<username>/ams-api:latest` and `<username>/ams-api:<short-sha>`.
2. The `deploy` job SSHes into Hetzner, pulls the new image, and restarts the `api` container with zero manual steps.

You can also trigger it manually from the **Actions** tab via **Run workflow**
(enabled by the `workflow_dispatch` trigger in the workflow file).

---

## 5. Firewall / networking

Only these ports need to be open to the internet (Hetzner firewall and `ufw`):

| Port | Why |
|------|-----|
| 22 | SSH (you, and the deploy job) |
| 80 | Caddy: redirects to HTTPS and answers the certificate challenge |
| 443 | Caddy: the API and the docs |

Do **not** open 8080 (the API) or 5432 (Postgres). The API is only reachable through Caddy, and Postgres is bound
to the server's loopback (`127.0.0.1:5432`, see below). Docker's published ports bypass `ufw`, so keep the
`127.0.0.1` prefix on the Postgres port in `compose.prod.yaml`.

---

## 6. Looking into the production database

Postgres is published on the server's loopback only, so it is reachable through an SSH tunnel and from nowhere else.

**DataGrip** (data source type PostgreSQL):

- **SSH/SSL tab**: tick *Use SSH tunnel*, host = the server, port `22`, your SSH user, authentication *Key pair*.
  Leave the local port dynamic, or use a high one such as `15432`; never `22` (a port below 1024 gives
  "Permission denied").
- **General tab**: host `localhost`, port `5432`, database `appdb`, user `appuser`, password = `POSTGRES_PASSWORD`
  from the server's `.env`. Host and port are resolved on the server, through the tunnel.
- Switch on read-only for the connection, so production data cannot be changed by accident.

**From a terminal** (no GUI): `ssh -N -L 15432:127.0.0.1:5432 <user>@<host>` and then connect a client to
`127.0.0.1:15432`. Or run `docker exec -it postgres psql -U appuser -d appdb` on the server.

If the tunnel is refused with "administratively prohibited", the server's `sshd` has `AllowTcpForwarding no`;
set it to `yes` (or `local`) in `/etc/ssh/sshd_config` and reload `ssh`. If the connection is refused on the server
side, check that the port is published: `ss -tlnp | grep 5432` must show `127.0.0.1:5432`.

---

## Summary of secrets checklist

- [ ] Docker Hub private repo created
- [ ] Docker Hub access token generated
- [ ] GitHub secrets added: `DOCKERHUB_USERNAME`, `DOCKERHUB_TOKEN`, `HETZNER_HOST`, `HETZNER_USERNAME`, `HETZNER_SSH_KEY`, `HETZNER_DEPLOY_PATH`
- [ ] Hetzner server has Docker installed
- [ ] `deploy` user created with SSH key + docker group membership
- [ ] `docker login` run once on Hetzner as the `deploy` user
- [ ] `/opt/ams-api/compose.prod.yaml`, `/opt/ams-api/Caddyfile` and `/opt/ams-api/.env` present on the server
- [ ] DNS for the domain points at the server; ports 22, 80 and 443 open (not 8080 or 5432)
