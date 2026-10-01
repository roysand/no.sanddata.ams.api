# Credentials Setup Guide (Beginner-Friendly)

This guide walks you through creating **every username, password, and key**
needed for the CI/CD pipeline described in `DockerDeployment.md`. Follow it
top to bottom — by the end you'll have everything to paste into GitHub.

You will create **4 things** in total:
1. A Docker Hub **access token** (acts like a password for GitHub Actions)
2. An **SSH key pair** (so GitHub can log into your Hetzner server)
3. A few **application secrets** (database password, JWT signing key)
4. **GitHub repository secrets** (where all of the above get stored safely)

---

## 1. Docker Hub Access Token

Your normal Docker Hub password should **never** be used in automation. Instead
you create a limited-purpose "access token" that can be revoked at any time
without changing your real password.

1. Go to https://hub.docker.com and log in.
2. Click your avatar (top-right) → **Account Settings**.
3. In the left menu, click **Personal access tokens** (previously under *Security*).
4. Click **Generate new token**.
   - **Description**: `github-actions-ams-api` (anything you'll recognize later)
   - **Access permissions**: `Read & Write`
   - Click **Generate**.
5. **Copy the token immediately** — Docker Hub only shows it once. Paste it
   somewhere temporary (a notes file you'll delete later), e.g.:
   ```
   DOCKERHUB_USERNAME = <your docker hub username, e.g. "johndoe">
   DOCKERHUB_TOKEN     = dckr_pat_xxxxxxxxxxxxxxxxxxxxxxxxxxxx
   ```

If you ever suspect the token leaked, come back to this page and click
**Delete** next to it, then generate a new one and update the GitHub secret.

---

## 2. SSH Key Pair (GitHub → Hetzner login)

This key pair lets the GitHub Actions workflow log into your Hetzner server
automatically, without a password. You generate it **once on your own
computer**, keep the private half secret, and put the public half on the
server.

### 2.1 Generate the key pair

Open a terminal (PowerShell is fine) on your own machine and run:

```powershell
ssh-keygen -t ed25519 -f "$HOME\ams-deploy-key" -C "github-actions-deploy" -N '""'
```

This creates two files in your home folder:
- `ams-deploy-key` — the **private key**. Never share this, never commit it to git.
- `ams-deploy-key.pub` — the **public key**. Safe to share; this goes on the server.

> If `ssh-keygen` isn't recognized, install the optional Windows "OpenSSH Client"
> feature (Settings → Apps → Optional Features), or use Git Bash which includes it.

### 2.2 Install the public key on the Hetzner server

Connect to your server (replace `your-server-ip`; ask your hosting panel for
the root password/IP if you don't have it yet):

```powershell
ssh root@your-server-ip
```

Once logged in **on the server**, create a dedicated low-privilege user for
deployments (safer than using `root` for automation):

```bash
useradd -m -s /bin/bash deploy
usermod -aG docker deploy
mkdir -p /home/deploy/.ssh
chmod 700 /home/deploy/.ssh
```

Now add your public key to that user. Easiest way: open `ams-deploy-key.pub`
in Notepad on your computer, copy its single line of text, then on the server:

```bash
nano /home/deploy/.ssh/authorized_keys
```

Paste the line, save (`Ctrl+O`, `Enter`, `Ctrl+X`), then fix permissions:

```bash
chown -R deploy:deploy /home/deploy/.ssh
chmod 600 /home/deploy/.ssh/authorized_keys
```

### 2.3 Test the key works

From your own computer:

```powershell
ssh -i "$HOME\ams-deploy-key" deploy@your-server-ip
```

You should log in **without being asked for a password**. If that works, the
key is set up correctly. Type `exit` to disconnect.

### 2.4 Values you now have

```
HETZNER_HOST       = your-server-ip (or hostname)
HETZNER_USERNAME   = deploy
HETZNER_SSH_KEY    = <the full contents of the ams-deploy-key file (private key)>
HETZNER_DEPLOY_PATH = /opt/ams-api
```

To view the private key's contents to copy into GitHub later:

```powershell
Get-Content "$HOME\ams-deploy-key"
```

Copy **everything**, including the `-----BEGIN OPENSSH PRIVATE KEY-----` and
`-----END OPENSSH PRIVATE KEY-----` lines.

---

## 3. Application Secrets (database password & JWT key)

These protect your database and the tokens your API issues. You choose these
yourself — just make them long, random, and unique.

### 3.1 Generate a strong database password

PowerShell:

```powershell
-join ((48..57)+(65..90)+(97..122) | Get-Random -Count 32 | ForEach-Object {[char]$_})
```

Copy the output. This is your `POSTGRES_PASSWORD`.

### 3.2 Generate a JWT signing key (must be 32+ characters)

```powershell
-join ((48..57)+(65..90)+(97..122) | Get-Random -Count 48 | ForEach-Object {[char]$_})
```

This is your `JWT_SECRET_KEY`.

### 3.3 Values you now have

```
POSTGRES_PASSWORD    = <random string from 3.1>
JWT_SECRET_KEY        = <random string from 3.2>
DB_CONNECTION_STRING  = Server=db;Port=5432;Database=amsdb;User Id=amsadmin;Password=<same as POSTGRES_PASSWORD>;
JWT_ISSUER            = AmsApi
JWT_AUDIENCE          = AmsApiClients
```

> These four (`POSTGRES_PASSWORD`, `DB_CONNECTION_STRING`, `JWT_SECRET_KEY`,
> `JWT_ISSUER`, `JWT_AUDIENCE`) are **not** GitHub secrets — they live only in
> the `.env` file *on the Hetzner server* (see `DockerDeployment.md` section 3.4).
> `DOCKERHUB_USERNAME` is used in both places.

---

## 4. Add the GitHub Repository Secrets

Only these 6 values go into **GitHub**:

1. Open your repo on GitHub.
2. Go to **Settings → Secrets and variables → Actions**.
3. Click **New repository secret** for each row below, entering the **Name**
   exactly as shown and the value you collected above:

| Name | Value comes from |
|------|------------------|
| `DOCKERHUB_USERNAME` | Step 1 |
| `DOCKERHUB_TOKEN` | Step 1 |
| `HETZNER_HOST` | Step 2.4 |
| `HETZNER_USERNAME` | Step 2.4 (`deploy`) |
| `HETZNER_SSH_KEY` | Step 2.4 (private key contents) |
| `HETZNER_DEPLOY_PATH` | Step 2.4 (`/opt/ams-api`) |

Click **Add secret** after each one. They become write-only — you (and the
workflow) can use them, but nobody can view their values again in the UI.

---

## 5. Clean up

- Delete any temporary notes file where you pasted the token/passwords.
- Keep `ams-deploy-key` (private key) somewhere safe on your computer in case
  you need to re-add the GitHub secret later (e.g. a password manager or
  encrypted folder) — do **not** commit it to git.

---

## Quick checklist

- [ ] Docker Hub access token created and copied
- [ ] SSH key pair generated (`ams-deploy-key` / `ams-deploy-key.pub`)
- [ ] `deploy` user created on Hetzner with public key installed
- [ ] Passwordless `ssh deploy@your-server-ip` confirmed working
- [ ] Database password and JWT key generated
- [ ] All 6 GitHub repository secrets added
- [ ] `.env` file with DB/JWT secrets created on the server (see `DockerDeployment.md`)

Once all boxes are checked, you're ready to push to `main` and watch the
pipeline run under the **Actions** tab.
