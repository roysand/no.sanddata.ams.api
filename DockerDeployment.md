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
   │  - docker compose pull && docker compose up -d
   ▼
Hetzner container running the new image
```

Files involved:

| File | Purpose |
|------|---------|
| `.github/workflows/docker-publish.yml` | CI/CD pipeline (build → push → deploy) |
| `src/api/Dockerfile` | Multi-stage build for the API (fixed: now copies all project references + `Directory.Build.props`/`Directory.Packages.props`/`Nuget.Config` before `dotnet restore`) |
| `compose.yaml` | **Local development** — builds the image from source |
| `compose.prod.yaml` | **Hetzner/production** — pulls the prebuilt image from Docker Hub |
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
(e.g. `scp compose.prod.yaml deploy@<host>:/opt/ams-api/`).

Create `/opt/ams-api/.env` on the server (this file is **never committed to git**
and only ever exists on the server) with real secret values:

```bash
# /opt/ams-api/.env
DOCKERHUB_USERNAME=<your-dockerhub-username>
POSTGRES_PASSWORD=<choose-a-strong-password>
DB_CONNECTION_STRING=Server=db;Port=5432;Database=amsdb;User Id=amsadmin;Password=<same-as-above>;
JWT_SECRET_KEY=<at-least-32-characters-random-string>
JWT_ISSUER=AmsApi
JWT_AUDIENCE=AmsApiClients
BOOTSTRAP_OWNER_PASSWORD=<choose-a-strong-password-for-the-first-admin-login>
```

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

Visit `http://<hetzner-ip>:8080/scalar/v1` to confirm the API responds.

> Scalar/OpenAPI (`/scalar/v1`, `/openapi/v1.json`) only serve outside Development when
> `EnableScalarDocs=true` is set on the `api` service (already in `compose.prod.yaml`). In normal use
> they're only reachable through Caddy, which gates those two paths with HTTP Basic Auth - see
> `Caddyfile.example`. Hitting the container directly on `:8080` like the line above bypasses that
> gate entirely, so only do it from a trusted network for a quick sanity check.

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

## 5. Firewall / networking reminder

Make sure port `8080` (or whichever you expose) is allowed through the Hetzner
firewall/Cloud Firewall rules, and consider putting a reverse proxy (e.g. Caddy
or Nginx) in front for TLS — not covered by this guide.

---

## Summary of secrets checklist

- [ ] Docker Hub private repo created
- [ ] Docker Hub access token generated
- [ ] GitHub secrets added: `DOCKERHUB_USERNAME`, `DOCKERHUB_TOKEN`, `HETZNER_HOST`, `HETZNER_USERNAME`, `HETZNER_SSH_KEY`, `HETZNER_DEPLOY_PATH`
- [ ] Hetzner server has Docker installed
- [ ] `deploy` user created with SSH key + docker group membership
- [ ] `docker login` run once on Hetzner as the `deploy` user
- [ ] `/opt/ams-api/compose.prod.yaml` and `/opt/ams-api/.env` present on the server
