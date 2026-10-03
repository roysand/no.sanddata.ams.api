using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddRolesAndSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "public",
                table: "Role",
                columns: new[] { "Id", "CreatedAt", "Description", "IsActive", "Name", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("a0000000-0000-0000-0000-000000000001"), new DateTime(2026, 10, 3, 0, 0, 0, 0, DateTimeKind.Utc), "Manages users, roles and location links", true, "Admin", new DateTime(2026, 10, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("a0000000-0000-0000-0000-000000000002"), new DateTime(2026, 10, 3, 0, 0, 0, 0, DateTimeKind.Utc), "Uses own account and own locations", true, "User", new DateTime(2026, 10, 3, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            // Every existing user keeps working as an ordinary user once the endpoints are locked down.
            // Idempotent: only users that hold no role yet get the "User" role.
            migrationBuilder.Sql(@"
                INSERT INTO ""UserRole"" (""UserId"", ""RoleId"", ""AssignedAt"")
                SELECT u.""Id"", 'a0000000-0000-0000-0000-000000000002'::uuid, now()
                FROM ""User"" u
                WHERE NOT EXISTS (SELECT 1 FROM ""UserRole"" ur WHERE ur.""UserId"" = u.""Id"");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""UserRole""
                WHERE ""RoleId"" IN ('a0000000-0000-0000-0000-000000000001'::uuid, 'a0000000-0000-0000-0000-000000000002'::uuid);");

            migrationBuilder.DeleteData(
                schema: "public",
                table: "Role",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                schema: "public",
                table: "Role",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000002"));
        }
    }
}
