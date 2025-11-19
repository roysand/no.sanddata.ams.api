# Database Migrations Guide

This document contains all the commands needed to manage Entity Framework Core migrations for the AMS API project.

## Prerequisites

### Install EF Core Tools

Before running any migration commands, ensure you have the EF Core tools installed globally:

```bash
# Check if dotnet-ef is installed
dotnet tool list -g

# If not installed, install it
dotnet tool install --global dotnet-ef

# Update to latest version (if already installed)
dotnet tool update --global dotnet-ef
```

## Project Structure

- **DbContext Location**: `Src/Infrastructure/Database/ApplicationDbContext.cs`
- **Migrations Folder**: `Src/Infrastructure/Database/Migrations/`
- **Startup Project**: `Src/Api/Api.csproj`
- **Infrastructure Project**: `Src/Infrastructure/Infrastructure.csproj`

## Common Migration Commands

### 1. Create Initial Migration

Creates the first migration based on your current entity models:

```bash
# Run from solution root directory
dotnet ef migrations add InitialCreate \
  --project Src/Infrastructure/Infrastructure.csproj \
  --startup-project Src/Api/Api.csproj \
  --output-dir Database/Migrations
```

**What this does:**
- Creates migration files in `Src/Infrastructure/Database/Migrations/`
- Generates `{timestamp}_InitialCreate.cs` (migration file)
- Generates `{timestamp}_InitialCreate.Designer.cs` (designer metadata)
- Creates `ApplicationDbContextModelSnapshot.cs` (current model snapshot)

### 2. Add New Migration

After making changes to your entities, create a new migration:

```bash
dotnet ef migrations add YourMigrationName \
  --project Src/Infrastructure/Infrastructure.csproj \
  --startup-project Src/Api/Api.csproj \
  --output-dir Database/Migrations
```

**Example migration names:**
- `AddUserEmailIndex`
- `UpdateLocationSchema`
- `AddApiKeyExpirationField`

### 3. Apply Migrations to Database

Apply all pending migrations to your database:

```bash
dotnet ef database update \
  --project Src/Infrastructure/Infrastructure.csproj \
  --startup-project Src/Api/Api.csproj
```

### 4. Apply Specific Migration

Roll back or forward to a specific migration:

```bash
# Roll back to a specific migration
dotnet ef database update YourMigrationName \
  --project Src/Infrastructure/Infrastructure.csproj \
  --startup-project Src/Api/Api.csproj

# Roll back all migrations (empty database)
dotnet ef database update 0 \
  --project Src/Infrastructure/Infrastructure.csproj \
  --startup-project Src/Api/Api.csproj
```

### 5. List All Migrations

View all migrations and their status:

```bash
dotnet ef migrations list \
  --project Src/Infrastructure/Infrastructure.csproj \
  --startup-project Src/Api/Api.csproj
```

### 6. Remove Last Migration

Remove the most recent migration (only if not applied to database):

```bash
dotnet ef migrations remove \
  --project Src/Infrastructure/Infrastructure.csproj \
  --startup-project Src/Api/Api.csproj
```

**Warning:** Only use this if the migration hasn't been applied to any database yet!

### 7. Generate SQL Script

Generate SQL script for migrations without applying them:

```bash
# Generate script for all migrations
dotnet ef migrations script \
  --project Src/Infrastructure/Infrastructure.csproj \
  --startup-project Src/Api/Api.csproj \
  --output migration.sql

# Generate script from one migration to another
dotnet ef migrations script FromMigration ToMigration \
  --project Src/Infrastructure/Infrastructure.csproj \
  --startup-project Src/Api/Api.csproj \
  --output migration.sql

# Generate idempotent script (safe to run multiple times)
dotnet ef migrations script \
  --project Src/Infrastructure/Infrastructure.csproj \
  --startup-project Src/Api/Api.csproj \
  --idempotent \
  --output migration.sql
```

### 8. Drop Database

Drop the database (use with caution!):

```bash
dotnet ef database drop \
  --project Src/Infrastructure/Infrastructure.csproj \
  --startup-project Src/Api/Api.csproj \
  --force
```

## Using Package Manager Console (Visual Studio)

If you're using Visual Studio, you can use these shorter commands in the Package Manager Console:

```powershell
# Set default project to Infrastructure
Default Project: Infrastructure

# Add migration
Add-Migration InitialCreate -OutputDir Database/Migrations

# Update database
Update-Database

# List migrations
Get-Migration

# Remove last migration
Remove-Migration

# Generate SQL script
Script-Migration

# Drop database
Drop-Database
```

## Auto-Apply Migrations on Startup (Optional)

To automatically apply pending migrations when the application starts, add this code to `Program.cs`:

```csharp
// After: var app = builder.Build();

// Apply pending migrations automatically
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    dbContext.Database.Migrate();
}
```

**Pros:**
- Convenient for development
- Ensures database is always up-to-date

**Cons:**
- Not recommended for production (use explicit migration scripts instead)
- Can cause issues with multiple instances starting simultaneously

## Connection String Configuration

Migrations use the connection string from your configuration. Ensure you have one of these configured:

1. **appsettings.json**: `ConnectionStrings:DefaultConnection`
2. **local.settings.json**: `ApplicationSettings:DbConnectionString`
3. **Environment Variable**: `ApplicationSettings:DbConnectionString`

## Troubleshooting

### Error: "No DbContext was found"

**Solution:** Ensure you're running commands from the solution root and specifying both projects correctly.

### Error: "Build failed"

**Solution:** Build the solution first:
```bash
dotnet build
```

### Error: "Unable to create an object of type 'ApplicationDbContext'"

**Solution:** Ensure your connection string is properly configured in `local.settings.json` or `appsettings.json`.

### Error: "The migration has already been applied to the database"

**Solution:** Use `dotnet ef migrations remove` only works for unapplied migrations. For applied migrations, create a new migration to revert changes.

## Best Practices

1. **Naming Conventions**: Use descriptive migration names (e.g., `AddUserEmailIndex`, not `Migration1`)
2. **Review Generated Code**: Always review migration files before applying
3. **Version Control**: Commit migration files to source control
4. **Production Deployments**: Use SQL scripts (`dotnet ef migrations script`) for production
5. **Backup First**: Always backup production databases before applying migrations
6. **Test Migrations**: Test migrations in a staging environment first
7. **Idempotent Scripts**: Use `--idempotent` flag for production scripts

## Quick Reference

```bash
# Most common workflow
dotnet ef migrations add MigrationName --project Src/Infrastructure/Infrastructure.csproj --startup-project Src/Api/Api.csproj --output-dir Database/Migrations
dotnet ef database update --project Src/Infrastructure/Infrastructure.csproj --startup-project Src/Api/Api.csproj

# Short aliases (create these in your shell profile)
alias efm='dotnet ef migrations add $1 --project Src/Infrastructure/Infrastructure.csproj --startup-project Src/Api/Api.csproj --output-dir Database/Migrations'
alias efu='dotnet ef database update --project Src/Infrastructure/Infrastructure.csproj --startup-project Src/Api/Api.csproj'
alias efl='dotnet ef migrations list --project Src/Infrastructure/Infrastructure.csproj --startup-project Src/Api/Api.csproj'
```

## Additional Resources

- [EF Core Migrations Documentation](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/)
- [EF Core CLI Reference](https://learn.microsoft.com/en-us/ef/core/cli/dotnet)
- [Migration Best Practices](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing)
