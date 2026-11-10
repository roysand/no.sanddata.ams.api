using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class ApiKeyBelongsToLocation : Migration
    {
        /// <summary>
        /// Moves the key-to-location link from Location.ApiKeyId to ApiKey.LocationId, so the key belongs to the
        /// location (deleting a location deletes its key; deleting a key never deletes a location). Existing
        /// links are copied; keys that belong to no location are deleted. Drops the unused ApiKeyUser table.
        /// </summary>
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                schema: "public",
                table: "ApiKey",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE public."ApiKey" AS k SET "LocationId" = l."Id"
                FROM public."Location" AS l WHERE l."ApiKeyId" = k."Id";
                DELETE FROM public."ApiKey" WHERE "LocationId" IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "LocationId",
                schema: "public",
                table: "ApiKey",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiKey_LocationId",
                schema: "public",
                table: "ApiKey",
                column: "LocationId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ApiKey_Location_LocationId",
                schema: "public",
                table: "ApiKey",
                column: "LocationId",
                principalSchema: "public",
                principalTable: "Location",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropForeignKey(
                name: "FK_Location_ApiKey_ApiKeyId",
                schema: "public",
                table: "Location");

            migrationBuilder.DropIndex(
                name: "IX_Location_ApiKeyId",
                schema: "public",
                table: "Location");

            migrationBuilder.DropColumn(
                name: "ApiKeyId",
                schema: "public",
                table: "Location");

            migrationBuilder.DropTable(
                name: "ApiKeyUser",
                schema: "public");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ApiKeyId",
                schema: "public",
                table: "Location",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE public."Location" AS l SET "ApiKeyId" = k."Id"
                FROM public."ApiKey" AS k WHERE k."LocationId" = l."Id";
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "ApiKeyId",
                schema: "public",
                table: "Location",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "ApiKeyUser",
                schema: "public",
                columns: table => new
                {
                    ApiKeyId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsersId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeyUser", x => new { x.ApiKeyId, x.UsersId });
                    table.ForeignKey(
                        name: "FK_ApiKeyUser_ApiKey_ApiKeyId",
                        column: x => x.ApiKeyId,
                        principalSchema: "public",
                        principalTable: "ApiKey",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApiKeyUser_User_UsersId",
                        column: x => x.UsersId,
                        principalSchema: "public",
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Location_ApiKeyId",
                schema: "public",
                table: "Location",
                column: "ApiKeyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeyUser_UsersId",
                schema: "public",
                table: "ApiKeyUser",
                column: "UsersId");

            migrationBuilder.AddForeignKey(
                name: "FK_Location_ApiKey_ApiKeyId",
                schema: "public",
                table: "Location",
                column: "ApiKeyId",
                principalSchema: "public",
                principalTable: "ApiKey",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropForeignKey(
                name: "FK_ApiKey_Location_LocationId",
                schema: "public",
                table: "ApiKey");

            migrationBuilder.DropIndex(
                name: "IX_ApiKey_LocationId",
                schema: "public",
                table: "ApiKey");

            migrationBuilder.DropColumn(
                name: "LocationId",
                schema: "public",
                table: "ApiKey");
        }
    }
}
